const fs=require('fs'),path=require('path'),sharp=require('sharp');
const root=path.resolve(__dirname,'../output/hudi-pet-v4'),smooth=path.join(root,'smooth');
async function main(){
 for(const sub of ['refs','sources','records'])fs.mkdirSync(path.join(smooth,sub),{recursive:true});
 const m=JSON.parse(fs.readFileSync(path.join(root,'frames/manifest.json')));if(m.density===3)throw new Error('Already dense; use existing smooth/manifest-base.json');
 fs.writeFileSync(path.join(smooth,'manifest-base.json'),JSON.stringify(m,null,2));
 const edges=new Map();
 function add(list){for(let i=1;i<list.length;i++){const a=list[i-1],b=list[i];if(a===b)continue;const key=[a,b].sort().join('|');if(!edges.has(key))edges.set(key,{a,b,key,index:edges.size});}}
 for(const key of ['right','left','up','down'])add(m[key]);
 for(const steps of Object.values(m.clips))add(['neutral',...steps.map(s=>s.frame),'neutral']);
 const all=[...edges.values()],jobs=[];
 for(let i=0;i<all.length;i+=2){
  const pair=all.slice(i,i+2);if(pair.length===1)pair.push({...pair[0],duplicate:true});
  const key='between-'+String(i/2).padStart(3,'0'),ref=path.join(smooth,'refs',key+'.png');
  const tiles=[];for(let row=0;row<2;row++)for(let col=0;col<2;col++)tiles.push({input:path.join(root,'frames',pair[row][col===0?'a':'b']+'.png'),left:col*512,top:row*768});
  await sharp({create:{width:1024,height:1536,channels:4,background:'#00000000'}}).composite(tiles).png().toFile(ref);
  jobs.push({key,reference:ref,edges:pair});
 }
 fs.writeFileSync(path.join(smooth,'jobs.json'),JSON.stringify(jobs,null,2));
 console.log(JSON.stringify({baseFrames:m.frames.length,transitions:all.length,newGeneratedFrames:all.length*2,sheets:jobs.length}));
}
main().catch(e=>{console.error(e);process.exitCode=1});
