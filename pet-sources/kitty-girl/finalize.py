"""Finish inspected loops with a shared exact endpoint; retain raw masters."""
from pathlib import Path
from zipfile import ZipFile,ZIP_STORED
import json,sys,subprocess
import numpy as np
from PIL import Image,ImageDraw

ROOT=Path(__file__).parent
import os,shutil
FF=os.environ.get('HUDI_FFMPEG') or shutil.which('ffmpeg') or str(next(p for p in Path(__file__).resolve().parents if (p/'src/HudiPet').is_dir())/'.animation-tools/node_modules/ffmpeg-static/ffmpeg.exe')

def blend(a,b,t):
    aa=a[:,:,3:4].astype(np.float32)/255;ba=b[:,:,3:4].astype(np.float32)/255
    al=aa*(1-t)+ba*t;pm=a[:,:,:3]*aa*(1-t)+b[:,:,:3]*ba*t
    rgb=np.divide(pm,al,out=np.zeros_like(pm),where=al>1e-6)
    return np.uint8(np.clip(np.concatenate([rgb,al*255],2).round(),0,255))

def error(a,b):
    u=(a[:,:,3]>16)|(b[:,:,3]>16)
    return float(np.abs(a[:,:,:3].astype(float)*a[:,:,3:4]/255-b[:,:,:3].astype(float)*b[:,:,3:4]/255)[u].mean())

def run(name):
    p=ROOT/'animations'/name;audit=json.loads((p/'matte-audit.json').read_text());assert audit['allInside']
    paths=sorted((p/'frames').glob('*.png'));fps=audit['fps']
    neutral=np.array(Image.open(ROOT/'animations/look-around/frames/0000.png').convert('RGBA'))
    first=np.array(Image.open(paths[0]));last=np.array(Image.open(paths[-1]));errs=[error(first,neutral),error(last,neutral)]
    assert max(errs)<10,(name,'Endpoint differs materially',errs)
    out=p/'transparent-frames';out.mkdir(exist_ok=True);small=[];boxes=[]
    ph=600;pw=round(neutral.shape[1]*ph/neutral.shape[0]);pw+=pw%2
    proc=subprocess.Popen([FF,'-hide_banner','-loglevel','error','-y','-f','rawvideo','-pixel_format','rgb24','-video_size',f'{pw*2}x{ph+24}','-framerate',str(fps),'-i','-','-an','-c:v','libx264','-crf','16','-pix_fmt','yuv420p','-movflags','+faststart',str(p/'final-preview.mp4')],stdin=subprocess.PIPE)
    for i,path in enumerate(paths):
        a=np.array(Image.open(path).convert('RGBA'))
        if i<4:a=blend(neutral,a,i/3)
        if i>=len(paths)-4:a=blend(a,neutral,(i-(len(paths)-4))/3)
        a[a[:,:,3]==0,:3]=0;im=Image.fromarray(a);im.save(out/path.name,compress_level=3);boxes.append(im.getbbox())
        im=im.resize((pw,ph),Image.Resampling.LANCZOS);small.append(im)
        canvas=Image.new('RGB',(pw*2,ph+24));draw=ImageDraw.Draw(canvas)
        for j,col in enumerate([(245,245,245,255),(18,18,18,255)]):
            bg=Image.new('RGBA',im.size,col);bg.alpha_composite(im);canvas.paste(bg.convert('RGB'),(j*pw,24));draw.text((j*pw+8,5),f'{name}  {i/fps:.2f}s',fill='white')
        proc.stdin.write(canvas.tobytes())
    proc.stdin.close();assert proc.wait()==0
    assert np.array_equal(np.array(Image.open(out/paths[0].name)),np.array(Image.open(out/paths[-1].name)))
    small[0].save(p/'final-loop.webp',save_all=True,append_images=small[1:],duration=1000/fps,loop=0,quality=95,method=4)
    manifest={'name':name,'frames':len(paths),'fps':fps,'frameSize':audit['size'],'alpha':'straight RGBA','firstLastIdentical':True,'commonNeutral':'look-around/frames/0000.png','endpointCorrection':'Only 4 frames (0.125s) at each already-settled boundary; premultiplied blend. Actual return motion retained.','sourceEndpointPremultipliedMae':errs,'allInside':all(b and min(b[:2])>0 and b[2]<neutral.shape[1] and b[3]<neutral.shape[0] for b in boxes),'model':'kling4_0','quotedCredits':16.25}
    (p/'export-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    with ZipFile(p/'transparent-frames.zip','w',ZIP_STORED) as z:
        z.write(p/'export-manifest.json','manifest.json')
        for path in sorted(out.glob('*.png')):z.write(path,'frames/'+path.name)
    print(name,'finalized',errs,flush=True)

if __name__=='__main__':
    for name in sys.argv[1:]:run(name)
