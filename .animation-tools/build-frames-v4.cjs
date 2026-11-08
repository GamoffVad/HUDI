// Splits complete generated poses; only uniform whole-image scaling/translation is used.
// No body-part masks, warping, optical flow, or synthetic in-between frames.
const fs = require('fs'), path = require('path'), sharp = require('sharp');
const root = path.resolve(__dirname, '../output/hudi-pet-v4');
const framesDir = path.join(root, 'frames');
const chosen = ['turn-right-heel-a','turn-right-heel-b','turn-right-heel-c',
  'turn-left-heel-a','turn-left-heel-b','turn-left-heel-c',
  'look-up','look-down','crouch-short','think','scratch-prep','scratch-contact','cross-arms-clean',
  'eyes','idle','shrug','tap','wave','stretch','surprised','sleepy',
  'think-bridge','cross-arms-bridge','scratch-bridge','crouch-bridge'];
const metrics = {};
function bounds(data,w,h,threshold=160,minY=0) {
  let x0=w,y0=h,x1=-1,y1=-1;
  for(let y=minY;y<h;y++)for(let x=0;x<w;x++)if(data[(y*w+x)*4+3]>threshold){x0=Math.min(x0,x);x1=Math.max(x1,x);y0=Math.min(y0,y);y1=Math.max(y1,y);}
  if(x1<0)throw new Error('Empty frame');
  return {x0,y0,x1,y1,width:x1-x0+1,height:y1-y0+1};
}
const ids = key => Array.from({length:4},(_,i)=>key+'-'+i);
const steps = (list, seconds=.13) => list.map(frame=>({frame,seconds}));
const loop = (list,hold=.65,speed=.13) => [...steps(['neutral'],.15),...steps(list.slice(0,-1),speed),...steps(list.slice(-1),hold),...steps(list.slice(0,-1).reverse(),speed),...steps(['neutral'],.25)];
async function main(){
  fs.mkdirSync(framesDir,{recursive:true});
  const log = JSON.parse(fs.readFileSync(path.join(root,'generation-log.json'),'utf8').replace(/^\uFEFF/,''));
  for(const item of log)fs.copyFileSync(item.path,path.join(root,'sources',item.key+'.png'));
  const missing=chosen.filter(key=>!fs.existsSync(path.join(root,'sources',key+'.png')));
  if(missing.length)throw new Error('Missing generated sheets: '+missing.join(', '));
  for(const key of chosen){
    const source=path.join(root,'sources',key==='look-up'&&fs.existsSync(path.join(root,'sources','look-up-clean.png'))?'look-up-clean.png':key+'.png');const meta=await sharp(source).metadata();
    if(meta.width!==1024||meta.height!==1536)throw new Error('Unexpected sheet size: '+key);
    const cells=[];
    for(let i=0;i<4;i++){
      const input=await sharp(source).extract({left:i%2*512,top:Math.floor(i/2)*768,width:512,height:768}).png().toBuffer();
      const {data}=await sharp(input).ensureAlpha().raw().toBuffer({resolveWithObject:true});
      const box=bounds(data,512,768),feet=bounds(data,512,768,160,Math.max(0,box.y1-90));
      cells.push({input,box,feet});
    }
    // One scale for the whole sheet keeps crouching height changes and cloth proportions intact.
    const scale=660/Math.max(...cells.map(c=>c.box.height));
    for(let i=0;i<4;i++){
      const {input,box,feet}=cells[i];const width=Math.round(512*scale),height=Math.round(768*scale);
      const left=Math.round(256-(feet.x0+feet.x1)/2*scale),top=Math.round(738-box.y1*scale);
      const padded=await sharp({create:{width:2048,height:2048,channels:4,background:'#00000000'}})
        .composite([{input:await sharp(input).resize(width,height).png().toBuffer(),left:left+768,top:top+768}]).png().toBuffer();
      const image=await sharp(padded).extract({left:768,top:768,width:512,height:768}).png().toBuffer();
      const id=key+'-'+i;fs.writeFileSync(path.join(framesDir,id+'.png'),image);
      const {data}=await sharp(image).raw().toBuffer({resolveWithObject:true});
      metrics[id]={...bounds(data,512,768,64),source:key+'.png',cell:i,scale,left,top};
    }
  }
  fs.copyFileSync(path.join(framesDir,'turn-right-heel-a-0.png'),path.join(framesDir,'neutral.png'));
  metrics.neutral={...metrics['turn-right-heel-a-0']};
  // Curated by the visible angles (generated sheets do not guarantee exact numeric yaw).
  const left=['neutral',...ids('turn-left-heel-a').slice(1),'turn-left-heel-b-1','turn-left-heel-b-2','turn-left-heel-b-3','turn-left-heel-c-2','turn-left-heel-c-3'];
  const right=['neutral',...ids('turn-right-heel-a').slice(1),'turn-right-heel-b-1','turn-right-heel-b-2','turn-right-heel-b-3','turn-right-heel-c-2','turn-right-heel-c-3'];
  const up=['neutral',...ids('look-up')],down=['neutral',...ids('look-down')];
  const withBridges=(key,bridge)=>ids(key).flatMap((id,i)=>[bridge+'-'+i,id]);
  const squat=[...ids('crouch-short'),...ids('crouch-bridge')].sort((a,b)=>metrics[b].height-metrics[a].height);
  const scratchPrep=withBridges('scratch-prep','scratch-bridge');
  const clips={
    'turn-left':loop(left.slice(1),.65,.095),'turn-right':loop(right.slice(1),.65,.095),
    'look-up':loop(up.slice(1),.8,.16),'look-down':loop(down.slice(1),.8,.16),
    crouch:loop(squat,.65,.1),think:loop(withBridges('think','think-bridge'),1.3,.1),
    'cross-arms':loop(withBridges('cross-arms-clean','cross-arms-bridge'),1.4,.1),
    scratch:[...steps(['neutral'],.15),...steps(scratchPrep,.095),
      ...steps(['scratch-contact-1','scratch-contact-0','scratch-contact-1','scratch-contact-2','scratch-contact-3','scratch-contact-2'],.18),
      ...steps(scratchPrep.slice().reverse(),.095),...steps(['neutral'],.25)],
    blink:[...steps(['eyes-0'],.055),...steps(['eyes-1'],.075),...steps(['eyes-0'],.055),...steps(['neutral'],.1)],
    wink:loop(['eyes-2'],.45),smile:loop(['eyes-0','eyes-3'],1.1),
    idle:steps(['neutral','idle-0','idle-1','idle-2','idle-3','idle-2','idle-1','idle-0'],.28),
    shrug:loop(ids('shrug'),.6,.16),tap:loop(ids('tap'),.14,.12),wave:loop(ids('wave'),.55,.18),
    stretch:loop(ids('stretch'),.75,.18),surprised:loop(ids('surprised'),.7,.16),sleepy:loop(ids('sleepy'),1,.23),
    curious:loop(['think-0','look-up-0','look-up-1','look-up-0','look-down-0'],.4,.19)
  };
  const manifest={version:4,frameSize:[512,768],rendering:'complete-generated-frames',neutral:'neutral',left,right,up,down,clips,frames:Object.keys(metrics)};
  fs.writeFileSync(path.join(framesDir,'manifest.json'),JSON.stringify(manifest,null,2));
  fs.writeFileSync(path.join(root,'frame-metrics.json'),JSON.stringify(metrics,null,2));
  const issues=Object.entries(metrics).filter(([id,b])=>b.x0<12||b.x1>500||b.y0<12||b.y1>751);
  if(issues.length)throw new Error('Insufficient margin: '+JSON.stringify(issues));
  const crouch=ids('crouch-short').map(id=>metrics[id].height);
  if(crouch[3]>=crouch[0]*.9)throw new Error('Crouch did not get shorter: '+crouch);
  console.log(JSON.stringify({generatedFrames:manifest.frames.length-1,clips:Object.keys(clips).length,crouchHeights:crouch,marginCheck:'passed'}));
}
main().catch(error=>{console.error(error);process.exitCode=1});
