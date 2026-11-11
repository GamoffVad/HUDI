"""Package the user-approved CorridorKey revision without changing its matte."""
import hashlib,json,shutil
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
from zipfile import ZipFile,ZIP_STORED
import importlib.metadata
import cv2,numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
PET=ROOT/'pet-sources/tousled-girl'
ANIM=PET/'animations/look-surprise-kling4'
SRC=ANIM/'revised-v3'
OUT=ROOT/'output/tousled-girl-v1'
SIZE=1000
cv2.setNumThreads(1)

def sha(p):return hashlib.file_digest(p.open('rb'),'sha256').hexdigest()
def linear(x):return np.where(x<=.04045,x/12.92,((x+.055)/1.055)**2.4)
def srgb(x):return np.where(x<=.0031308,x*12.92,1.055*np.maximum(x,0)**(1/2.4)-.055)

def freeze_recipe():
    folder=PET/'approved-matting';folder.mkdir(exist_ok=True)
    sources=[ANIM/'edge-review/physical_matte.py',ANIM/'edge-review/test_source_matte.py',
             ANIM/'edge-review/export_revision.py',ANIM/'edge-review/verify_revision.py',
             ANIM/'finish_cutout.py',ROOT/'output/corridorkey-hair-test/process_sample.py',
             ROOT/'output/corridorkey-hair-test/refine_edges.py']
    files={}
    for path in sources:
        dest=folder/path.name
        if dest.exists():assert sha(dest)==sha(path),'Approved script changed: '+path.name
        else:shutil.copy2(path,dest)
        files[path.name]={'sha256':sha(dest),'originalPath':str(path.relative_to(ROOT))}
    for name in ['manifest.json','verification.json']:
        shutil.copy2(SRC/name,folder/('approved-'+name))
    profile={
      'approvedOn':'2026-11-10','approval':'User accepted revised-v3 and requested that the method and all settings be saved.',
      'input':{'file':'animations/look-surprise-kling4/source.mp4','sha256':sha(ANIM/'source.mp4'),'resolution':[1440,1440],'fps':24,'frames':193},
      'corridorKey':{'version':'1.0','commit':'97e55a453060745bead1befd293f6e523c4b845c','checkpoint':'CorridorKey_v1.0.safetensors','checkpointSha256':sha(Path('C:/Users/gamx8/.codex/tools/CorridorKey/CorridorKeyModule/checkpoints/CorridorKey_v1.0.safetensors')),'device':'CUDA','modelSize':2048,'modelPrecision':'float16','mixedPrecision':False,'useRefiner':True,'skipCompile':True,'despillStrength':1.0,'autoDespeckle':True,'despeckleSize':100,'postProcessOnGpu':False,'alphaHint':{'formula':'clip((0.70-(G-max(R,B))/max(G,0.04))/0.45,0,1)','gaussianKernel':[5,5],'sigma':.8}},
      'baseFinish':{'rgbExtensionMaxDistance':80,'cleanAlpha':.999,'greenDifferenceMax':.004,'relativeGreenMax':.02,'sampleErosion':[5,5],'referenceBlur':[5,5,.8],'chromaWeightOffset':.005,'chromaWeightRange':.07,'distanceFadePixels':6,'alphaSupport':.0001,'hairFadeY':[.69,.74],'shadow':'Exact shadow_from_source in frozen finish_cutout.py'},
      'approvedHairRefinement':{'cleanAlpha':.998,'cleanGreenExcessMax':-.025,'cleanNeutralRatio':[.70,1.02],'cleanRedBlueFactor':1.04,'cleanSampleErosion':[3,3],'inputLinearBlur':[3,3,.6],'chromaSumFloor':.002,'chromaBlurSigma':3,'backgroundSideStrips':60,'linearSolveDenominatorFloor':1e-6,'guideRatio':[.70,1.02],'guideRatioDenominatorFloor':.02,'guideBackgroundGreenFloor':.5,'guideBlur':[3,3,.55],'guidedFilterRadius':4,'guidedFilterEpsilon':.0001,'unpremultiplyAlphaThreshold':.001,'foregroundCeilingMultiplier':2.5,'foregroundCeilingFloor':.03,'supportAlphaScale':.003,'premultAndAlphaBlur':[3,3,.75],'finalUnpremultiplyThreshold':.0001,'hairFadeY':[.69,.74],'coreAlpha':.995,'edgeBandPixels':14,'edgeBandFadePixels':8,'greenBandOffset':.002,'greenBandRange':.035,'silhouetteErosion':False,'globalSharpening':False},
      'output':{'folder':'animations/look-surprise-kling4/revised-v3/rgba','format':'16-bit straight sRGB RGBA PNG; linear alpha','firstLastIdentical':True,'lowerBodyAndShadowUnchanged':True,'frameSha256':{p.name:sha(p) for p in sorted((SRC/'rgba').glob('*.png'))}},
      'scripts':files,'dependencies':{name:importlib.metadata.version(name) for name in ['numpy','opencv-python','scipy','Pillow','imageio-ffmpeg']},
      'scope':'Approved for this brown-haired subject at 1440px; parameters must be recalibrated for other subjects.'}
    (folder/'recipe.json').write_text(json.dumps(profile,ensure_ascii=False,indent=2),encoding='utf-8')
    return sha(folder/'recipe.json')

def encode(i):
    src=cv2.cvtColor(cv2.imread(str(SRC/'rgba'/f'{i:04d}.png'),-1),cv2.COLOR_BGRA2RGBA).astype(np.float32)/65535
    alpha=src[...,3:4]
    packed=np.concatenate([linear(src[...,:3])*alpha,alpha],axis=2)
    packed=cv2.resize(packed,(SIZE,SIZE),interpolation=cv2.INTER_LANCZOS4)
    a=np.clip(packed[...,3:4],0,1)
    rgb=srgb(np.clip(np.divide(packed[...,:3],a,out=np.zeros_like(packed[...,:3]),where=a>1e-6),0,1))
    rgba=np.uint8(np.clip(np.concatenate([rgb,a],axis=2),0,1)*255+.5)
    fid='neutral' if i==0 else f'lookaround-{i:04d}'
    cv2.imwrite(str(OUT/'frames'/f'{fid}.png'),cv2.cvtColor(rgba,cv2.COLOR_RGBA2BGRA),[cv2.IMWRITE_PNG_COMPRESSION,6])
    y,x=np.where(rgba[...,3]>2)
    assert min(x.min(),y.min())>0 and max(x.max(),y.max())<SIZE-1
    return fid

def main():
    assert json.loads((SRC/'verification.json').read_text())['all_193_rgba_frames_valid']
    assert sha(SRC/'rgba/0000.png')==sha(SRC/'rgba/0192.png')
    recipe=freeze_recipe()
    (OUT/'frames').mkdir(parents=True,exist_ok=True)
    with ThreadPoolExecutor(max_workers=3) as pool:ids=list(pool.map(encode,range(192)))
    neutral=np.array(Image.open(OUT/'frames/neutral.png'))
    y,x=np.where(neutral[...,3]>128)
    bounds=[int(x.min()),int(y.min()),int(x.max()-x.min()+1),int(y.max()-y.min()+1)]
    steps=[{'frame':fid,'seconds':1/24} for fid in [*ids,'neutral']]
    manifest={'version':'pet-1','id':'tousled-girl','name':'Кудряшка','frameSize':[SIZE,SIZE],
              'displayBounds':bounds,'alphaPrepared':True,'breathAmplitude':0,'horizontalMirrored':False,
              'neutral':'neutral','frames':ids,'clips':{'lookaround':steps},'roles':{'lookaround':'lookaround'},
              'description':'Осматривается, удивляется и возвращается в исходную позу. Одобренная обработка волос CorridorKey revised-v3.'}
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    package=OUT/'tousled-girl.pet'
    with ZipFile(package,'w',ZIP_STORED) as z:
        z.write(OUT/'manifest.json','manifest.json')
        for fid in ids:z.write(OUT/'frames'/f'{fid}.png',f'{fid}.png')
    assert package.stat().st_size<1024**3
    bg=Image.new('RGBA',(SIZE*2,SIZE),'white')
    bg.paste(Image.new('RGBA',(SIZE,SIZE),'black'),(0,0));im=Image.fromarray(neutral)
    bg.alpha_composite(im,(0,0));bg.alpha_composite(im,(SIZE,0));bg.convert('RGB').save(OUT/'package-preview.png')
    report={'name':'Кудряшка','package':str(package),'sha256':sha(package),'bytes':package.stat().st_size,'uniqueFrames':192,'clipSteps':193,'fps':24,'durationSeconds':193/24,'frameSize':[SIZE,SIZE],'masterSize':[1440,1440],'displayBounds':bounds,'resampling':'One fixed linear-light premultiplied Lanczos pass; no additional matte or colour changes','approvedRecipeSha256':recipe,'firstLastFrame':'neutral'}
    (OUT/'build-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(report,ensure_ascii=False),flush=True)

if __name__=='__main__':main()
