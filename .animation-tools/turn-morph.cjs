// Landmark-guided mesh animation keeps the two luminous eyes registered during turns.
const sharp = require('sharp');
const path = require('path');
const W=362,H=544;
const boundary=[[0,0],[181,0],[361,0],[361,135],[361,270],[361,405],[361,543],[181,543],[0,543],[0,405],[0,270],[0,135]];
const anchor=[[0,404],[55,404],[110,404],[181,404],[250,404],[310,404],[361,404],[0,440],[110,440],[181,440],[250,440],[361,440]];
const head={
  center:[[181,24],[245,35],[280,68],[301,123],[306,180],[293,215],[254,237],[184,245],[115,238],[69,217],[47,180],[51,125],[76,69],[117,36], [187,83],[240,91],[263,115],[278,159],[276,193],[261,216],[219,230],[180,235],[133,229],[94,214],[76,191],[73,158],[85,118],[119,91]],
  right:[[185,24],[246,36],[285,66],[315,102],[325,150],[310,207],[275,239],[217,250],[152,240],[104,224],[67,195],[66,133],[98,70],[135,38], [238,83],[278,96],[300,119],[306,159],[294,200],[278,223],[249,237],[215,240],[183,229],[152,213],[137,193],[147,157],[173,119],[200,93]],
  left:[[179,24],[241,36],[280,69],[303,125],[312,180],[300,215],[263,237],[197,249],[132,240],[88,222],[57,192],[47,135],[65,83],[118,38], [118,86],[164,97],[195,120],[220,161],[241,194],[230,218],[203,233],[169,240],[127,233],[92,219],[76,193],[65,162],[64,126],[82,99]]
};
const eyes={
  center:[[121,139],[144,139],[144,179],[121,179],[133,159],[216,139],[238,139],[238,179],[216,179],[227,159]],
  right:[[220,137],[242,142],[234,179],[212,174],[227,158],[282,155],[296,158],[288,194],[276,189],[286,174]],
  left:[[77,156],[94,152],[106,194],[89,198],[91,175],[139,141],[160,137],[171,176],[146,181],[154,160]]
};
const body={
  center:[[112,260],[248,260],[82,300],[54,339],[40,363],[52,385],[76,395],[105,384],[118,352],[276,300],[299,339],[311,362],[296,385],[277,396],[249,384],[241,352],[161,248],[196,248],[163,334],[202,334],[144,337],[221,337],[121,383],[242,383]],
  right:[[128,262],[224,260],[101,300],[73,336],[51,357],[60,379],[79,392],[100,383],[108,350],[258,300],[292,335],[308,356],[293,382],[267,395],[233,383],[221,349],[154,247],[192,249],[136,337],[171,334],[116,338],[190,337],[101,383],[218,383]],
  left:[[126,262],[239,262],[101,300],[75,338],[58,363],[64,385],[91,395],[115,383],[128,349],[268,301],[291,335],[309,356],[294,381],[272,392],[253,378],[248,348],[171,250],[208,251],[206,336],[232,334],[181,339],[243,336],[147,383],[260,382]]
};
function points(name){return [...boundary,...head[name],...eyes[name],...body[name],...anchor];}
const coords={center:points('center'),right:points('right'),left:points('left')};
const lerp=(a,b,t)=>a+(b-a)*t;
function premultiply(data){
  const out=new Float32Array(data.length);
  for(let p=0;p<data.length;p+=4){const a=data[p+3]/255;out[p]=data[p]*a;out[p+1]=data[p+1]*a;out[p+2]=data[p+2]*a;out[p+3]=data[p+3];}return out;
}
function sample(data,x,y,c){
  x=Math.max(0,Math.min(W-1,x));y=Math.max(0,Math.min(H-1,y));
  const x0=Math.floor(x),y0=Math.floor(y),x1=Math.min(W-1,x0+1),y1=Math.min(H-1,y0+1),fx=x-x0,fy=y-y0;
  return lerp(lerp(data[(y0*W+x0)*4+c],data[(y0*W+x1)*4+c],fx),lerp(data[(y1*W+x0)*4+c],data[(y1*W+x1)*4+c],fx),fy);
}
async function prepare(work){
  const {default:Delaunator}=await import('delaunator');
  const buffers={};
  for(const [name,index] of [['center',0],['right',2],['left',6]]){
    const raw=await sharp(path.join(work,`key-${index}.png`)).ensureAlpha().raw().toBuffer();
    buffers[name]=premultiply(raw);
    // Snap the visible outline to each source's actual alpha contour.
    const silhouette=[0,1,2,3,4,5,9,10,11,12,13].map(i=>boundary.length+i);
    const bodyStart=boundary.length+28+10;
    for(const i of [2,3,4,5,9,10,11,12])silhouette.push(bodyStart+i);
    for(const id of silhouette){
      const [px,py]=coords[name][id];let nearest=[px,py],distance=Infinity;
      for(let y=Math.max(1,py-26);y<Math.min(H-1,py+27);y++)for(let x=Math.max(1,px-26);x<Math.min(W-1,px+27);x++){
        const p=(y*W+x)*4;
        if(raw[p+3]<128)continue;
        if(raw[p-4+3]>=128&&raw[p+4+3]>=128&&raw[p-W*4+3]>=128&&raw[p+W*4+3]>=128)continue;
        const d=(x-px)**2+(y-py)**2;
        if(d<distance){distance=d;nearest=[x,y];}
      }
      coords[name][id]=nearest;
    }
    // Measure luminous eye bounds, aligning their centers and tilt exactly.
    const eyeStart=boundary.length+28;
    for(let eye=0;eye<2;eye++){
      const [cx,cy]=coords[name][eyeStart+eye*5+4],pixels=[];
      for(let y=Math.max(100,cy-30);y<Math.min(214,cy+31);y++)for(let x=Math.max(0,cx-24);x<Math.min(W,cx+25);x++){
        const p=(y*W+x)*4;
        if(raw[p]>195&&raw[p]-raw[p+1]>25&&raw[p]-raw[p+2]>10&&raw[p+3]>128)pixels.push([x,y]);
      }
      if(pixels.length<30)continue;
      const mx=pixels.reduce((s,p)=>s+p[0],0)/pixels.length,my=pixels.reduce((s,p)=>s+p[1],0)/pixels.length;
      let xx=0,xy=0,yy=0;
      for(const [x,y] of pixels){xx+=(x-mx)**2;xy+=(x-mx)*(y-my);yy+=(y-my)**2;}
      const theta=.5*Math.atan2(2*xy,xx-yy);let major=[Math.cos(theta),Math.sin(theta)];
      if(major[1]<0)major=major.map(v=>-v);
      const minor=[major[1],-major[0]];
      let long=0,short=0;
      for(const [x,y] of pixels){long=Math.max(long,Math.abs((x-mx)*major[0]+(y-my)*major[1]));short=Math.max(short,Math.abs((x-mx)*minor[0]+(y-my)*minor[1]));}
      long+=3;short+=3;
      const corners=[[-1,-1],[1,-1],[1,1],[-1,1]].map(([u,v])=>[mx+minor[0]*u*short+major[0]*v*long,my+minor[1]*u*short+major[1]*v*long]);
      for(let k=0;k<4;k++)coords[name][eyeStart+eye*5+k]=corners[k];
      coords[name][eyeStart+eye*5+4]=[mx,my];
    }
  }
  const meshes={};
  for(const name of ['left','right']){
    const mid=coords.center.map((a,i)=>[lerp(a[0],coords[name][i][0],.5),lerp(a[1],coords[name][i][1],.5)]);
    meshes[name]=Delaunator.from(mid).triangles;
  }
  return function frame(i,N){
    const angle=Math.sin(i/N*2*Math.PI), name=angle<0?'left':'right',t=Math.abs(angle);
    if(t<1e-8)return null;
    const sourceA=coords.center,sourceB=coords[name],aData=buffers.center,bData=buffers[name];
    const target=sourceA.map((a,j)=>[lerp(a[0],sourceB[j][0],t),lerp(a[1],sourceB[j][1],t)]);
    const triangles=meshes[name],output=Buffer.alloc(W*H*4);
    for(let tri=0;tri<triangles.length;tri+=3){
      const ids=[triangles[tri],triangles[tri+1],triangles[tri+2]],p=ids.map(id=>target[id]);
      const [p0,p1,p2]=p;
      const denominator=(p1[1]-p2[1])*(p0[0]-p2[0])+(p2[0]-p1[0])*(p0[1]-p2[1]);
      if(Math.abs(denominator)<1e-6)continue;
      const xMin=Math.max(0,Math.floor(Math.min(...p.map(v=>v[0])))),xMax=Math.min(W-1,Math.ceil(Math.max(...p.map(v=>v[0]))));
      const yMin=Math.max(0,Math.floor(Math.min(...p.map(v=>v[1])))),yMax=Math.min(H-1,Math.ceil(Math.max(...p.map(v=>v[1]))));
      const a=ids.map(id=>sourceA[id]),b=ids.map(id=>sourceB[id]);
      for(let y=yMin;y<=yMax;y++)for(let x=xMin;x<=xMax;x++){
        const u=((p1[1]-p2[1])*(x-p2[0])+(p2[0]-p1[0])*(y-p2[1]))/denominator;
        const v=((p2[1]-p0[1])*(x-p2[0])+(p0[0]-p2[0])*(y-p2[1]))/denominator,w=1-u-v;
        if(u<-.00001||v<-.00001||w<-.00001)continue;
        const ax=u*a[0][0]+v*a[1][0]+w*a[2][0],ay=u*a[0][1]+v*a[1][1]+w*a[2][1];
        const bx=u*b[0][0]+v*b[1][0]+w*b[2][0],by=u*b[0][1]+v*b[1][1]+w*b[2][1];
        const offset=(y*W+x)*4,alpha=lerp(sample(aData,ax,ay,3),sample(bData,bx,by,3),t);
        output[offset+3]=Math.round(alpha);
        if(alpha>2)for(let c=0;c<3;c++)output[offset+c]=Math.round(Math.max(0,Math.min(255,lerp(sample(aData,ax,ay,c),sample(bData,bx,by,c),t)*255/alpha)));
      }
    }
    return output;
  };
}
module.exports={prepare};
