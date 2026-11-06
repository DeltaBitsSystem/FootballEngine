"""Final form-review checkpoint, with same-scale A/B evidence and source audit."""
import bpy
import bmesh
import json
import os
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'PhaseA-Connected-07';OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Connected-06/body-form.blend'))
scene=bpy.context.scene;body=bpy.data.objects['B_connected_0'];mesh=body.data
names=json.loads((BASE/'Iteration2/cage-construction.json').read_text())['regionNames']
regions=[names[a.value] for a in mesh.attributes['Anatomical_region_id'].data]
before=[v.co.copy() for v in mesh.vertices]
bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table()
thumb_roots=[v for v in bm.verts if regions[v.index].startswith(('thumb.','palm.')) and min((v.co-Vector((s*.604,-.005,1.035))).length for s in [-1,1])<.034]
for _ in range(14):bmesh.ops.smooth_vert(bm,verts=thumb_roots,factor=.18,use_axis_x=True,use_axis_y=True,use_axis_z=True)
bm.to_mesh(mesh);bm.free()
bpy.ops.object.select_all(action='DESELECT');body.hide_set(False);body.select_set(True);bpy.context.view_layer.objects.active=body
audit=[]
for s in [-1,1]:
    target=Vector((s*.032,-.063,1.696))
    candidates=[v for v in mesh.vertices if regions[v.index]=='head']
    nearest=min(candidates,key=lambda v:(v.co-target).length)
    for p in mesh.polygons:p.select=False
    for e in mesh.edges:e.select=False
    for v in mesh.vertices:v.select=False
    nearest.select=True
    selected_index=nearest.index
    saved=[v.co.copy() for v in mesh.vertices]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.transform.translate(value=(0,-.0025,0),use_proportional_edit=True,use_proportional_connected=True,proportional_size=.019,proportional_edit_falloff='SMOOTH')
    bpy.ops.object.mode_set(mode='OBJECT')
    movement=max((v.co-p).length for v,p in zip(mesh.vertices,saved))
    assert 1e-6<movement<.00251
    audit.append({'decision':'Supraorbital support '+str(s),'selectedVertex':selected_index,'maximumMovementMm':movement*1000})
bm=bmesh.new();bm.from_mesh(mesh)
components=0;seen=set()
for vertex in bm.verts:
    if vertex in seen:continue
    components+=1;todo=[vertex];seen.add(vertex)
    while todo:
        current=todo.pop()
        for edge in current.link_edges:
            other=edge.other_vert(current)
            if other not in seen:seen.add(other);todo.append(other)
mesh.calc_loop_triangles()
report={'stage':'FORM_REVIEW_ONLY','heightMeters':max(v.co.z for v in mesh.vertices)-min(v.co.z for v in mesh.vertices),'vertices':len(mesh.vertices),'triangles':len(mesh.loop_triangles),'components':components,'nonManifoldEdges':sum(not e.is_manifold for e in bm.edges),'degenerateFaces':sum(f.calc_area()<1e-12 for f in bm.faces),'thumbRootSmoothingVertices':len(thumb_roots),'maximumThisRevisionMovementMm':max((v.co-p).length for v,p in zip(mesh.vertices,before))*1000,'operations':audit,'newTopologyVertices':0,'rigStatus':'Frozen form study; inherited groups are not validated against edited rest geometry. No animation approval or new export.'}
bm.free();(OUT/'form-audit.json').write_text(json.dumps(report,indent=2))
for o in scene.objects:
    if o.name.startswith('A_connected'):o.hide_render=True;o.hide_set(True)
    if o.name.startswith('B_connected'):o.hide_render=False;o.hide_set(False)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-form-editable.blend'))
shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02),('head-front',(0,-3,1.67),(0,0,1.66),.37),('head-profile',(3,0,1.67),(0,0,1.66),.37),('head-three-quarter',(3,-5,1.7),(0,0,1.665),.37),('hand',(1,-3,1.10),(.635,0,.97),.32),('feet',(.7,-1.6,.55),(0,-.07,.105),.48)]
for name,position,target,scale in shots:
    if os.environ.get('FE_AUDIT_ONLY')=='1':continue
    cam=scene.camera;cam.location=position;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(name+'.png'));bpy.ops.render.render(write_still=True)
print('FORM_REVIEW',json.dumps(report),flush=True)
