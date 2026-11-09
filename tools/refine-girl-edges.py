"""Local RGB decontamination for this dark-haired doll; preserve original alpha."""
from pathlib import Path
import json,sys
import cv2,numpy as np
from PIL import Image,ImageDraw

def refine(rgba):
    rgb=rgba[:,:,:3].astype(np.float32);a=rgba[:,:,3].astype(np.float32)/255
    # Local opaque hair colour, not nearest-neighbour propagation (which makes
    # radial stripes). Higher-alpha samples suppress unstable colour at tips.
    dark=(rgb.max(2)<165)&((rgb.max(2)-rgb.min(2))<50)
    seed=np.clip((a-.85)/.15,0,1)**3*dark
    den=cv2.GaussianBlur(seed,(0,0),3)
    col=cv2.GaussianBlur(rgb*seed[:,:,None],(0,0),3)/np.maximum(den[:,:,None],1e-5)
    fallback_weight=a*a*dark
    fd=cv2.GaussianBlur(fallback_weight,(0,0),2)
    fallback=cv2.GaussianBlur(rgb*fallback_weight[:,:,None],(0,0),2)/np.maximum(fd[:,:,None],1e-5)
    col=np.where((den>.008)[:,:,None],col,fallback)
    green=rgb[:,:,1]-(rgb[:,:,0]+rgb[:,:,2])*.5
    # The subject has brown-black hair. Neutralise olive/cyan contamination,
    # including G==R or G==B; max(R,B) clipping alone leaves coloured fringes.
    gray=(col[:,:,0]+col[:,:,2])*.5
    contaminated=col[:,:,1]>gray
    col[:,:,1]=np.minimum(col[:,:,1],gray)
    col=np.where(contaminated[:,:,None],gray[:,:,None],col)
    neutral_fringe=((rgb.max(2)-rgb.min(2))<55)&(den>.008)&(col.max(2)<110)
    eligible=dark|neutral_fringe
    amount=np.clip((.995-a)/.30,0,1)*eligible*(fd>.002)
    out=rgb*(1-amount[:,:,None])+col*amount[:,:,None]
    remaining_green=out[:,:,1]-(out[:,:,0]+out[:,:,2])*.5
    neutralize=eligible&(a<.995)&(a>0)&(remaining_green>.5)
    edge_gray=(out[:,:,0]+out[:,:,2])*.5
    out=np.where(neutralize[:,:,None],edge_gray[:,:,None],out)
    # Preserve alpha byte-for-byte: no silhouette erosion or broad blur.
    result=rgba.copy();result[:,:,:3]=np.uint8(np.clip(out.round(),0,255));result[result[:,:,3]==0,:3]=0
    return result

if __name__=='__main__':
    root=Path('D:/PR/HUDI/output/glasses-girl-v1');out=Path('D:/PR/HUDI/output/glasses-girl-v2');out.mkdir(exist_ok=True)
    if '--package' in sys.argv:
        from concurrent.futures import ThreadPoolExecutor
        from zipfile import ZipFile,ZIP_STORED
        cv2.setNumThreads(1)
        manifest=json.loads((root/'manifest.json').read_text(encoding='utf-8'))
        (out/'frames').mkdir(exist_ok=True)
        def process(fid):
            old=np.array(Image.open(root/'frames'/(fid+'.png')).convert('RGBA'));new=refine(old)
            assert np.array_equal(old[:,:,3],new[:,:,3])
            assert np.array_equal(old[old[:,:,3]==255],new[old[:,:,3]==255])
            Image.fromarray(new).save(out/'frames'/(fid+'.png'))
            return int(np.any(old!=new,axis=2).sum())
        counts=[]
        with ThreadPoolExecutor(max_workers=3) as pool:
            for i,n in enumerate(pool.map(process,manifest['frames'])):
                counts.append(n)
                if i%150==0:print('refined',i+1,'/',len(manifest['frames']),flush=True)
        (out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
        with ZipFile(out/'glasses-girl.pet','w',ZIP_STORED) as z:
            z.write(out/'manifest.json','manifest.json')
            for fid in manifest['frames']:z.write(out/'frames'/(fid+'.png'),fid+'.png')
        report={'frames':len(counts),'changedRgbPixels':sum(counts),'alphaPreservedExactly':True,'opaquePixelsPreservedExactly':True,'sourcePackage':str(root/'glasses-girl.pet'),'outputPackage':str(out/'glasses-girl.pet'),'method':'Local weighted foreground colour reconstruction at translucent dark edges; neutralise olive/cyan and unstable bright fringe. No alpha erosion or blurring.'}
        (out/'edge-refinement-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report),flush=True)
        sys.exit(0)
    for name in ['neutral','dance-0144','groove-0120','adjustglasses-0120']:
        old=np.array(Image.open(root/'frames'/(name+'.png')).convert('RGBA'));new=refine(old)
        Image.fromarray(new).save(out/(name+'-sample.png'))
        box=(350,585,650,785) if name=='neutral' else (240,400,760,800)
        w=(box[2]-box[0])*2;h=(box[3]-box[1])*2;sheet=Image.new('RGB',(w*2,h*2))
        for row,bgcol in enumerate([(20,20,20,255),(240,240,240,255)]):
            for column,arr in enumerate([old,new]):
                bg=Image.new('RGBA',(960,960),bgcol);bg.alpha_composite(Image.fromarray(arr));sheet.paste(bg.crop(box).resize((w,h),Image.Resampling.NEAREST).convert('RGB'),(column*w,row*h))
        sheet.save(out/(name+'-comparison.png'))
    print('Four edge comparisons written.')
