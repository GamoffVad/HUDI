"""Merge the five approved whole-frame performances without clipping or recropping."""
import importlib.util
import argparse
import json
import shutil
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from zipfile import ZipFile, ZIP_STORED
import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'output/hudi-pet-v6.3'
SOURCE=ROOT/'assets/higgsfield/2026-11-08-salute-water'
OUT=ROOT/'output/hudi-pet-v7.3'
W,H=640,640
PAD=(128,80)
CLIPS={
    'salute':'hudi-salute-hands-back-lookaround.mp4',
    'march':'hudi-march.mp4',
    'notebook':'hudi-notebook-suspicious-finger-wag.mp4',
    'fire':'hudi-fire-mishap.mp4',
    'firecalm':'hudi-fire-mishap-v2.mp4',
}
# First-frame SIFT registration across all five source references: mean error0.25px.
# Source references were reflected relative to the actual legacy sprite pixels.
# One reflection and one fixed transform apply to every frame, including jumps.
MATRIX=np.array([[.63503,0,-113.174+PAD[0]],[0,.63503,-54.890+PAD[1]]],np.float32)

def load_keyer():
    spec=importlib.util.spec_from_file_location('hudi_keyer',ROOT/'.animation-tools/prepare-green-sprites-v6.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module

def body_bounds(alpha):
    ys,xs=np.where(alpha>224)
    return [int(xs.min()),int(ys.min()),int(xs.max())+1,int(ys.max())+1] if len(xs) else None

def faint_bounds(alpha):
    ys,xs=np.where(alpha>2)
    return [int(xs.min()),int(ys.min()),int(xs.max())+1,int(ys.max())+1] if len(xs) else None

def clean_march_plate(colors,alpha):
    """Remove disconnected green-plate residue, never erode the marching figure.

    This performance has no detached effects. Keep every soft component that
    contains opaque subject pixels; preserve its original RGB and alpha exactly.
    Do not apply this rule to smoke, ink, glows or other effects performances.
    """
    count,labels=cv2.connectedComponents((alpha>0).astype(np.uint8),8)
    keep=np.zeros(count,dtype=bool)
    keep[np.unique(labels[alpha>.5])]=True;keep[0]=False
    support=keep[labels]
    return colors*support[:,:,None],alpha*support

def main():
    parser=argparse.ArgumentParser()
    mode=parser.add_mutually_exclusive_group()
    mode.add_argument('--repair-fire',action='store_true')
    mode.add_argument('--repair-march',action='store_true')
    args=parser.parse_args()
    cv2.setNumThreads(1)
    outframes=OUT/'frames';outframes.mkdir(parents=True,exist_ok=True)
    if args.repair_march:
        backup=OUT/'rollback-before-march';backup.mkdir(exist_ok=True)
        for filename in ('sprites.zip','manifest.json'):
            if not (backup/filename).exists():shutil.copy2(OUT/filename,backup/filename)
        manifest=json.loads((OUT/'manifest.json').read_text(encoding='utf-8'))
        if manifest['frameSize']!=[W,H]:raise RuntimeError('March repair requires the current full-canvas pack.')
    elif args.repair_fire:
        backup=OUT/'rollback-before-smoke';backup.mkdir(exist_ok=True)
        for filename in ('sprites.zip','manifest.json'):
            if not (backup/filename).exists():shutil.copy2(OUT/filename,backup/filename)
        manifest=json.loads((backup/'manifest.json').read_text(encoding='utf-8'))
        if manifest['frameSize']!=[512,560] or manifest.get('displayBounds')!=[64,48,384,500]:
            raise RuntimeError('Smoke repair requires the original 512x560 pack backup.')
        with ZipFile(backup/'sprites.zip') as previous:
            def preserve_old(name):
                import io
                with Image.open(io.BytesIO(previous.read(name+'.png'))) as image:
                    canvas=Image.new('RGBA',(W,H));canvas.paste(image,(64,32));canvas.save(outframes/(name+'.png'),compress_level=2)
            with ThreadPoolExecutor(max_workers=4) as pool:list(pool.map(preserve_old,manifest['frames']))
        for metrics in manifest.get('addedEmotionSources',{}).values():
            if 'boundsUnion' in metrics:
                b=metrics['boundsUnion'];metrics['boundsUnion']=[b[0]+64,b[1]+32,b[2]+64,b[3]+32]
                b=metrics['boundsUnion'];metrics['minimumOutputMargin']=min(b[0],b[1],W-b[2],H-b[3])
        print(f'Preserved {len(manifest["frames"])} existing frames with offset(+64,+32).',flush=True)
    else:manifest=json.loads((BASE/'manifest.json').read_text(encoding='utf-8'))
    def pad_old(name):
        target=outframes/(name+'.png')
        # Recreate from the original pack: a cached frame may have the old canvas.
        with Image.open(BASE/'frames'/(name+'.png')) as image:
            canvas=Image.new('RGBA',(W,H));canvas.paste(image,PAD);canvas.save(target,compress_level=2)
    if not args.repair_fire and not args.repair_march:
        with ThreadPoolExecutor(max_workers=4) as pool:list(pool.map(pad_old,manifest['frames']))
        print(f'Padded {len(manifest["frames"])} existing frames without changing any source pixels.',flush=True)
    keyer=load_keyer()
    def extract(item):
        name,filename=item
        cap=cv2.VideoCapture(str(SOURCE/filename));fps=cap.get(cv2.CAP_PROP_FPS);count=int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
        steps=[];bounds=[];faint=[];source_edges=[];faint_source_edges=[]
        for index in range(count):
            ok,bgr=cap.read()
            if not ok:raise RuntimeError(f'Cannot decode {name}:{index}')
            # Retain the entire raw frame, including translucent smoke and glows.
            # This geometric check is independent of matte thresholds.
            sh,sw=bgr.shape[:2]
            corners=np.array([[[0.,0.],[sw-1.,0.],[0.,sh-1.],[sw-1.,sh-1.]]],np.float32)
            projected=cv2.transform(corners,MATRIX)[0]
            if projected[:,0].min()<3 or projected[:,1].min()<3 or projected[:,0].max()>W-4 or projected[:,1].max()>H-4:
                raise RuntimeError(f'Whole source frame would be cropped: {name}:{index}')
            if name=='march':
                # The ending has a moving horizontal hue gradient. The upper
                # 80 source rows are clear in every frame of this performance;
                # a column plate follows that gradient instead of averaging it.
                rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB).astype(np.float32)
                plate=cv2.GaussianBlur(np.median(rgb[:80],axis=0)[None,:,:],(31,1),0)
                colors,alpha,_=keyer.key_frame(bgr,plate_override=plate)
                if np.any(alpha[:80]>.5):raise RuntimeError(f'March reference strip contains subject pixels: {index}')
                colors,alpha=clean_march_plate(colors,alpha)
            else:colors,alpha,_=keyer.key_frame(bgr)
            # Inspect the actual raw frame, before scale, reflection or padding.
            rawbounds=body_bounds(np.uint8(np.clip(alpha,0,1)*255))
            if rawbounds and min(rawbounds[0],rawbounds[1],bgr.shape[1]-rawbounds[2],bgr.shape[0]-rawbounds[3])<2:
                source_edges.append(index)
            rawfaint=faint_bounds(np.uint8(np.clip(alpha,0,1)*255))
            if rawfaint and min(rawfaint[0],rawfaint[1],sw-rawfaint[2],sh-rawfaint[3])<2:faint_source_edges.append(index)
            pm=cv2.warpAffine(np.ascontiguousarray(colors[:,::-1]),MATRIX,(W,H),flags=cv2.INTER_AREA)
            a=cv2.warpAffine(np.ascontiguousarray(alpha[:,::-1]),MATRIX,(W,H),flags=cv2.INTER_AREA)
            rgb=np.divide(pm,a[:,:,None],out=np.zeros_like(pm),where=a[:,:,None]>1e-5)
            rgba=np.uint8(np.clip(np.dstack((rgb,a*255)),0,255))
            bbox=body_bounds(rgba[:,:,3]);bounds.append(bbox)
            fbox=faint_bounds(rgba[:,:,3]);faint.append(fbox)
            if fbox is None or min(fbox[0],fbox[1],W-fbox[2],H-fbox[3])<3:
                raise RuntimeError(f'Faint effects touch output edge {name}:{index}: {fbox}')
            if bbox is None or min(bbox[0],bbox[1],W-bbox[2],H-bbox[3])<3:
                raise RuntimeError(f'Clipped or empty output {name}:{index}: {bbox}')
            ident=f'{name}-{index:03d}'
            Image.fromarray(rgba).save(outframes/(ident+'.png'),compress_level=2)
            steps.append({'frame':ident,'seconds':1/fps})
            if index%120==0:print(f'{name}: {index+1}/{count}',flush=True)
        cap.release()
        if source_edges:raise RuntimeError(f'Source edges need review {name}: {source_edges}')
        tile_height=round(256*H/W)
        review=Image.new('RGB',(5*256,2*tile_height));draw=ImageDraw.Draw(review)
        for row,bg in enumerate(((24,28,34,255),(240,240,240,255))):
            for col,index in enumerate((0,count//4,count//2,count*3//4,count-1)):
                with Image.open(outframes/(steps[index]['frame']+'.png')) as im:
                    tile=Image.new('RGBA',(W,H),bg);tile.alpha_composite(im)
                    review.paste(tile.convert('RGB').resize((256,tile_height)),(col*256,row*tile_height))
        review.save(OUT/(name+'-review.jpg'),quality=94)
        return name,steps,{'source':filename,'fps':fps,'frames':count,'sourceEdgeViolations':source_edges,
            'wholeSourceFrameRetained':True,'faintAlphaThreshold':2,'faintSourceEdgeFrames':faint_source_edges,
            'minimumFaintOutputMargin':min(min(b[0],b[1],W-b[2],H-b[3]) for b in faint),
            'faintBoundsUnion':[min(b[0] for b in faint),min(b[1] for b in faint),max(b[2] for b in faint),max(b[3] for b in faint)],
            'minimumOutputMargin':min(min(b[0],b[1],W-b[2],H-b[3]) for b in bounds),
            'boundsUnion':[min(b[0] for b in bounds),min(b[1] for b in bounds),max(b[2] for b in bounds),max(b[3] for b in bounds)]}
    selected={k:v for k,v in CLIPS.items() if (not args.repair_fire or k in ('fire','firecalm')) and (not args.repair_march or k=='march')}
    with ThreadPoolExecutor(max_workers=3) as pool:results=list(pool.map(extract,selected.items()))
    manifest.update(version='7.3.0',frameSize=[W,H],displayBounds=[*PAD,384,500],alphaPrepared=True)
    if not args.repair_fire and not args.repair_march:manifest['addedEmotionSources']={}
    known=set(manifest['frames'])
    for name,steps,metrics in results:
        manifest['clips'][name]=steps;manifest['frames'] += [s['frame'] for s in steps if s['frame'] not in known]
        manifest['addedEmotionSources'][name]=metrics
    manifest['framing']={'legacyPadding':list(PAD),'newSourceTransform':MATRIX.tolist(),'newSourceReflected':True,
                         'note':'Fixed whole-clip transform; entire raw fire source frame retained, including faint smoke.',
                         'smokeRepairPreviousCanvas':[512,560],'smokeRepairOffset':[64,32]}
    (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    with ZipFile(OUT/'sprites-next.zip','w',ZIP_STORED) as archive:
        archive.writestr('manifest.json',json.dumps(manifest))
        for name in manifest['frames']:archive.write(outframes/(name+'.png'),name+'.png')
    (OUT/'sprites-next.zip').replace(OUT/'sprites.zip')
    print(json.dumps(manifest['addedEmotionSources'],indent=2),flush=True)
    print(f'Pack ready: {len(manifest["frames"])} frames, {W}x{H}',flush=True)

if __name__=='__main__':main()
