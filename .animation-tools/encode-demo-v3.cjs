const {spawnSync}=require('child_process');
const path=require('path');
const ffmpeg=require('ffmpeg-static');
const out=path.resolve(__dirname,'../output/hudi-pet-v3');
function run(args){const r=spawnSync(ffmpeg,['-hide_banner','-loglevel','warning','-y',...args],{windowsHide:true,encoding:'utf8',maxBuffer:20e6});if(r.status!==0)throw Error(r.stderr);}
const input=path.join(out,'demo-frames','demo-%03d.png');
run(['-framerate','20','-i',input,'-filter_complex','[0:v]split[a][b];[a]palettegen=max_colors=256:stats_mode=full[p];[b][p]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle','-loop','0',path.join(out,'HUDI-eyes-demo.gif')]);
run(['-framerate','20','-i',input,'-c:v','libx264','-crf','17','-pix_fmt','yuv420p','-movflags','+faststart',path.join(out,'HUDI-eyes-demo.mp4')]);
console.log('Saved animated GIF and MP4.');
