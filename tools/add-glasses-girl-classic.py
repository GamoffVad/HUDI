"""Add the explicitly requested intact early surprise clip to the existing pet."""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from zipfile import ZipFile,ZIP_STORED
import sys,json,shutil,importlib.util,hashlib
import cv2,numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[1];OLD=ROOT/'output/glasses-girl-v2';OUT=ROOT/'output/glasses-girl-v3'
SOURCE_ROOT=Path(sys.argv[1]) if len(sys.argv)>1 else ROOT/'pet-sources/glasses-girl'
SOURCE=SOURCE_ROOT/'animations/surprise-v1/source.mp4'
sys.path.insert(0,str(SOURCE_ROOT));from edge_matte_v2 import matte
sp=importlib.util.spec_from_file_location('refine',Path(__file__).with_name('refine-girl-edges.py'));mod=importlib.util.module_from_spec(sp);sp.loader.exec_module(mod)
cv2.setNumThreads(1)

def blend(a,b,t):
    aa=a[:,:,3:4].astype(np.float32)/255;bb=b[:,:,3:4].astype(np.float32)/255
    al=aa*(1-t)+bb*t;pm=a[:,:,:3]*aa*(1-t)+b[:,:,:3]*bb*t
    rgb=np.divide(pm,al,out=np.zeros_like(pm),where=al>1e-6)
    return np.uint8(np.clip(np.concatenate([rgb,al*255],2).round(),0,255))

def bounds(a):
    y,x=np.where(a[:,:,3]>200);return [int(x.min()),int(y.min()),int(x.max()+1),int(y.max()+1)]

def main():
    OUT.mkdir(exist_ok=True);shutil.copytree(OLD/'frames',OUT/'frames',dirs_exist_ok=True)
    manifest=json.loads((OLD/'manifest.json').read_text(encoding='utf-8'));neutral=np.array(Image.open(OLD/'frames/neutral.png').convert('RGBA'))
    cap=cv2.VideoCapture(str(SOURCE));fps=cap.get(5);n=int(cap.get(7));ok,f=cap.read();assert ok
    first=matte(f);b=bounds(first);target=bounds(neutral)
    scale=(target[3]-target[1])/(b[3]-b[1]);tx=(target[0]+target[2])/2-scale*(b[0]+b[2])/2;ty=target[3]-scale*b[3]
    matrix=np.float32([[scale,0,tx],[0,scale,ty]]);cap.set(cv2.CAP_PROP_POS_FRAMES,0)
    def work(pair):
        i,frame=pair
        if i==0 or i==n-1:return i,'neutral',None
        a=matte(frame);alpha=a[:,:,3:4].astype(np.float32)/255
        pm=np.concatenate([a[:,:,:3]*alpha,alpha],2);z=cv2.warpAffine(pm,matrix,(960,960),flags=cv2.INTER_LANCZOS4)
        al=np.clip(z[:,:,3:4],0,1);rgb=np.divide(z[:,:,:3],al,out=np.zeros_like(z[:,:,:3]),where=al>.002)
        a=np.uint8(np.clip(np.concatenate([rgb,al*255],2).round(),0,255));a=mod.refine(a)
        if i<5:a=blend(neutral,a,i/5)
        if i>=n-5:a=blend(a,neutral,(i-(n-5))/4)
        a[a[:,:,3]==0,:3]=0;fid=f'surpriseclassic-{i:04d}';im=Image.fromarray(a);im.save(OUT/'frames'/(fid+'.png'))
        return i,fid,im.getbbox()
    rows=[]
    with ThreadPoolExecutor(max_workers=2) as pool:
        pending=[];i=0
        while True:
            ok,f=cap.read()
            if not ok:break
            pending.append(pool.submit(work,(i,f)));i+=1
            if len(pending)>=4:rows.append(pending.pop(0).result())
        rows.extend(t.result() for t in pending)
    cap.release();assert len(rows)==n
    for _,fid,box in rows:
        if fid!='neutral':
            assert box and min(box[:2])>0 and max(box[2:])<960,(fid,box)
            manifest['frames'].append(fid)
    manifest['clips']['surpriseclassic']=[{'frame':fid,'seconds':1/fps} for _,fid,_ in rows]
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    package=OUT/'glasses-girl.pet'
    with ZipFile(package,'w',ZIP_STORED) as z:
        z.write(OUT/'manifest.json','manifest.json')
        for fid in manifest['frames']:z.write(OUT/'frames'/(fid+'.png'),fid+'.png')
    assert package.stat().st_size<512*1024**2
    report={'actions':list(manifest['clips']),'addedSourceJob':'2411f5b9-7496-4a3d-bdb0-97a400dc8322','addedSourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),'framesAdded':n-2,'sharedEndpoints':True,'scale':scale,'translation':[tx,ty],'sourceResolution':[1920,1080],'packageResolution':[960,960],'excludedClippedJob':'23b63920-ce8c-4a0d-9f14-6742dfeef128','exclusionConfirmedByUser':True,'newGenerationCredits':0}
    (OUT/'build-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report),flush=True)

if __name__=='__main__':main()
