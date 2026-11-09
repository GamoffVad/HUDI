"""Export one generated loop, native RGBA masters and a review preview."""
from pathlib import Path
import json,subprocess
from zipfile import ZipFile,ZIP_STORED
import numpy as np
from PIL import Image,ImageDraw
ROOT=Path(__file__).parent
import os,shutil
FF=os.environ.get('HUDI_FFMPEG') or shutil.which('ffmpeg') or str(next(p for p in Path(__file__).resolve().parents if (p/'src/HudiPet').is_dir())/'.animation-tools/node_modules/ffmpeg-static/ffmpeg.exe')

def blend(a,b,t):
    aa=a[:,:,3:4].astype(np.float32)/255;ba=b[:,:,3:4].astype(np.float32)/255
    alpha=aa*(1-t)+ba*t
    pm=a[:,:,:3]*aa*(1-t)+b[:,:,:3]*ba*t
    rgb=np.divide(pm,alpha,out=np.zeros_like(pm),where=alpha>1e-6)
    return np.clip(np.dstack((rgb,alpha[:,:,0]*255)).round(),0,255).astype(np.uint8)

def main():
    audit=json.loads((ROOT/'matte-audit.json').read_text());fps=audit['fps'];paths=sorted((ROOT/'frames').glob('*.png'))
    meta=json.loads((ROOT/'source-meta.json').read_text());join=meta['entryFrames'];out=ROOT/'transparent-frames';out.mkdir(exist_ok=True)
    first=np.array(Image.open(paths[0]));mid=np.array(Image.open(paths[join-1]));last=np.array(Image.open(paths[-1]))
    endpoint_mae=float(np.abs(first.astype(float)-last.astype(float)).mean())
    # Only brief endpoint settling. No reverse playback or synthetic body warps.
    for i,p in enumerate(paths):
        a=np.array(Image.open(p))
        if join<=i<join+5:
            t=(i-join)/4;t=t*t*(3-2*t);a=blend(mid,a,t)
        if i>=len(paths)-6:
            t=(i-(len(paths)-6))/5;t=t*t*(3-2*t);a=blend(a,first,t)
        a[a[:,:,3]==0,:3]=0;Image.fromarray(a).save(out/p.name)
    assert np.array_equal(np.array(Image.open(out/paths[0].name)),np.array(Image.open(out/paths[-1].name)))
    bounds=[Image.open(out/p.name).getbbox() for p in paths]
    w,h=first.shape[1],first.shape[0]
    box=(max(0,min(b[0] for b in bounds)-24),max(0,min(b[1] for b in bounds)-24),min(w,max(b[2] for b in bounds)+24),min(h,max(b[3] for b in bounds)+24))
    frames=[Image.open(out/p.name).convert('RGBA').crop(box) for p in paths]
    small=[f.resize((round(f.width*600/f.height),600),Image.Resampling.LANCZOS) for f in frames]
    small[0].save(ROOT/'stroll-loop-preview.webp',save_all=True,append_images=small[1:],loop=0,duration=1000/fps,quality=94,method=4)
    print('WebP complete',flush=True)
    height=min(900,frames[0].height);height+=height%2;tw=round(frames[0].width*height/frames[0].height);tw+=tw%2
    size=(tw*2,height+32)
    proc=subprocess.Popen([FF,'-hide_banner','-loglevel','error','-y','-f','rawvideo','-pixel_format','rgb24','-video_size',f'{size[0]}x{size[1]}','-framerate',str(fps),'-i','-','-an','-c:v','libx264','-crf','16','-pix_fmt','yuv420p','-movflags','+faststart',str(ROOT/'preview-light-dark.mp4')],stdin=subprocess.PIPE)
    picks=np.linspace(0,len(frames)-1,8).round().astype(int).tolist();sheet=Image.new('RGB',(tw*4,(height+32)*2))
    for i,f in enumerate(frames):
        f=f.resize((tw,height),Image.Resampling.LANCZOS);canvas=Image.new('RGB',size);draw=ImageDraw.Draw(canvas)
        for j,col in enumerate([(245,245,245,255),(24,24,24,255)]):
            bg=Image.new('RGBA',f.size,col);bg.alpha_composite(f);canvas.paste(bg.convert('RGB'),(j*tw,32));draw.rectangle((j*tw,0,(j+1)*tw,31),fill=col[:3]);draw.text((j*tw+8,10),f'{i/fps:.2f}s / '+('light' if j==0 else 'dark'),fill='black' if j==0 else 'white')
        proc.stdin.write(canvas.tobytes())
        if i in picks:
            n=picks.index(i);sheet.paste(canvas.crop((0,0,tw,height+32)),((n%4)*tw,(n//4)*(height+32)))
    proc.stdin.close();assert proc.wait()==0;sheet.save(ROOT/'contact-sheet.jpg',quality=95)
    manifest={'frames':len(paths),'fps':fps,'frameSize':[w,h],'firstLastIdentical':True,'originalEndpointRgbaMae':endpoint_mae,'allInside':all(b[0]>0 and b[1]>0 and b[2]<w and b[3]<h for b in bounds),'sharedPreviewCrop':box,'alpha':'straight RGBA, native frame resolution, HUDI chroma key with RGB-only despill; no alpha blur','motion':'two separately generated forward transitions; no reverse playback','endpointSettling':'5 frames at middle, 6 at loop boundary, premultiplied blend','sources':meta}
    (ROOT/'export-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    manifest['motion']='one continuous generated forward shot; no reverse playback'
    manifest['endpointSettling']='6 frames at loop boundary in premultiplied RGBA; no middle edit'
    manifest['alpha']=audit['method']+'; native-resolution straight RGBA'
    (ROOT/'export-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    with ZipFile(ROOT/'stroll-transparent-frames.zip','w',ZIP_STORED) as z:
        z.write(ROOT/'export-manifest.json','manifest.json')
        for p in sorted(out.glob('*.png')):z.write(p,'frames/'+p.name)
    print(json.dumps(manifest),flush=True)
if __name__=='__main__':main()
