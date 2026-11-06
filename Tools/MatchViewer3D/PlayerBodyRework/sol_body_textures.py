"""Native UV scaling and original PBR bakes on the preserved authored surface."""
import json
from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Revision-Sol'
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'final-body-source.blend'))
scene = bpy.context.scene
scene.frame_set(1)
body = bpy.data.objects['FINAL_Body_Connected']
mesh = body.data
regions = json.loads((OUT / 'final-body-authoring.json').read_text())['regionNames']
ids = mesh.attributes['Anatomical_region_id'].data
head = [regions[x.value].startswith(('head', 'orbital', 'eyelid')) for x in ids]
bpy.ops.object.select_all(action='DESELECT')
body.hide_set(False)
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.average_islands_scale()
bpy.ops.object.mode_set(mode='OBJECT')
uv = mesh.uv_layers.active.data
selected = [li for poly in mesh.polygons if all(head[v] for v in poly.vertices) for li in poly.loop_indices]
center = sum((uv[li].uv for li in selected), uv[selected[0]].uv * 0) / len(selected)
for li in selected:
    uv[li].uv = center + (uv[li].uv-center)*1.7
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.pack_islands(rotate=True, margin=.012)
bpy.ops.object.mode_set(mode='OBJECT')
uv = mesh.uv_layers.active.data
mesh.calc_loop_triangles()
density = {'head': [], 'body': []}
for tri in mesh.loop_triangles:
    points = [mesh.vertices[i].co for i in tri.vertices]
    world = (points[1]-points[0]).cross(points[2]-points[0]).length*.5
    a,b,c = [uv[i].uv for i in tri.loops]
    area = abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))*.5
    if world > 1e-12:
        density['head' if all(head[i] for i in tri.vertices) else 'body'].append(float((area/world)**.5*2048))
(OUT / 'texel-density.json').write_text(json.dumps({
    'method': 'Native island-area equalization, 1.7x head island scale, native pack without aspect distortion.',
    'pixelsPerMeterMedian': {k: float(np.median(v)) for k,v in density.items()},
}, indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'final-body-source.blend'))

# Reuse the existing original emission-only albedo bake and raster audit.
source = HERE / 'final_body_atlas.py'
text = source.read_text().split('# UV checker evidence with the real camera and lights.')[0]
assert text.count('Final-Body/Revision-E') == 1
text = text.replace('Final-Body/Revision-E', 'Final-Body/Revision-Sol')
namespace = {'__file__': str(source), '__name__': '__main__'}
exec(compile(text, str(source), 'exec'), namespace)
scene = bpy.context.scene
scene.render.threads_mode = 'FIXED'
scene.render.threads = 6
scene.cycles.samples = 8
scene.frame_set(1)
body = bpy.data.objects['FINAL_Body_Connected']
material = body.data.materials[0]
nodes,links = material.node_tree.nodes, material.node_tree.links
bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
output = next(n for n in nodes if n.type == 'OUTPUT_MATERIAL')
geometry = nodes.new('ShaderNodeNewGeometry')
noise = nodes.new('ShaderNodeTexNoise')
noise.inputs['Scale'].default_value = 1100
noise.inputs['Detail'].default_value = 2
links.new(geometry.outputs['Position'], noise.inputs['Vector'])
bump = nodes.new('ShaderNodeBump')
bump.inputs['Strength'].default_value = .15
bump.inputs['Distance'].default_value = .00008
links.new(noise.outputs['Fac'], bump.inputs['Height'])
links.new(bump.outputs['Normal'], bsdf.inputs['Normal'])


def image_target(name, fill):
    image = bpy.data.images.new(name, 2048, 2048, alpha=False)
    image.colorspace_settings.name = 'Non-Color'
    pixels = np.tile(np.array([*fill, 1], dtype=np.float32), 2048*2048)
    image.pixels.foreach_set(pixels)
    node = nodes.new('ShaderNodeTexImage')
    node.image = image
    for item in nodes:
        item.select = False
    node.select = True
    nodes.active = node
    return image, node


def save(image, name):
    image.filepath_raw = str(OUT / 'textures' / name)
    image.file_format = 'PNG'
    image.save()
    image.pack()


bake_types = [i.identifier for i in bpy.ops.object.bake.get_rna_type().properties['type'].enum_items]
normal_type = next(v for v in bake_types if v.casefold() == 'normal')
emit_type = next(v for v in bake_types if v.casefold() == 'emit')
normal,normal_node = image_target('Original_body_normal_2048', (.5,.5,1))
scene.render.bake.use_clear = False
bpy.ops.object.bake(type=normal_type, margin=16)
save(normal, 'final-body-normal-2048.png')
normal_map = nodes.new('ShaderNodeNormalMap')
links.new(normal_node.outputs['Color'], normal_map.inputs['Color'])
links.new(normal_map.outputs['Normal'], bsdf.inputs['Normal'])

noise.inputs['Scale'].default_value = 85
remap = nodes.new('ShaderNodeMapRange')
remap.inputs['From Min'].default_value = 0
remap.inputs['From Max'].default_value = 1
remap.inputs['To Min'].default_value = .48
remap.inputs['To Max'].default_value = .64
links.new(noise.outputs['Fac'], remap.inputs['Value'])
rough,rough_node = image_target('Original_body_roughness_2048', (.56,.56,.56))
emission = nodes.new('ShaderNodeEmission')
links.new(remap.outputs['Result'], emission.inputs['Color'])
links.new(emission.outputs[0], output.inputs['Surface'])
bpy.ops.object.bake(type=emit_type, margin=16)
save(rough, 'final-body-roughness-2048.png')
links.new(rough_node.outputs['Color'], bsdf.inputs['Roughness'])
links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
for node in [emission,remap,noise,bump,geometry]:
    nodes.remove(node)
if 'Subsurface Weight' in bsdf.inputs:
    bsdf.inputs['Subsurface Weight'].default_value = .08
    bsdf.inputs['Subsurface Scale'].default_value = .0012
    bsdf.inputs['Subsurface Radius'].default_value = (1,.35,.20)
scene.cycles.samples = 24
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'final-body-source.blend'))
(OUT / 'pbr-bakes.json').write_text(json.dumps({
    'albedo': 'sRGB, original tint and procedural pigmentation, emission-only bake',
    'normal': 'Non-Color, native Cycles tangent bake from 0.08mm pore bump at strength 0.15',
    'roughness': 'Non-Color, native emission bake of deterministic spatial field, range 0.48..0.64',
    'sourceImages': [], 'resolution': [2048,2048], 'marginPixels': 16,
    'sourceOnlySubsurface': 'Blender preview: 1.2mm scale, weight 0.08. Not represented by core glTF; runtime skin shading needs its own verified implementation.',
}, indent=2))
print('SOL_PBR_COMPLETE', flush=True)
