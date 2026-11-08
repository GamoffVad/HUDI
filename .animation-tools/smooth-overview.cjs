const fs=require('fs'),path=require('path'),sharp=require('sharp');
const root=path.resolve(__dirname,'../output/hudi-pet-v4/smooth'),jobs=JSON.parse(fs.readFileSync(path.join(root,'jobs.json')));
const start=Number(process.argv[2]||0),count=Number(process.argv[3]||8);
(async()=>{const tiles=[];for(let i=0;i<count&&start+i<jobs.length;i++){tiles.push({input:await sharp(jobs[start+i].reference).resize(384,576).png().toBuffer(),left:i%4*384,top:Math.floor(i/4)*576});}await sharp({create:{width:1536,height:Math.ceil(count/4)*576,channels:4,background:'#e5e6e9'}}).composite(tiles).png().toFile(path.join(root,'overview-'+start+'.png'));})();
