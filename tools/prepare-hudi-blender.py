"""Decode HUDI's approved source performances for Blender, preserving frame mapping."""
from pathlib import Path
import cv2,json,shutil,hashlib,zipfile
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/hudi-blender-v1'
BASE=ROOT/'output/hudi-pet-v7.3'
SOURCES={
 'idle':'output/hudi-higgsfield-v2-green-2026-11-07/01_idle_blink_wink.mp4',
 'think':'output/hudi-higgsfield-v2-green-2026-11-07/02_think_left.mp4',
 'squatstretch':'output/hudi-higgsfield-v2-green-2026-11-07/04_squat_stretch.mp4',
 'sad':'output/hudi-higgsfield-v2-green-2026-11-07/05_sad_eyes.mp4',
 'surprise':'output/hudi-higgsfield-v2-green-2026-11-07/06_surprise_eyes.mp4',
 'angry':'output/hudi-higgsfield-v2-green-2026-11-07/07_angry_lightning.mp4',
 'lookaround':'output/hudi-higgsfield-v2-green-2026-11-07/08_natural_left_right.mp4',
 'combat':'output/hudi-higgsfield-v2-green-2026-11-07/09_combat_guard.mp4',
 'dance':'output/hudi-higgsfield-v2-green-2026-11-07/10_dance_two_eyes.mp4',
 'hood':'output/hudi-hood-pockets-2026-11-07/11_hood.mp4',
 'pockets':'output/hudi-hood-pockets-2026-11-07/12_pockets.mp4',
 **{'return-'+n:'output/hudi-return-transitions-2026-11-07/'+n+'.mp4' for n in ['wink','think','crouch','stretch','angry','sad','surprise','lookaround','dance','combat']},
 'salute':'assets/higgsfield/2026-11-08-salute-water/hudi-salute-hands-back-lookaround.mp4',
 'march':'assets/higgsfield/2026-11-08-salute-water/hudi-march.mp4',
 'notebook':'assets/higgsfield/2026-11-08-salute-water/hudi-notebook-suspicious-finger-wag.mp4',
 'fire':'assets/higgsfield/2026-11-08-salute-water/hudi-fire-mishap.mp4',
 'firecalm':'assets/higgsfield/2026-11-08-salute-water/hudi-fire-mishap-v2.mp4',
}
def main():
 OUT.mkdir(exist_ok=True,parents=True);backup=OUT/'backup';backup.mkdir(exist_ok=True)
 for n in ['sprites.zip','manifest.json']:
  if not (backup/n).exists():shutil.copy2(BASE/n,backup/n)
 m=json.loads((backup/'manifest.json').read_text());frames=set(m['frames'])-{'neutral'}
 groups={n:sorted(int(f.rsplit('-',1)[1]) for f in frames if f.rsplit('-',1)[0]==n) for n in SOURCES}
 assert set().union(*({f'{n}-{i:03d}' for i in ids} for n,ids in groups.items()))==frames
 sheet=Image.new('RGB',(200*7,324*4),'#333333');d=ImageDraw.Draw(sheet);rows=[]
 for j,(name,rel) in enumerate(SOURCES.items()):
  path=ROOT/rel;cap=cv2.VideoCapture(str(path));fps=cap.get(5);count=int(cap.get(7));w=int(cap.get(3));h=int(cap.get(4));ids=groups[name]
  dest=OUT/'clips'/name/'input';dest.mkdir(parents=True,exist_ok=True)
  assert ids and max(ids)<count
  for i in range(count):
   ok,bgr=cap.read();assert ok
   if i not in ids:continue
   if not(dest/f'{i:04d}.png').exists():cv2.imwrite(str(dest/f'{i:04d}.png'),bgr,[cv2.IMWRITE_PNG_COMPRESSION,2])
  cap.release();first=Image.open(dest/f'{ids[0]:04d}.png');first.thumbnail((200,300));sheet.paste(first,(j%7*200,j//7*324));d.text((j%7*200+4,j//7*324+300),name,fill='white')
  rows.append(dict(name=name,source=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),width=w,height=h,sourceFrames=count,indices=ids,fps=fps,root=str(dest.parent),legacy=not name in ['salute','march','notebook','fire','firecalm']))
  print(name,len(ids),w,h,flush=True)
  (OUT/'inventory.json').write_text(json.dumps(rows,indent=2),encoding='utf8')
 sheet.save(OUT/'source-overview.jpg',quality=94);print('PREPARED',len(frames),flush=True)
if __name__=='__main__':main()
