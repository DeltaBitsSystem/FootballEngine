"""Whole-body form edits and matched clay evidence, preserving earlier studies."""
import bpy
import bmesh
import json
import math
import os
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
REVISION=int(os.environ.get('FE_BODY_REVISION','5'))
OUT=BASE/('PhaseA-Connected-%02d'%REVISION);OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Connected-04/orbital-form.blend'))
scene=bpy.context.scene;body=bpy.data.objects['B_connected_0'];mesh=body.data
names=json.loads((BASE/'Iteration2/cage-construction.json').read_text())['regionNames']
regions=[names[a.value] for a in mesh.attributes['Anatomical_region_id'].data]
audit=[]
bpy.ops.object.select_all(action='DESELECT');body.hide_set(False);body.select_set(True);bpy.context.view_layer.objects.active=body
scene.tool_settings.mesh_select_mode=(True,False,False)
def select(ids):
    for p in mesh.polygons:p.select=False
    for e in mesh.edges:e.select=False
    for v in mesh.vertices:v.select=False
    for i in ids:mesh.vertices[i].select=True
def measured(label,operation):
    before=[v.co.copy() for v in mesh.vertices]
    bpy.ops.object.mode_set(mode='EDIT');operation();bpy.ops.object.mode_set(mode='OBJECT')
    distances=[(v.co-p).length for v,p in zip(mesh.vertices,before)]
    if max(distances)<1e-7:raise RuntimeError('No movement: '+label)
    audit.append({'decision':label,'changedVertices':sum(d>1e-7 for d in distances),'maxMovementMm':max(distances)*1000})
def move(label,point,delta,radius,allowed=None):
    candidates=[v for v in mesh.vertices if allowed is None or allowed(regions[v.index],v.co)]
    nearest=min(candidates,key=lambda v:(v.co-Vector(point)).length)
    if (nearest.co-Vector(point)).length>.02:raise RuntimeError('Missed anchor '+label)
    select([nearest.index]);measured(label,lambda:bpy.ops.transform.translate(value=delta,use_proportional_edit=True,use_proportional_connected=True,proportional_size=radius,proportional_edit_falloff='SMOOTH'))
for s,side in [(-1,'R'),(1,'L')]:
    hand=[v.index for v in mesh.vertices if regions[v.index].endswith('.'+side) and regions[v.index].startswith(('palm.','thumb.','finger_'))]
    select(hand);scene.cursor.location=(s*.5578512,0,1.041322);scene.tool_settings.transform_pivot_point='CURSOR'
    measured('Adult hand scale '+side,lambda:bpy.ops.transform.resize(value=(1.08,1.08,1.08),use_proportional_edit=True,use_proportional_connected=True,proportional_size=.025,proportional_edit_falloff='SMOOTH'))
    scene.tool_settings.transform_pivot_point='MEDIAN_POINT'
    thumb=[v for v in mesh.vertices if regions[v.index]=='thumb.'+side]
    tip=max(thumb,key=lambda v:abs(v.co.x));point=tip.co.copy()
    if REVISION<6:move('Opposed relaxed thumb '+side,point,(-s*.009,-.003,-.020),.065,lambda r,p:r=='thumb.'+side)
    else:
        select([v.index for v in thumb]);scene.cursor.location=(s*.604,-.005,1.035);scene.tool_settings.transform_pivot_point='CURSOR'
        measured('Rotate opposed thumb without shortening '+side,lambda:bpy.ops.transform.rotate(value=s*.55,orient_axis='Y',use_proportional_edit=True,use_proportional_connected=True,proportional_size=.015,proportional_edit_falloff='SMOOTH'))
        scene.tool_settings.transform_pivot_point='MEDIAN_POINT'
    move('Lateral malleolus '+side,(s*.127,.001,.11),(s*.002,0,.001),.021)
    move('Medial malleolus '+side,(s*.075,-.002,.12),(-s*.0015,0,.001),.019)
    move('Reduce heel bulb '+side,(s*.1,.06,.04),(0,-.004,0),.036)
    move('Medial arch '+side,(s*.078,-.063,.019),(0,0,.003),.032)
    move('Patellar plane '+side,(s*.10,-.060,.495),(0,-.002,0),.028)
    move('Medial quadriceps transition '+side,(s*.068,-.047,.565),(0,-.002,0),.050)
    move('Sternocleidomastoid attachment '+side,(s*.031,-.025,1.485),(0,-.002,0),.036)

# The whole lip crease is displaced together, so it cannot pinch toward one cursor.
fissure=[v.index for v in mesh.vertices if abs(v.co.x)<.018 and v.co.y<-.06 and abs(v.co.z-1.610)<.0015]
select(fissure)
measured('Continuous shallow oral fissure',lambda:bpy.ops.transform.translate(value=(0,.001,0),use_proportional_edit=True,use_proportional_connected=True,proportional_size=.0025,proportional_edit_falloff='SMOOTH'))
bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table()
verts=[v for v in bm.verts if regions[v.index].startswith(('palm.','thumb.'))]
for _ in range(2):bmesh.ops.smooth_vert(bm,verts=verts,factor=.10,use_axis_x=True,use_axis_y=True,use_axis_z=True)
if REVISION>=6:
    feet=[v for v in bm.verts if v.co.z<.15 and v.co.y>-.12 and v.co.z>.012]
    original={v:v.co.copy() for v in feet}
    for _ in range(18):bmesh.ops.smooth_vert(bm,verts=feet,factor=.22,use_axis_x=True,use_axis_y=True,use_axis_z=True)
    audit.append({'decision':'Relax folded ankle-foot bridging loops before any retopology acceptance','selectedVertices':len(feet),'maxMovementMm':max((v.co-p).length for v,p in original.items())*1000,'nativeSmoothIterations':18})
bm.to_mesh(mesh);bm.free()
(OUT/'operations.json').write_text(json.dumps(audit,indent=2))

scene.render.threads_mode='FIXED';scene.render.threads=6;scene.cycles.samples=32
scene.render.resolution_x=640;scene.render.resolution_y=800
shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02),('head-front',(0,-3,1.67),(0,0,1.66),.37),('head-profile',(3,0,1.67),(0,0,1.66),.37),('head-three-quarter',(3,-5,1.7),(0,0,1.665),.37),('hand',(1,-3,1.10),(.635,0,.97),.32),('feet',(.7,-1.6,.55),(0,-.07,.105),.48)]
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'body-form.blend'))
for name,position,target,scale in shots:
    if os.environ.get('FE_EXTREMITIES_ONLY')=='1' and name not in ['hand','feet']:continue
    cam=scene.camera;cam.location=position;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(name+'.png'));bpy.ops.render.render(write_still=True)
print('WHOLE_BODY_FORM_EDIT_COMPLETE',json.dumps(audit),flush=True)
