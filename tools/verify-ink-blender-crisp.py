from pathlib import Path
import cv2,numpy as np,json
ROOT=Path(__file__).resolve().parents[1]/'output'
rows=json.loads((ROOT/'ink-blender-v2/inventory.json').read_text());checks=[]
for row in rows:
 name=row['name']
 picks=range(row['frames']) if name=='firetrick' else sorted(set([0,24,row['frames']//2,row['frames']-1]))
 for i in picks:
  a=cv2.imread(str(ROOT/'ink-blender-v1/clips'/name/'rgba'/f'{i:04d}.png'),-1)
  b=cv2.imread(str(ROOT/'ink-blender-v2/clips'/name/'rgba'/f'{i:04d}.png'),-1)
  assert a is not None and b is not None
  aa=a[:,:,3];ba=b[:,:,3]
  record=dict(clip=name,frame=i,contourChanged=int(((aa>=32768)!=(ba>=32768)).sum()),rgbChanged=int((a[:,:,:3]!=b[:,:,:3]).sum()),oldSoftPixels=int(((aa>256)&(aa<65279)).sum()),newSoftPixels=int(((ba>256)&(ba<65279)).sum()),exactPixels=bool(np.array_equal(a,b)))
  assert record['contourChanged']==0,record
  if name=='firetrick':assert record['exactPixels'],record
  else:assert record['newSoftPixels']<record['oldSoftPixels'],record
  checks.append(record)
out=dict(success=True,checkedFrames=len(checks),allFireFramesUnchanged=True,unchangedHalfAlphaContour=True,records=checks)
(ROOT/'ink-blender-v2/qa/crisp-edge-checks.json').write_text(json.dumps(out,indent=2),encoding='utf8')
print('PASS',len(checks),'frames; contour unchanged; fire pixel-identical',flush=True)
