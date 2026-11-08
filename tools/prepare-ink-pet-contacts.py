from pathlib import Path
import cv2
import numpy as np
from PIL import Image,ImageDraw
root=Path(__file__).resolve().parents[1]
out=root/'output/ink-pet/source-review';out.mkdir(parents=True,exist_ok=True)
for name in ('jump','paintmagic','shadowmerge'):
    cap=cv2.VideoCapture(str(root/'assets/pets/ink'/f'{name}.mp4'))
    count=int(cap.get(cv2.CAP_PROP_FRAME_COUNT));fps=cap.get(cv2.CAP_PROP_FPS)
    sheet=Image.new('RGB',(1024,840),'#202020');draw=ImageDraw.Draw(sheet)
    for i,n in enumerate(np.linspace(0,count-1,12,dtype=int)):
        cap.set(cv2.CAP_PROP_POS_FRAMES,int(n));ok,bgr=cap.read();assert ok
        rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB);x=i%4*256;y=i//4*280
        sheet.paste(Image.fromarray(rgb).resize((256,256)),(x,y));draw.text((x+5,y+258),f'{n/fps:.2f}s',fill='white')
    cap.release();sheet.save(out/f'{name}-contact.jpg',quality=94)
    print(name)
