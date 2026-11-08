const fs=require('fs'),path=require('path'),sharp=require('sharp');
const oldRoot=path.resolve(__dirname,'../output/hudi-pet-v4'),root=path.resolve(__dirname,'../output/hudi-pet-v4.1');
const original=JSON.parse(fs.readFileSync(path.join(oldRoot,'source-4.0/manifest.json'))),m=structuredClone(original),features=new Map(),distances=new Map();
function distance(a,b){if(a===b)return 0;const key=[a,b].sort().join('|');if(distances.has(key))return distances.get(key);const x=features.get(a),y=features.get(b);let s=0;for(let i=0;i<x.length;i++)s+=(x[i]-y[i])**2;const d=Math.sqrt(s/x.length);distances.set(key,d);return d;}
function prune(ids){
 ids=ids.filter((id,i)=>!i||id!==ids[i-1]);const cost=ids.map(()=>Infinity),prev=ids.map(()=>-1);cost[0]=0;
 for(let i=1;i<ids.length;i++)for(let j=Math.max(0,i-3);j<i;j++){const value=cost[j]+distance(ids[j],ids[i])**2*(i-j)**1.7;if(value<cost[i]){cost[i]=value;prev[i]=j;}}
 const out=[];for(let i=ids.length-1;i>=0;i=prev[i])out.unshift(ids[i]);return out;
}
function travel(ids,seconds){
 const lengths=ids.slice(1).map((id,i)=>Math.max(.005,distance(ids[i],id))),total=lengths.reduce((s,n)=>s+n,0);
 let accumulated=0,previous=0;const times=lengths.map(length=>{accumulated+=length;const eased=Math.acos(1-2*Math.min(1,accumulated/total))/Math.PI;const t=eased-previous;previous=eased;return Math.max(.045,t*seconds);});
 return ids.slice(1).map((frame,i)=>({frame,seconds:times[i]}));
}
async function main(){
 fs.mkdirSync(root,{recursive:true});
 for(const id of m.frames){
  const raw=await sharp(path.join(oldRoot,'frames',id+'.png')).resize(48,72).ensureAlpha().raw().toBuffer(),vector=new Float32Array(48*72*2);
  for(let p=0,j=0;p<raw.length;p+=4){const a=Math.max(0,(raw[p+3]-150)/105);vector[j++]=a*1.7;vector[j++]=(raw[p]*.2126+raw[p+1]*.7152+raw[p+2]*.0722)/255*a;}
  features.set(id,vector);
 }
 const specs={
  'turn-left':['turn-left-heel-c-3',1.5,.65],'turn-right':['turn-right-heel-c-3',1.5,.65],
  'look-up':['smooth-019-2',1.1,.7],'look-down':['smooth-023-2',1.1,.7],
  crouch:['crouch-short-3',1.35,.65],think:['think-3',1.3,1.1],'cross-arms':['cross-arms-clean-3',1.35,1.2],
  shrug:['shrug-3',.9,.45],tap:['tap-3',.65,.13],wave:['wave-3',1,.55],stretch:['stretch-3',1.25,.7],surprised:['surprised-3',.9,.55],sleepy:['sleepy-3',1.4,1.2]
 };
 const report={version:'4.1',eyeTrackingMovesBody:false,blinkMovesBody:false,clips:{}};
 for(const[name,[endpoint,seconds,hold]]of Object.entries(specs)){
  const clip=original.clips[name],end=clip.findIndex(s=>s.frame===endpoint);if(end<0)throw Error(name+' endpoint missing');
  const oldIds=['neutral',...clip.slice(0,end+1).map(s=>s.frame)].filter((id,i,a)=>!i||id!==a[i-1]);const ids=prune(oldIds);
  const outward=travel(ids,seconds);outward.at(-1).seconds=hold;
  const returning=travel(ids.slice().reverse(),seconds);returning.at(-1).seconds=.24;
  m.clips[name]=[{frame:'neutral',seconds:.16},...outward,...returning];
  const oldJumps=oldIds.slice(1).map((id,i)=>distance(oldIds[i],id)),newJumps=ids.slice(1).map((id,i)=>distance(ids[i],id));
  report.clips[name]={beforePoses:oldIds.length,afterPoses:ids.length,removed:oldIds.filter(id=>!ids.includes(id)),oldMaxJump:Math.max(...oldJumps),newMaxJump:Math.max(...newJumps),seconds:m.clips[name].reduce((n,s)=>n+s.seconds,0),poses:ids};
  const bank={'turn-left':'left','turn-right':'right','look-up':'up','look-down':'down'}[name];if(bank)m[bank]=ids;
 }
 // Keep contact phases of scratching, but discard noisy detours between each intentional pose.
 for(const name of ['scratch','curious']){
  const clip=original.clips[name],anchors=name==='scratch'?['scratch-prep-3','scratch-contact-1','scratch-contact-0','scratch-contact-1','scratch-contact-2','scratch-contact-3','scratch-contact-2','scratch-prep-3','neutral']:['look-up-1','look-down-0','look-up-1','neutral'];
  const out=[{frame:'neutral',seconds:.16}];let start=0,last='neutral';
  for(const anchor of anchors){let end=clip.findIndex((s,i)=>i>=start&&s.frame===anchor);if(end<0)continue;const ids=prune([last,...clip.slice(start,end+1).map(s=>s.frame)]);const duration=last==='neutral'||anchor==='neutral'?1.15:anchor.startsWith('scratch-contact')?.28:.6;const phase=travel(ids,duration);if(phase.length){phase.at(-1).seconds=anchor==='neutral'?.24:.18;out.push(...phase);}last=anchor;start=end+1;}
  m.clips[name]=out;report.clips[name]={beforePoses:clip.length,afterPoses:out.length,seconds:out.reduce((n,s)=>n+s.seconds,0)};
 }
 m.version='4.1';m.playback='eye-only-tracking-and-curated-whole-body-gestures';m.densityNote='The 293-frame source library is retained; inconsistent frames are excluded from active gestures.';
 fs.writeFileSync(path.join(root,'manifest.json'),JSON.stringify(m,null,2));fs.writeFileSync(path.join(root,'motion-review.json'),JSON.stringify(report,null,2));
 console.log(JSON.stringify(Object.fromEntries(Object.entries(report.clips).map(([k,v])=>[k,{before:v.beforePoses,after:v.afterPoses,seconds:+v.seconds.toFixed(2)}]))));
}
main().catch(e=>{console.error(e);process.exitCode=1});
