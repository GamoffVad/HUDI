"""Independent decoded-pixel regression and transparent-frame preview audit."""
from pathlib import Path
from zipfile import ZipFile
from concurrent.futures import ThreadPoolExecutor
import io
import json
import numpy as np
import cv2
from PIL import Image
import imageio_ffmpeg

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/hudi-pet-v7.3'
QA=OUT/'qa/hood-juggle'
pack=OUT/'sprites.zip';oldpack=OUT/'rollback-before-hood-juggle/sprites.zip'
processing=QA/'processing.json'
if not processing.exists() or pack.stat().st_mtime<processing.stat().st_mtime:
    raise RuntimeError('Final package is not ready; processing.json must precede sprites.zip.')

def decoded(archive,name):
    with Image.open(io.BytesIO(archive.read(name+'.png'))) as image:
        return np.array(image.convert('RGBA'))

checks={};report={'checks':checks}
with ZipFile(oldpack) as before,ZipFile(pack) as after:
    old=json.loads(before.read('manifest.json'));new=json.loads(after.read('manifest.json'))
    expected_ids=old['frames']
    checks['previous_3707_frames_present']=len(expected_ids)==3707 and set(expected_ids).issubset(new['frames'])
    checks['common_canvas_and_visible_viewport']=new['frameSize']==[640,704] and new['displayBounds']==[128,144,384,500]
    failures=[]
    def compare(name):
        source=decoded(before,name);target=decoded(after,name)
        return name if target.shape!=(704,640,4) or source.shape!=(640,640,4) or np.any(target[:64]) or not np.array_equal(target[64:],source) else None
    with ThreadPoolExecutor(max_workers=4) as pool:
        for index,result in enumerate(pool.map(compare,expected_ids)):
            if result:failures.append(result)
            if index%500==0:print(f'Compared old pixels {index+1}/{len(expected_ids)}',flush=True)
    checks['every_old_frame_is_exactly_shifted_down_64']=not failures
    report['previousFramesCompared']=len(expected_ids);report['changedOldFrames']=failures
    for clip,steps in old['clips'].items():
        checks['old_clip_'+clip+'_timing_unchanged']=steps==new['clips'].get(clip)

    metrics={};new_count=0;preview_count=0
    writer=imageio_ffmpeg.write_frames(str(QA/'preview.mp4'),(640,704),fps=24,codec='libx264',
        pix_fmt_in='rgb24',pix_fmt_out='yuv420p',output_params=['-crf','18','-preset','fast','-movflags','+faststart'])
    writer.send(None)
    try:
        for clip,expected_count in [('hoodtighten',241),('juggle',289)]:
            steps=new['clips'].get(clip,[])
            checks[clip+'_role_and_complete_24fps_clip']=len(steps)==expected_count and all(abs(step['seconds']-1/24)<1e-10 for step in steps)
            bad_bounds=[];green_frames=[];maximum_green=0;minimum_margin=100000
            for index,step in enumerate(steps):
                rgba=decoded(after,step['frame']);alpha=rgba[:,:,3]
                ys,xs=np.where(alpha>1)
                if not len(xs):bad_bounds.append(step['frame']+':empty');continue
                margin=min(int(xs.min()),int(ys.min()),640-1-int(xs.max()),704-1-int(ys.max()))
                minimum_margin=min(minimum_margin,margin)
                if margin<2:bad_bounds.append(step['frame'])
                rgb=rgba[:,:,:3].astype(np.int16)
                green=(alpha>16)&(rgb[:,:,1]>rgb[:,:,0]+30)&(rgb[:,:,1]>rgb[:,:,2]+30)&(rgb[:,:,1]>50)
                green_count=int(green.sum());maximum_green=max(maximum_green,green_count)
                if green_count>max(3,int((alpha>16).sum())//10000):green_frames.append({'frame':step['frame'],'pixels':green_count})
                a=alpha[:,:,None].astype(np.float32)/255
                dark=np.array([24,26,30],dtype=np.float32)
                composite=np.clip(rgba[:,:,:3]*a+dark*(1-a),0,255).astype(np.uint8)
                writer.send(composite);preview_count+=1;new_count+=1
            checks[clip+'_all_soft_alpha_inside_canvas']=not bad_bounds
            checks[clip+'_no_strong_chroma_haze']=not green_frames
            metrics[clip]={'framesAudited':len(steps),'minimumMarginAlphaAbove1':minimum_margin,'badBounds':bad_bounds,
                'maximumStrongGreenPixels':maximum_green,'strongGreenFrames':green_frames}
    finally:writer.close()
    checks['all_530_new_frames_audited']=new_count==530
    checks['frame_ids_unique_and_total_correct']=len(new['frames'])==4237 and len(set(new['frames']))==4237
    report['newClips']=metrics;report['previewFrames']=preview_count;report['previewFps']=24
    report['previewSeconds']=preview_count/24;report['previewClipOrder']=['hoodtighten','juggle']
    report['previewSource']='Decoded final packaged transparent PNGs alpha-composited over RGB24,26,30; no generation, per-frame registration or crop.'
cap=cv2.VideoCapture(str(QA/'preview.mp4'))
fps=cap.get(cv2.CAP_PROP_FPS);width=int(cap.get(cv2.CAP_PROP_FRAME_WIDTH));height=int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT));decoded_count=0
while True:
    ok,frame=cap.read()
    if not ok:break
    decoded_count+=1
cap.release()
report['previewDecoded']={'frames':decoded_count,'fps':fps,'width':width,'height':height}
checks['encoded_preview_decodes_complete']=decoded_count==530 and fps==24 and [width,height]==[640,704]
report['success']=all(checks.values())
(QA/'verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='checks'},indent=2),flush=True)
if not report['success']:raise SystemExit(1)
