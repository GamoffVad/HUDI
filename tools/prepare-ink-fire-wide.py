"""Replace firetrick from the wide, mouthless regeneration; preserve other clips."""
from pathlib import Path
from zipfile import ZipFile,ZIP_STORED
from concurrent.futures import ThreadPoolExecutor
import runpy,cv2,numpy as np,json,sys,io
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/ink-pet-v2.5';FRAMES=OUT/'frames';QA=OUT/'qa'
SOURCE=ROOT/'assets/pets/ink-restart/2026-11-08-fire-wide/fire-wide.mp4'
M=runpy.run_path(str(ROOT/'tools/prepare-ink-extra-reactions.py'))
TRANSFORM=np.float32([[1.25,0,-110],[0,1.25,-44]])

def body_bounds(p):
    y,x=np.where(p[:,:,3]>200)
    return [int(x.min()),int(y.min()),int(x.max()+1),int(y.max()+1)]

def register_scale(bgr):
    global TRANSFORM
    with ZipFile(ROOT/'output/ink-pet-v2.4/Ink.pet') as z:
        neutral=np.array(Image.open(io.BytesIO(z.read('neutral.png'))))
    source,_=M['matte'](bgr,True);s=body_bounds(source);t=body_bounds(neutral)
    # Match full standing height and the centre of the boot baseline to the
    # existing neutral pose. One uniform transform for ALL frames, including
    # crouches and raised feet; never normalize individual moving silhouettes.
    scale=(t[3]-t[1]-1)/(s[3]-s[1]-1)
    tx=(t[0]+t[2]-1)/2-scale*(s[0]+s[2]-1)/2
    ty=t[3]-1-scale*(s[3]-1)
    TRANSFORM=np.float32([[scale,0,tx],[0,scale,ty]])
    return dict(source=s,target=t,transform=TRANSFORM.tolist(),uniform_scale=scale)

def remove_filter_spill(p):
    # Cubic interpolation can overshoot a neutral source edge by a few channel
    # values. Cap only green dominance; alpha and the warm flame stay untouched.
    rgb=p[:,:,:3].astype(np.int16);limit=np.maximum(rgb[:,:,0],rgb[:,:,2])+3
    p[:,:,1]=np.minimum(rgb[:,:,1],limit).astype(np.uint8)
    return p

def measure(i,p,core_holes=0,bgr=None):
    r,g,b=p[:,:,:3].astype(float).transpose(2,0,1);a=p[:,:,3]/255;yy,xx=np.where(a>.04)
    # Detect ADDED contamination against the actual source. Pink/red fire
    # reflections on opaque boots must not be mistaken for a keying defect.
    score=np.maximum(np.minimum(r,b)-g,0)*a
    assert bgr is not None
    original=cv2.warpAffine(cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB).astype(np.float32),TRANSFORM,(960,960),flags=cv2.INTER_CUBIC)
    sr,sg,sb=original.transpose(2,0,1);source_score=np.maximum(np.minimum(sr,sb)-sg,0)
    added=(score>40)&(score>source_score+25)
    return dict(index=i,bounds=[int(xx.min()),int(yy.min()),int(xx.max()+1),int(yy.max()+1)],green=int(((g>np.maximum(r,b)+8)&(a>.04)).sum()),magenta=int(added.sum()),source_coloured_highlights=int(((score>40)&~added).sum()),core_holes=core_holes)

def frame(item):
    i,bgr=item;p,core=M['matte'](bgr,True);core_holes=int((core&(p[:,:,3]<250)).sum())
    # One fixed transform reverses the additional reference padding. No per-frame
    # auto-crop; premultiplied filtering retains clean transparent boundaries.
    pm=p.astype(np.float32)/255;pm[:,:,:3]*=pm[:,:,3:4]
    pm=cv2.warpAffine(pm,TRANSFORM,(960,960),flags=cv2.INTER_CUBIC)
    pm[:,:,3]=np.clip(pm[:,:,3],0,1);pm[:,:,:3]=np.clip(pm[:,:,:3],0,pm[:,:,3:4])
    p=remove_filter_spill(M['MAT']['straight'](pm));Image.fromarray(p).save(FRAMES/f'firetrick-{i:03}.png',compress_level=3)
    return measure(i,p,core_holes,bgr)

def main():
    FRAMES.mkdir(parents=True,exist_ok=True);QA.mkdir(exist_ok=True)
    cap=cv2.VideoCapture(str(SOURCE));n=int(cap.get(7));fps=cap.get(5);assert n==241 and fps==24
    ok,first=cap.read();assert ok;registration=register_scale(first);cap.set(cv2.CAP_PROP_POS_FRAMES,0)
    (QA/'registration.json').write_text(json.dumps(registration,indent=2))
    def source():
        for i in range(n):
            ok,b=cap.read();assert ok;yield i,b
    rows=[]
    if '--package-existing' in sys.argv:
        prior=json.loads((QA/'frame-audit.json').read_text())
        for i,bgr in source():rows.append(measure(i,np.array(Image.open(FRAMES/f'firetrick-{i:03}.png')),prior[i]['core_holes'],bgr))
    else:
        with ThreadPoolExecutor(max_workers=3) as pool:
            for row in pool.map(frame,source()):
                rows.append(row)
                if len(rows)%48==0:print(len(rows),'/',n,flush=True)
    cap.release()
    (QA/'frame-audit.json').write_text(json.dumps(rows,indent=2))
    start=body_bounds(np.array(Image.open(FRAMES/'firetrick-000.png')))
    end=body_bounds(np.array(Image.open(FRAMES/'firetrick-240.png')))
    target=registration['target'];height=lambda b:b[3]-b[1]
    registration.update(start=start,end=end,start_height_error=height(start)-height(target),end_height_error=height(end)-height(target))
    (QA/'registration.json').write_text(json.dumps(registration,indent=2))
    assert abs(height(start)-height(target))<=2 and abs(height(end)-height(target))<=2,registration
    assert abs(start[3]-target[3])<=2 and abs(end[3]-target[3])<=2,registration
    assert all(r['green']==0 and r['magenta']==0 and r['core_holes']==0 for r in rows)
    margins=np.array([[r['bounds'][0],r['bounds'][1],960-r['bounds'][2],960-r['bounds'][3]] for r in rows]).min(0).tolist()
    assert min(margins)>=40,margins
    with ZipFile(ROOT/'output/ink-pet-v2.4/Ink.pet') as old,ZipFile(OUT/'Ink.pet','w',ZIP_STORED) as z:
        manifest=json.loads(old.read('manifest.json'));manifest['matteVersion']='fresh-trimap-v4+warm-only-fire-v2+wide-fire-registered-scale'
        z.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False));unchanged=0
        for e in old.infolist():
            if e.filename=='manifest.json':continue
            if e.filename.startswith('firetrick-'):z.write(FRAMES/e.filename,e.filename)
            else:z.writestr(e,old.read(e));unchanged+=1
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
    report=dict(frames=n,other_frames_copied_unchanged=unchanged,minimum_margins_ltrb=margins,green_pixels=sum(r['green'] for r in rows),strong_magenta_pixels=sum(r['magenta'] for r in rows),records=rows)
    (QA/'report.json').write_text(json.dumps(report,indent=2),encoding='utf8')
    picks=[0,70,90,110,125,145,165,180,210,240];sheet=Image.new('RGB',(960,344*len(picks)))
    for row,i in enumerate(picks):
        p=Image.open(FRAMES/f'firetrick-{i:03}.png')
        for col,bg in enumerate([(24,24,24),(245,245,245),(185,45,155)]):
            im=Image.new('RGBA',p.size,bg+(255,));im.alpha_composite(p);im=im.convert('RGB').resize((320,320),Image.Resampling.LANCZOS)
            sheet.paste(im,(320*col,344*row));ImageDraw.Draw(sheet).text((320*col+6,344*row+322),f'frame {i}',fill='white')
    sheet.save(QA/'review.jpg',quality=95)
    print({k:v for k,v in report.items() if k!='records'},flush=True)
if __name__=='__main__':main()
