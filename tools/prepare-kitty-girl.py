"""Package inspected native RGBA loops using one shared 960px app canvas."""
from pathlib import Path
from zipfile import ZipFile, ZIP_STORED
from concurrent.futures import ThreadPoolExecutor
import json, hashlib, sys, time
import numpy as np
import cv2
from PIL import Image, ImageDraw
cv2.setNumThreads(1)

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'pet-sources/kitty-girl'
OUT=ROOT/'output/kitty-girl-v1'
CASES={'lookaround':'look-around','angry':'angry-stomp','impatient':'impatient-wait',
       'indignantlean':'indignant-lean','giggle':'mischievous-giggle','surprise':'surprised-step-v2',
       'reproach':'stern-reproach','temple':'temple-circle','headtap':'head-tap',
       'dance':'playful-dance','peekaround':'peek-both-shoulders','walkaround':'walk-directions'}

def resize(im):
    # Filter premultiplied colour to avoid transparent black fringes.
    im=im.convert('RGBa').resize((960,960),Image.Resampling.LANCZOS).convert('RGBA')
    a=np.array(im);rgb=a[:,:,:3].astype(np.float32);r,g,b=rgb.transpose(2,0,1)
    seed=(r>g*1.55)&(r>b*1.5)&(r<200)&(a[:,:,3]>245)
    weight=seed.astype(np.float32);den=cv2.GaussianBlur(weight,(0,0),5)
    local=cv2.GaussianBlur(rgb*weight[:,:,None],(0,0),5)/np.maximum(den[:,:,None],1e-5)
    depth=cv2.distanceTransform((a[:,:,3]>200).astype(np.uint8),cv2.DIST_L2,3)
    yy=np.arange(a.shape[0])[:,None]
    hair=(yy<a.shape[0]*.23)|((yy<a.shape[0]*.40)&(depth<6))
    olive=hair&(a[:,:,3]>0)&(r<215)&(g>r*.8)&(b<r*.7)&(den>.01)
    a[:,:,:3]=np.where(olive[:,:,None],np.uint8(np.clip(local,0,255)),a[:,:,:3])
    return Image.fromarray(a)

def run():
    OUT.mkdir(parents=True,exist_ok=True);frames=OUT/'frames';frames.mkdir(exist_ok=True)
    neutral=resize(Image.open(SOURCE/'animations/look-around/transparent-frames/0000.png').convert('RGBA'))
    neutral.save(frames/'neutral.png',compress_level=6)
    a=np.array(neutral);ys,xs=np.where(a[:,:,3]>128)
    bounds=[int(xs.min()),int(ys.min()),int(xs.max()-xs.min()+1),int(ys.max()-ys.min()+1)]
    manifest={'version':'pet-1','name':'Девочка в розовом','frameSize':[960,960],
              'displayBounds':bounds,'alphaPrepared':True,'breathAmplitude':0,
              'neutral':'neutral','frames':['neutral'],'clips':{}}
    report={'sources':[],'excluded':['surprised-step (source hair clipped at top)'],
            'nativeFrameSize':[1440,1440],'runtimeFrameSize':[960,960],
            'resampling':'single fixed premultiplied Lanczos resize for every frame; native masters retained',
            'runtimeEdgeCleanup':'Local warm brown RGB extension only into remaining olive hair pixels; alpha unchanged.'}
    sheet=Image.new('RGB',(4*360,3*380),(20,20,20));draw=ImageDraw.Draw(sheet)
    for scene_index,(action,scene) in enumerate(CASES.items()):
        folder=SOURCE/'animations'/scene
        if '--wait-ready' in sys.argv:
            deadline=time.monotonic()+1800
            neutral_time=(SOURCE/'animations/look-around/frames/0000.png').stat().st_mtime
            export=folder/'export-manifest.json';audit=folder/'matte-audit.json'
            while not(export.exists() and audit.exists() and export.stat().st_mtime>audit.stat().st_mtime>neutral_time):
                if time.monotonic()>deadline:raise TimeoutError('Not finalized: '+scene)
                time.sleep(2)
        meta=json.loads((folder/'export-manifest.json').read_text())
        assert meta['allInside'] and meta['firstLastIdentical'],scene
        paths=sorted((folder/'transparent-frames').glob('*.png'))
        assert len(paths)==meta['frames'],scene
        for p in (paths[0],paths[-1]):
            assert np.array_equal(np.array(resize(Image.open(p).convert('RGBA'))),a),(scene,'neutral mismatch')
        def encode(pair):
            i,path=pair;fid=f'{action}-{i:04d}'
            im=resize(Image.open(path).convert('RGBA'));im.save(frames/(fid+'.png'),compress_level=6)
            box=im.getbbox();assert box and min(box[:2])>0 and box[2]<960 and box[3]<960,(scene,i,box)
            return fid
        with ThreadPoolExecutor(max_workers=4) as pool:
            ids=list(pool.map(encode,enumerate(paths[1:-1],1)))
        manifest['frames']+=ids
        manifest['clips'][action]=[{'frame':fid,'seconds':1/meta['fps']} for fid in ['neutral',*ids,'neutral']]
        im=Image.open(paths[len(paths)//2]).convert('RGBA').resize((360,360),Image.Resampling.LANCZOS)
        bg=Image.new('RGBA',im.size,(20,20,20,255));bg.alpha_composite(im)
        x=(scene_index%4)*360;y=(scene_index//4)*380;sheet.paste(bg.convert('RGB'),(x,y+20));draw.text((x+8,y+3),action,fill='white')
        report['sources'].append({'action':action,'scene':scene,'frames':len(paths),'fps':meta['fps'],
             'sourceSha256':hashlib.sha256((folder/'source.mp4').read_bytes()).hexdigest()})
        print(action,len(paths),'frames',flush=True)
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    sheet.save(OUT/'all-actions.jpg',quality=95)
    package=OUT/'kitty-girl.pet'
    with ZipFile(package,'w',ZIP_STORED) as z:
        z.write(OUT/'manifest.json','manifest.json')
        for fid in manifest['frames']:z.write(frames/(fid+'.png'),fid+'.png')
    assert package.stat().st_size<1024*1024**2
    report.update({'package':str(package),'bytes':package.stat().st_size,'frameCount':len(manifest['frames']),
                   'actions':list(CASES),'commonNeutralEndpoints':True,'sha256':hashlib.sha256(package.read_bytes()).hexdigest()})
    (OUT/'build-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print('Package complete:',report['bytes'],'bytes',flush=True)

if __name__=='__main__':run()
