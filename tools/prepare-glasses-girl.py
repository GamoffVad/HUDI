"""Package approved full-resolution RGBA sequences for HUDI, without regenerating media."""
from pathlib import Path
from zipfile import ZipFile,ZIP_STORED
import json,hashlib,sys,importlib.util
import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
SOURCE=Path(sys.argv[1]) if len(sys.argv)>1 else ROOT/'pet-sources/glasses-girl'
OUT=ROOT/'output/glasses-girl-v2';OUT.mkdir(parents=True,exist_ok=True)
edge_spec=importlib.util.spec_from_file_location('girl_edge_refinement',Path(__file__).with_name('refine-girl-edges.py'))
edge_module=importlib.util.module_from_spec(edge_spec);edge_spec.loader.exec_module(edge_module)
CASES={'stroll':'curious-stroll-v1','surprise':'surprise-v3-kling','adjustglasses':'glasses-hair-v1','think':'look-surprise-think-v1','dance':'dynamic-dance-v1','angry':'expressive-anger-v1','groove':'funky-groove-v1','ironic':'ironic-temple-v1'}
neutral=np.array(Image.open(SOURCE/'animations/glasses-hair-v1/recut-v3/transparent-frames/0000.png').convert('RGBA'))
neutral=edge_module.refine(neutral)
assert neutral.shape==(960,960,4)
frames=OUT/'frames';frames.mkdir(exist_ok=True)
Image.fromarray(neutral).save(frames/'neutral.png')
manifest={'version':'pet-1','name':'Девушка в очках','frameSize':[960,960],'displayBounds':[355,137,252,680],'alphaPrepared':True,'breathAmplitude':0,'neutral':'neutral','frames':['neutral'],'clips':{}}
report={'sources':[],'excluded':['tecktonik-dance-v1','tecktonik-dance-v2','impatient-wait-v1','impatient-wait-v2'],'notes':{'ironic':'Eye roll and smirk with temple pointing; do not label it as precise circular finger gesture.'}}
def blend(a,b,t):
    aa=a[:,:,3:4].astype(np.float32)/255;bb=b[:,:,3:4].astype(np.float32)/255
    alpha=aa*(1-t)+bb*t;pm=a[:,:,:3]*aa*(1-t)+b[:,:,:3]*bb*t
    rgb=np.divide(pm,alpha,out=np.zeros_like(pm),where=alpha>1e-6)
    return np.uint8(np.clip(np.dstack((rgb,alpha[:,:,0]*255)).round(),0,255))
for action,scene in CASES.items():
    folder=SOURCE/'animations'/scene/'recut-v3';paths=sorted((folder/'transparent-frames').glob('*.png'))
    meta=json.loads((folder/'export-manifest.json').read_text());assert len(paths)==meta['frames'] and meta['allInside']
    first=np.array(Image.open(paths[0]).convert('RGBA'));mae=float(np.abs(first.astype(float)-neutral).mean());assert mae<2,(scene,mae)
    steps=[]
    for i,p in enumerate(paths):
        if i in (0,len(paths)-1):fid='neutral'
        else:
            a=np.array(Image.open(p).convert('RGBA'));assert a.shape==neutral.shape
            if i<5:a=blend(neutral,a,i/5)
            if i>=len(paths)-5:a=blend(a,neutral,(i-(len(paths)-5))/4)
            a=edge_module.refine(a);a[a[:,:,3]==0,:3]=0
            fid=f'{action}-{i:04d}';Image.fromarray(a).save(frames/(fid+'.png'));manifest['frames'].append(fid)
        steps.append({'frame':fid,'seconds':1/meta['fps']})
    manifest['clips'][action]=steps
    report['sources'].append({'action':action,'scene':scene,'frames':len(paths),'fps':meta['fps'],'neutralRgbaMae':mae,'sourceSha256':hashlib.sha256((folder.parent/'source.mp4').read_bytes()).hexdigest()})
    print(action,len(steps),'frames',flush=True)
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
package=OUT/'glasses-girl.pet'
with ZipFile(package,'w',ZIP_STORED) as z:
    z.write(OUT/'manifest.json','manifest.json')
    for fid in manifest['frames']:z.write(frames/(fid+'.png'),fid+'.png')
assert package.stat().st_size<512*1024**2
report.update({'package':str(package),'bytes':package.stat().st_size,'frameCount':len(manifest['frames']),'actions':list(CASES),'frameSize':[960,960],'commonNeutralEndpoints':True})
(OUT/'build-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:report[k] for k in ['package','bytes','frameCount','actions']},ensure_ascii=False),flush=True)
