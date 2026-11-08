"""Remove hallucinated mouth/pupils from the fire video's final upward glance."""
from pathlib import Path
import cv2,numpy as np,json,subprocess,imageio_ffmpeg
P=Path(__file__).resolve().parent
c=cv2.VideoCapture(str(P/'snap-fire-innocent.mp4'));fps=c.get(cv2.CAP_PROP_FPS)
frames=[]
while True:
    ok,b=c.read()
    if not ok:break
    frames.append(b)
c.release();h,w=frames[0].shape[:2]
anchors=[]
for i in range(158,224):
    b=frames[i];roi=b[340:480,400:490].astype(float);bl,g,r=roi.transpose(2,0,1)
    m=((r>g+13)&(r>bl+7)&(r>60)).astype('uint8')
    n,l,s,ct=cv2.connectedComponentsWithStats(m,8)
    valid=[k for k in range(1,n) if s[k,4]>8]
    if valid:
        k=max(valid,key=lambda k:s[k,4]);x,y,bw,bh,area=s[k]
        anchors.append([i,float(x+bw/2+400),float(y+bh/2+340+6)])
a=np.array(anchors)
ff=subprocess.Popen([imageio_ffmpeg.get_ffmpeg_exe(),'-hide_banner','-loglevel','error','-y','-f','rawvideo','-pix_fmt','bgr24','-s',f'{w}x{h}','-r',str(fps),'-i','-','-an','-c:v','libx264','-crf','14','-preset','slow','-pix_fmt','yuv420p','-movflags','+faststart',str(P/'snap-fire-innocent-fixed.mp4')],stdin=subprocess.PIPE)
edits=[]
for i,b in enumerate(frames):
    out=b.copy();mouth=np.zeros((h,w),np.uint8);pupils=mouth.copy()
    if 158<=i<=223:
        x=round(float(np.interp(i,a[:,0],a[:,1])));y=round(float(np.interp(i,a[:,0],a[:,2])))
        cv2.ellipse(mouth,(x,y),(38,19),0,0,360,255,-1)
        # Reconstruct ivory eye surfaces from their external contours.
        gray=cv2.cvtColor(b,cv2.COLOR_BGR2GRAY)
        bright=np.zeros((h,w),np.uint8);bright[270:420,370:575]=(gray[270:420,370:575]>145).astype('uint8')*255
        contours,_=cv2.findContours(bright,cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE)
        for contour in contours:
            if 40<cv2.contourArea(contour)<8000:
                fill=np.zeros_like(bright);cv2.drawContours(fill,[contour],-1,255,-1)
                holes=cv2.bitwise_and(fill,cv2.bitwise_not(bright))
                holes=cv2.dilate(holes,np.ones((3,3),np.uint8))
                pupils|=cv2.bitwise_and(holes,cv2.erode(fill,np.ones((3,3),np.uint8)))
        mask=mouth|pupils
        restored=cv2.inpaint(b,pupils,5,cv2.INPAINT_NS)
        # Fit the smooth local face illumination instead of spreading mouth rim pixels.
        yy,xx=np.mgrid[max(0,y-36):min(h,y+37),max(0,x-58):min(w,x+59)]
        dx=(xx-x)/38;dy=(yy-y)/19;radius=np.sqrt(dx*dx+dy*dy)
        patch=b[yy,xx].astype(float)
        ring=(radius>1.1)&(radius<1.65)&(patch.max(axis=2)<110)
        design=np.stack([np.ones_like(dx),dx,dy,dx*dx,dx*dy,dy*dy],axis=-1)
        coefficients=np.linalg.lstsq(design[ring],patch[ring],rcond=None)[0]
        surface=np.clip(design@coefficients,0,110)
        restored[yy,xx]=surface.astype('uint8')
        blend=np.zeros((h,w),float)
        blend[yy,xx]=np.clip((1.45-radius)/0.65,0,1)
        fade=min(1,(i-157)/3,(224-i)/3)
        blend*=fade
        out=np.uint8(np.clip(b*(1-blend[:,:,None])+restored*blend[:,:,None],0,255))
        out=cv2.inpaint(out,pupils,5,cv2.INPAINT_NS)
        edits.append({'frame':i,'mouth_centre':[x,y],'pupil_pixels':int(np.count_nonzero(pupils))})
    ff.stdin.write(out.tobytes())
ff.stdin.close();assert ff.wait()==0
(P/'snap-fire-retouch.json').write_text(json.dumps({'source':'snap-fire-innocent.mp4','output':'snap-fire-innocent-fixed.mp4','frames':len(frames),'edits':edits},indent=2),encoding='utf8')
print('Saved corrected video',len(frames))
