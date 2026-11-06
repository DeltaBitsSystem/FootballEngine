"""Measured native connected proportional edits on our preserved anatomical cage."""
import bpy
import bmesh
import json
import os
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
REVISION=int(os.environ.get('FE_FORM_REVISION','1'))
OUT=BASE/('PhaseA-Connected' if REVISION==1 else 'PhaseA-Connected-02');OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Sculpt/previous-iteration2.blend'))
scene=bpy.context.scene;scene.frame_set(1)
body=bpy.data.objects['GEO_AthleteBody_Iteration2'];rig=bpy.data.objects['GEO_AthleteRig_Iteration2']
sources=[body]+[o for o in scene.objects if o.type=='MESH' and o!=body and not o.hide_render and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)]
for o in scene.objects:
    if o.type in {'MESH','ARMATURE'} and o.name!='Studio_floor_NOT_exported':o.hide_render=True;o.hide_set(True)
def grey(name,value):
    mat=bpy.data.materials.new(name);mat.use_nodes=True
    bsdf=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value=(value,value,value,1)
    bsdf.inputs['Roughness'].default_value=.68
    return mat
clay=grey('Review_clay',.3);cloth=grey('Review_cloth',.13)
A=[];B=[]
for i,source in enumerate(sources):
    for label,collection in [('A',A),('B',B)]:
        o=source.copy();o.data=source.data.copy();scene.collection.objects.link(o)
        o.name=label+'_connected_'+str(i);o.parent=None;o.matrix_world=source.matrix_world.copy();o.modifiers.clear()
        o.hide_set(False);o.hide_render=label=='A';o.data.materials.clear()
        value=.5 if 'Eyeball' in source.name else .10 if 'Iris' in source.name else .025
        mat=cloth if 'compression' in source.name else clay if i==0 else grey('Eye_'+o.name,value)
        o.data.materials.append(mat)
        for p in o.data.polygons:p.material_index=0
        collection.append(o)
body=B[0];mesh=body.data
bpy.ops.object.select_all(action='DESELECT');body.select_set(True);bpy.context.view_layer.objects.active=body
scene.tool_settings.mesh_select_mode=(True,False,False)
audit=[]
def translate(label,point,delta,radius,selection_radius=.002):
    before=[v.co.copy() for v in mesh.vertices];point=Vector(point)
    candidates=[v for v in mesh.vertices if (v.co-point).length<selection_radius]
    if not candidates:candidates=[min(mesh.vertices,key=lambda v:(v.co-point).length)]
    error=min((v.co-point).length for v in candidates)
    if error>.012:raise RuntimeError(label+': target is not on surface '+str(error))
    for p in mesh.polygons:p.select=False
    for e in mesh.edges:e.select=False
    for v in mesh.vertices:v.select=False
    for v in candidates:v.select=True
    selected=[v.index for v in candidates]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.transform.translate(value=delta,use_proportional_edit=True,use_proportional_connected=True,proportional_size=radius,proportional_edit_falloff='SMOOTH')
    bpy.ops.object.mode_set(mode='OBJECT')
    distances=[(v.co-p).length for v,p in zip(mesh.vertices,before)]
    moved=sum(d>1e-7 for d in distances);maximum=max(distances)
    if moved==0 or maximum>Vector(delta).length*1.01+1e-6:raise RuntimeError(label+': invalid operator movement')
    audit.append(dict(operation='Blender connected proportional translate',decision=label,selected=selected,target=list(point),surfaceErrorMm=error*1000,radiusMm=radius*1000,requestedDeltaMm=[d*1000 for d in delta],maxMovementMm=maximum*1000,changedVertices=moved))
def smooth(label,predicate,factor,iterations):
    before=[v.co.copy() for v in mesh.vertices];bm=bmesh.new();bm.from_mesh(mesh)
    vertices=[v for v in bm.verts if predicate(v.co)]
    if not vertices:raise RuntimeError('Empty smooth selection '+label)
    for _ in range(iterations):bmesh.ops.smooth_vert(bm,verts=vertices,factor=factor,use_axis_x=True,use_axis_y=True,use_axis_z=True)
    bm.to_mesh(mesh);bm.free()
    distances=[(v.co-p).length for v,p in zip(mesh.vertices,before)]
    audit.append(dict(operation='Blender native smooth vertices',decision=label,selectedCount=len(vertices),iterations=iterations,factor=factor,maxMovementMm=max(distances)*1000,changedVertices=sum(d>1e-7 for d in distances)))

# Oral mass first: the original mouth is behind both the nose base and the chin.
translate('Project and raise maxillary/oral mass',(0,-.061,1.603),(0,-.012,.009),.036,.006)
translate('Raise chin to shorten overlong lower face',(0,-.067,1.561),(0,.002,.010),.041,.006)
translate('Establish submental break above neck',(0,-.044,1.541),(0,.009,.003),.025,.004)
translate('Reduce nasal wedge projection',(0,-.091,1.641),(0,.005,0),.019,.004)
translate('Lift columella to define nasal underside',(0,-.075,1.629),(0,-.001,.004),.013,.003)
for s in [-1,1]:
    translate('Support nasal ala '+str(s),(s*.014,-.074,1.634),(s*.002,-.003,0),.010,.003)
    translate('Malar plane '+str(s),(s*.046,-.060,1.652),(s*.001,-.003,.001),.022,.003)
    translate('Restrained supraorbital plane '+str(s),(s*.032,-.060,1.705),(0,-.002,0),.021,.003)
smooth('Blend lip steps without a crease brush',lambda p:abs(p.x)<.025 and p.y<-.055 and 1.598<p.z<1.622,.18,3)
smooth('Blend nasal cheek transitions',lambda p:.012<abs(p.x)<.028 and p.y<-.04 and 1.628<p.z<1.659,.15,3)
smooth('Round acromial corners with bounded local averaging',lambda p:.17<abs(p.x)<.26 and 1.32<p.z<1.425,.23,9)
for s in [-1,1]:
    translate('Adult pectoral plane '+str(s),(s*.085,-.102,1.31),(0,-.004,0),.075,.008)
    translate('Reduce excessive calf bulge '+str(s),(s*.10,.077,.34),(0,-.003,0),.085,.006)
smooth('Integrate anterior ankle and instep',lambda p:.07<p.z<.13 and abs(p.x)<.15,.15,4)

if REVISION>=2:
    for s in [-1,1]:
        translate('Level oral commissure '+str(s),(s*.021,-.066,1.605),(0,-.004,.006),.017,.003)
        translate('Define nasal wing underside '+str(s),(s*.010,-.077,1.636),(0,.0025,0),.006,.0015)
        translate('Support mandibular neck attachment '+str(s),(s*.031,-.002,1.545),(s*.004,0,0),.032,.004)
    smooth('Settle perioral planes',lambda p:abs(p.x)<.033 and p.y<-.05 and 1.596<p.z<1.628,.16,4)
    smooth('Smooth the nasal tip without pinching',lambda p:abs(p.x)<.019 and p.y<-.069 and 1.631<p.z<1.652,.12,4)
    region_names=json.loads((BASE/'Iteration2/cage-construction.json').read_text())['regionNames']
    regions=[region_names[a.value] for a in mesh.attributes['Anatomical_region_id'].data]
    for s,suffix in [(-1,'R'),(1,'L')]:
        selected=[v for v in mesh.vertices if regions[v.index]=='ear.'+suffix]
        before=[v.co.copy() for v in selected]
        center=Vector((s*.078,.011,1.672))
        for v in selected:
            d=v.co-center;v.co=center+Vector((d.x*.70,d.y*.83,d.z*.83))
        audit.append(dict(operation='Explicit ear cage scaling',decision='Reduce protrusion and long flat auricle '+suffix,changedVertices=len(selected),maxMovementMm=max((v.co-p).length for v,p in zip(selected,before))*1000,scale=[.70,.83,.83]))
    smooth('Blend auricular attachments',lambda p:abs(p.x)>.071 and 1.642<p.z<1.705 and p.y>-.01,.15,3)

(OUT/'operations.json').write_text(json.dumps(audit,indent=2))
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.threads_mode='FIXED';scene.render.threads=6
scene.render.resolution_x=640;scene.render.resolution_y=800;scene.render.resolution_percentage=100
for o in scene.objects:
    if o.type=='LIGHT':o.data.color=(1,1,1)
cam=scene.camera
shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02),('head-front',(0,-3,1.67),(0,0,1.66),.37),('head-profile',(3,0,1.67),(0,0,1.66),.37),('head-three-quarter',(3,-5,1.7),(0,0,1.665),.37),('hand',(1,-3,1.10),(.635,0,.97),.30),('feet',(.7,-1.6,.55),(0,-.07,.105),.48)]
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'connected-form.blend'))
for label,objects in [('A',A),('B',B)]:
    for o in A+B:o.hide_render=o not in objects
    for name,position,target,scale in shots:
        if os.environ.get('FE_HEAD_ONLY')=='1' and not name.startswith('head'):continue
        if label=='A' and REVISION>=2:continue
        cam.location=position;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
        scene.render.filepath=str(OUT/'evidence'/(label+'-'+name+'.png'));bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'connected-form.blend'))
print('COMPLETED_MEASURED_EDITS',json.dumps(audit),flush=True)
