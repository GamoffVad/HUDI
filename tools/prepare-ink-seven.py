"""Build Ink's seven new full-frame animations, never using legacy PNG alpha."""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor,wait,FIRST_COMPLETED
from zipfile import ZipFile,ZIP_STORED
import cv2,numpy as np,json,runpy,shutil,hashlib,sys
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1]
NATIVE_EDGES='--native-edges' in sys.argv
OUT=ROOT/('output/ink-pet-v2.1' if NATIVE_EDGES else 'output/ink-pet-v2');FRAMES=OUT/'frames';QA=OUT/'qa';SIZE=960 if NATIVE_EDGES else 512
SOURCE=ROOT/'assets/pets/ink-restart'
SPECS=[('hood',SOURCE/'2026-11-08-loop/ink-loop.mp4'),('lookaround',SOURCE/'2026-11-08-six/look-around.mp4'),('surprise',SOURCE/'2026-11-08-six/surprise-v2.mp4'),('temple',SOURCE/'2026-11-08-six/temple-v2.mp4'),('skeptical',SOURCE/'2026-11-08-six/skeptical.mp4'),('shrug',SOURCE/'2026-11-08-six/shrug.mp4'),('dance',SOURCE/'2026-11-08-six/dance.mp4')]
MAT=runpy.run_path(str(ROOT/'tools/prepare-ink-pet-fresh.py'))
cv2.setNumThreads(1)

def process(name,index,bgr):
 pm=MAT['fresh_matte'](bgr)
 rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB).astype(np.float32)
 neutral=(rgb.max(2)-rgb.min(2)<18)&(rgb.max(2)<110)
 core=cv2.erode(neutral.astype(np.uint8),np.ones((5,5),np.uint8)).astype(bool)
 # A 5x5 neighbourhood of dark neutral material cannot be the saturated
 # chroma plate. Preserve it as opaque rather than weakening fabric pixels.
 clean=rgb.copy();r,g,b=clean.transpose(2,0,1);spill=g>np.maximum(r,b)+3
 clean[:,:,1][spill]=((r+b)*.5)[spill]
 pm[core,:3]=clean[core]/255;pm[core,3]=1
 if NATIVE_EDGES:
  # Preserve the 50% silhouette contour; narrow only the alpha transition.
  # Ink has opaque cloth, no translucent smoke or water in these seven clips.
  old=pm[:,:,3].copy();t=np.clip((old-.08)/.84,0,1);alpha=t*t*(3-2*t)
  scale=np.divide(alpha,old,out=np.zeros_like(alpha),where=old>1e-6)
  pm[:,:,:3]*=scale[:,:,None];pm[:,:,3]=alpha
 holes=int((core&(pm[:,:,3]<.98)).sum())
 opaque=pm[:,:,3]>.1;ys,xs=np.where(opaque);source_box=[int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1)]
 small=pm if NATIVE_EDGES else cv2.resize(pm,(SIZE,SIZE),interpolation=cv2.INTER_AREA);pixels=MAT['straight'](small)
 ident=f'{name}-{index:03}';Image.fromarray(pixels).save(FRAMES/(ident+'.png'),compress_level=3)
 r,g,b=pixels[:,:,:3].astype(np.int16).transpose(2,0,1);a=pixels[:,:,3];green=((g>np.maximum(r,b)+8)&(a>10));yy,xx=np.where(a>5)
 return {'index':index,'frame':ident,'source_bounds':source_box,'bounds':[int(xx.min()),int(yy.min()),int(xx.max()+1),int(yy.max()+1)],'green_pixels':int(green.sum()),'opaque_core_holes':holes}

def sheet(name,ids,suffix):
 out=Image.new('RGB',(SIZE*3,(SIZE+24)*len(ids)),(45,45,45));draw=ImageDraw.Draw(out)
 for row,ident in enumerate(ids):
  p=np.array(Image.open(FRAMES/(ident+'.png')));a=p[:,:,3:4]/255
  for col,bg in enumerate([(245,245,245),(20,20,20),(200,40,170)]):
   im=Image.fromarray(np.uint8(np.clip(p[:,:,:3]*a+np.array(bg)*(1-a),0,255)));out.paste(im,(col*SIZE,row*(SIZE+24)));draw.text((col*SIZE+5,row*(SIZE+24)+SIZE+3),ident,fill='white')
 out.save(QA/(name+'-'+suffix+'.jpg'))

def main():
 FRAMES.mkdir(parents=True,exist_ok=True);QA.mkdir(parents=True,exist_ok=True)
 sample='--sample' in sys.argv;clips={};records={};sources={}
 with ThreadPoolExecutor(max_workers=4) as pool:
  for name,path in SPECS:
   cap=cv2.VideoCapture(str(path));fps=cap.get(cv2.CAP_PROP_FPS);count=int(cap.get(cv2.CAP_PROP_FRAME_COUNT));assert (int(cap.get(3)),int(cap.get(4)))==(960,960)
   indices=set([0,60,96,150,192]) if sample else set(range(count));pending=set();rows=[]
   for i in range(count):
    ok,bgr=cap.read();assert ok,(name,i)
    if i not in indices:continue
    pending.add(pool.submit(process,name,i,bgr))
    if len(pending)>=8:
     done,pending=wait(pending,return_when=FIRST_COMPLETED);rows.extend(f.result() for f in done)
    if i%48==0:print(f'{name}: {i+1}/{count}',flush=True)
   rows.extend(f.result() for f in pending);cap.release();rows.sort(key=lambda x:x['index']);records[name]=rows
   chosen=[rows[0]['frame'],rows[len(rows)//2]['frame'],max(rows,key=lambda x:(x['opaque_core_holes'],x['green_pixels']))['frame']];sheet(name,chosen,'sample' if sample else 'review')
   if not sample:
    clips[name]=[{'frame':r['frame'],'seconds':1/fps} for r in rows]
    sources[name]={'path':str(path.relative_to(ROOT)),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'frames':count,'fps':fps}
   (QA/(name+'-frames.json')).write_text(json.dumps(rows),encoding='utf8')
   print(f'{name} done: green={sum(r["green_pixels"] for r in rows)}, core holes={sum(r["opaque_core_holes"] for r in rows)}',flush=True)
 if sample:return
 allrows=[r for rows in records.values() for r in rows]
 clipping=[r['frame'] for r in allrows if min(r['bounds'][0],r['bounds'][1],SIZE-r['bounds'][2],SIZE-r['bounds'][3])<2]
 holes=[r for r in allrows if r['opaque_core_holes']>0]
 if clipping:raise RuntimeError('Clipped output '+str(clipping))
 shutil.copyfile(FRAMES/'hood-000.png',FRAMES/'neutral.png')
 p=np.array(Image.open(FRAMES/'neutral.png'));yy,xx=np.where(p[:,:,3]>10);bounds=[int(xx.min()),int(yy.min()),int(xx.max()-xx.min()+1),int(yy.max()-yy.min()+1)]
 ids=['neutral']+[r['frame'] for r in allrows]
 if NATIVE_EDGES:
  # Match the previous display geometry exactly, even at fractional source pixels.
  previous=json.loads((ROOT/'output/ink-pet-v2/manifest.json').read_text(encoding='utf8'))
  bounds=[v*SIZE/previous['frameSize'][0] for v in previous['displayBounds']]
 manifest={'version':'pet-1','name':'Инк','id':'ink-v2','frameSize':[SIZE,SIZE],'displayBounds':bounds,'alphaPrepared':True,'breathAmplitude':0,'horizontalMirrored':False,'neutral':'neutral','frames':ids,'clips':clips,'matteVersion':MAT['VERSION']+('-native-edges' if NATIVE_EDGES else ''),'description':'Seven newly generated animations. Temple gesture retains the accepted drawn circle from the source video.'}
 (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
 with ZipFile(OUT/'Ink.pet','w',ZIP_STORED) as z:
  z.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False))
  for ident in ids:z.write(FRAMES/(ident+'.png'),ident+'.png')
 report={'frames':len(ids),'clips':list(clips),'sources':sources,'displayBounds':bounds,'old_alpha_used':False,'clipping':clipping,'green_pixels':sum(r['green_pixels'] for r in allrows),'opaque_core_hole_frames':holes,'known_source_artifact':'Temple gesture contains drawn circle; user requested integration after disclosure.'}
 (QA/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8');print(json.dumps({k:v for k,v in report.items() if k not in ('sources','opaque_core_hole_frames')}),flush=True)
if __name__=='__main__':main()
