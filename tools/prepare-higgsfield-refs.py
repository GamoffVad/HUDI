"""Prepare exact HUDI runtime assets on the requested chroma-green background."""
from pathlib import Path
import numpy as np
import cv2
from PIL import Image, ImageOps

ROOT=Path(__file__).resolve().parents[1]
DEST=ROOT/'assets/higgsfield/2026-11-08-salute-water'
DEST.mkdir(parents=True,exist_ok=True)

def green_canvas(image, canvas_size=1024):
    background=Image.new('RGBA',(canvas_size,canvas_size),(0,255,0,255))
    background.alpha_composite(image,((canvas_size-image.width)//2,(canvas_size-image.height)//2))
    return background.convert('RGB')

# Use the same alpha cleanup and horizontal orientation as the shipped pet.
pet=np.array(Image.open(ROOT/'output/hudi-pet-v6.3/frames/neutral.png').convert('RGBA'))
coverage=np.clip((pet[:,:,3].astype(float)-150)/70,0,1)
pet[:,:,3]=np.round(coverage*coverage*(3-2*coverage)*255).astype(np.uint8)
pet=ImageOps.mirror(Image.fromarray(pet))
pet=pet.resize((645,840),Image.Resampling.LANCZOS)
green_canvas(pet).save(DEST/'hudi-green-reference.png')

# Match WaterOrbFrames' rendering palette, preserving its existing alpha.
rgb=np.asarray(Image.open(ROOT/'assets/water-orb/v3/water-orb-green.png').convert('RGB'),dtype=float)/255
key=np.median(np.concatenate((rgb[:40].reshape(-1,3),rgb[-40:].reshape(-1,3))),axis=0)
alpha=np.clip((1-(rgb[:,:,1]-np.maximum(rgb[:,:,0],rgb[:,:,2]))/(key[1]-max(key[0],key[2]))-.012)/.988,0,1)
seed=cv2.morphologyEx(np.uint8(alpha>.12)*255,cv2.MORPH_CLOSE,np.ones((5,5),np.uint8))
contours,_=cv2.findContours(seed,cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE)
mask=np.zeros(seed.shape,np.uint8)
cv2.drawContours(mask,[max(contours,key=cv2.contourArea)],-1,255,cv2.FILLED)
mask=cv2.GaussianBlur(cv2.dilate(mask,np.ones((5,5),np.uint8)),(5,5),.8)/255
alpha*=mask
alpha[alpha<.006]=0
alpha=alpha[:,:,None]
premult=np.clip(rgb-(1-alpha)*key,0,alpha)
colour=np.divide(premult,alpha,out=np.zeros_like(premult),where=alpha>1e-6)
light=colour.max(axis=2,keepdims=True)
white=.06+.32*alpha**3
shoe=np.array([129,168,197],dtype=float)
tint=shoe[2]-(shoe[2]-shoe)*1.5
colour=np.round(np.clip((tint+(255-tint)*white)*light,0,255)).astype(np.uint8)
orb=np.concatenate((colour,np.round(alpha*255).astype(np.uint8)),axis=2)
orb=Image.fromarray(orb).resize((1024,1024),Image.Resampling.LANCZOS)
green_canvas(orb).save(DEST/'water-blue-green-reference.png')
print(DEST)
