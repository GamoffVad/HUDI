"""Rebuild current doll cutouts from original videos, preserving previous exports."""
import sys,json,importlib.util,shutil
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import cv2,numpy as np
from PIL import Image,ImageDraw
from edge_matte_v2 import matte
cv2.setNumThreads(1)
ROOT=Path(__file__).parent
CASES=['curious-stroll-v1','glasses-hair-v1','surprise-v3-kling','angry-v3']

def review(a,path):
 im=Image.fromarray(a);b=im.getbbox();box=(max(0,b[0]-12),max(0,b[1]-12),min(im.width,b[2]+12),min(im.height,b[3]+12));im=im.crop(box)
 out=Image.new('RGB',(im.width*3,im.height))
 for j,c in enumerate([(100,100,100,255),(245,245,245,255),(25,25,25,255)]):
  bg=Image.new('RGBA',im.size,c);bg.alpha_composite(im);out.paste(bg.convert('RGB'),(j*im.width,0))
 out.save(path)

def run(name):
 src=ROOT/'animations'/name;out=src/'recut-v3';(out/'frames').mkdir(parents=True,exist_ok=True)
 cap=cv2.VideoCapture(str(src/'source.mp4'));fps=cap.get(cv2.CAP_PROP_FPS);n=int(cap.get(cv2.CAP_PROP_FRAME_COUNT));w=int(cap.get(3));h=int(cap.get(4))
 pool=ThreadPoolExecutor(max_workers=3);pending=[]
 while True:
  ok,f=cap.read()
  if not ok:break
  pending.append(pool.submit(matte,f))
 cap.release();rows=[]
 for i,future in enumerate(pending):
  a=future.result();im=Image.fromarray(a);im.save(out/'frames'/f'{i:04d}.png',compress_level=3)
  b=im.getbbox();rows.append({'frame':i,'bounds':b})
  if i%24==0:
   review(a,out/f'frame-{i:04d}-review.png');print(name,'keyed',i+1,'/',n,flush=True)
 pool.shutdown()
 assert len(rows)==n
 audit={'frames':n,'fps':fps,'size':[w,h],'method':'foreground-guided green-channel alpha; restore dark edge pixels; clean foreground colour extension; narrow 0.6px antialias, no broad feather; separate original contact shadow','allInside':all(b['bounds'][0]>0 and b['bounds'][1]>0 and b['bounds'][2]<w and b['bounds'][3]<h for b in rows),'bounds':rows}
 (out/'matte-audit.json').write_text(json.dumps(audit,indent=2))
 meta={'entryFrames':97 if name=='surprise-v3-kling' else n,'sourceFile':str(src/'source.mp4'),'size':[w,h],'fps':fps,'recutVersion':3}
 if (src/'source-meta.json').exists():meta.update(json.loads((src/'source-meta.json').read_text()));meta['entryFrames']=97 if name=='surprise-v3-kling' else n
 (out/'source-meta.json').write_text(json.dumps(meta,indent=2))
 template=ROOT/'animations/curious-stroll-v1/export_cycle.py';code=template.read_text()
 code=code.replace('stroll-loop-preview.webp','loop-preview.webp').replace('stroll-transparent-frames.zip','transparent-frames.zip')
 if name=='surprise-v3-kling':
  code=code.replace("one continuous generated forward shot; no reverse playback","two generated forward transitions; no reverse playback").replace("6 frames at loop boundary in premultiplied RGBA; no middle edit","5 frames at middle, 6 at loop boundary in premultiplied RGBA")
 (out/'export_cycle.py').write_text(code)
 spec=importlib.util.spec_from_file_location('export_'+name.replace('-','_'),out/'export_cycle.py');mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod);mod.main()
 print(name,'COMPLETE',flush=True)
 return {'name':name,'frames':n,'allInside':audit['allInside'],'output':str(out)}

if __name__=='__main__':
 selected=sys.argv[1:] or CASES;reports=[]
 for name in selected:reports.append(run(name))
 (ROOT/'recut-v3-report.json').write_text(json.dumps(reports,indent=2));print('All completed',flush=True)
