"""Use Blender bone-heat weighting, then validate the original staged poses.

Run after refine_surface.py; no runtime assets are opened or changed.
"""
import bpy
import bmesh
import json
import math
from pathlib import Path
from mathutils import Vector, Quaternion
from mathutils.kdtree import KDTree

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'athlete-body-editable.blend'))
scene=bpy.context.scene;scene.frame_set(1)
body=bpy.data.objects['Athlete_ContinuousSkin_LOD0'];rig=bpy.data.objects['AthleteRig_Staging']
for p in body.data.polygons:p.use_smooth=True
bm=bmesh.new();bm.from_mesh(body.data)
shoulders=[v for v in bm.verts if 1.29<v.co.z<1.445 and .125<abs(v.co.x)<.27]
for _ in range(8):bmesh.ops.smooth_vert(bm,verts=shoulders,factor=.35,use_axis_x=True,use_axis_y=True,use_axis_z=True)
bm.to_mesh(body.data);bm.free()
brief=bpy.data.objects['Separate_briefs']
bm=bmesh.new();bm.from_mesh(brief.data)
remove=[f for f in bm.faces if abs(f.calc_center_median().x)>.25]
if remove:bmesh.ops.delete(bm,geom=remove,context='FACES')
loose=[v for v in bm.verts if not v.link_faces]
if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
bm.to_mesh(brief.data);bm.free()
for p in brief.data.polygons:p.use_smooth=True
rig.data.bones['root'].use_deform=False
for m in list(body.modifiers):
    if m.type=='ARMATURE':body.modifiers.remove(m)
body.vertex_groups.clear()
bpy.ops.object.select_all(action='DESELECT');body.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
result=bpy.ops.object.parent_set(type='ARMATURE_AUTO')
if result!={'FINISHED'}:raise RuntimeError('Blender automatic weights did not finish: '+str(result))
if any(not v.groups for v in body.data.vertices):raise RuntimeError('Bone heat left unweighted vertices')
for v in body.data.vertices:
    ranked=sorted([(g.group,g.weight) for g in v.groups if g.weight>1e-7],key=lambda p:-p[1])[:4]
    if not ranked:raise RuntimeError('Bone heat left zero-weight vertices')
    for g in body.vertex_groups:g.remove([v.index])
    total=sum(w for _,w in ranked)
    for i,w in ranked:body.vertex_groups[i].add([v.index],w/total,'REPLACE')

brief=bpy.data.objects['Separate_briefs']
brief.vertex_groups.clear()
for g in body.vertex_groups:brief.vertex_groups.new(name=g.name)
kd=KDTree(len(body.data.vertices))
for v in body.data.vertices:kd.insert(v.co,v.index)
kd.balance()
for v in brief.data.vertices:
    _,i,_=kd.find(v.co)
    for g in body.data.vertices[i].groups:brief.vertex_groups[g.group].add([v.index],g.weight,'REPLACE')

def rotate_world_axis(name,axis,degrees):
    b=rig.pose.bones[name];b.rotation_mode='QUATERNION'
    local=rig.data.bones[name].matrix_local.to_3x3().inverted()@Vector(axis)
    b.rotation_quaternion=Quaternion(local,math.radians(degrees))

# Shoulder elevation shares motion with the clavicle, rather than asking the
# glenohumeral joint alone to execute an anatomically implausible 110 degrees.
scene.frame_set(20)
for s,sf in [(1,'L'),(-1,'R')]:
    rotate_world_axis('clavicle.'+sf,(0,1,0),-s*25)
    rotate_world_axis('upper_arm.'+sf,(0,1,0),-s*85)
for b in rig.pose.bones:b.keyframe_insert('rotation_quaternion',frame=20,group=b.name)
scene.frame_set(40)
for s,sf in [(1,'L'),(-1,'R')]:rotate_world_axis('forearm.'+sf,(-.72,0,-s*.69),110)
for b in rig.pose.bones:b.keyframe_insert('rotation_quaternion',frame=40,group=b.name)
scene.frame_set(80)
for s,sf in [(1,'L'),(-1,'R')]:rotate_world_axis('forearm.'+sf,(-.72,0,-s*.69),85)
for b in rig.pose.bones:b.keyframe_insert('rotation_quaternion',frame=80,group=b.name)
scene.frame_set(1)
report=json.loads((OUT/'validation.json').read_text())
report['skinning']='Blender built-in bone heat, four largest influences normalized; garment transferred from corresponding skin vertices; root non-deforming.'
report['maximumWeights']=max(len(v.groups) for v in body.data.vertices)
report['unweightedVertices']=sum(not v.groups for v in body.data.vertices)
report['maxWeightSumError']=max(abs(sum(g.weight for g in v.groups)-1) for v in body.data.vertices)
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);body.select_set(True);brief.select_set(True)
for o in scene.objects:
    if o.name.startswith(('Eye.','Iris.','Pupil.')):o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.gltf(filepath=str(OUT/'athlete-body-staging.glb'),export_format='GLB',use_selection=True,export_animations=False,export_skins=True,export_yup=True,export_apply=False)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-body-editable.blend'))
cam=scene.camera
for name,frame,scale,target in [('arms-raised',20,2.25,(0,0,1.02)),('elbows-flexed',40,2.12,(0,0,.98)),('hip-knee-flexed',60,2.05,(0,0,.90)),('running-stride',80,2.05,(0,0,.90))]:
    scene.frame_set(frame);cam.location=(3,-5,1.3);cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(name+'.png'));bpy.ops.render.render(write_still=True)
for name,pos,target,scale in [('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02),('head-front',(0,-3,1.67),(0,0,1.66),.37),('head-profile',(3,-.8,1.67),(0,0,1.66),.37),('hand-closeup',(1,-3,1.10),(.635,0,.97),.30),('feet-closeup',(.7,-1.6,.55),(0,-.07,.105),.48)]:
    scene.frame_set(1);cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(name+'.png'));bpy.ops.render.render(write_still=True)
print('BONE_HEAT_VALIDATION_COMPLETE')
