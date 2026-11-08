"""Key the selected Ink animations into one independent transparent pet package."""
from pathlib import Path
from zipfile import ZipFile, ZIP_STORED
import json
import hashlib
import runpy
import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'assets/pets/ink'
OUT=ROOT/'output/ink-pet'
SIZE=416
NAMES=['dash','pockets','jump','paintmagic','dance','sidestep','cutthroat','lookdown','lookleft','lookright','lookup']
SOURCE_NAMES={'jump':'jump-v2','shadowmerge':'shadowmerge-v2'}

def reviewed_steps(name,steps):
    if name!='lookright':return steps
    # Source turns right correctly, then incorrectly turns left. Keep only the
    # reviewed 0..16 head turn, settle briefly, and reverse that small motion.
    # This preserves the real face and clothing; no mirroring or synthetic warp.
    selected=[]
    for i in range(17):
        duration=1/24 if i<12 else [.05,.06,.075,.09,.35][i-12]
        selected.append({'frame':f'lookright-{i:03}','seconds':duration})
    for i in range(15,-1,-1):
        duration={15:.09,14:.075,13:.06,12:.05,0:.16}.get(i,1/24)
        selected.append({'frame':f'lookright-{i:03}','seconds':duration})
    return selected

def registration(source, target):
    sift=cv2.SIFT_create(nfeatures=3000)
    ka,da=sift.detectAndCompute(cv2.cvtColor(source,cv2.COLOR_BGR2GRAY),None)
    kb,db=sift.detectAndCompute(cv2.cvtColor(target,cv2.COLOR_BGR2GRAY),None)
    matches=cv2.BFMatcher().knnMatch(da,db,k=2)
    good=[a for a,b in matches if a.distance<.7*b.distance]
    a=np.float32([ka[m.queryIdx].pt for m in good]);b=np.float32([kb[m.trainIdx].pt for m in good])
    affine,inliers=cv2.estimateAffinePartial2D(a,b,method=cv2.RANSAC,ransacReprojThreshold=1.5)
    if affine is None or int(inliers.sum())<15:raise RuntimeError('Cannot register smaller jump reference reliably')
    keep=inliers.ravel().astype(bool);a=a[keep];b=b[keep]
    ac=a-a.mean(0);bc=b-b.mean(0);scale=float((ac*bc).sum()/(ac*ac).sum())
    offset=b.mean(0)-scale*a.mean(0)
    residual=np.linalg.norm(a*scale+offset-b,axis=1)
    if np.median(residual)>1.5:raise RuntimeError('Reference registration residual too high')
    return np.float32([[scale,0,offset[0]],[0,scale,offset[1]]]),{'scale':scale,'offset':offset.tolist(),'inliers':int(keep.sum()),'median_residual_px':float(np.median(residual))}

def main():
    # Always freshly decode the original videos; no previous PNG/alpha cache.
    runpy.run_path(str(ROOT/'tools/prepare-ink-pet-fresh.py'))['build']()

if __name__=='__main__':main()
