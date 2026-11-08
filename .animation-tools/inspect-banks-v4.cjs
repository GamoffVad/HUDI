const fs=require('fs'),path=require('path'),sharp=require('sharp');
const root=path.resolve(__dirname,'../output/hudi-pet-v4'),m=JSON.parse(fs.readFileSync(path.join(root,'frames/manifest.json')));
async function main(){const all={};for(const bank of ['left','right','up','down']){
 const metrics=[],tiles=[];
 for(let i=0;i<m[bank].length;i++){
  const id=m[bank][i],file=path.join(root,'frames',id+'.png'),data=await sharp(file).ensureAlpha().raw().toBuffer();let top=768;
  for(let y=0;y<768;y++)for(let x=0;x<512;x++)if(data[(y*512+x)*4+3]>160){top=Math.min(top,y);}
  let x0=512,x1=0,n=0,sx=0,sy=0;
  for(let y=top;y<top+240;y++)for(let x=0;x<512;x++){const p=(y*512+x)*4;if(data[p+3]>160){x0=Math.min(x0,x);x1=Math.max(x1,x);}if(data[p+3]>220&&data[p]>150&&data[p]-data[p+1]>30&&data[p]-data[p+2]>10){sx+=x;sy+=y;n++;}}
  metrics.push({id,yaw:+((sx/n-(x0+x1)/2)/(x1-x0)).toFixed(4),pitch:+((sy/n-top)/(x1-x0)).toFixed(4),n});
  const label=Buffer.from(`<svg width="150" height="24"><text x="5" y="17" fill="white" font-size="12">${i}: ${id}</text></svg>`);
  const input=await sharp(file).resize(150,225).flatten({background:'#1c2028'}).extend({top:24,bottom:0,left:0,right:0,background:'#1c2028'}).composite([{input:label,left:0,top:0}]).png().toBuffer();tiles.push({input,left:i%5*150,top:Math.floor(i/5)*249});
 }
 all[bank]=metrics;await sharp({create:{width:750,height:Math.ceil(m[bank].length/5)*249,channels:4,background:'#1c2028'}}).composite(tiles).png().toFile(path.join(root,'previews',bank+'-bank.png'));
 }fs.writeFileSync(path.join(root,'smooth/bank-metrics.json'),JSON.stringify(all,null,2));console.log(JSON.stringify(all));}
main().catch(e=>{console.error(e);process.exitCode=1});
