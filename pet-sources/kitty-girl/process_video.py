"""Decode native frames, inspect alpha, and export review videos without crop jitter."""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import sys,json,subprocess
import cv2,numpy as np
from PIL import Image,ImageDraw
from matte import matte

ROOT=Path(__file__).parent
import os,shutil
FF=os.environ.get('HUDI_FFMPEG') or shutil.which('ffmpeg') or str(next(p for p in Path(__file__).resolve().parents if (p/'src/HudiPet').is_dir())/'.animation-tools/node_modules/ffmpeg-static/ffmpeg.exe')
cv2.setNumThreads(1)

def run(name):
    folder=ROOT/'animations'/name;out=folder/'frames';out.mkdir(parents=True,exist_ok=True)
    cap=cv2.VideoCapture(str(folder/'source.mp4'))
    fps=cap.get(cv2.CAP_PROP_FPS);w=int(cap.get(3));h=int(cap.get(4));n=int(cap.get(7))
    if not n:raise ValueError('No video frames')
    def work(pair):
        i,frame=pair;a=matte(frame);im=Image.fromarray(a);im.save(out/f'{i:04d}.png',compress_level=3)
        box=im.getbbox()
        return {'index':i,'bounds':box,'opaquePixels':int((a[:,:,3]==255).sum())}
    def read():
        i=0
        while True:
            ok,frame=cap.read()
            if not ok:break
            yield i,frame;i+=1
    rows=[]
    with ThreadPoolExecutor(max_workers=3) as pool:
        pending=[]
        for pair in read():
            pending.append(pool.submit(work,pair))
            if len(pending)>=6:rows.append(pending.pop(0).result())
            if len(rows) and len(rows)%30==0:print(name,len(rows),'/',n,flush=True)
        rows.extend(f.result() for f in pending)
    cap.release()
    paths=sorted(out.glob('*.png'));assert len(paths)==n
    first=np.array(Image.open(paths[0]));last=np.array(Image.open(paths[-1]))
    union=(first[:,:,3]>16)|(last[:,:,3]>16)
    pm0=first[:,:,:3].astype(float)*first[:,:,3:4]/255
    pm1=last[:,:,:3].astype(float)*last[:,:,3:4]/255
    report={'frames':n,'fps':fps,'size':[w,h],'allInside':all(r['bounds'] and r['bounds'][0]>0 and r['bounds'][1]>0 and r['bounds'][2]<w and r['bounds'][3]<h for r in rows),'firstLastIdentical':bool(np.array_equal(first,last)),'endpointForegroundPremultipliedMae':float(np.abs(pm0-pm1)[union].mean()),'endpointAlphaMae':float(np.abs(first[:,:,3].astype(float)-last[:,:,3])[union].mean()),'bounds':rows,'method':'native resolution, local foreground reconstruction and warm brown hair RGB mixing, no blanket alpha blur or erosion; source contact shadow'}
    (folder/'matte-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    # One common canvas for all frames and backgrounds.
    ph=600;pw=round(w*ph/h);pw+=pw%2
    picks=np.linspace(0,n-1,12).round().astype(int).tolist()
    sheet=Image.new('RGB',(pw*6,(ph+24)*2))
    proc=subprocess.Popen([FF,'-hide_banner','-loglevel','error','-y','-f','rawvideo','-pixel_format','rgb24','-video_size',f'{pw*2}x{ph+24}','-framerate',str(fps),'-i','-','-an','-c:v','libx264','-crf','16','-pix_fmt','yuv420p','-movflags','+faststart',str(folder/'preview-light-dark.mp4')],stdin=subprocess.PIPE)
    small=[]
    for i,path in enumerate(paths):
        im=Image.open(path).convert('RGBA').resize((pw,ph),Image.Resampling.LANCZOS);small.append(im)
        canvas=Image.new('RGB',(pw*2,ph+24));draw=ImageDraw.Draw(canvas)
        for j,color in enumerate([(245,245,245,255),(18,18,18,255)]):
            bg=Image.new('RGBA',im.size,color);bg.alpha_composite(im);canvas.paste(bg.convert('RGB'),(j*pw,24));draw.text((j*pw+8,5),f'{name}  {i/fps:.2f}s',fill='white')
        proc.stdin.write(canvas.tobytes())
        if i in picks:
            k=picks.index(i);sheet.paste(canvas.crop((pw,0,pw*2,ph+24)),((k%6)*pw,(k//6)*(ph+24)))
    proc.stdin.close();assert proc.wait()==0
    sheet.save(folder/'contact-sheet.jpg',quality=95)
    small[0].save(folder/'loop-preview.webp',save_all=True,append_images=small[1:],duration=1000/fps,loop=0,quality=95,method=4)
    print(json.dumps({k:v for k,v in report.items() if k!='bounds'}),flush=True)

if __name__=='__main__':
    for name in sys.argv[1:]:run(name)
