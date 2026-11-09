"""Local foreground-guided green key: remove codec fringe, retain dark edges."""
import cv2,numpy as np
from scipy.ndimage import distance_transform_edt

def matte(bgr):
    rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB).astype(np.float32)
    h,w=rgb.shape[:2];r,g,b=rgb.transpose(2,0,1)
    plate=np.median(np.concatenate((rgb[:,:30],rgb[:,-30:]),axis=1),axis=1)[:,None,:]
    plate=cv2.GaussianBlur(plate,(1,21),0)
    bg_g=np.maximum(plate[:,:,1],180)
    excess=g-np.maximum(r,b);bg_excess=np.maximum(bg_g-np.maximum(plate[:,:,0],plate[:,:,2]),30)
    initial=np.clip(1-np.maximum(excess,0)/bg_excess,0,1)
    core=(initial>.985)&(g<np.maximum(r,b)+5)
    count,labels,stats,_=cv2.connectedComponentsWithStats(core.astype(np.uint8),8)
    # Eliminate isolated codec noise, retaining thin spectacles/fingers/strands.
    keep=np.zeros(count,bool)
    if count>1:keep[1:]=stats[1:,cv2.CC_STAT_AREA]>=4
    core=keep[labels]
    initial_core=core.copy();initial_depth=distance_transform_edt(initial_core)
    olive=(np.minimum(r,g)-b>5)&(g>r-18)
    # Do not propagate olive chroma spill into reconstructed strand colours.
    # Interior warm metal/skin is preserved; thin dark spectacle rims survive.
    dark_clean=(rgb.max(axis=2)<75)&(excess<3)&~olive
    core=initial_core&((initial_depth>2)|dark_clean)&~(olive&(initial_depth<5))
    distance,indices=distance_transform_edt(~core,return_indices=True)
    fg=rgb[indices[0],indices[1]]
    denom=bg_g-fg[:,:,1]
    alpha=np.clip((bg_g-g)/np.maximum(denom,20),0,1)
    # Near-white materials have too little green-channel contrast; use RGB fit.
    diff=fg-plate;fit=np.sum((rgb-plate)*diff,axis=2)/np.maximum(np.sum(diff*diff,axis=2),1)
    alpha=np.where(denom<35,np.clip(fit,0,1),alpha)
    # Geometry support may contain dark, green-contaminated hair/boots that are
    # unsuitable as colour seeds. Do not punch holes by confusing these roles.
    support_core=core|((g<200)&(excess<.9*g)&(r+b>12))
    _,support_labels=cv2.connectedComponents(support_core.astype(np.uint8),8)
    attached=np.unique(support_labels[core]);attached=attached[attached!=0]
    support_core=np.isin(support_labels,attached)
    support_distance=distance_transform_edt(~support_core)
    alpha*=np.clip((6-support_distance)/3,0,1)
    alpha[core]=1
    # A narrow subpixel antialias step, not broad feathering or edge erosion.
    alpha=cv2.GaussianBlur(alpha,(3,3),.6)
    alpha[core & (distance_transform_edt(core)>1.5)]=1
    alpha[alpha<.012]=0
    # Reconstruct boundary colour from clean foreground, never divide noisy RGB
    # by tiny coverage. This prevents white/yellow codec rings and green spill.
    depth=distance_transform_edt(core)
    mix=np.clip((2.5-depth)/2.0,0,1)
    fg=cv2.GaussianBlur(fg,(5,5),.8)
    clean=rgb*(1-mix[:,:,None])+fg*mix[:,:,None]
    # Far from opaque seeds, nearest-neighbour colour makes radial Voronoi
    # streaks across translucent flowing hair. Recover its measured red/blue
    # channels instead; the green plate contributes almost nothing to these.
    recovered=(rgb-plate*(1-alpha[:,:,None]))/np.maximum(alpha[:,:,None],.08)
    recovered=np.clip(recovered,0,255)
    recovered[:,:,1]=(recovered[:,:,0]+recovered[:,:,2])*.5
    recover_mix=np.clip((distance-3)/5,0,1)*np.clip((alpha-.1)/.25,0,1)
    clean=clean*(1-recover_mix[:,:,None])+recovered*recover_mix[:,:,None]
    clean[:,:,1]=np.minimum(clean[:,:,1],np.maximum(clean[:,:,0],clean[:,:,2]))
    # Preserve source contact shadow independently below the lowest opaque boot.
    ys,xs=np.where(core)
    if not len(xs):raise RuntimeError('No foreground core')
    floor=int(ys.max());feet=xs[ys>floor-35];cx=(feet.min()+feet.max())/2
    rx=max((feet.max()-feet.min())/2+65,85);yy,xx=np.ogrid[:h,:w]
    support=np.clip((1-((xx-cx)/rx)**2-((yy-floor)/27)**2)*4,0,1)
    shadow=np.clip((1-g/bg_g-.018)/.982,0,.65)*support*(excess>g*.55)
    total=alpha+shadow*(1-alpha)
    pm=clean*alpha[:,:,None]
    straight=np.divide(pm,total[:,:,None],out=np.zeros_like(pm),where=total[:,:,None]>1e-6)
    out=np.uint8(np.clip(np.dstack((straight,total*255)).round(),0,255));out[out[:,:,3]<=2]=0
    return out
