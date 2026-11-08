"""Re-key the fire clip only; retain all other installed PNG entries byte-for-byte."""
from pathlib import Path
from zipfile import ZipFile,ZIP_STORED
from concurrent.futures import ThreadPoolExecutor,wait,FIRST_COMPLETED
import runpy,cv2,numpy as np,json,io
from PIL import Image
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/ink-pet-v2.3';FRAMES=OUT/'frames';QA=OUT/'qa'
M=runpy.run_path(str(ROOT/'tools/prepare-ink-extra-reactions.py'))
def score(p):
    r,g,b=p[:,:,:3].astype(float).transpose(2,0,1);a=p[:,:,3]/255
    v=np.maximum(np.minimum(r,b)-g,0)*a
    return {'max':float(v.max()),'energy':float(v.sum()),'strong_pixels':int((v>40).sum())}
def process(i,bgr):
    p,core=M['matte'](bgr,True);name=f'firetrick-{i:03}.png';Image.fromarray(p).save(FRAMES/name,compress_level=3)
    old=np.array(Image.open(ROOT/'output/ink-pet-v2.2/frames'/name))
    before=score(old);after=score(p)
    a=p[:,:,3];r,g,b=p[:,:,:3].astype(int).transpose(2,0,1)
    yy,xx=np.where(a>64)
    return dict(frame=name,before=before,after=after,green_pixels=int(((g>np.maximum(r,b)+8)&(a>10)).sum()),core_holes=int((core&(a<250)).sum()),bounds=[int(xx.min()),int(yy.min()),int(xx.max()+1),int(yy.max()+1)])
def main():
    FRAMES.mkdir(parents=True,exist_ok=True);QA.mkdir(exist_ok=True)
    cap=cv2.VideoCapture(str(M['SOURCE']/'snap-fire-innocent-fixed.mp4'));count=int(cap.get(7));rows=[];pending=set()
    with ThreadPoolExecutor(max_workers=4) as pool:
        for i in range(count):
            ok,b=cap.read();assert ok;pending.add(pool.submit(process,i,b))
            if len(pending)>=6:
                done,pending=wait(pending,return_when=FIRST_COMPLETED);rows.extend(f.result() for f in done)
            if i%48==0:print(i+1,'/',count,flush=True)
        rows.extend(f.result() for f in pending)
    cap.release();rows.sort(key=lambda x:x['frame'])
    assert all(r['green_pixels']==0 and r['core_holes']==0 and r['after']['strong_pixels']==0 for r in rows)
    assert all(min(r['bounds'][0],r['bounds'][1],960-r['bounds'][2],960-r['bounds'][3])>=2 for r in rows)
    with ZipFile(ROOT/'output/ink-pet-v2.2/Ink.pet') as old,ZipFile(OUT/'Ink.pet','w',ZIP_STORED) as z:
        manifest=json.loads(old.read('manifest.json'));manifest['matteVersion']='fresh-trimap-v4-native-edges+warm-only-fire-v2'
        z.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False))
        unchanged=0
        for entry in old.infolist():
            if entry.filename=='manifest.json':continue
            if entry.filename.startswith('firetrick-'):z.write(FRAMES/entry.filename,entry.filename)
            else:z.writestr(entry,old.read(entry));unchanged+=1
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
    report=dict(frames=count,other_frames_copied_unchanged=unchanged,geometry_unchanged=True,clip_timing_unchanged=True,before_strong_pixels=sum(r['before']['strong_pixels'] for r in rows),after_strong_pixels=sum(r['after']['strong_pixels'] for r in rows),records=rows)
    (QA/'colour-audit.json').write_text(json.dumps(report,indent=2),encoding='utf8');print({k:v for k,v in report.items() if k!='records'},flush=True)
if __name__=='__main__':main()
