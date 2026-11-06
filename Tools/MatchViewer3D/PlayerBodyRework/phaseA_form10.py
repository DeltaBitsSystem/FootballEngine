"""Reproducible local Blender lattice editing of the preserved form study."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'PhaseA-Connected-10';OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Connected-09/athlete-form-editable.blend'))
scene=bpy.context.scene;body=bpy.data.objects['B_connected_0'];mesh=body.data
names=json.loads((BASE/'Iteration2/cage-construction.json').read_text())['regionNames']
regions=[names[x.value] for x in mesh.attributes['Anatomical_region_id'].data]
bpy.ops.object.select_all(action='DESELECT');body.hide_set(False);body.select_set(True);bpy.context.view_layer.objects.active=body
scene.tool_settings.mesh_select_mode=(True,False,False)
records=[]
def move(label,point,delta,radius,allowed):
    candidates=[v for v in mesh.vertices if allowed(regions[v.index],v.co)]
    anchor=min(candidates,key=lambda v:(v.co-Vector(point)).length)
    error=(anchor.co-Vector(point)).length
    if error>.025:raise RuntimeError('Missed surface '+label+' '+str(error))
    for p in mesh.polygons:p.select=False
    for e in mesh.edges:e.select=False
    for v in mesh.vertices:v.select=False
    anchor.select=True;index=anchor.index
    previous=[v.co.copy() for v in mesh.vertices]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.transform.translate(value=delta,use_proportional_edit=True,use_proportional_connected=True,proportional_size=radius,proportional_edit_falloff='SMOOTH')
    bpy.ops.object.mode_set(mode='OBJECT')
    distances=[(v.co-p).length for v,p in zip(mesh.vertices,previous)]
    maximum=max(distances)
    if not 1e-6<maximum<Vector(delta).length*1.01:raise RuntimeError('Invalid displacement '+label)
    records.append({'landmark':label,'vertex':index,'radiusMm':radius*1000,'maxMovementMm':maximum*1000,'movedVertices':sum(d>1e-7 for d in distances),'anchorErrorMm':error*1000})
for s,side in [(-1,'R'),(1,'L')]:
    leg=lambda r,p:r=='leg.'+side
    arm=lambda r,p:r=='arm.'+side
    torso=lambda r,p:r=='torso'
    head=lambda r,p:r=='head' and p.y<0
    move('Medial quadriceps '+side,(s*.057,-.044,.64),(-s*.006,-.004,0),.085,leg)
    move('Lateral quadriceps '+side,(s*.16,-.02,.66),(s*.004,-.002,0),.09,leg)
    move('Inner knee '+side,(s*.063,-.025,.50),(-s*.003,0,0),.035,leg)
    move('Medial gastrocnemius '+side,(s*.07,.06,.36),(-s*.004,.003,0),.075,leg)
    move('Brachioradialis '+side,(s*.44,-.03,1.145),(0,-.003,0),.06,arm)
    move('Posterior deltoid '+side,(s*.215,.035,1.36),(0,.003,.002),.05,arm)
    move('Anterior clavicle '+side,(s*.08,-.055,1.40),(0,-.003,.001),.055,torso)
    move('Temporal hollow '+side,(s*.061,-.024,1.711),(-s*.003,.001,0),.030,head)
    move('Cheek to jaw transition '+side,(s*.050,-.036,1.615),(-s*.002,-.002,.001),.040,head)
move('Glabella',(0,-.062,1.71),(0,-.002,0),.023,lambda r,p:r=='head' and p.y<0)
(OUT/'operations.json').write_text(json.dumps(records,indent=2))
for o in scene.objects:
    if o.name.startswith('A_connected'):o.hide_render=True;o.hide_set(True)
    if o.name.startswith('B_connected'):o.hide_render=False;o.hide_set(False)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-form-editable.blend'))
shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02),('head-front',(0,-3,1.67),(0,0,1.66),.37),('head-profile',(3,0,1.67),(0,0,1.66),.37),('head-three-quarter',(3,-5,1.7),(0,0,1.665),.37),('hand',(1,-3,1.10),(.635,0,.97),.32),('feet',(.7,-1.6,.55),(0,-.07,.105),.48)]
for name,pos,target,scale in shots:
    cam=scene.camera;cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(name+'.png'));bpy.ops.render.render(write_still=True)
print('FORM10_COMPLETED',json.dumps(records),flush=True)
