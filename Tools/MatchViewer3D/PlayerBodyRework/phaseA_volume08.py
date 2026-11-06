"""Measured proportional edits on a separate copy of the original clay study."""
import bpy
import json
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'PhaseA-Connected-08'
OUT.mkdir(exist_ok=True)
(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Connected-07/athlete-form-editable.blend'))
scene=bpy.context.scene
body=bpy.data.objects['B_connected_0']
mesh=body.data
regions=json.loads((BASE/'Iteration2/cage-construction.json').read_text())['regionNames']
region=[regions[item.value] for item in mesh.attributes['Anatomical_region_id'].data]
bpy.ops.object.select_all(action='DESELECT')
body.hide_set(False);body.select_set(True);bpy.context.view_layer.objects.active=body
scene.tool_settings.mesh_select_mode=(True,False,False)
audit=[]
def move(label,point,delta,radius,allowed):
    target=Vector(point)
    candidates=[v for v in mesh.vertices if allowed(region[v.index],v.co)]
    v=min(candidates,key=lambda v:(v.co-target).length)
    error=(v.co-target).length
    if error>.018:raise RuntimeError('Anchor outside expected surface: '+label+' '+str(error))
    for p in mesh.polygons:p.select=False
    for e in mesh.edges:e.select=False
    for p in mesh.vertices:p.select=False
    v.select=True;index=v.index
    before=[p.co.copy() for p in mesh.vertices]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.transform.translate(value=delta,use_proportional_edit=True,use_proportional_connected=True,proportional_size=radius,proportional_edit_falloff='SMOOTH')
    bpy.ops.object.mode_set(mode='OBJECT')
    movements=[(p.co-q).length for p,q in zip(mesh.vertices,before)]
    maximum=max(movements)
    if not .000001<maximum<=Vector(delta).length*1.01:raise RuntimeError('Unexpected movement '+label)
    audit.append({'label':label,'vertex':index,'anchorErrorMm':error*1000,'radiusMm':radius*1000,'maxMovementMm':maximum*1000,'changedVertices':sum(x>1e-7 for x in movements)})
head=lambda r,p:r=='head' and p.y<0
torso=lambda r,p:r=='torso'
for s in [-1,1]:
    move('Malar eminence '+str(s),(s*.05,-.055,1.658),(s*.002,-.004,.002),.027,head)
    move('Upper lateral forehead plane '+str(s),(s*.058,-.034,1.735),(-s*.002,.001,0),.042,head)
    move('Mandibular angle '+str(s),(s*.053,-.013,1.596),(-s*.003,.002,.002),.034,head)
    move('Pectoral volume '+str(s),(s*.085,-.095,1.31),(0,-.005,0),.073,torso)
    move('Clavicle support '+str(s),(s*.105,-.050,1.397),(0,-.003,.001),.043,torso)
    move('Scapular plane '+str(s),(s*.085,.09,1.32),(0,.004,0),.065,torso)
move('Philtrum and oral support',(0,-.074,1.619),(0,-.002,0),.015,head)
move('Chin plane',(0,-.069,1.568),(0,-.002,.002),.025,head)
(OUT/'operations.json').write_text(json.dumps(audit,indent=2))
for o in scene.objects:
    if o.name.startswith('A_connected'):o.hide_render=True;o.hide_set(True)
    if o.name.startswith('B_connected'):o.hide_render=False;o.hide_set(False)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-form-editable.blend'))
shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02),('head-front',(0,-3,1.67),(0,0,1.66),.37),('head-profile',(3,0,1.67),(0,0,1.66),.37),('head-three-quarter',(3,-5,1.7),(0,0,1.665),.37),('hand',(1,-3,1.10),(.635,0,.97),.32),('feet',(.7,-1.6,.55),(0,-.07,.105),.48)]
for name,position,target,scale in shots:
    camera=scene.camera;camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(name+'.png'));bpy.ops.render.render(write_still=True)
print('VOLUME_08_COMPLETE',json.dumps(audit),flush=True)
