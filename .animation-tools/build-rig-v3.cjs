const fs=require('fs'),path=require('path'),sharp=require('sharp');
const out=path.resolve(__dirname,'../output/hudi-pet-v3');
const reflect=poly=>poly.map(([x,y])=>[1024-x,y]);
const leftArm=[[365,685],[369,804],[340,900],[316,1010],[285,1090],[277,1160],[248,1232],[90,1232],[80,905],[170,800],[270,712]];
const leftLeg=[[301,1115],[514,1115],[514,1385],[237,1385],[242,1312],[270,1295],[280,1230],[291,1160]];
const shapes={
  head:[[90,15],[930,15],[930,632],[765,660],[610,675],[414,675],[259,660],[90,632]],
  torso:[[325,652],[699,652],[740,760],[724,880],[742,1010],[742,1152],[282,1152],[282,1010],[300,880],[284,760]],
  leftArm,rightArm:reflect(leftArm),
  leftLeg,rightLeg:reflect(leftLeg),
  leftFoot:[[225,1344],[514,1344],[514,1522],[225,1522]],
  rightFoot:[[510,1344],[799,1344],[799,1522],[510,1522]]
};
(async()=>{
  const manifest={width:1024,height:1536,parts:{}};
  for(const[name,poly]of Object.entries(shapes)){
    const minX=Math.max(0,Math.floor(Math.min(...poly.map(p=>p[0])))-2),minY=Math.max(0,Math.floor(Math.min(...poly.map(p=>p[1])))-2);
    const width=Math.min(1024-minX,Math.ceil(Math.max(...poly.map(p=>p[0])))-minX+3),height=Math.min(1536-minY,Math.ceil(Math.max(...poly.map(p=>p[1])))-minY+3);
    const mask=Buffer.from(`<svg width="1024" height="1536"><polygon points="${poly.map(p=>p.join(',')).join(' ')}" fill="white"/></svg>`);
    const source=name==='head'?path.join(out,'blank-face-source.png'):path.resolve(__dirname,'../output/hudi-pet-v2/sources/front.png');
    const cut=await sharp(source).ensureAlpha().composite([{input:mask,blend:'dest-in'}]).png().toBuffer();
    await sharp(cut).extract({left:minX,top:minY,width,height}).png().toFile(path.join(out,'assets',name+'.png'));
    manifest.parts[name]={x:minX,y:minY,width,height};
  }
  fs.writeFileSync(path.join(out,'assets','rig.json'),JSON.stringify(manifest,null,2));
  console.log('Prepared eight articulated layers; hands remain inside their arm layers.');
})().catch(e=>{console.error(e);process.exit(1)});
