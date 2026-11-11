"""Decode original Ink videos for Blender; no keying or alpha processing here."""
from pathlib import Path
import cv2, json, hashlib, shutil, zipfile
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/ink-blender-v2'
SRC=ROOT/'assets/pets/ink-restart'
SPECS={
 'hood':'2026-11-08-loop/ink-loop.mp4',
 'lookaround':'2026-11-08-six/look-around.mp4',
 'surprise':'2026-11-08-six/surprise-v2.mp4',
 'temple':'2026-11-08-six/temple-v2.mp4',
 'skeptical':'2026-11-08-six/skeptical.mp4',
 'shrug':'2026-11-08-six/shrug.mp4',
 'dance':'2026-11-08-six/dance.mp4',
 'indignation':'2026-11-08-extra-reactions/indignant-temple.mp4',
 'firetrick':'2026-11-08-fire-wide/fire-wide.mp4',
 'impatient':'2026-11-08-extra-reactions/impatient.mp4',
}
def main():
 OUT.mkdir(parents=True,exist_ok=True)
 installed=Path.home()/'AppData/Local/HUDI/pets/675de893b2d4487da12ebfabac090b58'
 backup=OUT/'backup';backup.mkdir(exist_ok=True)
 metadata=json.loads((installed/'pet.json').read_text(encoding='utf-8-sig'))
 for file in ['pet.zip','pet.json','appearance.json']:
  source=installed/(metadata.get('package','pet.zip') if file=='pet.zip' else file)
  if not (backup/file).exists():shutil.copy2(source,backup/file)
 with zipfile.ZipFile(backup/'pet.zip') as z:manifest=json.loads(z.read('manifest.json'))
 assert set(manifest['clips'])==set(SPECS)
 (OUT/'original-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
 rows=[]
 for name,rel in SPECS.items():
  path=SRC/rel;cap=cv2.VideoCapture(str(path));n=int(cap.get(7));fps=cap.get(5);w=int(cap.get(3));h=int(cap.get(4))
  assert n==len(manifest['clips'][name]),(name,n)
  dest=OUT/'clips'/name/'input';dest.mkdir(parents=True,exist_ok=True)
  for i in range(n):
   ok,bgr=cap.read();assert ok
   if not (dest/f'{i:04d}.png').exists():cv2.imwrite(str(dest/f'{i:04d}.png'),bgr,[cv2.IMWRITE_PNG_COMPRESSION,2])
  cap.release()
  rows.append(dict(name=name,source=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),width=w,height=h,frames=n,fps=fps,root=str(dest.parent)))
  print(name,n,w,h,flush=True)
 (OUT/'inventory.json').write_text(json.dumps(rows,indent=2),encoding='utf8')
 print('prepared',sum(r['frames'] for r in rows),flush=True)
if __name__=='__main__':main()
