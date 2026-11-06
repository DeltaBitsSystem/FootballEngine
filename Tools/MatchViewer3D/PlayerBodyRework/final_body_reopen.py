"""Save/reopen verification: a fresh Blender process opens the delivered blend,
checks the contract, renders one frame and writes the result as JSON."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path('/home/pablo/Projects/FootballEngine')
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Revision-E'
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'final-body-source.blend'))
scene = bpy.context.scene
body = bpy.data.objects['FINAL_Body_Connected']
rig = bpy.data.objects['FINAL_Rig']
me = body.data
mat = me.materials[0]
bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
tex = next((n for n in mat.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image), None)
groups = {g.index: g.name for g in body.vertex_groups}
sums = [sum(g.weight for g in v.groups) for v in me.vertices]
report = {
    'opened': True,
    'bodyVertices': len(me.vertices),
    'bodyTriangles': sum(len(p.vertices) - 2 for p in me.polygons),
    'armatureModifier': [(m.type, m.object.name if m.object else None) for m in body.modifiers],
    'parent': body.parent.name if body.parent else None,
    'bones': len(rig.data.bones),
    'action': rig.animation_data.action.name if rig.animation_data and rig.animation_data.action else None,
    'frameRange': list(rig.animation_data.action.frame_range) if rig.animation_data and rig.animation_data.action else None,
    'uvLayers': [l.name for l in me.uv_layers],
    'material': mat.name,
    'baseColorLinkedToImage': bool(bsdf.inputs['Base Color'].is_linked and tex),
    'image': (tex.image.name, list(tex.image.size), bool(tex.image.packed_file)) if tex else None,
    'outputLinked': bool(next(n for n in mat.node_tree.nodes if n.type == 'OUTPUT_MATERIAL').inputs['Surface'].is_linked),
    'maxInfluences': max(len(v.groups) for v in me.vertices),
    'unweighted': sum(1 for v in me.vertices if not v.groups),
    'weightSumMin': min(sums),
    'weightSumMax': max(sums),
    'lod1Present': 'FINAL_Body_Connected_LOD1' in bpy.data.objects,
    'accessories': sorted(o.name for o in scene.objects if o.name.startswith('GEO_')),
}
scene.frame_set(20)
dg = bpy.context.evaluated_depsgraph_get()
ev = body.evaluated_get(dg)
m = ev.to_mesh()
zs = [v.co.z for v in m.vertices]
report['poseFrame20BoundsZ'] = [min(zs), max(zs)]
report['poseFinite'] = all(abs(v.co.x) < 10 and abs(v.co.y) < 10 and abs(v.co.z) < 10 for v in m.vertices)
ev.to_mesh_clear()
scene.frame_set(1)
for o in list(scene.objects):
    if o.type == 'LIGHT':
        bpy.data.objects.remove(o, do_unlink=True)
for loc, en, sz in [((2.4, -2.6, 2.6), 240, 2.2), ((-2.8, -3.2, 1.4), 55, 3.0), ((1.2, 3.2, 2.4), 110, 2.0)]:
    d = bpy.data.lights.new('L', 'AREA')
    d.energy = en
    d.size = sz
    l = bpy.data.objects.new('L', d)
    scene.collection.objects.link(l)
    l.location = loc
    l.rotation_euler = (Vector((0, 0, 1.2)) - l.location).to_track_quat('-Z', 'Y').to_euler()
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.render.resolution_x = 640
scene.render.resolution_y = 800
cam = scene.camera
cam.location = (3.2, -4.6, 1.45)
cam.rotation_euler = (Vector((0, 0, .92)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam.data.type = 'ORTHO'
cam.data.ortho_scale = 2.02
scene.render.filepath = str(OUT / 'evidence/reopen-check.png')
bpy.ops.render.render(write_still=True)
report['render'] = 'evidence/reopen-check.png'
(OUT / 'reopen-verification.json').write_text(json.dumps(report, indent=2))
print('REOPEN', json.dumps(report), flush=True)
