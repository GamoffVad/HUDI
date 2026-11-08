"""Alpha-preserving, component-local removal of green/olive edge contamination."""
from pathlib import Path
import json
from zipfile import ZipFile, ZIP_STORED
import numpy as np
import cv2
from scipy.ndimage import distance_transform_edt
from PIL import Image, ImageDraw

VERSION='component-rgb-v2'
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'output/ink-pet'

def repair_rgba(pixels):
    result=pixels.copy();rgb=pixels[:,:,:3].astype(np.float32);alpha=pixels[:,:,3]
    visible=alpha>2
    # Very faint source haze must not push the actual silhouette boundary inward.
    depth=distance_transform_edt(alpha>=128)
    r,g,b=rgb.transpose(2,0,1)
    olive=np.minimum(r,g)-b
    green=g-np.maximum(r,b)
    # Yellow-green already clipped to R=G is just as contaminated as pure green.
    confidence=np.maximum(np.clip((olive-1)/5,0,1)*np.clip((g-r+25)/15,0,1),np.clip((green-1)/5,0,1))
    band=np.clip((7-depth)/1.5,0,1)
    # Separate meaningful foreground components before assigning low-alpha haze.
    # At alpha>2, source haze can connect a floating glyph to the whole body.
    count,solid,stats,_=cv2.connectedComponentsWithStats((alpha>=32).astype(np.uint8),8)
    if count>1:
        _,nearest=distance_transform_edt(solid==0,return_indices=True)
        labels=solid[nearest[0],nearest[1]]
        largest=1+np.argmax(stats[1:,cv2.CC_STAT_AREA])
        band[(labels!=largest)&visible]=1
    else:
        labels=np.zeros_like(alpha,dtype=np.int32)
    protected_ivory=(depth>=7)&(rgb.min(axis=2)>100)
    # Follow a strong olive patch connected to the boundary when compression
    # spill extends deeper than the geometric band. Disconnected eye regions
    # remain protected, and this never changes alpha or component geometry.
    candidates=(olive>6)&(g>=r-12)&visible&~protected_ivory
    _,patches=cv2.connectedComponents(candidates.astype(np.uint8),8)
    boundary_patches=np.unique(patches[candidates&(depth<=5.5)])
    if len(boundary_patches):band[np.isin(patches,boundary_patches)&candidates]=1
    band[protected_ivory]=0
    weight=confidence*band*(alpha>0)
    affected=weight>.001
    if not affected.any():return result
    clean=(alpha>=250)&(confidence<.01)&(depth>=2)
    target=rgb.copy()
    for label in range(1,count):
        ys,xs=np.where((labels==label)&visible)
        if not len(xs):continue
        x,y=int(xs.min()),int(ys.min());w,h=int(xs.max()-x+1),int(ys.max()-y+1)
        component=(labels[y:y+h,x:x+w]==label)&visible[y:y+h,x:x+w]
        use=affected[y:y+h,x:x+w]&component
        if not use.any():continue
        seeds=clean[y:y+h,x:x+w]&component
        local=rgb[y:y+h,x:x+w]
        # No chroma normalization: tiny dark channel values must never explode.
        neutral=np.minimum.reduce(local,axis=2)+.12*(np.maximum.reduce(local,axis=2)-np.minimum.reduce(local,axis=2))
        replacement=np.repeat(neutral[:,:,None],3,axis=2)
        if seeds.any():
            distance,indices=distance_transform_edt(~seeds,return_indices=True)
            neighbor=local[indices[0],indices[1]]
            valid=(distance<=8)&(np.max(neighbor,axis=2)>=12)
            replacement[valid]=neighbor[valid]
        target[y:y+h,x:x+w][use]=replacement[use]
    changed=rgb*(1-weight[:,:,None])+target*weight[:,:,None]
    result[:,:,:3]=np.uint8(np.clip(np.rint(changed),0,255))
    return result

def edge_scores(pixels):
    rgb=pixels[:,:,:3].astype(np.float32);r,g,b=rgb.transpose(2,0,1)
    depth=distance_transform_edt(pixels[:,:,3]>=128)
    score=np.maximum(np.minimum(r,g)-b,0)*pixels[:,:,3]/255
    score[(r-g>12)|(depth>=7)]=0
    y,x=np.unravel_index(np.argmax(score),score.shape)
    return {'count_over_10':int((score>10).sum()),'max':float(score[y,x]),'xy':[int(x),int(y)]}

def main():
    manifest=json.loads((OUT/'manifest.json').read_text(encoding='utf-8'))
    raw=OUT/'keyed-original';raw.mkdir(exist_ok=True)
    qa=OUT/'qa';qa.mkdir(exist_ok=True)
    before_archive=OUT/'rollback-green-edge/Ink.pet'
    missing=[name for name in manifest['frames'] if not (raw/(name+'.png')).exists()]
    if missing:
        with ZipFile(before_archive) as archive:
            for name in missing:(raw/(name+'.png')).write_bytes(archive.read(name+'.png'))
    records=[];samples=['neutral','paintmagic-072','paintmagic-135','pockets-050','lookup-030','dash-086']
    sheet=Image.new('RGB',(6*240,len(samples)*260),'#333333');draw=ImageDraw.Draw(sheet)
    for index,name in enumerate(manifest['frames']):
        before=np.array(Image.open(raw/(name+'.png')).convert('RGBA'))
        after=repair_rgba(before)
        if not np.array_equal(before[:,:,3],after[:,:,3]):raise RuntimeError('Alpha modified')
        depth=distance_transform_edt(before[:,:,3]>=128)
        # Interior ivory eyes are bright, warm, and deep inside the opaque face.
        eyes=(depth>=7)&(before[:,:,:3].min(axis=2)>100)
        if not np.array_equal(before[eyes],after[eyes]):raise RuntimeError('Interior ivory pixels modified')
        Image.fromarray(after).save(OUT/'frames'/(name+'.png'),compress_level=2)
        records.append({'frame':name,'changed_rgb_pixels':int(np.any(before[:,:,:3]!=after[:,:,:3],axis=2).sum()),'before':edge_scores(before),'after':edge_scores(after)})
        if name in samples:
            row=samples.index(name)
            # A fixed head/torso crop at 300%; compare before/after on three backgrounds.
            crop=(220,85,300,165) if name!='dash-086' else (300,205,380,285)
            for bgindex,bg in enumerate([(24,24,24),(245,245,245),(215,30,180)]):
                for side,pixels in enumerate([before,after]):
                    a=pixels[:,:,3:4]/255
                    comp=np.uint8(pixels[:,:,:3]*a+np.array(bg)*(1-a))
                    im=Image.fromarray(comp).crop(crop).resize((240,240),Image.Resampling.NEAREST)
                    col=bgindex*2+side;sheet.paste(im,(col*240,row*260));draw.text((col*240+4,row*260+241),name+(' before' if side==0 else ' after'),fill='white')
        if index%250==0:print(f'RGB repair {index+1}/{len(manifest["frames"])}',flush=True)
    sheet.save(qa/'green-edge-comparison.png')
    worst=sorted(records,key=lambda r:r['after']['max'],reverse=True)[:12]
    worst_sheet=Image.new('RGB',(1440,len(worst)*260),'#333333');worst_draw=ImageDraw.Draw(worst_sheet)
    for row,item in enumerate(worst):
        name=item['frame'];x,y=item['after']['xy'];box=(x-40,y-40,x+40,y+40)
        for side,folder in enumerate([raw,OUT/'frames']):
            pixels=np.array(Image.open(folder/(name+'.png')));a=pixels[:,:,3:4]/255
            for bi,bg in enumerate([(24,24,24),(245,245,245),(215,30,180)]):
                comp=np.uint8(pixels[:,:,:3]*a+np.array(bg)*(1-a));im=Image.fromarray(comp).crop(box).resize((240,240),Image.Resampling.NEAREST)
                col=bi*2+side;worst_sheet.paste(im,(col*240,row*260));worst_draw.text((col*240+3,row*260+241),f'{name} {"before" if side==0 else "after"} max{item["after"]["max"]:.1f}',fill='white')
    worst_sheet.save(qa/'rgb-v2-worst-after.png')
    report={'version':VERSION,'frames':len(records),'alpha_identical':True,'interior_ivory_pixels_identical':True,'depth_alpha_threshold':128,'body_full_band':5.5,'body_fade_end':7,'connected_strong_olive_patch_extension':True,'separate_effect_components_full_spill_clean':True,'count_over_10_before':sum(r['before']['count_over_10'] for r in records),'count_over_10_after':sum(r['after']['count_over_10'] for r in records),'worst':worst,'records':records}
    (qa/'rgb-repair.json').write_text(json.dumps(report,indent=2))
    manifest['rgbRepair']=VERSION
    for path in qa.glob('*-geometry.json'):
        data=json.loads(path.read_text());data['rgb_repair_version']=VERSION
        path.write_text(json.dumps(data),encoding='utf-8')
    (OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    with ZipFile(OUT/'Ink.pet','w',ZIP_STORED) as archive:
        archive.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False))
        for name in manifest['frames']:archive.write(OUT/'frames'/(name+'.png'),name+'.png')
    print('RGB repair and package complete',flush=True)

if __name__=='__main__':main()
