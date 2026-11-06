"""Recontour existing eyelid control loops against the actual eyeball surface."""
import bpy
import bmesh
import json
import math
import os
from pathlib import Path
from mathutils import Vector
from mathutils.kdtree import KDTree

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
REVISION=int(os.environ.get('FE_ORBITAL_REVISION','3'))
OUT=BASE/('PhaseA-Connected-%02d'%REVISION);OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Connected-02/connected-form.blend'))
scene=bpy.context.scene;body=bpy.data.objects['B_connected_0'];mesh=body.data
names=json.loads((BASE/'Iteration2/cage-construction.json').read_text())['regionNames']
regions=[names[a.value] for a in mesh.attributes['Anatomical_region_id'].data]
before=[v.co.copy() for v in mesh.vertices];audit=[]
head=[v for v in mesh.vertices if regions[v.index]=='head']
tree=KDTree(len(head))
for i,v in enumerate(head):tree.insert(v.co,i)
tree.balance()
for s,side in [(1,'L'),(-1,'R')]:
    rim=[v for v in mesh.vertices if regions[v.index]=='orbital_rim.'+side]
    lids=[v for v in mesh.vertices if regions[v.index]=='eyelid.'+side]
    inner=[v for v in mesh.vertices if regions[v.index]=='eyelid_inner.'+side]
    cavity=[v for v in mesh.vertices if regions[v.index]=='orbital_cavity.'+side]
    n=len(rim);assert len(lids)==2*n and len(inner)==n
    center=Vector((s*.032,-.0525,1.682));radius=.0132
    for j in range(n):
        # The preserved cavity loop supplies the original cyclic correspondence.
        d=cavity[j].co-center;t=math.atan2(d.z,d.x)
        upper=math.sin(t)>=0
        aperture_dx=.0126*math.cos(t)
        aperture_dz=(.0048 if upper else .0038)*math.sin(t)+s*.0007*math.cos(t)
        aperture_y=center.y-math.sqrt(max(0,radius*radius-aperture_dx**2-aperture_dz**2))-.00035
        margin=Vector((center.x+aperture_dx,aperture_y,center.z+aperture_dz))
        lids[n+j].co=margin
        inner[j].co=margin+Vector((0,.0007,0))
        dx=.019*math.cos(t);dz=(.0115 if upper else .010)*math.sin(t)+s*.0007*math.cos(t)
        target=Vector((center.x+dx,0,center.z+dz))
        nearest=sorted(head,key=lambda v:(v.co.x-target.x)**2+(v.co.z-target.z)**2+max(0,v.co.y)**2)[:4]
        outer_y=sum(v.co.y for v in nearest)/len(nearest)
        rim[j].co=(target.x,outer_y-.0005,target.z)
        middle=margin.lerp(rim[j].co,.45)
        rr=(middle.x-center.x)**2+(middle.z-center.z)**2
        if rr<radius*radius:middle.y=min(middle.y,center.y-math.sqrt(radius*radius-rr)-.00065)
        lids[j].co=middle
    audit.append({'operation':'Recontour existing orbital/lid loops','side':side,'loopVertices':n,'newVertices':0,'eyeRadiusMm':radius*1000,'lidSurfaceClearanceMm':.35,'upperApertureMm':4.8,'lowerApertureMm':3.8})

# Native smoothing acts only on the transition, preserving the seated lid margin.
bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table()
outer=[v for v in bm.verts if regions[v.index].startswith('orbital_rim.')]
for _ in range(2):bmesh.ops.smooth_vert(bm,verts=outer,factor=.12,use_axis_x=True,use_axis_y=True,use_axis_z=True)
bm.to_mesh(mesh);bm.free()

# A frontal hemisphere without temple planes reads as a mannequin. Edit the
# existing cranial cage with native proportional transforms at paired landmarks.
bpy.ops.object.select_all(action='DESELECT');body.hide_set(False);body.select_set(True);bpy.context.view_layer.objects.active=body
scene.tool_settings.mesh_select_mode=(True,False,False)
def move(label,point,delta,radius):
    point=Vector(point);old=[v.co.copy() for v in mesh.vertices]
    for p in mesh.polygons:p.select=False
    for e in mesh.edges:e.select=False
    for v in mesh.vertices:v.select=False
    v=min(mesh.vertices,key=lambda v:(v.co-point).length);error=(v.co-point).length
    if error>.015:raise RuntimeError(label+': anchor misses surface')
    v.select=True;index=v.index
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.transform.translate(value=delta,use_proportional_edit=True,use_proportional_connected=True,proportional_size=radius,proportional_edit_falloff='SMOOTH')
    bpy.ops.object.mode_set(mode='OBJECT')
    distances=[(v.co-p).length for v,p in zip(mesh.vertices,old)]
    assert max(distances)>1e-6 and max(distances)<Vector(delta).length*1.01
    audit.append({'operation':'Native connected proportional translate','decision':label,'selectedVertex':index,'anchorErrorMm':error*1000,'maxMovementMm':max(distances)*1000,'changedVertices':sum(d>1e-7 for d in distances),'radiusMm':radius*1000})
for s in [-1,1]:
    move('Temporal plane '+str(s),(s*.065,-.025,1.715),(-s*.002,0,0),.033)
    move('Zygomatic transition '+str(s),(s*.053,-.044,1.655),(0,-.002,0),.025)
    move('Broaden alar support '+str(s),(s*.014,-.077,1.638),(s*.0015,-.001,0),.009)

if REVISION>=4:
    # Move the globes and their margins together behind the supraorbital plane.
    for o in scene.objects:
        if o.name.startswith('B_connected_') and int(o.name.rsplit('_',1)[1])>=2:
            for v in o.data.vertices:v.co.y+=.005
    for v in mesh.vertices:
        name=regions[v.index]
        if name.startswith(('eyelid_inner.','orbital_cavity.')):v.co.y+=.005
        elif name.startswith('eyelid.'):
            ids=[i for i,r in enumerate(regions) if r==name]
            v.co.y+=.005 if v.index>=ids[len(ids)//2] else .003
    move('Rounded nasal tip mass',(0,-.085,1.643),(0,-.002,-.005),.013)
    for s in [-1,1]:
        move('Lower alar rim '+str(s),(s*.014,-.078,1.638),(0,0,-.003),.009)
        move('Buccal plane below cheekbone '+str(s),(s*.049,-.047,1.624),(0,.0025,0),.023)
    bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table()
    transition=[v for v in bm.verts if (regions[v.index]=='head' and .012<abs(v.co.x)<.058 and 1.66<v.co.z<1.706 and v.co.y<0) or regions[v.index].startswith('orbital_rim.')]
    for _ in range(6):bmesh.ops.smooth_vert(bm,verts=transition,factor=.18,use_axis_x=True,use_axis_y=True,use_axis_z=True)
    bm.to_mesh(mesh);bm.free()
    audit.append({'operation':'Eye seating and orbital transition edit','globeAndMarginDepthMm':5,'transitionSmoothingIterations':6,'newVertices':0})

distances=[(v.co-p).length for v,p in zip(mesh.vertices,before)]
audit.append({'totalMovedVertices':sum(d>1e-7 for d in distances),'totalMaximumDisplacementMm':max(distances)*1000,'sourceVertexCount':len(before),'resultVertexCount':len(mesh.vertices)})
(OUT/'operations.json').write_text(json.dumps(audit,indent=2))
for o in scene.objects:
    if o.name.startswith('A_connected'):o.hide_render=True
    if o.name.startswith('B_connected'):o.hide_render=False
scene.cycles.samples=32
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'orbital-form.blend'))
shots=[('head-front',(0,-3,1.67),(0,0,1.66),.37),('head-profile',(3,0,1.67),(0,0,1.66),.37),('head-three-quarter',(3,-5,1.7),(0,0,1.665),.37)]
for name,position,target,scale in shots:
    cam=scene.camera;cam.location=position;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(name+'.png'));bpy.ops.render.render(write_still=True)
print('ORBITAL_CAGE_EDIT_COMPLETE',json.dumps(audit),flush=True)
