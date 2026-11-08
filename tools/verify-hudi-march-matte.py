"""Audit the repaired march against its source and the pre-repair package."""
from pathlib import Path
from zipfile import ZipFile
import importlib.util
import json
import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/hudi-pet-v7.3'
QA=OUT/'qa/march-matte'
QA.mkdir(parents=True,exist_ok=True)
spec=importlib.util.spec_from_file_location('emotions',ROOT/'tools/prepare-hudi-emotions.py')
em=importlib.util.module_from_spec(spec);spec.loader.exec_module(em)
keyer=em.load_keyer()
cv2.setNumThreads(1)
cap=cv2.VideoCapture(str(em.SOURCE/'hudi-march.mp4'))
rows=[]
for i in range(int(cap.get(cv2.CAP_PROP_FRAME_COUNT))):
    ok,bgr=cap.read()
    if not ok:raise RuntimeError(f'Decode failed: {i}')
    rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB).astype(np.float32)
    plate=cv2.GaussianBlur(np.median(rgb[:80],axis=0)[None,:,:],(31,1),0)
    pm,alpha,_=keyer.key_frame(bgr,plate_override=plate)
    clean_pm,clean_alpha=em.clean_march_plate(pm,alpha)
    opaque=alpha>.5
    # A clean-plate component filter must not alter the actual body.
    assert np.array_equal(alpha[opaque],clean_alpha[opaque])
    assert np.array_equal(pm[opaque],clean_pm[opaque])
    assert not np.any(clean_alpha[:80]>.01)
    rgba=np.array(Image.open(OUT/'frames'/f'march-{i:03}.png'))
    ys,xs=np.where(rgba[:,:,3]>1)
    bounds=[int(xs.min()),int(ys.min()),int(xs.max())+1,int(ys.max())+1]
    margin=min(bounds[0],bounds[1],640-bounds[2],640-bounds[3])
    assert margin>=60,(i,bounds)
    rows.append({'frame':i,'bounds':bounds,'margin':margin})
cap.release()
with ZipFile(OUT/'rollback-before-march/sprites.zip') as old,ZipFile(OUT/'sprites.zip') as new:
    untouched=[name for name in old.namelist() if name.endswith('.png') and not name.startswith('march-')]
    # CRC equality plus full decoded-entry bytes: smoke and all other actions stay unchanged.
    for name in untouched:assert old.read(name)==new.read(name),name
report={'frames':len(rows),'minimumSoftMargin':min(r['margin'] for r in rows),
        'opaqueSubjectPixelsUnchangedByComponentFilter':True,'otherFrameEntriesByteIdentical':len(untouched),
        'plateMethod':'clear top80 rows -> per-column RGB median -> horizontal31px smoothing',
        'source':'assets/higgsfield/2026-11-08-salute-water/hudi-march.mp4','perFrame':rows}
(QA/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
ids=[0,144,192,208,212,213,214,216,228,240]
sheet=Image.new('RGB',(300*len(ids),640))
for col,i in enumerate(ids):
    with Image.open(OUT/'frames'/f'march-{i:03}.png') as im:
        for row,color in enumerate([(24,24,24,255),(245,245,245,255)]):
            tile=Image.new('RGBA',im.size,color);tile.alpha_composite(im)
            sheet.paste(tile.resize((300,300)),(col*300,row*320))
            ImageDraw.Draw(sheet).text((col*300+5,row*320+300),str(i),fill='orange')
sheet.save(QA/'after.jpg',quality=96)
print(json.dumps({k:v for k,v in report.items() if k!='perFrame'}))
