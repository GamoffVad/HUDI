"""Green-plate foreground reconstruction calibrated for warm brown flyaways."""
import numpy as np
import cv2
from scipy.ndimage import distance_transform_edt

def matte(bgr):
    rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB).astype(np.float32)
    h,w=rgb.shape[:2];r,g,b=rgb.transpose(2,0,1)
    plate=np.median(np.concatenate((rgb[:,:24],rgb[:,-24:]),axis=1),axis=1)[:,None,:]
    plate=cv2.GaussianBlur(plate,(1,15),0)
    excess=g-np.maximum(r,b)
    seed=(excess<3)&((r+b)>12)
    _,labels,stats,_=cv2.connectedComponentsWithStats(seed.astype(np.uint8),8)
    keep=np.zeros(len(stats),bool);keep[1:]=stats[1:,cv2.CC_STAT_AREA]>=2
    seed=keep[labels]
    dist,idx=distance_transform_edt(~seed,return_indices=True)
    fg=rgb[idx[0],idx[1]]
    d=fg-plate
    alpha=np.clip(np.sum((rgb-plate)*d,axis=2)/np.maximum(np.sum(d*d,axis=2),1),0,1)
    alpha[seed]=1
    alpha*=np.clip((7-dist)/3,0,1)
    depth=distance_transform_edt(seed)
    hair_edge=(np.arange(h)[:,None]<h*.45)&(depth<5)
    # Warm-brown hair mixing model; red and blue determine expected green.
    # A green-contaminated brown edge can still have R > G, so max(R,B)
    # alone is not a sufficient spill test.
    plate_excess=np.maximum(plate[:,:,1]-.55*plate[:,:,0]-.45*plate[:,:,2],30)
    hair_alpha=np.clip(1-(g-.55*r-.45*b)/plate_excess,0,1)
    alpha=np.where(hair_edge,np.minimum(alpha,hair_alpha),alpha)
    alpha=cv2.GaussianBlur(alpha,(3,3),.5)
    alpha[seed&(depth>2)&~hair_edge]=1
    alpha[alpha<.008]=0
    rec=np.clip((rgb-plate*(1-alpha[:,:,None]))/np.maximum(alpha[:,:,None],.03),0,255)
    t=np.clip(alpha/.25,0,1)[:,:,None]
    clean=rec*t+fg*(1-t)
    safe_seed=seed&~hair_edge
    clean[safe_seed]=rgb[safe_seed]
    # Codec mixing leaves olive/yellow colours even where alpha is correct.
    # Propagate local *brown* foreground colours only into translucent hair;
    # do not desaturate it like black hair or erode its coverage.
    brown_seed=(r>g*1.55)&(r>b*1.5)&(r<175)&(alpha>.97)
    wt=brown_seed.astype(np.float32)
    den=cv2.GaussianBlur(wt,(0,0),4)
    colour=cv2.GaussianBlur(rgb*wt[:,:,None],(0,0),4)/np.maximum(den[:,:,None],1e-5)
    brown_colour=(clean[:,:,0]>clean[:,:,1]*1.15)&(clean[:,:,0]>clean[:,:,2]*1.35)
    repair=hair_edge&(alpha>0)&((alpha<.999)|(depth<3))&(clean.max(2)<225)&brown_colour&(den>.001)
    clean=np.where(repair[:,:,None],colour,clean)
    # Warm skin and cream fabric also collect green along moving contours.
    # Reconstruct coverage there from the original plate, then extend clean
    # nearby foreground colour into the narrow translucent fringe only.
    body_edge=(np.arange(h)[:,None]>=h*.35)&(depth<18)&(alpha>0)
    body_excess=np.maximum(plate[:,:,1]-.65*plate[:,:,0]-.35*plate[:,:,2],30)
    body_alpha=np.clip(1-(g-.65*r-.35*b)/body_excess,0,1)
    candidate=np.where(body_edge,np.minimum(alpha,body_alpha),alpha)
    smoothed=cv2.GaussianBlur(candidate,(3,3),.5)
    alpha=np.where(body_edge,smoothed,alpha)
    reliable=seed&(depth>3)&(g<.65*r+.35*b+3)
    _,nearest=distance_transform_edt(~reliable,return_indices=True)
    solid=cv2.GaussianBlur(rgb[nearest[0],nearest[1]],(3,3),.6)
    # Straight-alpha RGB is undefined at zero coverage; local colour extension
    # avoids both green contamination and magenta inverse-key artifacts.
    fringe=body_edge&((alpha<.995)|(g>r*.96))
    clean=np.where(fringe[:,:,None],solid,clean)
    alpha[alpha<.008]=0
    ys,xs=np.where(seed)
    if not len(xs):raise ValueError('No foreground')
    floor=int(ys.max());feet=xs[ys>floor-35];cx=(feet.min()+feet.max())/2
    rx=max((feet.max()-feet.min())/2+65,85);yy,xx=np.ogrid[:h,:w]
    support=np.clip((1-((xx-cx)/rx)**2-((yy-floor)/27)**2)*4,0,1)
    shadow=np.clip(1-g/np.maximum(plate[:,:,1],180)-.015,0,.65)*support*(excess>g*.55)
    total=alpha+shadow*(1-alpha)
    straight=np.divide(clean*alpha[:,:,None],total[:,:,None],out=np.zeros_like(clean),where=total[:,:,None]>1e-6)
    out=np.uint8(np.clip(np.dstack((straight,total*255)).round(),0,255));out[out[:,:,3]<=2]=0
    return out
