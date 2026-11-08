from pathlib import Path
import cv2,sys
from PIL import Image,ImageDraw
P=Path(__file__).resolve().parent
for name in sys.argv[1:]:
    out=P/name;out.mkdir(exist_ok=True)
    c=cv2.VideoCapture(str(P/(name+'.mp4')));i=0;tiles=[]
    while True:
        ok,b=c.read()
        if not ok:break
        im=Image.fromarray(cv2.cvtColor(b,cv2.COLOR_BGR2RGB)).crop((280,220,660,490)).resize((190,135))
        ImageDraw.Draw(im).text((3,3),str(i),fill='red');tiles.append(im);i+=1
    c.release()
    for start in range(0,i,48):
        s=Image.new('RGB',(1520,810))
        for j,im in enumerate(tiles[start:start+48]):s.paste(im,((j%8)*190,(j//8)*135))
        s.save(out/f'faces-{start//48}.jpg',quality=96)
    print(name,i)
