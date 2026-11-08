"""Read-only contact sheets and every-frame edge audit of generated Ink videos."""
from pathlib import Path
import json
import cv2
import numpy as np
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1];SOURCE=ROOT/'assets/pets/ink';OUT=ROOT/'output/ink-pet/source-review';OUT.mkdir(exist_ok=True,parents=True)
summaries=[]
for video in SOURCE.glob('*.mp4'):
    cap=cv2.VideoCapture(str(video));count=int(cap.get(cv2.CAP_PROP_FRAME_COUNT));fps=cap.get(cv2.CAP_PROP_FPS)
    sample=np.linspace(0,count-1,12,dtype=int).tolist();sheet=Image.new('RGB',(1024,840),'#202020');draw=ImageDraw.Draw(sheet)
    minmargin=10000;worst=None;flags=[];boxes=[]
    for i in range(count):
        ok,bgr=cap.read();assert ok
        rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB);r,g,b=rgb.astype(float).transpose(2,0,1);mx=np.maximum(r,b)
        mask=np.uint8((g-mx<25)|(g-mx<g*.28));n,labels,stats,_=cv2.connectedComponentsWithStats(mask,8)
        keep=np.zeros_like(mask)
        for j in range(1,n):
            if stats[j,cv2.CC_STAT_AREA]>16:keep[labels==j]=1
        ys,xs=np.where(keep)
        if len(xs):
            box=[int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1)];margin=min(box[0],box[1],bgr.shape[1]-box[2],bgr.shape[0]-box[3])
            if margin<minmargin:minmargin=margin;worst=(i,bgr.copy())
            if margin<4:flags.append(i)
        else:box=None
        boxes.append(box)
        if i in sample:
            j=sample.index(i);x=j%4*256;y=j//4*280;sheet.paste(Image.fromarray(rgb).resize((256,256)),(x,y));draw.text((x+5,y+258),f'{i/fps:.2f}s',fill='white')
    cap.release();sheet.save(OUT/(video.stem+'-contact.jpg'),quality=94)
    if worst:cv2.imwrite(str(OUT/(video.stem+'-closest-edge.png')),worst[1])
    result={'name':video.stem,'frames':count,'fps':fps,'minmargin':minmargin,'edgeflags':flags,'closest_frame':worst[0] if worst else None,'bounds':boxes}
    (OUT/(video.stem+'-audit.json')).write_text(json.dumps(result),encoding='utf8');summaries.append({k:v for k,v in result.items() if k!='bounds'})
print(json.dumps(summaries,indent=2))
