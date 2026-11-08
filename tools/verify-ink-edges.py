"""Read-only comparison of installed-resolution silhouettes and opaque details."""
from pathlib import Path
from PIL import Image
import cv2,numpy as np,json,runpy
ROOT=Path(__file__).resolve().parents[1];OLD=ROOT/'output/ink-pet-v2';NEW=ROOT/'output/ink-pet-v2.1'
def main():
 manifest=json.loads((NEW/'manifest.json').read_text(encoding='utf8'));rows=[]
 sources=json.loads((NEW/'qa/report.json').read_text(encoding='utf8'))['sources']
 for name,clip in manifest['clips'].items():
  cap=cv2.VideoCapture(str(ROOT/sources[name]['path']));old_soft=new_soft=bad=0;area_deltas=[]
  for step in clip:
   ok,bgr=cap.read();assert ok
   rgba=np.array(Image.open(NEW/'frames'/(step['frame']+'.png')));a=rgba[:,:,3]
   rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB).astype(float);r,g,b=rgb.transpose(2,0,1)
   bright=(r>130)&(g>110)&(b>60)&(g<np.maximum(r,b)+4)
   core=cv2.erode(bright.astype('uint8'),np.ones((5,5),'uint8')).astype(bool)
   bad+=int((core&(a<250)).sum())
   # Compare both at the user's current 707px canvas, with identical filtering.
   old=np.array(Image.open(OLD/'frames'/(step['frame']+'.png')).convert('RGBA').resize((707,707),Image.Resampling.BICUBIC))[:,:,3]
   new=np.array(Image.fromarray(rgba).resize((707,707),Image.Resampling.BICUBIC))[:,:,3]
   old_soft+=int(((old>10)&(old<245)).sum());new_soft+=int(((new>10)&(new<245)).sum());area_deltas.append(float(abs((new>=128).sum()-(old>=128).sum())/max((old>=128).sum(),1)))
  cap.release();rows.append({'clip':name,'frames':len(clip),'opaque_bright_core_holes':bad,'old_soft_edge_pixels':old_soft,'new_soft_edge_pixels':new_soft,'soft_edge_reduction':1-new_soft/max(old_soft,1),'max_relative_silhouette_area_change':max(area_deltas)});print(name,rows[-1],flush=True)
 oldmanifest=json.loads((OLD/'manifest.json').read_text(encoding='utf8'))
 same_clips=manifest['clips']==oldmanifest['clips'];same_geometry=np.allclose(np.array(manifest['displayBounds'])/manifest['frameSize'][0],np.array(oldmanifest['displayBounds'])/oldmanifest['frameSize'][0])
 report={'success':same_clips and same_geometry and all(r['opaque_bright_core_holes']==0 and r['new_soft_edge_pixels']<r['old_soft_edge_pixels'] and r['max_relative_silhouette_area_change']<.02 for r in rows),'clip_timing_unchanged':same_clips,'normalized_display_geometry_unchanged':bool(same_geometry),'clips':rows};(NEW/'qa/edge-audit.json').write_text(json.dumps(report,indent=2),encoding='utf8');print('success',report['success'])
if __name__=='__main__':
 main()
 if not json.loads((NEW/'qa/edge-audit.json').read_text(encoding='utf8'))['success']:
  raise SystemExit(1)
