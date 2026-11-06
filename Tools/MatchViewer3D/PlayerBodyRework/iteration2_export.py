"""Reproducible rigged GLBs, native unsubdivide LOD, pose-level reimport checks."""
import bpy
import json
import math
import struct
import hashlib
from pathlib import Path
from mathutils import Vector
from mathutils.kdtree import KDTree

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Iteration2'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'athlete-iteration2.blend'))
scene=bpy.context.scene;scene.frame_set(1)
body=bpy.data.objects['GEO_AthleteBody_Iteration2'];rig=bpy.data.objects['GEO_AthleteRig_Iteration2']
accessories=[o for o in scene.objects if o.type=='MESH' and o!=body and not o.hide_render and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)]
lod=body.copy();lod.data=body.data.copy();scene.collection.objects.link(lod);lod.name='GEO_AthleteBody_Iteration2_LOD1'
bpy.ops.object.select_all(action='DESELECT');lod.select_set(True);bpy.context.view_layer.objects.active=lod
for m in list(lod.modifiers):
    if m.type=='ARMATURE':lod.modifiers.remove(m)
dec=lod.modifiers.new('Native_control_loop_unsubdivide_LOD','DECIMATE')
types=[i.identifier for i in dec.bl_rna.properties['decimate_type'].enum_items]
assert 'UNSUBDIV' in types;dec.decimate_type='UNSUBDIV';dec.iterations=2
bpy.ops.object.modifier_apply(modifier=dec.name)
mod=lod.modifiers.new('Controlled_linear_skinning','ARMATURE');mod.object=rig;lod.parent=rig

def weight_stats(o):
    o.data.calc_loop_triangles()
    return {'vertices':len(o.data.vertices),'triangles':len(o.data.loop_triangles),'maxInfluences':max(len(v.groups) for v in o.data.vertices),'unweighted':sum(not v.groups for v in o.data.vertices),'maxSumError':max(abs(sum(g.weight for g in v.groups)-1) for v in o.data.vertices)}
report={'source':weight_stats(body),'lod1':weight_stats(lod),'lodMethod':'Two native Blender Un-Subdivide iterations on the authored quad cage. No heat recomputation, collapse-decimation or top-four weight pruning.','boneCount':len(rig.data.bones),'original22LocalPoseDifference':0,'exports':[],'roundTrip':[]}
assert report['source']['maxInfluences']<=4 and report['lod1']['maxInfluences']<=4
assert report['source']['unweighted']==0 and report['lod1']['unweighted']==0

frames=[1,20,40,60,80];reference={};rest={}
for o in [body,lod]:
    reference[o.name]={};rest[o.name]=[o.matrix_world@v.co for v in o.data.vertices]
    for frame in frames:
        scene.frame_set(frame)
        old=bpy.data.objects['AthleteRig_Staging']
        for bone in old.pose.bones:
            q1=bone.rotation_quaternion;q2=rig.pose.bones[bone.name].rotation_quaternion
            report['original22LocalPoseDifference']=max(report['original22LocalPoseDifference'],min(max(abs(q1[i]-q2[i]) for i in range(4)),max(abs(q1[i]+q2[i]) for i in range(4))))
        eo=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=eo.to_mesh()
        reference[o.name][frame]=[o.matrix_world@v.co for v in m.vertices]
        eo.to_mesh_clear()
scene.frame_set(1)

# Read the installed exporter's enum definitions, including RNA dynamic enums.
rna=bpy.ops.export_scene.gltf.get_rna_type()
cls=bpy.types.Operator.bl_rna_get_subclass_py(rna.identifier)
def values(name):
    result=[i.identifier for i in rna.properties[name].enum_items]
    if result:return result
    annotation=next((base.__dict__.get('__annotations__',{}).get(name) for base in cls.__mro__ if name in base.__dict__.get('__annotations__',{})),None)
    items=getattr(annotation,'keywords',{}).get('items',[])
    if callable(items):
        try:items=items(None,bpy.context)
        except (AttributeError,TypeError):items=[]
    return [i[0] for i in items] if isinstance(items,(tuple,list)) else []
formats=values('export_format');modes=values('export_animation_mode')
if not formats:
    import io_scene_gltf2
    if 'io_scene_gltf2' not in bpy.context.preferences.addons:
        bpy.ops.preferences.addon_enable(module='io_scene_gltf2')
    formats=[item[0] for item in io_scene_gltf2.get_format_items(scene,bpy.context)]
print('EXPORT_ENUMS',formats,modes,flush=True)
assert 'GLB' in formats
options={'export_format':'GLB','use_selection':True,'export_animations':True,'export_skins':True,'export_yup':True,'export_apply':False,'export_force_sampling':True,'export_frame_range':True}
if 'ACTIVE_ACTIONS' in modes:options['export_animation_mode']='ACTIVE_ACTIONS'
elif 'ACTIONS' in modes:options['export_animation_mode']='ACTIONS'
else:raise RuntimeError('Installed glTF animation modes could not be established')
for o,name in [(body,'athlete-v2-lod0.glb'),(lod,'athlete-v2-lod1.glb')]:
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);o.select_set(True)
    for accessory in accessories:accessory.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.gltf(filepath=str(OUT/name),**options)
    raw=(OUT/name).read_bytes();length=struct.unpack_from('<I',raw,12)[0];doc=json.loads(raw[20:20+length])
    primitives=[p for m in doc['meshes'] for p in m['primitives']]
    report['exports'].append({'file':name,'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest(),'triangles':sum(doc['accessors'][p['indices']]['count']//3 for p in primitives),'joints':len(doc['skins'][0]['joints']),'animations':[a.get('name','') for a in doc.get('animations',[])],'texturedBody':any('baseColorTexture' in m.get('pbrMetallicRoughness',{}) for m in doc.get('materials',[])),'hasColour0':any('COLOR_0' in p['attributes'] for p in primitives),'allTriangulated':all(p.get('mode',4)==4 for p in primitives),'allSkinned':all('JOINTS_0' in p['attributes'] and 'WEIGHTS_0' in p['attributes'] for p in primitives),'allHaveUV':all('TEXCOORD_0' in p['attributes'] for p in primitives)})
lod.hide_render=True;lod.hide_set(True)
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-iteration2.blend'))

studio=[o for o in scene.objects if o.type in {'CAMERA','LIGHT'} or o.name=='Studio_floor_NOT_exported']
for original,name in [(body,'athlete-v2-lod0.glb'),(lod,'athlete-v2-lod1.glb')]:
    inspection=bpy.data.scenes.new('Isolated_pose_roundtrip_'+name);inspection.world=scene.world;inspection.render.fps=scene.render.fps
    for item in studio:
        clone=item.copy();inspection.collection.objects.link(clone)
        if clone.type=='CAMERA':inspection.camera=clone
    inspection.render.engine='CYCLES';inspection.cycles.device='CPU';inspection.cycles.samples=32;inspection.cycles.use_denoising=True
    inspection.render.threads_mode='FIXED';inspection.render.threads=6
    inspection.render.resolution_x=640;inspection.render.resolution_y=800;inspection.render.resolution_percentage=100
    bpy.context.window.scene=inspection
    before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(OUT/name))
    imported=[o for o in bpy.data.objects if o not in before]
    meshes=[o for o in imported if o.type=='MESH' and any(m.type=='ARMATURE' for m in o.modifiers)]
    imported_body=max(meshes,key=lambda o:len(o.data.vertices));imported_rig=next(o for o in imported if o.type=='ARMATURE')
    if not imported_rig.animation_data or not imported_rig.animation_data.action:raise RuntimeError('Reimported pose action is not active')
    start=float(imported_rig.animation_data.action.frame_range[0]);offset=start-1
    inspection.frame_set(int(start));bpy.context.view_layer.update()
    kd=KDTree(len(rest[original.name]))
    for i,p in enumerate(rest[original.name]):kd.insert(p,i)
    kd.balance()
    mapping=[];rest_error=0
    for vertex in imported_body.data.vertices:
        p=imported_body.matrix_world@vertex.co;_,i,d=kd.find(p);mapping.append(i);rest_error=max(rest_error,d)
    row={'file':name,'skinnedMeshes':len(meshes),'joints':len(imported_rig.data.bones),'importActionStartFrame':start,'restCorrespondenceErrorMeters':rest_error,'poseErrors':[]}
    for frame in frames:
        target=frame+offset;inspection.frame_set(int(target),subframe=target-int(target))
        eo=imported_body.evaluated_get(bpy.context.evaluated_depsgraph_get());m=eo.to_mesh()
        errors=[(imported_body.matrix_world@vertex.co-reference[original.name][frame][mapping[i]]).length for i,vertex in enumerate(m.vertices)]
        row['poseErrors'].append({'sourceFrame':frame,'maxPositionErrorMillimeters':max(errors)*1000,'rmsPositionErrorMillimeters':math.sqrt(sum(e*e for e in errors)/len(errors))*1000,'finite':all(math.isfinite(e) for e in errors)})
        eo.to_mesh_clear()
    for frame,label,scale,center in [(1,'rest',2.02,(0,0,.90)),(20,'raised',2.25,(0,0,1.02))]:
        target=frame+offset;inspection.frame_set(int(target),subframe=target-int(target))
        cam=inspection.camera;cam.location=(3,-5,1.5);cam.rotation_euler=(Vector(center)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
        inspection.render.filepath=str(OUT/'evidence'/('roundtrip-'+('lod1' if 'lod1' in name else 'lod0')+'-'+label+'.png'));bpy.ops.render.render(write_still=True)
    report['roundTrip'].append(row)
    for o in imported:bpy.data.objects.remove(o,do_unlink=True)
    bpy.context.window.scene=scene
(OUT/'export-roundtrip-report.json').write_text(json.dumps(report,indent=2))
print('EXPORT_REPORT',json.dumps(report),flush=True)
