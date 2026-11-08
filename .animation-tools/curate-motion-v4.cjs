const fs=require('fs'),path=require('path');
const root=path.resolve(__dirname,'../output/hudi-pet-v4'),file=path.join(root,'frames/manifest.json'),m=JSON.parse(fs.readFileSync(file));
if(m.curated){console.log('Motion already curated');process.exit(0);}
const orders={
 right:[0,1,3,4,6,7,2,5,9,10,8,12,11,13,15,16,14,19,18,17,21,22,20,23,24],
 left:[0,1,3,4,2,6,7,5,10,12,11,8,9,13,15,16,14,19,18,17,22,20,21,23,24],
 up:[0,1,3,2,4,6,7,5,9,10,8,12,11],
 down:[0,1,3,4,2,6,5,7,9,10,8,12,11]
};
const audit={};
for(const[bank,order]of Object.entries(orders)){
 const old=m[bank],name={left:'turn-left',right:'turn-right',up:'look-up',down:'look-down'}[bank];
 if(order.length!==old.length||new Set(order).size!==old.length)throw Error('Incomplete curation '+bank);
 const frames=order.map(i=>old[i]);m[bank]=frames;
 const previous=m.clips[name],duration=previous.reduce((s,f)=>s+f.seconds,0),hold=Math.max(...previous.map(s=>s.seconds));
 const ids=[...frames.slice(1),...frames.slice(0,-1).reverse()],step=(duration-hold)/(ids.length-1);
 m.clips[name]=ids.map((frame,i)=>({frame,seconds:i===frames.length-2?hold:step}));
 audit[bank]={order,duration,step,hold};
}
m.curated=true;fs.writeFileSync(file,JSON.stringify(m,null,2));fs.writeFileSync(path.join(root,'smooth/curation.json'),JSON.stringify(audit,null,2));
console.log('Curated four orientation sequences; preserved every generated frame and clip duration.');
