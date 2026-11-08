"""Append three reviewed clips, preserving the seven installed Ink animations."""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, wait, FIRST_COMPLETED
from zipfile import ZipFile, ZIP_STORED
import cv2, numpy as np, json, runpy, hashlib, sys
from PIL import Image, ImageDraw
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/ink-pet-v2.2'; FRAMES=OUT/'frames'; QA=OUT/'qa'
SOURCE=ROOT/'assets/pets/ink-restart/2026-11-08-extra-reactions'
BASE=ROOT/'output/ink-pet-v2.1/Ink.pet'
SPECS=[('indignation','indignant-temple'),('firetrick','snap-fire-innocent-fixed'),('impatient','impatient')]
MAT=runpy.run_path(str(ROOT/'tools/prepare-ink-pet-fresh.py'))
cv2.setNumThreads(1)

def matte(bgr,fire):
    rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB).astype(np.float32)
    r,g,b=rgb.transpose(2,0,1);pm=MAT['fresh_matte'](bgr)
    neutral=(rgb.max(2)-rgb.min(2)<18)&(rgb.max(2)<110)
    core=cv2.erode(neutral.astype('uint8'),np.ones((5,5),'uint8')).astype(bool)
    clean=rgb.copy();spill=g>np.maximum(r,b)+3
    clean[:,:,1][spill]=((r+b)*.5)[spill]
    pm[core,:3]=clean[core]/255;pm[core,3]=1
    if fire:
        # Approved frame-090 fire method, with a warm-colour validity check so
        # grey garment boundaries never use the luminous effect's alpha model.
        warm=(r>=g-5)&(r>b+14)&(pm[:,:,3]>.98)
        pm[warm,:3]=rgb[warm]/255
        plate=np.median(rgb[:60].reshape(-1,3),axis=0)
        yy=np.indices(r.shape)[0]
        candidate=(yy>500)&~core&(g>50)&((g>np.maximum(r,b)+5)|((g>=r*.85)&(r>b+25)))
        a=np.clip((r-plate[0]-5)/(250-plate[0]),0,1)
        a*=np.clip((r-plate[0]-5)/10,0,1)
        unmixed=rgb-(1-a[:,:,None])*plate
        # The fire model assumes a warm luminous foreground. Grey cloth mixed
        # with green violates that model: subtracting the plate would turn its
        # green channel negative and lower its alpha, creating a magenta rim.
        # Preserve the original garment matte unless the solve describes fire.
        effect=candidate&(unmixed[:,:,1]>=unmixed[:,:,0]*.35)&(unmixed[:,:,0]>np.maximum(unmixed[:,:,2],0)*1.8+5)
        color=np.maximum(unmixed,0)
        color=np.minimum(color,255*a[:,:,None]);color[:,:,1]=np.minimum(color[:,:,1],color[:,:,0])
        pm[effect,:3]=color[effect]/255;pm[effect,3]=a[effect]
    else:
        old=pm[:,:,3].copy();t=np.clip((old-.08)/.84,0,1);alpha=t*t*(3-2*t)
        scale=np.divide(alpha,old,out=np.zeros_like(alpha),where=old>1e-6)
        pm[:,:,:3]*=scale[:,:,None];pm[:,:,3]=alpha
    return MAT['straight'](pm),core

def process(name,i,bgr):
    p,core=matte(bgr,name=='firetrick');ident=f'{name}-{i:03}'
    Image.fromarray(p).save(FRAMES/(ident+'.png'),compress_level=3)
    r,g,b=p[:,:,:3].astype('int16').transpose(2,0,1);a=p[:,:,3]
    ys,xs=np.where(a>64)
    return dict(frame=ident,index=i,green_pixels=int(((g>np.maximum(r,b)+8)&(a>10)).sum()),opaque_core_holes=int((core&(a<250)).sum()),bounds=[int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1)])

def review_sheet(name,rows):
    picks=np.linspace(0,len(rows)-1,12,dtype=int)
    sheet=Image.new('RGB',(960,344*len(picks)),(35,35,35));d=ImageDraw.Draw(sheet)
    for row,index in enumerate(picks):
        ident=rows[index]['frame'];p=np.array(Image.open(FRAMES/(ident+'.png')));a=p[:,:,3:4]/255
        for col,bg in enumerate([(245,245,245),(24,24,24),(185,45,155)]):
            im=Image.fromarray(np.uint8(np.clip(p[:,:,:3]*a+np.array(bg)*(1-a),0,255)))
            sheet.paste(im.resize((320,320),Image.Resampling.LANCZOS),(col*320,row*344));d.text((col*320+5,row*344+323),ident,fill='white')
    sheet.save(QA/(name+'-review.jpg'),quality=94)

def main():
    FRAMES.mkdir(parents=True,exist_ok=True);QA.mkdir(exist_ok=True)
    with ZipFile(BASE) as z:manifest=json.loads(z.read('manifest.json'))
    previous_clips=dict(manifest['clips']);records={};sources={}
    repair='--repair' in sys.argv
    flagged={r['frame'] for r in json.loads((QA/'report.json').read_text())['issues']} if repair else set()
    with ThreadPoolExecutor(max_workers=4) as pool:
        for name,source in SPECS:
            path=SOURCE/(source+'.mp4');cap=cv2.VideoCapture(str(path));fps=cap.get(5);n=int(cap.get(7));assert (int(cap.get(3)),int(cap.get(4)))==(960,960)
            cached={r['index']:r for r in json.loads((QA/(name+'-frames.json')).read_text())} if repair else {}
            rows=[];pending=set()
            for i in range(n):
                ok,bgr=cap.read();assert ok
                if repair and f'{name}-{i:03}' not in flagged:
                    rows.append(cached[i]);continue
                pending.add(pool.submit(process,name,i,bgr))
                if len(pending)>=6:
                    done,pending=wait(pending,return_when=FIRST_COMPLETED);rows.extend(f.result() for f in done)
                if i%48==0:print(name,i+1,'/',n,flush=True)
            rows.extend(f.result() for f in pending);cap.release();rows.sort(key=lambda r:r['index']);records[name]=rows
            manifest['clips'][name]=[dict(frame=r['frame'],seconds=1/fps) for r in rows]
            manifest['frames'].extend(r['frame'] for r in rows)
            sources[name]=dict(path=str(path.relative_to(ROOT)),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),fps=fps,frames=n)
            (QA/(name+'-frames.json')).write_text(json.dumps(rows),encoding='utf8');review_sheet(name,rows)
            print(name,'done',flush=True)
    allrows=[r for rows in records.values() for r in rows]
    bad=[r for r in allrows if r['green_pixels'] or r['opaque_core_holes'] or min(r['bounds'][0],r['bounds'][1],960-r['bounds'][2],960-r['bounds'][3])<2]
    report=dict(new_frames=len(allrows),total_frames=len(manifest['frames']),sources=sources,issues=bad,old_clips_preserved=all(manifest['clips'][k]==v for k,v in previous_clips.items()))
    approved=np.array(Image.open(ROOT/'output/ink-cutout-preview/ink-fire-frame-090.png'))
    report['approved_sample_matches_exactly']=bool(np.array_equal(approved,np.array(Image.open(FRAMES/'firetrick-090.png'))))
    (QA/'report.json').write_text(json.dumps(report,indent=2),encoding='utf8')
    if bad:raise RuntimeError(f'Review {len(bad)} flagged frames before packaging')
    # The colour repair changes invalid glow pixels from the approved sample.
    # Compare the dominant flame interior without requiring defective edge RGB.
    fixed=np.array(Image.open(FRAMES/'firetrick-090.png'))
    yy,xx=np.indices(approved.shape[:2])
    flame=(approved[:,:,0]>200)&(approved[:,:,1]>180)&(approved[:,:,2]<180)&(approved[:,:,3]>240)&(yy>600)&(xx<415)
    assert np.percentile(np.abs(approved[flame].astype(float)-fixed[flame]).max(1),90)<=1, 'Flame interior changed'
    manifest['description']='Ten Ink animations; three additional reviewed reactions. Fire uses the approved soft-glow chroma extraction.'
    manifest['matteVersion']='fresh-trimap-v4-native-edges+approved-fire-v1'
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
    with ZipFile(BASE) as old,ZipFile(OUT/'Ink.pet','w',ZIP_STORED) as z:
        z.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False))
        for entry in old.infolist():
            if entry.filename!='manifest.json':z.writestr(entry,old.read(entry))
        for r in allrows:z.write(FRAMES/(r['frame']+'.png'),r['frame']+'.png')
    assert (OUT/'Ink.pet').stat().st_size<512*1024*1024
    print('Package ready',len(manifest['frames']),'frames',flush=True)
if __name__=='__main__':main()
