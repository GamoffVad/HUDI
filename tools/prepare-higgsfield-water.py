"""Key the approved Higgsfield sphere, keeping one fixed crop and source cadence."""
import json
from pathlib import Path
from zipfile import ZipFile, ZIP_STORED
import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets/higgsfield/2026-11-08-salute-water/blue-water-sphere-loop.mp4'
DEST = ROOT / 'assets/water-orb/higgsfield-v1'
DEST.mkdir(parents=True, exist_ok=True)
SIZE = 192

def key_frame(frame):
    rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB).astype(np.float32) / 255
    border = np.concatenate((rgb[:24].reshape(-1, 3), rgb[-24:].reshape(-1, 3)))
    key = np.median(border, axis=0)
    contrast = key[1] - max(key[0], key[2])
    alpha = np.clip((1 - (rgb[:,:,1] - np.maximum(rgb[:,:,0], rgb[:,:,2])) / contrast - .012) / .988, 0, 1)
    seed = np.uint8(alpha > .07) * 255
    seed = cv2.morphologyEx(seed, cv2.MORPH_CLOSE, np.ones((9,9), np.uint8))
    contours, _ = cv2.findContours(seed, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    contour = max(contours, key=cv2.contourArea)
    mask = np.zeros(seed.shape, np.uint8)
    cv2.drawContours(mask, [contour], -1, 255, cv2.FILLED)
    mask = cv2.GaussianBlur(cv2.dilate(mask, np.ones((7,7), np.uint8)), (5,5), .8) / 255
    alpha *= mask
    alpha[alpha < .006] = 0
    premult = np.clip(rgb - (1-alpha[:,:,None]) * key, 0, alpha[:,:,None])
    premult[:,:,1] = np.minimum(premult[:,:,1], np.maximum(premult[:,:,0], premult[:,:,2]))
    return np.dstack((premult, alpha)).astype(np.float32), cv2.boundingRect(contour)

cap = cv2.VideoCapture(str(SOURCE))
fps = cap.get(cv2.CAP_PROP_FPS)
raw_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
bounds = []
while True:
    ok, frame = cap.read()
    if not ok: break
    _, rect = key_frame(frame)
    bounds.append(rect)
assert len(bounds) == raw_count == 121 and fps == 24
x0 = min(b[0] for b in bounds); y0 = min(b[1] for b in bounds)
x1 = max(b[0]+b[2] for b in bounds); y1 = max(b[1]+b[3] for b in bounds)
side = max(x1-x0, y1-y0) + 24
cx, cy = (x0+x1)/2, (y0+y1)/2
scale = SIZE/side
matrix = np.array([[scale,0,SIZE/2-cx*scale],[0,scale,SIZE/2-cy*scale]], np.float32)
cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
frames = []
while True:
    ok, frame = cap.read()
    if not ok: break
    pm, _ = key_frame(frame)
    frames.append(np.clip(cv2.warpAffine(pm, matrix, (SIZE,SIZE), flags=cv2.INTER_AREA),0,1))
cap.release()

# The provider supplies both endpoints. Correct only the small residual difference
# over the final half-second, then omit the duplicate endpoint (120 frames / 24 fps).
delta = frames[0] - frames[-1]
before = float(np.abs(frames[-2]-frames[0]).mean())
for i in range(108,121):
    t = (i-108)/12
    frames[i] = np.clip(frames[i] + delta*(t*t*(3-2*t)), 0, 1)
    frames[i][:,:,:3] = np.minimum(frames[i][:,:,:3], frames[i][:,:,3:4])
frames = frames[:-1]
after = float(np.abs(frames[-1]-frames[0]).mean())
paths=[]; previews=[]
folder=DEST/'frames-alpha'; folder.mkdir(exist_ok=True)
for i, pm in enumerate(frames):
    alpha=pm[:,:,3:4]
    rgb=np.divide(pm[:,:,:3],alpha,out=np.zeros_like(pm[:,:,:3]),where=alpha>1e-6)
    rgba=np.uint8(np.round(np.concatenate((rgb,alpha),axis=2)*255))
    path=folder/f'frame-{i:03}.png'; Image.fromarray(rgba).save(path); paths.append(path)
    previews.append(np.uint8(np.round((pm[:,:,:3]+np.array([24,28,34])/255*(1-alpha))*255)))
manifest={'frames':120,'fps':24,'size':SIZE,'source':str(SOURCE.relative_to(ROOT)).replace('\\','/'),
          'job_id':'8ad53a24-06e0-490c-80ee-bdc729b0310f','fixed_crop':[cx-side/2,cy-side/2,side,side],
          'tint':'source colours; chroma despill only','seam_mean_premult_delta_before':before,
          'seam_mean_premult_delta_after':after,'seam_correction_frames':12}
(DEST/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
with ZipFile(ROOT/'src/HudiPet/Assets/water-orb.zip','w',ZIP_STORED) as archive:
    archive.writestr('manifest.json',json.dumps(manifest))
    for p in paths: archive.write(p,p.name)
sheet=Image.new('RGB',(SIZE*4,SIZE*3))
for row,bg in enumerate(((24,28,34),(245,245,245),(91,52,109))):
    for col,index in enumerate((0,24,48,72)):
        pm=frames[index]; result=np.uint8(np.round((pm[:,:,:3]+np.array(bg)/255*(1-pm[:,:,3:4]))*255))
        sheet.paste(Image.fromarray(result),(col*SIZE,row*SIZE))
sheet.save(DEST/'matte-review.png')
# APNG is only a preview; runtime uses the exact source's 24 fps clock.
images=[Image.fromarray(p) for p in previews]
images[0].save(DEST/'preview.apng',save_all=True,append_images=images[1:],duration=1000/fps,loop=0)
print(json.dumps(manifest,indent=2))
