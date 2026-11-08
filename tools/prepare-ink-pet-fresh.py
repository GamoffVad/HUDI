"""Fresh source-video trimap matting. Never reads old keyed/processed PNG alpha."""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor,wait,FIRST_COMPLETED
from zipfile import ZipFile,ZIP_STORED
import json,hashlib,runpy,shutil,sys
import cv2
import numpy as np
from scipy.ndimage import distance_transform_edt,binary_fill_holes
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parents[1]
VERSION='fresh-trimap-v4'
cv2.setNumThreads(1)
PLATE_BASES={}

def plate_surface(rgb, foreground):
    h,w=rgb.shape[:2]
    yy,xx=np.mgrid[0:h:16,0:w:16]
    samples=rgb[yy,xx];mask=~cv2.dilate(foreground.astype(np.uint8),np.ones((17,17),np.uint8))[yy,xx].astype(bool)
    mask&=(samples[:,:,1]>100)
    x=xx[mask]/w;y=yy[mask]/h
    design=np.stack([np.ones_like(x),x,y,x*x,x*y,y*y],axis=1);values=samples[mask]
    keep=np.ones(len(x),bool)
    for _ in range(3):
        fit=np.linalg.lstsq(design[keep],values[keep],rcond=None)[0]
        error=np.linalg.norm(design@fit-values,axis=1)
        keep=error<=max(8,np.percentile(error,85))
    if (h,w) not in PLATE_BASES:
        y,x=np.mgrid[0:h,0:w];x=x.astype(np.float32)/w;y=y.astype(np.float32)/h
        PLATE_BASES[(h,w)]=np.stack([np.ones_like(x),x,y,x*x,x*y,y*y],axis=-1)
    return np.clip(PLATE_BASES[(h,w)]@fit.astype(np.float32),0,255)

def fresh_matte(bgr):
    rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB).astype(np.float32)
    r,g,b=rgb.transpose(2,0,1)
    high=rgb.max(axis=2);low=rgb.min(axis=2)
    saturation=(high-low)/np.maximum(high,1)
    # A green-screen background must be green-dominant. Warm/dark ivory and
    # brown cloth remain foreground at every brightness, not only above a
    # special white threshold. Yellow-green spill is a color issue, not alpha.
    green=(g>np.maximum(r,b)+5)&(g-low>9)&(saturation>.25)
    weak_spill=(g>np.maximum(r,b)+3)&~green
    foreground=~green
    count,labels,stats,_=cv2.connectedComponentsWithStats(foreground.astype(np.uint8),8)
    keep=np.zeros(count,bool);keep[1:]=stats[1:,cv2.CC_STAT_AREA]>=6
    foreground=keep[labels]
    # Recover green-lit fabric enclosed inside the main body, without filling
    # actual green-screen gaps. Real background has negligible red and blue;
    # shaded cloth retains both. Separate glyph holes are not part of the body.
    body_label=1+np.argmax(stats[1:,cv2.CC_STAT_AREA])
    body=labels==body_label
    enclosed=binary_fill_holes(body)&~foreground
    restored_cloth=enclosed&((np.minimum(r,b)>12)|(g<=np.maximum(r,b)*1.05))
    foreground|=restored_cloth
    weak_spill|=restored_cloth
    plate=plate_surface(rgb,foreground)
    inside=cv2.distanceTransform(foreground.astype(np.uint8),cv2.DIST_L2,5)
    outside=cv2.distanceTransform((~foreground).astype(np.uint8),cv2.DIST_L2,5)
    core=inside>=2
    # Thin real ink details may have no two-pixel interior; retain their clean seeds.
    for label in range(1,count):
        if keep[label] and not np.any(core[labels==label]):core[labels==label]=True
    _,nearest=distance_transform_edt(~core,return_indices=True)
    # Desaturated green light on opaque grey fabric is foreground, not a hole.
    # Correct its color in the source-space foreground model while keeping core alpha=1.
    clean_rgb=rgb.copy()
    clean_rgb[:,:,1][weak_spill]=((r+b)*.5)[weak_spill]
    # Grey fabric can retain a yellow-green reflected light even when G≈R.
    # Restrict this source-space spill model to the silhouette border. Red-
    # dominant lining/brass and ivory eyes deep inside the face are excluded.
    olive_spill=(g>=r-5)&(np.minimum(r,g)-b>8)&(inside<=5)&foreground
    blend=np.clip((np.minimum(r,g)-b-8)/12,0,1)*np.clip((g-r+7)/2,0,1)
    neutral=(r+g+2*b)/4
    clean_rgb[olive_spill]=clean_rgb[olive_spill]*(1-blend[olive_spill,None])+neutral[olive_spill,None]*blend[olive_spill,None]
    local_foreground=clean_rgb[nearest[0],nearest[1]]
    direction=local_foreground-plate
    alpha=np.clip(np.sum((rgb-plate)*direction,axis=2)/np.maximum(np.sum(direction*direction,axis=2),1),0,1)
    alpha[core]=1
    alpha[outside>2.5]=0
    prediction=alpha[:,:,None]*local_foreground+(1-alpha[:,:,None])*plate
    residual=np.linalg.norm(rgb-prediction,axis=2)
    unknown=~core
    # Inconsistent chroma haze is not a physical foreground/background mixture.
    alpha[unknown]*=np.clip((65-residual[unknown])/40,0,1)
    alpha[alpha<.02]=0
    # Recover premultiplied foreground from the newly estimated alpha/2D plate.
    colors=clean_rgb-(1-alpha[:,:,None])*plate
    colors=np.clip(colors,0,255*alpha[:,:,None])
    # Unknown antialias pixels use the physical local foreground-color estimate;
    # this is part of the fresh matte solve, not repainting a previous PNG edge.
    colors[unknown]=local_foreground[unknown]*alpha[unknown,None]
    return np.dstack((colors/255,alpha)).astype(np.float32)

def straight(pm):
    a=pm[:,:,3:4];rgb=np.divide(pm[:,:,:3],a,out=np.zeros_like(pm[:,:,:3]),where=a>1e-6)
    return np.uint8(np.round(np.clip(np.concatenate((rgb,a),axis=2),0,1)*255))

def output_canvas(pm, transform):
    # Filter premultiplied color and alpha together over the original source
    # pixel coverage. Linear downscaling alone aliases diagonal silhouettes.
    hi=cv2.warpAffine(pm,np.float32(transform)*2.5,(1040,1040),flags=cv2.INTER_LINEAR)
    return np.clip(cv2.resize(hi,(416,416),interpolation=cv2.INTER_AREA),0,1)

def candidates():
    out=ROOT/'output/ink-pet/fresh-candidates';out.mkdir(exist_ok=True)
    cases=[('neutral','dash',0),('pockets','pockets',50),('paintmagic','paintmagic',123),('dash','dash',86)]
    sheet=Image.new('RGB',(416*3,456*len(cases)),'#333');draw=ImageDraw.Draw(sheet)
    crops=Image.new('RGB',(240*3,260*len(cases)),'#333');cd=ImageDraw.Draw(crops)
    for row,(name,source,index) in enumerate(cases):
        cap=cv2.VideoCapture(str(ROOT/'assets/pets/ink'/f'{source}.mp4'));cap.set(cv2.CAP_PROP_POS_FRAMES,index);ok,bgr=cap.read();cap.release();assert ok
        cv2.imwrite(str(out/f'{name}-source.png'),bgr)
        pm=fresh_matte(bgr)
        pm=output_canvas(pm,[[.4,0,16],[0,.4,16]])
        Image.fromarray(straight(pm)).save(out/f'{name}.png')
        crop={'neutral':(220,85,300,165),'pockets':(165,260,245,340),'paintmagic':(45,175,125,255),'dash':(300,205,380,285)}[name]
        for col,bg in enumerate([(24,24,24),(245,245,245),(215,30,180)]):
            comp=np.uint8(np.clip((pm[:,:,:3]+np.array(bg)/255*(1-pm[:,:,3:4]))*255,0,255));im=Image.fromarray(comp)
            sheet.paste(im,(col*416,row*456));draw.text((col*416+4,row*456+420),name,fill='white')
            crops.paste(im.crop(crop).resize((240,240),Image.Resampling.NEAREST),(col*240,row*260));cd.text((col*240+3,row*260+242),name,fill='white')
    sheet.save(out/'actual-size.png');crops.save(out/'details300.png')
    print(str(out),flush=True)

def frame_job(name,index,bgr,transform,folder):
    source=fresh_matte(bgr)
    ys,xs=np.where(source[:,:,3]>.15)
    source_box=[int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1)] if len(xs) else None
    pm=output_canvas(source,transform);pixels=straight(pm)
    ident=f'{name}-{index:03}';Image.fromarray(pixels).save(folder/(ident+'.png'),compress_level=2)
    r,g,b=pixels[:,:,:3].astype(float).transpose(2,0,1);a=pixels[:,:,3]/255
    depth=distance_transform_edt(a>=.5);edge=(depth<4)&(a>.02)
    green=np.maximum(g-np.maximum(r,b),0)*a
    olive=np.maximum(np.minimum(r,g)-b,0)*a;olive[r-g>12]=0
    green[~edge]=0;olive[~edge]=0
    gy,gx=np.unravel_index(np.argmax(green),green.shape);oy,ox=np.unravel_index(np.argmax(olive),olive.shape)
    occupied=pixels[:,:,3]>5
    yy,xx=np.where(occupied)
    return {'index':index,'frame':ident,'source_bounds':source_box,'output_bounds':[int(xx.min()),int(yy.min()),int(xx.max()+1),int(yy.max()+1)] if len(xx) else None,'green_max':float(green.max()),'green_over_5':int((green>5).sum()),'olive_max':float(olive.max()),'olive_over_10':int((olive>10).sum()),'green_xy':[int(gx),int(gy)],'olive_xy':[int(ox),int(oy)],'alpha_area':float(a.sum()),'soft_alpha_pixels':int(((a>0)&(a<1)).sum())}

def build():
    out=ROOT/'output/ink-pet';folder=out/'fresh-frames';folder.mkdir(exist_ok=True)
    qa=out/'qa-fresh';qa.mkdir(exist_ok=True)
    config=runpy.run_path(str(ROOT/'tools/prepare-ink-pet.py'))
    names=config['NAMES'];source_names=config['SOURCE_NAMES'];reviewed_steps=config['reviewed_steps']
    cap=cv2.VideoCapture(str(ROOT/'assets/pets/ink/dash.mp4'));ok,target=cap.read();cap.release();assert ok
    clips={};records={};source_meta={};ids=['neutral']
    with ThreadPoolExecutor(max_workers=4) as pool:
        for name in names:
            path=ROOT/'assets/pets/ink'/f'{source_names.get(name,name)}.mp4'
            cap=cv2.VideoCapture(str(path));fps=cap.get(cv2.CAP_PROP_FPS);count=int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
            width=int(cap.get(cv2.CAP_PROP_FRAME_WIDTH));height=int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
            if (width,height)!=(960,960):raise RuntimeError('Unexpected source dimensions; never crop to fit')
            transform=np.float32([[1,0,0],[0,1,0]]);reg=None
            if name=='jump':
                ok,first=cap.read();cap.set(cv2.CAP_PROP_POS_FRAMES,0);transform,reg=config['registration'](first,target)
            transform*=.4;transform[:,2]+=16
            results=[];pending=set()
            for i in range(count):
                ok,bgr=cap.read()
                if not ok:raise RuntimeError(f'Cannot freshly decode {name}:{i}')
                pending.add(pool.submit(frame_job,name,i,bgr,transform,folder))
                if len(pending)>=8:
                    done,pending=wait(pending,return_when=FIRST_COMPLETED)
                    results.extend(f.result() for f in done)
                if i%48==0:print(f'Fresh source {name}: {i+1}/{count}',flush=True)
            results.extend(f.result() for f in pending);cap.release();results.sort(key=lambda r:r['index'])
            records[name]=results;clips[name]=reviewed_steps(name,[{'frame':r['frame'],'seconds':1/fps} for r in results]);ids.extend(s['frame'] for s in clips[name])
            source_meta[name]={'path':str(path.relative_to(ROOT)),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'frames':count,'fps':fps,'registration':reg,'decoded_from_original_video':True}
            (qa/f'{name}-frames.json').write_text(json.dumps(results),encoding='utf-8')
    shutil.copyfile(folder/'dash-000.png',folder/'neutral.png')
    ids=list(dict.fromkeys(ids))
    metrics=[r for results in records.values() for r in results]
    clipped=[r['frame'] for r in metrics if r['output_bounds'] and min(r['output_bounds'][0],r['output_bounds'][1],416-r['output_bounds'][2],416-r['output_bounds'][3])<2]
    if clipped:raise RuntimeError('Fresh output clips: '+str(clipped))
    # Review each action at actual size and 300% on three backgrounds.
    worst=[]
    for name,steps in clips.items():
        chosen=[steps[0]['frame'],steps[len(steps)//2]['frame'],steps[-1]['frame']]
        action_records=[r for r in records[name] if r['frame'] in {s['frame'] for s in steps}]
        item=max(action_records,key=lambda r:r['olive_max']);worst.append(item)
        chosen.append(item['frame'])
        sheet=Image.new('RGB',(1248,456*4),'#333');draw=ImageDraw.Draw(sheet)
        detail=Image.new('RGB',(720,260*3),'#333');dd=ImageDraw.Draw(detail)
        for row,ident in enumerate(chosen):
            p=np.array(Image.open(folder/(ident+'.png')));a=p[:,:,3:4]/255
            for col,bg in enumerate([(24,24,24),(245,245,245),(215,30,180)]):
                im=Image.fromarray(np.uint8(p[:,:,:3]*a+np.array(bg)*(1-a)))
                sheet.paste(im,(col*416,row*456));draw.text((col*416+4,row*456+420),ident,fill='white')
                if row==3:
                    x,y=item['olive_xy'];boxes=[(220,85,300,165),(165,260,245,340),(x-40,y-40,x+40,y+40)]
                    for dr,box in enumerate(boxes):
                        detail.paste(im.crop(box).resize((240,240),Image.Resampling.NEAREST),(col*240,dr*260));dd.text((col*240+3,dr*260+242),ident+[' hood',' legs',' worst chroma'][dr],fill='white')
        sheet.save(qa/f'{name}-actual.png');detail.save(qa/f'{name}-details300.png')
    report={'version':VERSION,'sources':source_meta,'all_source_frames_decoded':len(metrics),'used_frames':len(ids),'old_alpha_or_png_used':False,'output_clipping':clipped,'green_over_5':sum(r['green_over_5'] for r in metrics),'olive_over_10':sum(r['olive_over_10'] for r in metrics),'worst_per_action':worst,'method':'fresh RGB trimap, robust quadratic 2D green plate, local foreground/background alpha solve, PM unmix, area-filtered output'}
    (qa/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    manifest={'version':'pet-1','name':'Инк','id':'ink','frameSize':[416,416],'displayBounds':[139,89,137,254],'alphaPrepared':True,'breathAmplitude':0,'horizontalMirrored':False,'neutral':'neutral','frames':ids,'clips':clips,'matteVersion':VERSION,'description':'Eleven animations freshly extracted from original green-background videos'}
    (out/'manifest-fresh.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    with ZipFile(out/'Ink-fresh.pet','w',ZIP_STORED) as z:
        z.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False))
        for ident in ids:z.write(folder/(ident+'.png'),ident+'.png')
    print(json.dumps({'package':str(out/'Ink-fresh.pet'),'frames':len(ids),'clipping':clipped,'green_over_5':report['green_over_5'],'olive_over_10':report['olive_over_10']}),flush=True)

if __name__=='__main__':
    if '--build' in sys.argv:build()
    else:candidates()
