from pathlib import Path
import cv2,numpy as np,json,runpy
from PIL import Image,ImageDraw
P=Path(__file__).resolve().parent
m=runpy.run_path(str(P.parents[3]/'tools/prepare-ink-pet-fresh.py'))
cap=cv2.VideoCapture(str(P/'ink-loop.mp4'));fps=cap.get(cv2.CAP_PROP_FPS);n=int(cap.get(cv2.CAP_PROP_FRAME_COUNT));w=int(cap.get(3));h=int(cap.get(4))
picks=set(np.linspace(0,n-1,16,dtype=int).tolist());tiles=[];rows=[];first=None;last=None;prev=None;diff=[]
for i in range(n):
 ok,bgr=cap.read()
 if not ok:break
 rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB);f=rgb.astype(np.float32);r,g,b=f.transpose(2,0,1)
 fg=~((g>np.maximum(r,b)+10)&(g>70));nc,labels,stats,_=cv2.connectedComponentsWithStats(fg.astype(np.uint8),8);k=1+np.argmax(stats[1:,4]);x,y,bw,bh,area=stats[k];rows.append([i,int(x),int(y),int(w-x-bw),int(h-y-bh)])
 if first is None:first=f
 if prev is not None:diff.append(float(np.abs(f-prev).mean()))
 prev=f;last=f
 if i in picks:
  pm=m['fresh_matte'](bgr);bg=np.array([210,70,160]);arr=np.uint8(np.clip(pm[:,:,:3]*255+(1-pm[:,:,3:4])*bg,0,255));im=Image.fromarray(arr);im.thumbnail((320,320));d=ImageDraw.Draw(im);d.text((8,8),f'{i/fps:.2f}s',fill='white');tiles.append(im)
cap.release()
sheet=Image.new('RGB',(1280,1280),(30,30,30))
for i,im in enumerate(tiles):sheet.paste(im,((i%4)*320,(i//4)*320))
sheet.save(P/'contact-sheet.jpg')
report={'decoded_frames':len(rows),'expected_frames':n,'fps':fps,'size':[w,h],'minimum_margins_ltrb':np.array(rows)[:,1:].min(axis=0).tolist(),'loop_fullframe_mae':float(np.abs(first-last).mean()),'adjacent_mae_median':float(np.median(diff)),'adjacent_mae_max':float(max(diff)),'frame_bounds_ltrb':rows,'note':'Frame-wide bounds checked on every decoded frame; contact sheet is sampled matting, not approval for installing.'}
(P/'qa.json').write_text(json.dumps(report,indent=2),encoding='utf8');print(json.dumps({k:v for k,v in report.items() if k!='frame_bounds_ltrb'}))
