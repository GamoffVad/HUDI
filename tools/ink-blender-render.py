"""Schedule all native compositing through Blender's event loop; launched by MCP."""
import bpy,json,time,traceback
from pathlib import Path
ROOT=Path(globals().get('INK_OUTPUT','D:/PR/HUDI/output/ink-blender-v2'))
rows=json.loads((ROOT/'inventory.json').read_text())
s=bpy.context.scene;t=s.compositing_node_group
img=next(n for n in t.nodes if n.bl_idname=='CompositorNodeImage')
key=next(n for n in t.nodes if n.bl_idname=='CompositorNodeKeying')
nodes=[]
for n in t.nodes:
 vals={}
 for i in n.inputs:
  if not hasattr(i,'default_value'):continue
  v=i.default_value
  try:v=list(v) if not isinstance(v,(str,int,float,bool)) else v
  except TypeError:v=str(v)
  vals[i.identifier]=v
 item=dict(name=n.name,type=n.bl_idname,inputs=vals)
 if n.bl_idname=='ShaderNodeValue':item['value']=n.outputs[0].default_value
 if n.bl_idname=='ShaderNodeMath':item['operation']=n.operation
 if n.bl_idname=='CompositorNodeConvertColorSpace':item.update(fromSpace=n.from_color_space,toSpace=n.to_color_space)
 nodes.append(item)
recipe=dict(tool='Blender',transport='MCP for Blender',version=bpy.app.version_string,nodes=nodes,links=[[l.from_node.name,l.from_socket.identifier,l.to_node.name,l.to_socket.identifier] for l in t.links],format='16-bit RGBA PNG; straight alpha sRGB',nativeResolution=[960,960],blackLevel=.07,edgeTolerance=.2,noSilhouetteErosion=True,noAddedBlur=True,opaqueCore='Source neutral colour range < 0.07',despill='Average R/B, only where G exceeds both R and B',background='Per-clip measured original chroma colour',edgeContrast='Smoothstep 0.4 to 0.6 preserves 0.5 contour; fire uses original soft matte',creditsSpent=0)
(ROOT/'recipe.json').write_text(json.dumps(recipe,indent=2),encoding='utf8')
jobs=[(r,i) for r in rows for i in range(r['frames'])]
state={'done':0,'total':len(jobs),'started':time.time(),'status':'rendering'}
old_image=None
def tick():
 global old_image
 try:
  r,i=jobs[state['done']];name=r['name'];dest=Path(r['root'])/'rgba';dest.mkdir(parents=True,exist_ok=True)
  plate=(27/255,1,14/255) if name in ('skeptical','impatient') else ((10/255,242/255,4/255) if name=='firetrick' else (8/255,242/255,6/255))
  key.inputs['Key Color'].default_value=(*plate,1)
  if 'Edge contrast strength' in t.nodes:t.nodes['Edge contrast strength'].outputs[0].default_value=0 if name=='firetrick' else 1
  loaded=bpy.data.images.load(str(Path(r.get('inputRoot',str(Path(r['root'])/'input')))/f'{i:04d}.png'),check_existing=False);loaded.colorspace_settings.name='sRGB';img.image=loaded
  if old_image is not None:bpy.data.images.remove(old_image)
  old_image=loaded
  s.frame_set(i+1);s.render.filepath=str(dest/f'{i:04d}.png');bpy.ops.render.render(write_still=True)
  state.update(done=state['done']+1,clip=name,frame=i,seconds=round(time.time()-state['started'],1))
  if i==r['frames']-1:
   (Path(r['root'])/'recipe.json').write_text(json.dumps(dict(recipe,keyColour=plate),indent=2),encoding='utf8')
  if state['done']==len(jobs):state['status']='complete'
  if i%12==0 or state['status']=='complete':(ROOT/'progress.json').write_text(json.dumps(state),encoding='utf8')
  if state['status']=='complete':
   bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'ink-keying.blend'));return None
  return .01
 except Exception:
  state.update(status='error',error=traceback.format_exc());(ROOT/'progress.json').write_text(json.dumps(state),encoding='utf8');return None
bpy.app.timers.register(tick,first_interval=.1)
print('Scheduled',len(jobs),'frames in separate Ink scene via MCP')
