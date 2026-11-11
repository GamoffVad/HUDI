"""Audit Blender masters, preserve existing clip geometry, and package HUDI Ink."""
from pathlib import Path
from zipfile import ZipFile,ZIP_STORED
import cv2,numpy as np,json,hashlib,shutil,sys
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/(sys.argv[1] if len(sys.argv)>1 else 'output/ink-blender-v2')
def main():
 progress=json.loads((OUT/'progress.json').read_text());assert progress['status']=='complete',progress
 inv=json.loads((OUT/'inventory.json').read_text());m=json.loads((OUT/'original-manifest.json').read_text(encoding='utf8'))
 frames=OUT/'frames';frames.mkdir(exist_ok=True);qa=OUT/'qa';qa.mkdir(exist_ok=True)
 transform=np.float32(json.loads((ROOT/'output/ink-pet-v2.5/qa/registration.json').read_text())['transform'])
 records=[];margins={};contact=[]
 for clip in inv:
  name=clip['name'];rows=[]
  for i,step in enumerate(m['clips'][name]):
   path=Path(clip['root'])/'rgba'/f'{i:04d}.png'
   p=cv2.imread(str(path),cv2.IMREAD_UNCHANGED);assert p is not None and p.shape==(960,960,4) and p.dtype==np.uint16,path
   p=p.astype(np.float32)/65535
   # Registration only: no external alpha/keying/spill edits. Keep one fixed
   # uniform transform for the whole fire clip as in the accepted old package.
   if name=='firetrick':
    p[:,:,:3]*=p[:,:,3:4];p=cv2.warpAffine(p,transform,(960,960),flags=cv2.INTER_LINEAR)
    p[:,:,:3]=np.divide(p[:,:,:3],p[:,:,3:4],out=np.zeros_like(p[:,:,:3]),where=p[:,:,3:4]>1e-8)
   a=p[:,:,3];b,g,r=p[:,:,:3].transpose(2,0,1)
   yy,xx=np.where(a>8/255);assert len(xx),path
   box=[int(xx.min()),int(yy.min()),int(xx.max()+1),int(yy.max()+1)]
   edge=np.concatenate([a[0],a[-1],a[:,0],a[:,-1]])
   row=dict(clip=name,index=i,frame=step['frame'],bounds=box,edgeAlphaMax=float(edge.max()),greenPixels=int(((g>np.maximum(r,b)+8/255)&(a>10/255)).sum()))
   rows.append(row);records.append(row)
   pixels=np.uint8(np.clip(np.rint(p*255),0,255))
   cv2.imwrite(str(frames/(step['frame']+'.png')),pixels,[cv2.IMWRITE_PNG_COMPRESSION,3])
   if i in (0,clip['frames']//2,clip['frames']-1):contact.append((name,i,step['frame']))
  margins[name]=np.array([[r['bounds'][0],r['bounds'][1],960-r['bounds'][2],960-r['bounds'][3]] for r in rows]).min(0).tolist()
  print(name,'frames',len(rows),'margins',margins[name],flush=True)
 shutil.copy2(frames/'hood-000.png',frames/'neutral.png')
 m.update(name='Инк',matteVersion='Blender-5.2-native-sRGB-crisp-v2',description='Ten original accepted animations re-keyed with Blender via MCP; crisp clothing alpha with unchanged 50 percent contour; soft fire retained; native 16-bit masters.')
 issues=[r for r in records if r['edgeAlphaMax']>1/255 or r['greenPixels']>0]
 report=dict(frames=len(records),clips=len(inv),nativeResolution=[960,960],runtimeResolution=[960,960],greenPixels=sum(r['greenPixels'] for r in records),margins=margins,issues=issues,registration=transform.tolist(),creditsSpent=0)
 (qa/'all-frames.json').write_text(json.dumps(records,indent=2),encoding='utf8');(qa/'report.json').write_text(json.dumps(report,indent=2),encoding='utf8')
 assert not issues,[(r['frame'],r['edgeAlphaMax'],r['greenPixels']) for r in issues[:10]]
 sheet=Image.new('RGB',(960,len(contact)*340),(45,45,45));draw=ImageDraw.Draw(sheet)
 for row,(name,i,ident) in enumerate(contact):
  im=Image.open(frames/(ident+'.png')).convert('RGBA')
  for col,bg in enumerate([(0,0,0,255),(255,255,255,255),(100,100,100,255)]):
   view=Image.alpha_composite(Image.new('RGBA',im.size,bg),im).convert('RGB').resize((320,320),Image.Resampling.LANCZOS)
   sheet.paste(view,(col*320,row*340));draw.text((col*320+4,row*340+322),f'{name} {i}',fill='white')
 sheet.save(qa/'all-clips-review.jpg',quality=95)
 (OUT/'manifest.json').write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf8')
 with ZipFile(OUT/'Ink.pet','w',ZIP_STORED) as z:
  z.writestr('manifest.json',json.dumps(m,ensure_ascii=False))
  z.write(OUT/'backup/appearance.json','appearance.json')
  for ident in m['frames']:z.write(frames/(ident+'.png'),ident+'.png')
 size=(OUT/'Ink.pet').stat().st_size;assert size<1024**3,size
 report.update(packageBytes=size,sha256=hashlib.sha256((OUT/'Ink.pet').read_bytes()).hexdigest());(qa/'report.json').write_text(json.dumps(report,indent=2),encoding='utf8')
 print('PACKAGE',size,report['sha256'],flush=True)
if __name__=='__main__':main()
