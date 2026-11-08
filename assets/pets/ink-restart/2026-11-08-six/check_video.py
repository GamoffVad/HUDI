from pathlib import Path
import cv2,numpy as np,json,runpy,sys
from PIL import Image,ImageDraw
P=Path(__file__).resolve().parent
m=runpy.run_path(str(P.parents[3]/'tools/prepare-ink-pet-fresh.py'))
name=sys.argv[1];out=P/name;out.mkdir(exist_ok=True)
cap=cv2.VideoCapture(str(P/(name+'.mp4')));fps=cap.get(cv2.CAP_PROP_FPS);n=int(cap.get(cv2.CAP_PROP_FRAME_COUNT));w=int(cap.get(3));h=int(cap.get(4))
assert n>0, name
picks=set(np.linspace(0,n-1,16,dtype=int).tolist());tiles=[];rows=[];first=None;last=None;prev=None;diff=[];plate=[]
for i in range(n):
 ok,bgr=cap.read()
 if not ok:break
 rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB);f=rgb.astype(np.float32);r,g,b=f.transpose(2,0,1)
 fg=~((g>np.maximum(r,b)+10)&(g>70));nc,labels,stats,_=cv2.connectedComponentsWithStats(fg.astype(np.uint8),8)
 # Include every substantial piece, not only the largest component.
 valid=np.where(stats[1:,4]>=16)[0]+1;ss=stats[valid];x=ss[:,0].min();y=ss[:,1].min();right=(ss[:,0]+ss[:,2]).max();bottom=(ss[:,1]+ss[:,3]).max();rows.append([i,int(x),int(y),int(w-right),int(h-bottom)])
 border=np.concatenate((f[:24].reshape(-1,3),f[-24:].reshape(-1,3),f[:, :24].reshape(-1,3),f[:,-24:].reshape(-1,3)));plate.append(float(np.std(border,axis=0).max()))
 if first is None:first=f
 if prev is not None:diff.append(float(np.abs(f-prev).mean()))
 prev=f;last=f
 if i in picks:
  pm=m['fresh_matte'](bgr);bg=np.array([210,70,160]);arr=np.uint8(np.clip(pm[:,:,:3]*255+(1-pm[:,:,3:4])*bg,0,255));im=Image.fromarray(arr);im.thumbnail((320,320));d=ImageDraw.Draw(im);d.text((8,8),f'{i/fps:.2f}s',fill='white');tiles.append(im)
cap.release();sheet=Image.new('RGB',(1280,1280),(30,30,30))
for i,im in enumerate(tiles):sheet.paste(im,((i%4)*320,(i//4)*320))
sheet.save(out/'contact-sheet.jpg')
mask=(first[:,:,1]<np.maximum(first[:,:,0],first[:,:,2])+10)|(last[:,:,1]<np.maximum(last[:,:,0],last[:,:,2])+10)
report={'name':name,'decoded_frames':len(rows),'expected_frames':n,'fps':fps,'size':[w,h],'minimum_margins_ltrb':np.array(rows)[:,1:].min(axis=0).tolist(),'loop_foreground_mae':float(np.abs(first-last)[mask].mean()),'loop_fullframe_mae':float(np.abs(first-last).mean()),'adjacent_mae_median':float(np.median(diff)),'adjacent_mae_max':float(max(diff)),'max_background_border_std':max(plate),'frame_bounds_ltrb':rows,'note':'All source frames measured; 16 freshly matted poses inspected separately. This is generation QA, not a finished sprite package.'}
(out/'qa.json').write_text(json.dumps(report,indent=2),encoding='utf8');print(json.dumps({k:v for k,v in report.items() if k!='frame_bounds_ltrb'}),flush=True)
