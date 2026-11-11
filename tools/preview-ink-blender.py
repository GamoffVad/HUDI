"""Read-only black/white video preview of the final packaged frames."""
from pathlib import Path
import json,subprocess
import numpy as np
from PIL import Image,ImageDraw
import imageio_ffmpeg
ROOT=Path(__file__).resolve().parents[1]/'output/ink-blender-v2'
m=json.loads((ROOT/'manifest.json').read_text(encoding='utf8'));dest=ROOT/'qa/ink-black-white.mp4'
cmd=[imageio_ffmpeg.get_ffmpeg_exe(),'-y','-loglevel','error','-f','rawvideo','-pix_fmt','rgb24','-s','800x424','-r','12','-i','-','-an','-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(dest)]
p=subprocess.Popen(cmd,stdin=subprocess.PIPE)
for name,clip in m['clips'].items():
 for i in range(0,len(clip),2):
  frame=Image.open(ROOT/'frames'/(clip[i]['frame']+'.png')).convert('RGBA')
  out=Image.new('RGB',(800,424),(40,40,40));draw=ImageDraw.Draw(out)
  for col,bg in enumerate([(0,0,0,255),(255,255,255,255)]):
   view=Image.alpha_composite(Image.new('RGBA',frame.size,bg),frame).convert('RGB').resize((400,400),Image.Resampling.LANCZOS)
   out.paste(view,(col*400,0));draw.text((col*400+8,405),name,fill='white')
  p.stdin.write(out.tobytes())
p.stdin.close();assert p.wait()==0
neutral=Image.open(ROOT/'frames/neutral.png').convert('RGBA');still=Image.new('RGB',(960,480))
for col,bg in enumerate([(0,0,0,255),(255,255,255,255)]):still.paste(Image.alpha_composite(Image.new('RGBA',neutral.size,bg),neutral).convert('RGB').resize((480,480),Image.Resampling.LANCZOS),(col*480,0))
still.save(ROOT/'qa/ink-preview.png')
print(dest,flush=True)
