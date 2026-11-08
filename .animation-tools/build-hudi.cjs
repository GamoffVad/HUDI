// Assemble generated turn poses into a seamless alpha animation and GIF preview.
const fs = require('fs');
const path = require('path');
const sharp = require('sharp');
const {spawnSync} = require('child_process');
const ffmpeg = require('ffmpeg-static');
const out = path.resolve(__dirname, '../output/hudi-pet');
const work = path.join(out, 'work');
const framesDir = path.join(out, 'frames');
const W = 362, H = 544, N = 160;
const smooth = t => { t=Math.max(0,Math.min(1,t));return t*t*(3-2*t); };
function run(args) {
  const r=spawnSync(ffmpeg,['-hide_banner','-loglevel','warning','-y',...args],{windowsHide:true,encoding:'utf8',maxBuffer:20e6});
  if(r.status!==0)throw new Error(r.stderr); if(r.stderr)console.log(r.stderr.trim());
}
function sample(data,x,y,c){if(x<0||x>=W||y<0||y>=H)return 0;return data[(y*W+x)*4+c];}
function align(base,frame){
  let best={dx:0,dy:0,error:Infinity};
  for(let dy=-5;dy<=5;dy++)for(let dx=-12;dx<=12;dx++){
    let error=0;
    for(let y=441;y<520;y+=2)for(let x=80;x<280;x+=2){
      for(let c=0;c<4;c++)error+=Math.abs(base[(y*W+x)*4+c]-sample(frame,x+dx,y+dy,c));
    }
    if(error<best.error)best={dx,dy,error};
  }return best;
}
(async()=>{
  fs.mkdirSync(work,{recursive:true});fs.mkdirSync(framesDir,{recursive:true});
  const sprites=[];
  for(let i=0;i<8;i++)sprites.push(await sharp(path.join(out,'turn-sprites.png')).extract({left:(i%4)*362,top:Math.floor(i/4)*543,width:362,height:543}).extend({bottom:1,background:{r:0,g:0,b:0,alpha:0}}).ensureAlpha().raw().toBuffer());
  const base=sprites[2], regs=sprites.map(f=>align(base,f));
  // The generator put pose 1 on the right. Select correctly directed poses explicitly.
  const order=[2,3,4,3,2,7,0,7];
  for(let k=0;k<8;k++){
    const source=sprites[order[k]], reg=regs[order[k]], data=Buffer.alloc(W*H*4);
    for(let y=0;y<H;y++)for(let x=0;x<W;x++){
      const a=smooth((y-386)/18),p=(y*W+x)*4;
      for(let c=0;c<4;c++)data[p+c]=Math.round(sample(source,x+reg.dx,y+reg.dy,c)*(1-a)+base[p+c]*a);
      if(data[p+3]<3){data[p]=0;data[p+1]=0;data[p+2]=0;data[p+3]=0;}
    }
    await sharp(data,{raw:{width:W,height:H,channels:4}}).png().toFile(path.join(work,`key-${k}.png`));
    await sharp(data,{raw:{width:W,height:H,channels:4}}).removeAlpha().png().toFile(path.join(work,`rgb-${k}.png`));
    await sharp(data,{raw:{width:W,height:H,channels:4}}).extractChannel(3).png().toFile(path.join(work,`alpha-${k}.png`));
  }
  console.log('Key poses aligned. Rendering 160 frames with eye-registered mesh turns.');
  const morph=await require('./turn-morph.cjs').prepare(work);
  let maxFeetDifference=0,totalMotion=0,boundaryDifference=0;let first,last;
  for(let i=0;i<N;i++){
    const stem=String(i).padStart(3,'0');
    const movingFrame=morph(i,N)||base;
    const data=Buffer.alloc(W*H*4);
    for(let y=0;y<H;y++)for(let x=0;x<W;x++){
      const p=(y*W+x)*4, a=smooth((y-386)/18);
      for(let c=0;c<4;c++){
        const moving=movingFrame[p+c];
        data[p+c]=Math.round(moving*(1-a)+base[p+c]*a);
      }
      if(data[p+3]<3){data[p]=0;data[p+1]=0;data[p+2]=0;data[p+3]=0;}
    }
    if(first){
      for(let p=0;p<data.length;p++)if(p>=W*424*4)maxFeetDifference=Math.max(maxFeetDifference,Math.abs(data[p]-first[p]));
      for(let p=0;p<data.length;p+=4)totalMotion+=Math.abs(data[p]-first[p]);
    }else first=Buffer.from(data);
    last=data;
    await sharp(data,{raw:{width:W,height:H,channels:4}}).png().toFile(path.join(framesDir,`hudi-${stem}.png`));
    await sharp(data,{raw:{width:W,height:H,channels:4}}).flatten({background:'#eceef2'}).png().toFile(path.join(work,`preview-${stem}.png`));
  }
  for(let p=0;p<last.length;p++)boundaryDifference+=Math.abs(last[p]-first[p]);
  console.log('Encoding GIF and video previews.');
  run(['-framerate','25','-i',path.join(work,'preview-%03d.png'),'-filter_complex','[0:v]split[a][b];[a]palettegen=max_colors=256:stats_mode=full[p];[b][p]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle','-loop','0',path.join(out,'HUDI-turn-loop.gif')]);
  run(['-framerate','25','-i',path.join(work,'preview-%03d.png'),'-c:v','libx264','-crf','17','-pix_fmt','yuv420p','-movflags','+faststart',path.join(out,'HUDI-turn-loop.mp4')]);
  run(['-framerate','25','-i',path.join(framesDir,'hudi-%03d.png'),'-filter_complex','[0:v]split[a][b];[a]palettegen=reserve_transparent=1:stats_mode=full[p];[b][p]paletteuse=dither=bayer:bayer_scale=3:alpha_threshold=128','-loop','0',path.join(out,'HUDI-transparent.gif')]);
  const picks=[0,20,40,60,80,100,120,140];
  const tiles=await Promise.all(picks.map(i=>sharp(path.join(work,`preview-${String(i).padStart(3,'0')}.png`)).resize(181,272).toBuffer()));
  await sharp({create:{width:724,height:544,channels:3,background:'#eceef2'}}).composite(tiles.map((input,i)=>({input,left:(i%4)*181,top:Math.floor(i/4)*272}))).png().toFile(path.join(out,'contact-sheet.png'));
  fs.writeFileSync(path.join(out,'verification.json'),JSON.stringify({width:W,height:H,frames:N,fps:25,durationSeconds:N/25,loop:'infinite',maxFeetDifference,totalMotion,boundaryMeanDifference:boundaryDifference/last.length,sourcePoseOrder:order,registrations:regs},null,2));
  console.log(JSON.stringify({output:out,maxFeetDifference,frames:N}));
})().catch(e=>{console.error(e);process.exit(1)});
