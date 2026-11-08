"""Render a local dark-background review of every selected Ink clip."""
from pathlib import Path
import json
import cv2
import numpy as np
from PIL import Image

root=Path(__file__).resolve().parents[1]
p=root/'output/ink-pet'
m=json.loads((p/'manifest.json').read_text(encoding='utf-8'))
width,height=m['frameSize']
writer=cv2.VideoWriter(str(p/'preview.mp4'),cv2.VideoWriter_fourcc(*'mp4v'),24,(width,height+40))
for name,steps in m['clips'].items():
    for step in steps:
        rgba=np.array(Image.open(p/'frames'/(step['frame']+'.png'))).astype(np.float32)/255
        alpha=rgba[:,:,3:4];background=np.array([30,33,40])/255
        rgb=np.uint8(np.clip((rgba[:,:,:3]*alpha+background*(1-alpha))*255,0,255))
        canvas=np.full((height+40,width,3),(40,33,30),np.uint8);canvas[:height]=rgb[:,:,::-1]
        cv2.putText(canvas,name,(14,height+27),cv2.FONT_HERSHEY_SIMPLEX,.65,(210,220,235),1,cv2.LINE_AA)
        for _ in range(max(1,round(step['seconds']*24))):writer.write(canvas)
writer.release()
print(str(p/'preview.mp4'))
