"""Conservative whole-sequence opacity review, with original-video evidence."""
from pathlib import Path
import json,cv2,numpy as np
from scipy.ndimage import binary_fill_holes
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output/ink-pet'

def main():
    manifest=json.loads((OUT/'manifest-fresh.json').read_text(encoding='utf-8'))
    generation=json.loads((OUT/'qa-fresh/report.json').read_text())
    captures={};flags=[]
    for ident in manifest['frames']:
        if ident=='neutral':name,index='dash',0
        else:name,number=ident.rsplit('-',1);index=int(number)
        p=np.array(Image.open(OUT/'fresh-frames'/(ident+'.png')));alpha=p[:,:,3]
        count,labels,stats,_=cv2.connectedComponentsWithStats((alpha>=128).astype(np.uint8),8)
        main_label=1+np.argmax(stats[1:,4]);x,y,w,h,_=stats[main_label]
        filled=binary_fill_holes(labels==main_label)
        depth=cv2.distanceTransform(filled.astype(np.uint8),cv2.DIST_L2,5)
        head=np.zeros(alpha.shape,bool);head[y:y+int(h*.45),x:x+w]=True
        holes=(depth>3)&head&(alpha<180)
        yy,xx=np.where(holes)
        if not len(xx):continue
        if name not in captures:captures[name]=cv2.VideoCapture(str(ROOT/generation['sources'][name]['path']))
        cap=captures[name];cap.set(cv2.CAP_PROP_POS_FRAMES,index);ok,bgr=cap.read();assert ok
        raw=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB)
        sx=(xx+.3-16)/.4;sy=(yy+.3-16)/.4
        reg=generation['sources'][name].get('registration')
        if reg:
            sx=(sx-reg['offset'][0])/reg['scale'];sy=(sy-reg['offset'][1])/reg['scale']
        sx=np.clip(np.rint(sx).astype(int),0,959);sy=np.clip(np.rint(sy).astype(int),0,959)
        colors=raw[sy,sx].astype(float);r,g,b=colors.T
        material=(np.minimum(r,b)>15)&(g<np.maximum(r,b)*1.4)
        flagged=[{'xy':[int(px),int(py)],'alpha':int(alpha[py,px]),'source_rgb':c.astype(int).tolist(),'likely_material':bool(mat)} for px,py,c,mat in zip(xx,yy,colors,material)]
        flags.append({'frame':ident,'low_alpha_pixels':len(xx),'likely_material_pixels':int(material.sum()),'source_samples':flagged})
    for cap in captures.values():cap.release()
    flags.sort(key=lambda x:(x['likely_material_pixels'],x['low_alpha_pixels']),reverse=True)
    report={'version':generation['version'],'frames_checked':len(manifest['frames']),'criterion':'filled main-component head region, depth>3px and alpha<180; source RGB distinguishes real green gaps from possible missing material','candidate_frames':len(flags),'likely_material_frames':sum(r['likely_material_pixels']>0 for r in flags),'flags':flags}
    (OUT/'qa-fresh/head-opacity-audit.json').write_text(json.dumps(report,indent=2))
    chosen=flags[:12];sheet=Image.new('RGB',(3*416,456*((len(chosen)+2)//3 or 1)),'#222');draw=ImageDraw.Draw(sheet)
    for i,item in enumerate(chosen):
        p=np.array(Image.open(OUT/'fresh-frames'/(item['frame']+'.png')));a=p[:,:,3:4]/255;comp=np.uint8(p[:,:,:3]*a+np.array([215,30,180])*(1-a));im=Image.fromarray(comp)
        x=i%3*416;y=i//3*456;sheet.paste(im,(x,y));draw.text((x+4,y+420),f'{item["frame"]}: {item["likely_material_pixels"]} material / {item["low_alpha_pixels"]} flags',fill='white')
    sheet.save(OUT/'qa-fresh/head-opacity-flags.png')
    print(json.dumps({k:v for k,v in report.items() if k!='flags'}),flush=True)
    print(json.dumps([{k:v for k,v in r.items() if k!='source_samples'} for r in flags[:12]]),flush=True)

if __name__=='__main__':main()
