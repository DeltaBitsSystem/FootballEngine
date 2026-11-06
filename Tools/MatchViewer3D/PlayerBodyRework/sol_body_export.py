"""Three bounded-cost skinned LODs, genuine GLB roundtrip and posed surface error."""
import hashlib
import json
import struct
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Revision-Sol'
legacy = HERE / 'final_body_export.py'
text = legacy.read_text().replace('Final-Body/Revision-E','Final-Body/Revision-Sol')
helpers = {'__file__': str(legacy), '__name__': '__main__'}
exec(compile(text.split('# --- LOD1.')[0], str(legacy), 'exec'), helpers)
normalize = helpers['normalize_top4']
stats = helpers['mesh_stats']
scene = bpy.context.scene
body = bpy.data.objects['FINAL_Body_Connected']
rig = bpy.data.objects['FINAL_Rig']
for collection in list(bpy.data.collections):
    if collection.get('fe_generated_body_lod'):
        for obj in list(collection.objects):
            bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.collections.remove(collection)
accessories = [o for o in scene.objects if o.type=='MESH' and o.name.startswith('GEO_') and o.parent==rig]
exec(compile(text[text.index('rna = bpy.ops.export_scene.gltf.get_rna_type()'):text.index('frames = [1, 20, 40, 60, 80]')], str(legacy), 'exec'), helpers)
options = helpers['options']
options['export_tangents'] = True
frames = [1,20,40,60,80,120,160,180,190,200]


def positions(obj, frame):
    scene.frame_set(frame)
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    points = [obj.matrix_world @ v.co for v in mesh.vertices]
    evaluated.to_mesh_clear()
    return points


reference = {frame: positions(body,frame) for frame in frames}
body.data.calc_loop_triangles()
triangles = [tuple(t.vertices) for t in body.data.loop_triangles]
surfaces = {frame: BVHTree.FromPolygons(points,triangles,all_triangles=True) for frame,points in reference.items()}
scene.frame_set(1)


def optimized(source, name, ratio):
    obj = source.copy()
    obj.data = source.data.copy()
    scene.collection.objects.link(obj)
    obj.name = name
    obj.hide_set(False)
    obj.hide_render = True
    for modifier in list(obj.modifiers):
        if modifier.type == 'ARMATURE':
            obj.modifiers.remove(modifier)
    if ratio < 1:
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        modifier = obj.modifiers.new('Export surface reduction','DECIMATE')
        modifier.ratio = ratio
        modifier.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    normalize(obj)
    modifier = obj.modifiers.new('Export linear skin','ARMATURE')
    modifier.object = rig
    obj.parent = rig
    return obj


report = {'bones': [b.name for b in rig.data.bones], 'exports': [],
          'lodMethod': 'Body and accessories independently reduced; distant LODs omit subpixel nails. Surface errors evaluated in rest and four poses, not inferred from file size.',
          'runtimeCompatibility': 'Standard glTF verified; game-specific atlas/motion manifest pack not installed.'}
roundtrips = []
for level,ratio in enumerate([1.0,.34,.14]):
    scene.frame_set(1)
    obj = optimized(body, f'Export_Body_LOD{level}', ratio)
    parts = [obj]
    for accessory in accessories:
        if level > 0 and accessory.name.startswith('GEO_Nail'):
            continue
        if accessory.name == 'GEO_Hair':
            reduction = [.20,.075,.035][level]
        elif accessory.name.startswith('GEO_Nail'):
            reduction = .25
        else:
            reduction = [.6,.3,.15][level]
        parts.append(optimized(accessory,f'Export_LOD{level}_{accessory.name}',reduction))
    sample = {frame:positions(obj,frame) for frame in frames}
    surface_error = {}
    for frame,points in sample.items():
        errors = sorted(surfaces[frame].find_nearest(p)[3] for p in points)
        surface_error[str(frame)] = {'maxMm': max(errors)*1000, 'p95Mm': errors[int(.95*(len(errors)-1))]*1000}
    scene.frame_set(1)
    bpy.ops.object.select_all(action='DESELECT')
    rig.hide_set(False)
    rig.select_set(True)
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = rig
    path = OUT / f'final-body-lod{level}.glb'
    bpy.ops.export_scene.gltf(filepath=str(path), **options)
    raw = path.read_bytes()
    length = struct.unpack_from('<I',raw,12)[0]
    doc = json.loads(raw[20:20+length])
    primitives = [p for mesh in doc['meshes'] for p in mesh['primitives']]
    record = {'file':path.name, 'bytes':len(raw), 'sha256':hashlib.sha256(raw).hexdigest(),
              'body':stats(obj), 'trianglesTotal':sum(doc['accessors'][p['indices']]['count']//3 for p in primitives),
              'jointCount':len(doc['skins'][0]['joints']), 'skinCount':len(doc['skins']),
              'animations':len(doc.get('animations',[])), 'imageCount':len(doc.get('images',[])),
              'hasNormalTexture':any('normalTexture' in mat for mat in doc['materials']),
              'hasRoughnessTexture':any('metallicRoughnessTexture' in mat.get('pbrMetallicRoughness',{}) for mat in doc['materials']),
              'posedSourceSurfaceError':surface_error,
              'parts':[{'name':p.name,**stats(p)} for p in parts]}
    report['exports'].append(record)
    roundtrips.append((path,sample))
    collection=bpy.data.collections.new(f'Runtime_LOD{level}')
    collection['fe_generated_body_lod']=True
    scene.collection.children.link(collection)
    for part in parts:
        collection.objects.link(part)
        for existing in list(part.users_collection):
            if existing!=collection:
                existing.objects.unlink(part)
        part.hide_render=True
        part.hide_set(True)
    collection.hide_render=True

scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
bpy.context.view_layer.objects.active=body
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'final-body-source.blend'))

for path,sample in roundtrips:
    fresh = bpy.data.scenes.new('GLB_actual_reimport_'+path.stem)
    bpy.context.window.scene = fresh
    scene = fresh
    bpy.ops.import_scene.gltf(filepath=str(path))
    objects = list(scene.objects)
    imported = next(o for o in objects if o.type=='MESH' and o.name.startswith('Export_Body'))
    imported_rig = next(o for o in objects if o.type=='ARMATURE')
    rest = positions(imported,1)
    lookup = KDTree(len(sample[1]))
    for i,point in enumerate(sample[1]):
        lookup.insert(point,i)
    lookup.balance()
    mapping = [lookup.find(p)[1] for p in rest]
    errors = {}
    for frame in frames:
        points = positions(imported,frame)
        errors[str(frame)] = max((p-sample[frame][mapping[i]]).length for i,p in enumerate(points))*1000
    record = next(item for item in report['exports'] if item['file']==path.name)
    record['roundtripMaxPoseErrorMm'] = errors
    record['reimportedJointCount'] = len(imported_rig.data.bones)
    record['reimportedUvLayers'] = [uv.name for uv in imported.data.uv_layers]
    if max(errors.values()) > .15:
        raise RuntimeError(f'GLB roundtrip pose error exceeds 0.15mm: {path.name}: {errors}')
    if record['skinCount']!=1 or not record['hasNormalTexture'] or not record['hasRoughnessTexture']:
        raise RuntimeError(f'Incomplete PBR/skin export: {path.name}')
(OUT / 'sol-export-roundtrip.json').write_text(json.dumps(report,indent=2))
print('SOL_EXPORT_VERIFIED',json.dumps([{'file':r['file'],'triangles':r['trianglesTotal'],'poseErrorMm':max(r['roundtripMaxPoseErrorMm'].values())} for r in report['exports']]),flush=True)
