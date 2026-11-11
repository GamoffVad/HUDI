"""Reconstruct the final saved compositor recipe in a new Blender scene via MCP."""
import bpy,json
from pathlib import Path
ROOT=Path(globals().get('INK_OUTPUT','D:/PR/HUDI/output/ink-blender-v2'))
recipe=json.loads((ROOT/'recipe.json').read_text(encoding='utf8'))
s=bpy.data.scenes.new('Ink Blender Rebuild');bpy.context.window.scene=s
cam=bpy.data.objects.new('InkKeyCamera',bpy.data.cameras.new('InkKeyCamera'));s.collection.objects.link(cam);s.camera=cam
s.render.engine='BLENDER_EEVEE';s.render.resolution_x=s.render.resolution_y=960;s.render.resolution_percentage=100;s.render.film_transparent=True
s.render.image_settings.file_format='PNG';s.render.image_settings.color_mode='RGBA';s.render.image_settings.color_depth='16';s.render.compositor_device='CPU';s.render.fps=24
s.view_settings.view_transform='Standard';s.view_settings.look='None';s.view_settings.exposure=0;s.view_settings.gamma=1
t=bpy.data.node_groups.new('Ink final native sRGB key','CompositorNodeTree');s.compositing_node_group=t
t.interface.new_socket(name='Image',in_out='OUTPUT',socket_type='NodeSocketColor')
nodes={}
for item in recipe['nodes']:
 n=t.nodes.new(item['type']);n.name=item['name'];nodes[item['name']]=n
 if 'value' in item:n.outputs[0].default_value=item['value']
 if 'operation' in item:n.operation=item['operation']
 if 'fromSpace' in item:n.from_color_space=item['fromSpace'];n.to_color_space=item['toSpace']
 for key,value in item['inputs'].items():
  socket=next(i for i in n.inputs if i.identifier==key);socket.default_value=value
for a,sa,b,sb in recipe['links']:
 t.links.new(next(i for i in nodes[a].outputs if i.identifier==sa),next(i for i in nodes[b].inputs if i.identifier==sb))
print('Reconstructed final Ink graph:',len(nodes),'nodes; run ink-blender-render.py through MCP next')
