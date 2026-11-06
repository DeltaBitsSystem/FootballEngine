"""LOD1, rigged GLB export and genuine reimport verification for the final body.

Everything runs on the authored blend; the live scene is never touched. The
reimport happens in a fresh isolated scene, from the written GLB bytes, and the
pose is compared against the source evaluated mesh at the authored test frames.
"""
import bpy
import json
import math
import struct
import hashlib
from pathlib import Path
from mathutils import Vector
from mathutils.kdtree import KDTree

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Revision-E'
EVIDENCE = OUT / 'evidence'
EVIDENCE.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'final-body-source.blend'))
scene = bpy.context.scene
scene.frame_set(1)
body = bpy.data.objects['FINAL_Body_Connected']
rig = bpy.data.objects['FINAL_Rig']
accessories = [o for o in scene.objects if o.type == 'MESH' and o != body
               and any(m.type == 'ARMATURE' and m.object == rig for m in o.modifiers)]
print('ACCESSORIES', sorted(o.name for o in accessories), flush=True)


def mesh_stats(o):
    o.data.calc_loop_triangles()
    sums = [sum(g.weight for g in v.groups) for v in o.data.vertices]
    return {
        'vertices': len(o.data.vertices),
        'triangles': len(o.data.loop_triangles),
        'maxInfluences': max(len(v.groups) for v in o.data.vertices),
        'unweighted': sum(1 for v in o.data.vertices if not v.groups),
        'maxWeightSumError': max(abs(s - 1) for s in sums),
        'uvLayers': [l.name for l in o.data.uv_layers],
    }


def normalize_top4(o):
    groups = {g.index: g for g in o.vertex_groups}
    for v in o.data.vertices:
        ws = sorted(((g.group, g.weight) for g in v.groups if g.weight > 0), key=lambda p: -p[1])[:4]
        total = sum(w for _, w in ws)
        if not ws or total <= 0:
            raise RuntimeError('unweighted vertex after decimation: %d' % v.index)
        keep = {i for i, _ in ws}
        for g in list(v.groups):
            if g.group not in keep:
                groups[g.group].remove([v.index])
        for i, w in ws:
            groups[i].add([v.index], w / total, 'REPLACE')


# --- LOD1. Collapse decimation keeps the UVs and the skin weights; the weights
# are then renormalised to the runtime's four-influence budget.
lod = body.copy()
lod.data = body.data.copy()
scene.collection.objects.link(lod)
lod.name = 'FINAL_Body_Connected_LOD1'
for m in list(lod.modifiers):
    if m.type == 'ARMATURE':
        lod.modifiers.remove(m)
dec = lod.modifiers.new('LOD1_collapse', 'DECIMATE')
dec.decimate_type = 'COLLAPSE'
dec.ratio = 0.42
dec.use_collapse_triangulate = True
bpy.ops.object.select_all(action='DESELECT')
lod.select_set(True)
bpy.context.view_layer.objects.active = lod
bpy.ops.object.modifier_apply(modifier=dec.name)
normalize_top4(lod)
mod = lod.modifiers.new('Controlled_linear_skinning', 'ARMATURE')
mod.object = rig
lod.parent = rig
report = {
    'source': mesh_stats(body),
    'lod1': mesh_stats(lod),
    'lodMethod': 'Collapse decimation at ratio 0.42 with triangulated result, UVs and vertex groups preserved, then top-four renormalisation.',
    'boneCount': len(rig.data.bones),
    'bones': [b.name for b in rig.data.bones],
    'accessories': sorted(o.name for o in accessories),
}

# --- Export. Selection only: the scene still holds the Iteration1 staging
# meshes and they must not enter the deliverable.
rna = bpy.ops.export_scene.gltf.get_rna_type()
cls = bpy.types.Operator.bl_rna_get_subclass_py(rna.identifier)


def values(name):
    result = [i.identifier for i in rna.properties[name].enum_items]
    if result:
        return result
    annotation = next((base.__dict__.get('__annotations__', {}).get(name) for base in cls.__mro__
                       if name in base.__dict__.get('__annotations__', {})), None)
    items = getattr(annotation, 'keywords', {}).get('items', [])
    if callable(items):
        try:
            items = items(None, bpy.context)
        except (AttributeError, TypeError):
            items = []
    return [i[0] for i in items] if isinstance(items, (tuple, list)) else []


modes = values('export_animation_mode')
if not modes:
    import io_scene_gltf2  # noqa: F401  (registers the dynamic enums)
    modes = values('export_animation_mode')
print('EXPORT_ENUMS', modes, flush=True)
options = {
    'export_format': 'GLB', 'use_selection': True, 'export_animations': True,
    'export_skins': True, 'export_yup': True, 'export_apply': False,
    'export_force_sampling': True, 'export_frame_range': True,
    'export_image_format': 'AUTO', 'export_texcoords': True, 'export_normals': True,
}
if 'ACTIVE_ACTIONS' in modes:
    options['export_animation_mode'] = 'ACTIVE_ACTIONS'
elif 'ACTIONS' in modes:
    options['export_animation_mode'] = 'ACTIONS'
else:
    raise RuntimeError('Installed glTF animation modes could not be established')

frames = [1, 20, 40, 60, 80]
def sample_source(o):
    rest = [o.matrix_world @ v.co for v in o.data.vertices]
    pose = {}
    for frame in frames:
        scene.frame_set(frame)
        ev = o.evaluated_get(bpy.context.evaluated_depsgraph_get())
        m = ev.to_mesh()
        pose[frame] = [o.matrix_world @ v.co for v in m.vertices]
        ev.to_mesh_clear()
    scene.frame_set(1)
    return rest, pose

samples = {body: sample_source(body), lod: sample_source(lod)}
report['exports'] = []
for o, name in [(body, 'final-body-lod0.glb'), (lod, 'final-body-lod1.glb')]:
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    o.select_set(True)
    for accessory in accessories:
        accessory.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.gltf(filepath=str(OUT / name), **options)
    raw = (OUT / name).read_bytes()
    length = struct.unpack_from('<I', raw, 12)[0]
    doc = json.loads(raw[20:20 + length])
    prims = [p for m in doc['meshes'] for p in m['primitives']]
    report['exports'].append({
        'file': name,
        'bytes': len(raw),
        'sha256': hashlib.sha256(raw).hexdigest(),
        'meshes': len(doc['meshes']),
        'triangles': sum(doc['accessors'][p['indices']]['count'] // 3 for p in prims),
        'joints': len(doc['skins'][0]['joints']) if doc.get('skins') else 0,
        'animations': [a.get('name', '') for a in doc.get('animations', [])],
        'texturedBody': any('baseColorTexture' in m.get('pbrMetallicRoughness', {}) for m in doc.get('materials', [])),
        'images': [(i.get('mimeType'), i.get('name', '')) for i in doc.get('images', [])],
        'allTriangulated': all(p.get('mode', 4) == 4 for p in prims),
        'allSkinned': all('JOINTS_0' in p['attributes'] and 'WEIGHTS_0' in p['attributes'] for p in prims),
        'bodyHasUV': any('TEXCOORD_0' in p['attributes'] for p in prims if p.get('material') is not None and doc['materials'][p['material']].get('name', '').startswith('Original_skin')),
        'bodyTextured': any('baseColorTexture' in doc['materials'][p['material']].get('pbrMetallicRoughness', {}) for p in prims if p.get('material') is not None and doc['materials'][p['material']].get('name', '').startswith('Original_skin')),
    })

# --- Genuine reimport into an isolated scene, from the file on disk.
studio = [o for o in scene.objects if o.type in {'CAMERA', 'LIGHT'}]
report['roundTrip'] = []
for original, name in [(body, 'final-body-lod0.glb'), (lod, 'final-body-lod1.glb')]:
    tag = 'lod1' if 'lod1' in name else 'lod0'
    inspection = bpy.data.scenes.new('Isolated_reimport_' + tag)
    inspection.world = scene.world
    inspection.render.fps = scene.render.fps
    for item in studio:
        clone = item.copy()
        inspection.collection.objects.link(clone)
        if clone.type == 'CAMERA':
            inspection.camera = clone
    inspection.render.engine = 'CYCLES'
    inspection.cycles.device = 'CPU'
    inspection.cycles.samples = 32
    inspection.cycles.use_denoising = True
    inspection.render.resolution_x = 640
    inspection.render.resolution_y = 800
    bpy.context.window.scene = inspection
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(OUT / name))
    imported = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in imported if o.type == 'MESH' and any(m.type == 'ARMATURE' for m in o.modifiers)]
    imported_body = max(meshes, key=lambda o: len(o.data.vertices))
    imported_rig = next(o for o in imported if o.type == 'ARMATURE')
    if not imported_rig.animation_data or not imported_rig.animation_data.action:
        raise RuntimeError('Reimported pose action is not active')
    action = imported_rig.animation_data.action
    start = float(action.frame_range[0])
    offset = start - 1
    inspection.frame_set(int(start))
    bpy.context.view_layer.update()
    source_rest, source_pose = samples[original]
    kd = KDTree(len(source_rest))
    for i, p in enumerate(source_rest):
        kd.insert(p, i)
    kd.balance()
    mapping = []
    rest_error = 0.0
    for vertex in imported_body.data.vertices:
        p = imported_body.matrix_world @ vertex.co
        _, i, d = kd.find(p)
        mapping.append(i)
        rest_error = max(rest_error, d)
    row = {
        'file': name,
        'importedMeshes': len(meshes),
        'importedMeshNames': sorted(o.name for o in meshes),
        'joints': len(imported_rig.data.bones),
        'hasAction': True,
        'actionRange': [float(action.frame_range[0]), float(action.frame_range[1])],
        'restCorrespondenceErrorMeters': rest_error,
        'uvLayers': [l.name for l in imported_body.data.uv_layers],
        'materials': [m.name for m in imported_body.data.materials],
        'hasImageTexture': any(n.type == 'TEX_IMAGE' and n.image for m in imported_body.data.materials if m.use_nodes for n in m.node_tree.nodes),
        'poseErrors': [],
    }
    for frame in frames:
        target = frame + offset
        inspection.frame_set(int(target), subframe=target - int(target))
        ev = imported_body.evaluated_get(bpy.context.evaluated_depsgraph_get())
        m = ev.to_mesh()
        errors = [(imported_body.matrix_world @ v.co - source_pose[frame][mapping[i]]).length
                  for i, v in enumerate(m.vertices)]
        row['poseErrors'].append({
            'sourceFrame': frame,
            'maxPositionErrorMillimeters': max(errors) * 1000,
            'rmsPositionErrorMillimeters': math.sqrt(sum(e * e for e in errors) / len(errors)) * 1000,
            'finite': all(math.isfinite(e) for e in errors),
        })
        ev.to_mesh_clear()
    for frame, label, scale, center in [(1, 'rest', 2.02, (0, 0, .90)),
                                        (20, 'raised', 2.25, (0, 0, 1.02)),
                                        (60, 'hipflex', 2.10, (0, 0, .95))]:
        target = frame + offset
        inspection.frame_set(int(target), subframe=target - int(target))
        cam = inspection.camera
        cam.location = (3, -5, 1.5)
        cam.rotation_euler = (Vector(center) - cam.location).to_track_quat('-Z', 'Y').to_euler()
        cam.data.ortho_scale = scale
        inspection.render.filepath = str(EVIDENCE / ('reimport-%s-%s.png' % (tag, label)))
        bpy.ops.render.render(write_still=True)
    report['roundTrip'].append(row)
    for o in imported:
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.window.scene = scene

lod.hide_render = True
lod.hide_set(True)
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'final-body-source.blend'))
(OUT / 'export-roundtrip-report.json').write_text(json.dumps(report, indent=2))
print('EXPORT_REPORT', json.dumps(report), flush=True)
