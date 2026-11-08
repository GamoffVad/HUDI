const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const sharp = require('sharp');
const ffmpeg = require('ffmpeg-static');

const input = 'C:/Users/gamx8/.codex/generated_images/01a1b1e4-0646-7423-b61f-9233f504dfe1/exec-03518a20-87a8-4b39-acb1-3bc1d6b49141.png';
const out = 'D:/PR/HUDI/output/robot-stomp';
const work = path.join(out, 'frames');
const W = 251, H = 392;
const smooth = t => { t = Math.max(0, Math.min(1, t)); return t*t*(3-2*t); };
const rect = (x,y,l,t,r,b,f) => smooth((x-l)/f)*smooth((r-x)/f)*smooth((y-t)/f)*smooth((b-y)/f);
function run(args) {
  const r = spawnSync(ffmpeg, ['-hide_banner','-loglevel','warning','-y',...args], { encoding:'utf8', windowsHide:true, maxBuffer:16*1024*1024 });
  if(r.status !== 0) throw new Error(r.stderr || 'ffmpeg failed');
  if(r.stderr) console.log(r.stderr.trim());
}
function sample(buf,x,y,c) { x=Math.max(0,Math.min(W-1,x));y=Math.max(0,Math.min(H-1,y));return buf[(y*W+x)*3+c]; }
function registration(base, frame) {
  let best={dx:0,dy:0,error:Infinity};
  for(let dy=-4;dy<=4;dy++)for(let dx=-5;dx<=5;dx++){
    let error=0;
    for(let y=38;y<280;y+=3)for(let x=45;x<206;x+=3){
      if(y>98&&y<155)continue;
      const bgx=sample(base,x+1,y,1)-sample(base,x-1,y,1);
      const bgy=sample(base,x,y+1,1)-sample(base,x,y-1,1);
      const fgx=sample(frame,x+dx+1,y+dy,1)-sample(frame,x+dx-1,y+dy,1);
      const fgy=sample(frame,x+dx,y+dy+1,1)-sample(frame,x+dx,y+dy-1,1);
      error+=Math.min(300,(bgx-fgx)**2+(bgy-fgy)**2);
    }
    if(error<best.error)best={dx,dy,error};
  }
  return best;
}
(async()=>{
  fs.mkdirSync(work,{recursive:true});
  const meta=await sharp(input).metadata();
  const frames=[];
  for(let i=0;i<16;i++){
    const col=i%4,row=Math.floor(i/4);
    const left=Math.round(col*meta.width/4),right=Math.round((col+1)*meta.width/4);
    frames.push(await sharp(input).extract({left,top:row*H,width:right-left,height:H}).resize(W,H).removeAlpha().raw().toBuffer());
  }
  const base=frames[0];
  const registrations=[];
  for(let i=0;i<16;i++){
    const reg=registration(base,frames[i]); registrations.push(reg);
    const result=Buffer.from(base);
    for(let y=0;y<H;y++)for(let x=0;x<W;x++){
      // Freeze the complete head, torso, hands and background. Only use the
      // generated animation phases in the two leg areas and during the blink.
      let a=rect(x,y,57,278,201,386,10);
      if(i>=9&&i<=11){
        a=Math.max(a,rect(x,y,101,96,138,142,7),rect(x,y,158,107,190,153,7));
      }
      if(a===0)continue;
      const p=(y*W+x)*3;
      for(let c=0;c<3;c++)result[p+c]=Math.round(base[p+c]*(1-a)+sample(frames[i],x+reg.dx,y+reg.dy,c)*a);
    }
    await sharp(result,{raw:{width:W,height:H,channels:3}}).resize(252,392).png().toFile(path.join(work,`key-${String(i).padStart(3,'0')}.png`));
  }
  // Repeat surrounding phases so optical flow sees a continuous loop boundary.
  for(let i=0;i<48;i++)fs.copyFileSync(path.join(work,`key-${String(i%16).padStart(3,'0')}.png`),path.join(work,`cycle-${String(i).padStart(3,'0')}.png`));
  run(['-framerate','25/4','-i',path.join(work,'cycle-%03d.png'),'-vf','format=yuv444p,minterpolate=fps=25:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:me=umh:mb_size=4:search_param=16:vsbmc=1:scd=none,trim=start_frame=64:end_frame=128,setpts=PTS-STARTPTS,scale=512:800:flags=lanczos','-fps_mode','passthrough',path.join(work,'smooth-%03d.png')]);
  run(['-framerate','25','-i',path.join(work,'smooth-%03d.png'),'-filter_complex','[0:v]split[a][b];[a]palettegen=max_colors=256:stats_mode=full[p];[b][p]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle','-loop','0',path.join(out,'robot-stomp-loop.gif')]);
  run(['-framerate','25','-i',path.join(work,'smooth-%03d.png'),'-c:v','libx264','-preset','slow','-crf','17','-pix_fmt','yuv420p','-movflags','+faststart',path.join(out,'robot-stomp-loop.mp4')]);
  const chosen=[0,8,16,24,32,40,48,56];
  const tiles=await Promise.all(chosen.map(i=>sharp(path.join(work,`smooth-${String(i+1).padStart(3,'0')}.png`)).resize(256,400).toBuffer()));
  await sharp({create:{width:1024,height:800,channels:3,background:'#c0c3c9'}}).composite(tiles.map((input,i)=>({input,left:(i%4)*256,top:Math.floor(i/4)*400}))).png().toFile(path.join(out,'animation-contact-sheet.png'));
  fs.writeFileSync(path.join(out,'build-info.json'),JSON.stringify({source:input,width:512,height:800,frames:64,fps:25,durationSeconds:2.56,loop:'infinite',registrations},null,2));
  console.log(JSON.stringify({gif:path.join(out,'robot-stomp-loop.gif'),mp4:path.join(out,'robot-stomp-loop.mp4'),bytes:fs.statSync(path.join(out,'robot-stomp-loop.gif')).size,frames:64,fps:25}));
})().catch(e=>{console.error(e);process.exit(1)});
