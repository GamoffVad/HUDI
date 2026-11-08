const fs=require('fs'),path=require('path'),sharp=require('sharp');
const {spawnSync}=require('child_process'),ffmpeg=require('ffmpeg-static');
const rig=require('./rig-v2.cjs');
const OUT=path.resolve(__dirname,'../output/hudi-pet-v2');
const W=600,H=900,N=320,FPS=50,S=W/1024;
const lerp=(a,b,t)=>a+(b-a)*t;
function run(args){const r=spawnSync(ffmpeg,['-hide_banner','-loglevel','warning','-y',...args],{windowsHide:true,encoding:'utf8',maxBuffer:20e6});if(r.status!==0)throw Error(r.stderr);if(r.stderr)console.log(r.stderr.trim());}
function sample(d,x,y,c){x=Math.max(0,Math.min(W-1,x));y=Math.max(0,Math.min(H-1,y));const x0=Math.floor(x),y0=Math.floor(y),x1=Math.min(W-1,x0+1),y1=Math.min(H-1,y0+1),fx=x-x0,fy=y-y0;return lerp(lerp(d[(y0*W+x0)*4+c],d[(y0*W+x1)*4+c],fx),lerp(d[(y1*W+x0)*4+c],d[(y1*W+x1)*4+c],fx),fy);}
function measureEye(raw,center){
  const [cx,cy]=center.map(v=>v*S),pixels=[];
  for(let y=Math.floor(cy-45);y<cy+46;y++)for(let x=Math.floor(cx-30);x<cx+31;x++){
    const p=(y*W+x)*4;if(raw[p]>195&&raw[p]-raw[p+1]>25&&raw[p]-raw[p+2]>10&&raw[p+3]>128)pixels.push([x,y]);
  }
  if(pixels.length<30)throw Error('Eye not located');
  const mx=pixels.reduce((s,p)=>s+p[0],0)/pixels.length,my=pixels.reduce((s,p)=>s+p[1],0)/pixels.length;
  let xx=0,xy=0,yy=0;for(const [x,y]of pixels){xx+=(x-mx)**2;xy+=(x-mx)*(y-my);yy+=(y-my)**2;}
  const theta=.5*Math.atan2(2*xy,xx-yy);let major=[Math.cos(theta),Math.sin(theta)];if(major[1]<0)major=major.map(v=>-v);
  const minor=[major[1],-major[0]];let long=0,short=0;
  for(const[x,y]of pixels){long=Math.max(long,Math.abs((x-mx)*major[0]+(y-my)*major[1]));short=Math.max(short,Math.abs((x-mx)*minor[0]+(y-my)*minor[1]));}long+=4;short+=4;
  return [...[[-1,-1],[1,-1],[1,1],[-1,1]].map(([u,v])=>[mx+minor[0]*u*short+major[0]*v*long,my+minor[1]*u*short+major[1]*v*long]),[mx,my]];
}
function snap(raw,p,radius=12){
  const[px,py]=p;let best=p,distance=Infinity;
  for(let y=Math.max(1,Math.floor(py-radius));y<Math.min(H-1,py+radius);y++)for(let x=Math.max(1,Math.floor(px-radius));x<Math.min(W-1,px+radius);x++){
    const q=(y*W+x)*4;if(raw[q+3]<128)continue;
    if(raw[q-1]>=128&&raw[q+7]>=128&&raw[q-W*4+3]>=128&&raw[q+W*4+3]>=128)continue;
    const d=(x-px)**2+(y-py)**2;if(d<distance){distance=d;best=[x,y];}
  }return best;
}
function animatePoint(p,index,phase,groups){
  if(index<groups.outer)return p;
  let[x,y]=p;const sway=Math.sin(phase),breathe=Math.sin(2*phase);
  if(index<groups.body){x+=1.5*sway;y+=1.2*breathe;}
  else if(index<groups.hands){const w=Math.max(0,(690-y)/300);x+=2*sway*w;y+=1.2*breathe*w;}
  else if(index<groups.legs){const side=x<W/2?-1:1,weight=Math.max(0,Math.min(1,(y-615)/100));x+=side*2.5*Math.sin(phase+.45)*weight;y+=1.7*Math.sin(2*phase+.3)*weight;}
  else{const side=x<W/2?-1:1,knee=Math.exp(-(((y-745)/60)**2));x+=2.2*sway*knee;y+=1.6*(1+side*sway)*knee;
    if(y>806){const cx=side<0?213:381,cy=878,angle=.009*Math.sin(phase+side*.7),dx=x-cx,dy=y-cy;x=cx+dx*Math.cos(angle)-dy*Math.sin(angle);y=cy+dx*Math.sin(angle)+dy*Math.cos(angle);}}
  return[x,y];
}
function render(aData,bData,a,b,triangles,t,phase,groups){
  const target=a.map((p,i)=>animatePoint([lerp(p[0],b[i][0],t),lerp(p[1],b[i][1],t)],i,phase,groups)),out=Buffer.alloc(W*H*4);
  for(let k=0;k<triangles.length;k+=3){const ids=[triangles[k],triangles[k+1],triangles[k+2]],p=ids.map(id=>target[id]),[p0,p1,p2]=p;
    const den=(p1[1]-p2[1])*(p0[0]-p2[0])+(p2[0]-p1[0])*(p0[1]-p2[1]);if(Math.abs(den)<1e-7)continue;
    const x0=Math.max(0,Math.floor(Math.min(...p.map(q=>q[0])))),x1=Math.min(W-1,Math.ceil(Math.max(...p.map(q=>q[0])))),y0=Math.max(0,Math.floor(Math.min(...p.map(q=>q[1])))),y1=Math.min(H-1,Math.ceil(Math.max(...p.map(q=>q[1]))));
    const ap=ids.map(id=>a[id]),bp=ids.map(id=>b[id]);
    for(let y=y0;y<=y1;y++)for(let x=x0;x<=x1;x++){
      const u=((p1[1]-p2[1])*(x-p2[0])+(p2[0]-p1[0])*(y-p2[1]))/den,v=((p2[1]-p0[1])*(x-p2[0])+(p0[0]-p2[0])*(y-p2[1]))/den,w=1-u-v;
      if(u<-.00001||v<-.00001||w<-.00001)continue;
      const ax=u*ap[0][0]+v*ap[1][0]+w*ap[2][0],ay=u*ap[0][1]+v*ap[1][1]+w*ap[2][1],bx=u*bp[0][0]+v*bp[1][0]+w*bp[2][0],by=u*bp[0][1]+v*bp[1][1]+w*bp[2][1],q=(y*W+x)*4;
      const alpha=lerp(sample(aData,ax,ay,3),sample(bData,bx,by,3),t);out[q+3]=alpha<2?0:Math.round(alpha);
      if(alpha>=2)for(let c=0;c<3;c++)out[q+c]=Math.round(Math.max(0,Math.min(255,lerp(sample(aData,ax,ay,c),sample(bData,bx,by,c),t)*255/alpha)));
    }
  }return out;
}
(async()=>{
  const {default:Delaunator}=await import('delaunator');
  const frames=path.join(OUT,'frames'),work=path.join(OUT,'work');fs.mkdirSync(frames,{recursive:true});fs.mkdirSync(work,{recursive:true});
  const buffers={},points={},groups={};
  for(const name of ['front','left','right']){
    const raw=await sharp(path.join(OUT,'sources',name+'.png')).resize(W,H).ensureAlpha().raw().toBuffer(),premul=new Float32Array(raw.length);
    for(let q=0;q<raw.length;q+=4){const alpha=raw[q+3]/255;for(let c=0;c<3;c++)premul[q+c]=raw[q+c]*alpha;premul[q+3]=raw[q+3];}buffers[name]=premul;
    const p=[[0,0],[W/2,0],[W-1,0],[W-1,H/3],[W-1,H*2/3],[W-1,H-1],[W/2,H-1],[0,H-1],[0,H*2/3],[0,H/3]];groups.outer=p.length;
    p.push(...rig.outer[name].map((q,i)=>i>=7&&i<=9?q.map(v=>v*S):snap(raw,q.map(v=>v*S),20)));
    p.push(...rig.face[name].map(q=>q.map(v=>v*S)));
    for(const eye of rig.eyeCenters[name])p.push(...measureEye(raw,eye));
    groups.body=p.length;
    p.push(...rig.body[name].map((q,i)=>[4,5,6,7,8,9].includes(i)?snap(raw,q.map(v=>v*S),12):q.map(v=>v*S)));
    groups.hands=p.length;
    p.push(...rig.hands[name].map((q,i)=>[2,3,4,5,6].includes(i%8)?snap(raw,q.map(v=>v*S),10):q.map(v=>v*S)));
    groups.legs=p.length;
    p.push(...rig.legs[name].map(q=>q.map(v=>v*S)));
    points[name]=p;
  }
  const meshes={};for(const name of ['left','right'])meshes[name]=Delaunator.from(points.front.map((p,i)=>[lerp(p[0],points[name][i][0],.5),lerp(p[1],points[name][i][1],.5)])).triangles;
  let first,last,maxEdgeAlpha=0,maxFrameStep=0,previous,legMotion=0;const handAreas=[];
  for(let i=0;i<N;i++){
    const phase=i/N*Math.PI*2,s=Math.sin(phase),name=s<0?'left':'right',t=Math.abs(s);
    const data=render(buffers.front,buffers[name],points.front,points[name],meshes[name],t,phase,groups);let step=0,handLeft=0,handRight=0;
    for(let y=0;y<H;y++)for(let x=0;x<W;x++){
      const q=(y*W+x)*4;
      if(x<5||x>=W-5||y<5||y>=H-5)maxEdgeAlpha=Math.max(maxEdgeAlpha,data[q+3]);
      if(y>625&&y<730&&data[q+3]>128){if(x>70&&x<170)handLeft++;if(x>430&&x<530)handRight++;}
      if(previous){step+=Math.abs(data[q]-previous[q]);if(y>740)legMotion+=Math.abs(data[q]-first[q]);}
    }
    handAreas.push([handLeft,handRight]);if(previous)maxFrameStep=Math.max(maxFrameStep,step/(W*H));first??=Buffer.from(data);previous=data;last=data;
    const stem=String(i).padStart(3,'0');
    await sharp(data,{raw:{width:W,height:H,channels:4}}).png().toFile(path.join(frames,`hudi-${stem}.png`));
    await sharp(data,{raw:{width:W,height:H,channels:4}}).flatten({background:'#eceef2'}).png().toFile(path.join(work,`preview-${stem}.png`));
    if(i%80===0)console.log(`Rendered ${i}/${N}`);
  }
  let loopDifference=0;for(let q=0;q<first.length;q++)loopDifference+=Math.abs(first[q]-last[q]);
  run(['-framerate',String(FPS),'-i',path.join(work,'preview-%03d.png'),'-filter_complex','[0:v]split[a][b];[a]palettegen=max_colors=256:stats_mode=full[p];[b][p]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle','-loop','0',path.join(OUT,'HUDI-v2-loop.gif')]);
  run(['-framerate',String(FPS),'-i',path.join(work,'preview-%03d.png'),'-c:v','libx264','-preset','slow','-crf','16','-pix_fmt','yuv420p','-movflags','+faststart',path.join(OUT,'HUDI-v2-loop.mp4')]);
  const picks=[0,20,40,80,120,160,200,240,280,300],tiles=await Promise.all(picks.map(i=>sharp(path.join(work,`preview-${String(i).padStart(3,'0')}.png`)).resize(240,360).toBuffer()));
  await sharp({create:{width:1200,height:720,channels:3,background:'#eceef2'}}).composite(tiles.map((input,i)=>({input,left:(i%5)*240,top:Math.floor(i/5)*360}))).png().toFile(path.join(OUT,'contact-sheet.png'));
  const manifest={version:2,width:W,height:H,frames:N,fps:FPS,durationSeconds:N/FPS,loop:'infinite',motion:'whole body, hands, knees, gentle foot rocking'};
  fs.writeFileSync(path.join(OUT,'animation.json'),JSON.stringify(manifest,null,2));
  fs.writeFileSync(path.join(OUT,'verification.json'),JSON.stringify({...manifest,maxEdgeAlpha,legMotion,maxFrameStep,loopMeanDifference:loopDifference/first.length,minHandPixels:[Math.min(...handAreas.map(a=>a[0])),Math.min(...handAreas.map(a=>a[1]))]},null,2));
  if(maxEdgeAlpha!==0||legMotion===0||handAreas.some(a=>a[0]<800||a[1]<800))throw Error('Animation bounds or hand visibility verification failed');
  console.log('Completed v2 animation with complete hands and moving legs.');
})().catch(e=>{console.error(e);process.exit(1)});
