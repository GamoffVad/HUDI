"""Adapt approved whole Higgsfield frames to Codex's local v1 pet format.
No generation or redrawing. This local format deliberately has no cursor-gaze rows.
"""
import json
from pathlib import Path
from PIL import Image, ImageOps

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'output/hudi-pet-v6.3/frames'
OUT=ROOT/'output/codex-pet'
OUT.mkdir(parents=True,exist_ok=True)
# Compact motion excerpts use the fixed runtime cell counts. Greeting uses the
# existing wink; dragged movement uses dance steps. Full clips remain in Windows.
rows=[
 ('idle','idle',[0,4,8,12,8,4],False),
 ('running-right','dance',[36,40,44,48,52,48,44,40],True),
 ('running-left','dance',[36,40,44,48,52,48,44,40],False),
 ('waving','idle',[12,24,36,48],False),
 ('jumping','surprise',[12,18,24,30,36],False),
 ('failed','sad',[20,26,32,38,44,38,32,26],False),
 ('waiting','pockets',[72,77,82,86,82,77],False),
 ('running','think',[38,42,46,48,46,42],False),
 ('review','lookaround',[35,40,45,50,45,40],False),
]
sheet=Image.new('RGBA',(1536,1872))
contact=Image.new('RGBA',(192*6,208*3),(24,34,46,255))
report=[]
for row,(state,prefix,indices,flip) in enumerate(rows):
 frames=[]
 for col,index in enumerate(indices):
  original=Image.open(SOURCE/f'{prefix}-{index:03}.png').convert('RGBA')
  if flip:original=ImageOps.mirror(original)
  # Identical scale and registration for all frames retains ground/shadow anchors.
  fitted=original.resize((146,190),Image.Resampling.LANCZOS)
  cell=Image.new('RGBA',(192,208));cell.alpha_composite(fitted,(23,9))
  sheet.alpha_composite(cell,(col*192,row*208));frames.append(cell)
  if col<2:contact.alpha_composite(cell,((row%3)*384+col*192,(row//3)*208))
 frames[0].save(OUT/f'{state}.gif',save_all=True,append_images=frames[1:],duration=130,loop=0,disposal=2)
 report.append({'state':state,'source':prefix,'indices':indices,'mirrored':flip,'frames':len(frames)})
sheet.save(OUT/'spritesheet.png',optimize=True)
contact.convert('RGB').save(OUT/'contact.jpg',quality=93)
(OUT/'pet.json').write_text(json.dumps({'displayName':'HUDI · Higgsfield','description':'Оригинальный HUDI: голубые глаза, шнурки и кроссовки. Движения из готовых кадров Higgsfield; без курсорного взгляда.','spriteVersionNumber':1,'spritesheetPath':'spritesheet.png'},ensure_ascii=False,indent=2),encoding='utf-8')
(OUT/'adaptation.json').write_text(json.dumps({'source':'approved v6.3','resizing':'shared 146x190, offset23,9','states':report,'note':'Local Codex v1 adaptation, not a newly generated v2 cloud pet.'},indent=2),encoding='utf-8')
print('Prepared local Codex pet from approved frames.')
