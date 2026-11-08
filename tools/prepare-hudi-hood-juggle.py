"""Add the two source performances with one fixed transform per clip.

Padding is derived from ALL keyed frames, then added equally to the old library.
The existing body viewport, source pixels and physical display size are preserved.
"""
import importlib.util
import json
import shutil
from pathlib import Path
from zipfile import ZipFile, ZIP_STORED
from concurrent.futures import ThreadPoolExecutor
import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/hudi-pet-v7.3'
SOURCE=ROOT/'assets/higgsfield/2026-11-08-hood-juggle'
SOURCES={'hoodtighten':'hudi-hoodtighten-v2.mp4','juggle':'hudi-juggle-v2.mp4'}
spec=importlib.util.spec_from_file_location('keyer',ROOT/'.animation-tools/prepare-green-sprites-v6.py')
keyer=importlib.util.module_from_spec(spec);spec.loader.exec_module(keyer)

def bounds(alpha,threshold):
    y,x=np.where(alpha>threshold)
    if not len(x):raise RuntimeError('Empty matte')
    return np.array([x.min(),y.min(),x.max()+1,y.max()+1],dtype=float)

def body_bounds(alpha):
    n,labels,stats,_=cv2.connectedComponentsWithStats((alpha>.8).astype(np.uint8),8)
    if n<2:raise RuntimeError('Missing body')
    main=1+np.argmax(stats[1:,cv2.CC_STAT_AREA])
    return bounds(labels==main,0)

def projected(b,m):
    points=np.array([[[b[0],b[1]],[b[2],b[3]]]],np.float32)
    a=cv2.transform(points,m)[0]
    return [float(a[:,0].min()),float(a[:,1].min()),float(a[:,0].max()),float(a[:,1].max())]

def shift_existing_metadata(manifest,pad,w,h):
    for info in manifest.get('addedEmotionSources',{}).values():
        for field,marginfield in [('boundsUnion','minimumOutputMargin'),('faintBoundsUnion','minimumFaintOutputMargin')]:
            if field in info:
                b=info[field];b=[b[0]+pad[0],b[1]+pad[1],b[2]+pad[0],b[3]+pad[1]]
                info[field]=b;info[marginfield]=min(b[0],b[1],w-b[2],h-b[3])
    framing=manifest.get('framing',{})
    if 'legacyPadding' in framing:framing['legacyPadding']=[framing['legacyPadding'][0]+pad[0],framing['legacyPadding'][1]+pad[1]]
    if 'newSourceTransform' in framing:
        framing['newSourceTransform'][0][2]+=pad[0];framing['newSourceTransform'][1][2]+=pad[1]
    framing['additionalPerformancesPadding']=pad

def main():
    cv2.setNumThreads(1)
    for filename in SOURCES.values():
        if not (SOURCE/filename).exists():raise RuntimeError('Missing reviewed source '+filename)
    backup=OUT/'rollback-before-hood-juggle';backup.mkdir(exist_ok=True)
    for filename in ('manifest.json','sprites.zip'):
        if not (backup/filename).exists():shutil.copy2(OUT/filename,backup/filename)
    manifest=json.loads((backup/'manifest.json').read_text(encoding='utf-8'))
    if any(n in manifest['clips'] for n in SOURCES):raise RuntimeError('Backup already contains new clips')
    import io
    with ZipFile(backup/'sprites.zip') as z:
        target=body_bounds(np.array(Image.open(io.BytesIO(z.read('neutral.png'))))[:,:,3]/255)
    oldw,oldh=manifest['frameSize'];metrics={};allbounds=[]
    for name,filename in SOURCES.items():
        cap=cv2.VideoCapture(str(SOURCE/filename));fps=cap.get(cv2.CAP_PROP_FPS);count=int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
        boxes=[];matrix=None
        for index in range(count):
            ok,bgr=cap.read()
            if not ok:raise RuntimeError(f'Decode failed {name}:{index}')
            pm,a,_=keyer.key_frame(bgr);a=np.ascontiguousarray(a[:,::-1])
            if matrix is None:
                b=body_bounds(a);scale=(target[3]-target[1])/(b[3]-b[1])
                matrix=np.array([[scale,0,(target[0]+target[2]-scale*(b[0]+b[2]))/2],
                                 [0,scale,target[3]-scale*b[3]]],np.float32)
            box=bounds(a,2/255)
            if min(box[0],box[1],a.shape[1]-box[2],a.shape[0]-box[3])<3:
                raise RuntimeError(f'Source subject/effect at edge {name}:{index}: {box}')
            boxes.append(projected(box,matrix))
            if index%120==0:print(f'{name} framing {index+1}/{count}',flush=True)
        cap.release();allbounds.extend(boxes)
        metrics[name]={'source':filename,'fps':fps,'frames':count,'matrixBeforePadding':matrix.tolist(),
                       'frameBoundsBeforePadding':boxes,'reflected':True}
    b=np.array(allbounds)
    # Enough transparent breathing room for soft antialiasing on every side.
    pad=[max(0,int(np.ceil((12-b[:,0].min())/16)*16)),max(0,int(np.ceil((12-b[:,1].min())/16)*16)),
         max(0,int(np.ceil((b[:,2].max()+12-oldw)/16)*16)),max(0,int(np.ceil((b[:,3].max()+12-oldh)/16)*16))]
    w,h=oldw+pad[0]+pad[2],oldh+pad[1]+pad[3]
    if w*h>1048576:raise RuntimeError(f'Canvas exceeds runtime pixel budget: {w}x{h}')
    print(f'Fixed common canvas {w}x{h}, extra padding {pad}',flush=True)
    shift_existing_metadata(manifest,pad,w,h)
    frames=OUT/'frames';qa=OUT/'qa/hood-juggle';qa.mkdir(parents=True,exist_ok=True)
    with ZipFile(backup/'sprites.zip') as archive:
        def preserve(name):
            raw=archive.read(name+'.png')
            if not any(pad):(frames/(name+'.png')).write_bytes(raw);return
            with Image.open(io.BytesIO(raw)) as im:
                canvas=Image.new('RGBA',(w,h));canvas.paste(im,(pad[0],pad[1]));canvas.save(frames/(name+'.png'),compress_level=2)
        with ThreadPoolExecutor(max_workers=4) as pool:list(pool.map(preserve,manifest['frames']))
    for name,filename in SOURCES.items():
        info=metrics[name];matrix=np.array(info['matrixBeforePadding'],np.float32);matrix[:,2]+=pad[:2]
        cap=cv2.VideoCapture(str(SOURCE/filename));steps=[];softbounds=[]
        for i in range(info['frames']):
            ok,bgr=cap.read()
            if not ok:raise RuntimeError(f'Decode failed {name}:{i}')
            pm,a,_=keyer.key_frame(bgr)
            pm=cv2.warpAffine(np.ascontiguousarray(pm[:,::-1]),matrix,(w,h),flags=cv2.INTER_AREA)
            a=cv2.warpAffine(np.ascontiguousarray(a[:,::-1]),matrix,(w,h),flags=cv2.INTER_AREA)
            rgb=np.divide(pm,a[:,:,None],out=np.zeros_like(pm),where=a[:,:,None]>1e-5)
            rgba=np.uint8(np.clip(np.dstack((rgb,a*255)),0,255));bb=bounds(rgba[:,:,3],1)
            if min(bb[0],bb[1],w-bb[2],h-bb[3])<3:raise RuntimeError(f'Output edge {name}:{i}')
            ident=f'{name}-{i:03d}';Image.fromarray(rgba).save(frames/(ident+'.png'),compress_level=2)
            steps.append({'frame':ident,'seconds':1/info['fps']});softbounds.append(bb.tolist())
            if i%120==0:print(f'{name} output {i+1}/{info["frames"]}',flush=True)
        cap.release();manifest['clips'][name]=steps;manifest['frames'].extend(s['frame'] for s in steps)
        info['matrix']=matrix.tolist();info['outputBounds']=softbounds
        manifest.setdefault('addedEmotionSources',{})[name]={k:v for k,v in info.items() if 'Bounds' not in k}
        ids=np.linspace(0,len(steps)-1,9).astype(int)
        tileh=round(h/w*240);sheet=Image.new('RGB',(9*240,2*tileh))
        for col,i in enumerate(ids):
            im=Image.open(frames/(steps[i]['frame']+'.png'))
            for row,color in enumerate([(24,26,30,255),(245,245,245,255)]):
                tile=Image.new('RGBA',(w,h),color);tile.alpha_composite(im)
                sheet.paste(tile.resize((240,tileh)),(col*240,row*tileh))
                ImageDraw.Draw(sheet).text((col*240+5,row*tileh+5),f'{name} {i}',fill='orange')
        sheet.save(qa/(name+'-contact.jpg'),quality=95)
    manifest['frameSize']=[w,h];manifest['displayBounds'][0]+=pad[0];manifest['displayBounds'][1]+=pad[1]
    manifest['version']='7.3.2';manifest['additionalCanvasPadding']=pad
    (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    (qa/'processing.json').write_text(json.dumps({'padding':pad,'previousCanvas':[oldw,oldh],'canvas':[w,h],'clips':metrics},indent=2),encoding='utf-8')
    with ZipFile(OUT/'sprites-next.zip','w',ZIP_STORED) as archive:
        archive.writestr('manifest.json',json.dumps(manifest))
        for ident in manifest['frames']:archive.write(frames/(ident+'.png'),ident+'.png')
    (OUT/'sprites-next.zip').replace(OUT/'sprites.zip')
    print(f'Pack ready: {len(manifest["frames"])} frames',flush=True)

if __name__=='__main__':main()
