"""Assemble Blender RGBA masters into HUDI; geometric resampling only, no keying."""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from zipfile import ZipFile,ZIP_STORED
from PIL import Image,ImageDraw
import cv2,numpy as np,json,hashlib,shutil,sys
ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/hudi-blender-v1';FRAMES=OUT/'frames';QA=OUT/'qa'
def process(job):
 meta,i=job;ident=f'{meta["name"]}-{i:03d}'
 folder='rgba-clean' if (Path(meta['root'])/'rgba-clean').exists() else ('rgba-final' if meta['name']=='march' else 'rgba')
 path=Path(meta['root'])/folder/f'{i:04d}.png'
 p=cv2.imread(str(path),cv2.IMREAD_UNCHANGED);assert p is not None and p.dtype==np.uint16,(path,None if p is None else p.dtype)
 p=p[:,:,[2,1,0,3]].astype(np.float32)/65535
 a=p[:,:,3];ys,xs=np.where(a>.04)
 native_bounds=[int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1)]
 edge=np.concatenate((a[0],a[-1],a[:,0],a[:,-1]));source_border_alpha=float(edge.max())
 # One fixed transform matches the existing built-in geometry. The entire
 # source now fits in640²; never crop a moving body/effect independently.
 if meta['legacy']:
  matrix=np.float32([[-.46,0,499.32],[0,.46*1168/meta['height'],51.8]])
 else:
  matrix=np.float32([[-.63503,0,.63503*959+14.826],[0,.63503,25.11]])
 p[:,:,:3]*=a[:,:,None]
 p=cv2.warpAffine(p,matrix,(640,640),flags=cv2.INTER_AREA)
 a=np.clip(p[:,:,3:4],0,1);rgb=np.divide(p[:,:,:3],a,out=np.zeros_like(p[:,:,:3]),where=a>1e-7)
 rgba=np.rint(np.clip(np.dstack([rgb,a]),0,1)*255).astype('uint8')
 Image.fromarray(rgba).save(FRAMES/(ident+'.png'),compress_level=3)
 a=rgba[:,:,3];yy,xx=np.where(a>10);bounds=[int(xx.min()),int(yy.min()),int(xx.max()+1),int(yy.max()+1)]
 r,g,b=rgba[:,:,:3].astype(np.int16).transpose(2,0,1)
 green=(g>np.maximum(r,b)+8)&(a>10)
 return dict(frame=ident,nativeBounds=native_bounds,sourceBorderMaxAlpha=source_border_alpha,bounds=bounds,minimumMargin=min(bounds[0],bounds[1],640-bounds[2],640-bounds[3]),greenPixels=int(green.sum()),alphaAtOutputBorder=int(max(a[0].max(),a[-1].max(),a[:,0].max(),a[:,-1].max())))
def main():
 FRAMES.mkdir(exist_ok=True);QA.mkdir(exist_ok=True);cv2.setNumThreads(1)
 inventory=json.loads((OUT/'inventory.json').read_text());alljobs=[(m,i) for m in inventory for i in m['indices']];jobs=alljobs
 reused=[]
 if '--refresh-cleaned' in sys.argv:
  cleaned=set(json.loads((OUT/'cleanup-clips.json').read_text()))
  reused=[r for r in json.loads((QA/'frames.json').read_text()) if r['frame'].rsplit('-',1)[0] not in cleaned]
  jobs=[(m,i) for m,i in alljobs if m['name'] in cleaned]
 with ThreadPoolExecutor(max_workers=4) as pool:
  records=[]
  for row in pool.map(process,jobs):
   records.append(row)
   if len(records)%200==0:print('ASSEMBLED',len(records),len(jobs),flush=True)
 records+=reused
 shutil.copy2(FRAMES/'idle-000.png',FRAMES/'neutral.png')
 m=json.loads((OUT/'backup/manifest.json').read_text());old_clips=m['clips'];old_display=m['displayBounds']
 m.update(version='7.3.17-blender',name='HUDI',matteVersion='Blender-5.2.2-calibrated-plate-v2',rendering='Blender compositor via MCP; whole original frames',contactShadow='original source floor darkening retained in alpha',mattingRecipe='recipe.json + recipe-calibrated.json')
 m['blenderRebuild']={'sourcePerformances':len(inventory),'nativeFrames':len(alljobs),'maskGeneratedBy':'Blender compositor: native key for uniform plates, calibrated RGB plate solve for gradient plates','spillGeneratedBy':'Blender ColorSpill / foreground plate unmix','resolution':'native 16-bit RGBA masters;640x640 runtime','fixedLegacyTransform':[[-.46,0,499.32],[0,.46,51.8]],'sourceHeight1172NormalizedTo1168':True,'fixedNewTransform':[[-.63503,0,.63503*959+14.826],[0,.63503,25.11]],'oldAlphaUsed':False}
 assert set(m['frames'])=={'neutral'}|{r['frame'] for r in records}
 assert m['clips']==old_clips and m['displayBounds']==old_display
 (OUT/'manifest.json').write_text(json.dumps(m,ensure_ascii=False,indent=2),encoding='utf8')
 with ZipFile(OUT/'sprites.zip','w',ZIP_STORED) as z:
  z.writestr('manifest.json',json.dumps(m,ensure_ascii=False))
  for ident in m['frames']:z.write(FRAMES/(ident+'.png'),ident+'.png')
 total=(OUT/'sprites.zip').stat().st_size;assert total<1024**3
 report={'frames':len(m['frames']),'clips':list(m['clips']),'allOriginalFrameIdsPreserved':True,'allClipMappingsUnchanged':True,'displayBoundsUnchanged':True,'nativeMasters16Bit':True,'creditsSpent':0,'alphaBorderViolations':[r for r in records if r['alphaAtOutputBorder']>0],'sourceEdgeReview':[r for r in records if r['sourceBorderMaxAlpha']>.04],'greenPixels':sum(r['greenPixels'] for r in records),'packageBytes':total,'sha256':hashlib.sha256((OUT/'sprites.zip').read_bytes()).hexdigest()}
 (QA/'frames.json').write_text(json.dumps(records,indent=2));(QA/'report.json').write_text(json.dumps(report,indent=2))
 selected=['neutral','think-060','hood-090','return-dance-030','angry-040','salute-120','notebook-220','march-220','fire-180','fire-280','firecalm-250']
 sheet=Image.new('RGB',(960,len(selected)*344),(60,60,60));d=ImageDraw.Draw(sheet)
 for row,ident in enumerate(selected):
  p=Image.open(FRAMES/(ident+'.png'))
  for col,bg in enumerate([(0,0,0),(255,255,255),(100,100,100)]):
   q=Image.new('RGBA',p.size,bg+(255,));q.alpha_composite(p);q=q.convert('RGB').resize((320,320),Image.Resampling.LANCZOS);sheet.paste(q,(col*320,row*344));d.text((col*320+4,row*344+324),ident,fill='white')
 sheet.save(QA/'review.jpg',quality=95)
 print(json.dumps({k:v for k,v in report.items() if k!='sourceEdgeReview'}),flush=True)
if __name__=='__main__':main()
