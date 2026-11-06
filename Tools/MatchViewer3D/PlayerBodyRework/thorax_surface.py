"""Targeted subdivision and planar patch editing of the preserved Skin cage."""
import bpy,bmesh,json,math,os
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'Reconstruction-Study/Branched-Cage/Anatomy-Refined/Thorax';OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT.parent/'anatomy-refined.blend'))
scene=bpy.context.scene;ref=bpy.data.objects['Anatomy_refined_control_cage'];ctx=bpy.data.objects['Separate_head_arms_legs_context']
for o in scene.objects:
    if o.type=='MESH' and o not in (ref,ctx) and o.name!='Studio_floor_NOT_exported':o.hide_render=True;o.hide_set(True)
    if o in (ref,ctx):o.hide_render=False;o.hide_set(False)
mesh=ref.data;mesh.update()
groups={g.index:g.name for g in ref.vertex_groups}
dump={'visibleMeshes':[o.name for o in scene.objects if o.type=='MESH' and not o.hide_render],'vertices':[],'faces':[]}
for v in mesh.vertices:
    if 1.20<v.co.z<1.52:
        dump['vertices'].append({'id':v.index,'xyz':list(v.co),'groups':[groups[g.group] for g in v.groups]})
for f in mesh.polygons:
    if any(1.20<mesh.vertices[i].co.z<1.52 for i in f.vertices):
        dump['faces'].append({'id':f.index,'vertices':list(f.vertices),'normal':list(f.normal)})
(OUT/'source-thorax-topology.json').write_text(json.dumps(dump,indent=2))
old=ref.copy();old.data=ref.data.copy();scene.collection.objects.link(old);old.name='Thorax_before';old.hide_render=True;old.hide_set(True)
# Insert actual control edges in the upper chest and shoulder patches. Vertices
# outside selected edges and their immediate face transitions remain untouched.
bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table()
edges=[e for e in bm.edges if all(1.23<v.co.z<1.43 and abs(v.co.x)<.28 for v in e.verts)]
selected=len(edges)
bmesh.ops.subdivide_edges(bm,edges=edges,cuts=1,use_grid_fill=True)
bm.normal_update();bm.to_mesh(mesh);bm.free();mesh.update()
before=[v.co.copy() for v in mesh.vertices];before_normals=[v.normal.copy() for v in mesh.vertices]
def ramp(x,a,b):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def band(x,a,b,c,d):return ramp(x,a,b)*(1-ramp(x,c,d))
edits={k:0 for k in ['sternal_plane','pectoral_plane','clavicular_plane','deltoid_cap','scapular_plane']}
for v in mesh.vertices:
    p=v.co;x,y,z=p;ax=abs(x);s=1 if x>=0 else -1
    # Project anterior polygons toward three different sloped planes. Weights
    # blend the patch boundaries instead of moving the entire chest rigidly.
    front=1-ramp(y,-.035,-.008)
    stern=band(z,1.23,1.28,1.36,1.40)*(1-ramp(ax,.015,.055))*front
    if stern:
        target=-.096+.12*(z-1.31);p.y=p.y*(1-stern*.65)+target*stern*.65;edits['sternal_plane']+=1
    pec=band(z,1.22,1.27,1.34,1.39)*band(ax,.025,.055,.115,.17)*front
    if pec:
        target=-.111+.23*(ax-.085)+.20*(z-1.30)
        p.y=p.y*(1-pec*.85)+target*pec*.85;edits['pectoral_plane']+=1
    clav=band(ax,.03,.065,.15,.20)*band(z,1.35,1.38,1.415,1.445)*front
    if clav:
        target=-.075+.11*(ax-.08);p.y=p.y*(1-clav*.70)+target*clav*.70;p.z+=.007*clav;edits['clavicular_plane']+=1
    cap=band(ax,.15,.19,.25,.30)*band(z,1.28,1.32,1.39,1.43)
    if cap:
        # Radial shoulder surface wraps over front and rear humeral branch.
        p.x+=s*.010*cap;p.z+=.009*cap
        p.y+=(-.010 if y<0 else .010)*cap;edits['deltoid_cap']+=1
    back=ramp(y,.015,.07)*band(ax,.035,.06,.13,.17)*band(z,1.25,1.29,1.36,1.40)
    if back:
        target=.106-.16*(ax-.085)-.12*(z-1.31);p.y=p.y*(1-back*.80)+target*back*.80;edits['scapular_plane']+=1
mesh.update();after_normals=[v.normal.copy() for v in mesh.vertices]
delta=[(v.co-before[v.index]).length for v in mesh.vertices]
angle=[before_normals[i].angle(after_normals[i]) for i in range(len(before)) if before_normals[i].length and after_normals[i].length]
metrics={'targetEdgesSubdivided':selected,'patchVertexCounts':edits,'changedVertices':sum(x>1e-6 for x in delta),'maxMovementMm':max(delta)*1000,'maxNormalChangeDegrees':math.degrees(max(angle)),'normalChangesOver3Degrees':sum(a>math.radians(3) for a in angle),'visibleMeshes':[o.name for o in scene.objects if o.type=='MESH' and not o.hide_render]}
(OUT/'edit-measurements.json').write_text(json.dumps(metrics,indent=2))
tag='B' if os.environ.get('FE_THORAX_FINAL')=='1' else 'preview'
if tag=='B':
    # Correct the remaining shoulder spike and central flatness using the
    # actual subdivided cage, without changing the pelvis.
    for v in mesh.vertices:
        p=v.co;ax=abs(p.x)
        if .19<ax<.28 and 1.36<p.z<1.42:p.z-=.004
        if ax<.04 and p.y<-.04 and 1.26<p.z<1.34:p.y-=.004
        if .035<ax<.145 and p.y<-.045 and 1.27<p.z<1.35:
            # Explicit planar wedge: pectoral slopes back as it meets humerus.
            target=-.119+.25*(ax-.07)+.12*(p.z-1.31)
            p.y=.35*p.y+.65*target
        if .045<ax<.16 and p.y<-.025 and 1.37<p.z<1.415:
            p.y-=.009;p.z+=.004
    mesh.update()
scene.render.threads_mode='FIXED';scene.render.threads=6;scene.cycles.samples=24;scene.cycles.use_denoising=False
scene.render.resolution_x=640;scene.render.resolution_y=800
scene.camera.location=(3,-5,1.4);scene.camera.rotation_euler=(Vector((0,0,1.28))-scene.camera.location).to_track_quat('-Z','Y').to_euler();scene.camera.data.ortho_scale=.64
ref.hide_render=False;ctx.hide_render=True;old.hide_render=True
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/('thorax-final.blend' if tag=='B' else 'thorax-preview.blend')))
shots=[('front',(0,-4,1.28),(0,0,1.28),.64),('profile',(4,0,1.28),(0,0,1.28),.64),('back',(0,4,1.28),(0,0,1.28),.64),('three-quarter',(3,-5,1.4),(0,0,1.28),.64)]
for label,obj in [('A',old),(tag,ref)]:
    if label=='A' and tag=='B':continue
    old.hide_render=obj!=old;ref.hide_render=obj!=ref
    for name,pos,target,scale in shots:
        cam=scene.camera;cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
        scene.render.filepath=str(OUT/'evidence'/(label+'-'+name+'.png'));bpy.ops.render.render(write_still=True)
old.hide_render=True;ref.hide_render=False;ctx.hide_render=False
cam=scene.camera;cam.location=(3,-5,1.5);cam.rotation_euler=(Vector((0,0,.90))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=2.02
scene.render.filepath=str(OUT/'evidence'/(tag+'-context.png'));bpy.ops.render.render(write_still=True)
if tag=='B':bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'thorax-final.blend'))
print('THORAX_EDITS',json.dumps(metrics),flush=True)
