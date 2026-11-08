const fs=require('fs'),path=require('path'),sharp=require('sharp');
const root=path.resolve(__dirname,'../output/hudi-pet-v4'),smooth=path.join(root,'smooth'),dir=path.join(root,'frames');
function box(data,w,h,minY=0){let x0=w,y0=h,x1=-1,y1=-1;for(let y=minY;y<h;y++)for(let x=0;x<w;x++)if(data[(y*w+x)*4+3]>160){x0=Math.min(x0,x);y0=Math.min(y0,y);x1=Math.max(x1,x);y1=Math.max(y1,y);}if(x1<0)throw Error('Empty sprite');return{x0,y0,x1,y1,height:y1-y0+1};}
async function main(){
 const jobs=JSON.parse(fs.readFileSync(path.join(smooth,'jobs.json'))),base=JSON.parse(fs.readFileSync(path.join(smooth,'manifest-base.json'))),metrics={};
 const missing=jobs.filter(j=>!fs.existsSync(path.join(smooth,'records',j.key+'.json')));if(missing.length)throw Error('Still missing '+missing.length+' sheets: '+missing.map(j=>j.key).join(', '));
 const bounds={};for(const id of base.frames){const{data,info}=await sharp(path.join(dir,id+'.png')).ensureAlpha().raw().toBuffer({resolveWithObject:true});bounds[id]=box(data,info.width,info.height);}
 const edges=new Map(),added=[];
 for(const job of jobs){
  const record=JSON.parse(fs.readFileSync(path.join(smooth,'records',job.key+'.json')));const source=path.join(smooth,'sources',job.key+'.png');fs.copyFileSync(record.path,source);
  const meta=await sharp(source).metadata();if(meta.width!==1024||meta.height!==1536)throw Error('Wrong sheet size '+job.key);
  for(let row=0;row<2;row++){
   const edge=job.edges[row];if(edge.duplicate)continue;const intermediates=[];
   for(let col=0;col<2;col++){
    const t=(col+1)/3,id='smooth-'+String(edge.index).padStart(3,'0')+'-'+(col+1);
    const raw=await sharp(source).extract({left:col*512,top:row*768,width:512,height:768}).png().toBuffer();
    const{data}=await sharp(raw).ensureAlpha().raw().toBuffer({resolveWithObject:true});const b=box(data,512,768),feet=box(data,512,768,Math.max(0,b.y1-90));
    const targetHeight=bounds[edge.a].height*(1-t)+bounds[edge.b].height*t,scale=targetHeight/b.height;
    const w=Math.round(512*scale),h=Math.round(768*scale),left=Math.round(256-(feet.x0+feet.x1)*.5*scale),top=Math.round(738-b.y1*scale);
    const padded=await sharp({create:{width:2560,height:2560,channels:4,background:'#00000000'}}).composite([{input:await sharp(raw).resize(w,h).png().toBuffer(),left:left+1024,top:top+1024}]).png().toBuffer();
    const output=await sharp(padded).extract({left:1024,top:1024,width:512,height:768}).png().toBuffer();fs.writeFileSync(path.join(dir,id+'.png'),output);
    const pixels=await sharp(output).ensureAlpha().raw().toBuffer();const final=box(pixels,512,768);
    if(final.x0<8||final.x1>503||final.y0<8||final.y1>759)throw Error('Clipped generated sprite '+id);
    metrics[id]={...final,from:edge.a,to:edge.b,fraction:t,scale,source:job.key,cell:row*2+col};
    intermediates.push(id);added.push(id);
   }
   edges.set(edge.key,{...edge,intermediates});
  }
 }
 function between(a,b){if(a===b)return[];const edge=edges.get([a,b].sort().join('|'));if(!edge)throw Error('Unprepared transition '+a+' -> '+b);return edge.a===a?edge.intermediates:edge.intermediates.slice().reverse();}
 const manifest={...base,density:3,orientationFrameSeconds:.11/3,baseFrameCount:base.frames.length,frames:[...base.frames,...added],clips:{}};
 for(const name of ['left','right','up','down'])manifest[name]=base[name].flatMap((id,i)=>i?[...between(base[name][i-1],id),id]:[id]);
 for(const[name,clip]of Object.entries(base.clips)){
  let previous='neutral';const sequence=[];
  for(const step of clip){const inserted=between(previous,step.frame);const time=Math.min(step.seconds,.12)/3;for(const frame of inserted)sequence.push({frame,seconds:time});sequence.push({frame:step.frame,seconds:step.seconds-time*inserted.length});previous=step.frame;}
  if(previous!=='neutral'){
   // Idle returns to its start continuously; allocate existing final-step time to its return.
   const tail=between(previous,'neutral'),last=sequence.at(-1),time=Math.min(last.seconds,.12)/(tail.length+1);last.seconds-=time*tail.length;for(const frame of tail)sequence.push({frame,seconds:time});
  }
  manifest.clips[name]=sequence;
  const oldTime=clip.reduce((a,s)=>a+s.seconds,0),newTime=sequence.reduce((a,s)=>a+s.seconds,0);if(Math.abs(newTime-oldTime)>1e-7)throw Error('Changed clip duration '+name);
 }
 fs.writeFileSync(path.join(dir,'manifest.json'),JSON.stringify(manifest,null,2));fs.writeFileSync(path.join(smooth,'metrics.json'),JSON.stringify(metrics,null,2));
 const report={generatedIntermediateFrames:added.length,totalFrames:manifest.frames.length,transitions:edges.size,density:3,durationsPreserved:true,borderCheck:'passed'};
 fs.writeFileSync(path.join(smooth,'integration-report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
}
main().catch(e=>{console.error(e);process.exitCode=1});
