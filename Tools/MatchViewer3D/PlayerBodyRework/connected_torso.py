"""Editable Blender lattice on the original, already branched connected body cage."""
import bpy,bmesh,json,math,os
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'Reconstruction-Study/Connected-Torso';OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Sculpt-11/athlete-sculpt-study.blend'))
scene=bpy.context.scene;body=bpy.data.objects['B_connected_0'];mesh=body.data
original=body.copy();original.data=body.data.copy();scene.collection.objects.link(original);original.name='A_original_connected';original.hide_render=True;original.hide_set(True)
for o in scene.objects:
    if o.name.startswith('A_connected') or o.name=='B_connected_1':o.hide_render=True;o.hide_set(True)
    if o.name.startswith('B_connected') and o.name!='B_connected_1':o.hide_render=False;o.hide_set(False)
body.name='B_connected_body'
# Native lattice modifies a single connected skin, including its existing
# axillary loops and perineal bridge; no duplicate surface or boolean union.
lat=bpy.data.lattices.new('Torso_anatomical_control_lattice');lat.points_u=7;lat.points_v=5;lat.points_w=9
for prop in ['interpolation_type_u','interpolation_type_v','interpolation_type_w']:
    identifiers=[i.identifier for i in lat.bl_rna.properties[prop].enum_items]
    assert 'KEY_BSPLINE' in identifiers;setattr(lat,prop,'KEY_BSPLINE')
control=bpy.data.objects.new('Torso_anatomical_control_lattice',lat);scene.collection.objects.link(control)
control.location=(0,0,1.175);control.scale=(.58,.32,.78);control.hide_render=True
# Control values are physical offsets in metres, specified per height layer.
rows=[(.0,.0,.0),(.0,.012,.0),(.006,.013,.0),(.007,.006,.0),(.012,.010,.0),(.020,.020,.0),(.025,.022,.006),(.016,.006,.010),(.0,.0,.0)]
if os.environ.get('FE_PASS')=='final':rows[1]=(.002,.018,0);rows[5]=(.012,.012,0);rows[6]=(.018,.015,.004)
for k,(wide,depth,raise_) in enumerate(rows):
    for j in range(5):
        for i in range(7):
            p=lat.points[i+7*(j+5*k)];sx=(i-3)/3;sy=(j-2)/2
            # The front/back central plane stays flatter than the sides.
            delta=Vector((wide*sx,depth*sy,raise_*(1-abs(sx)*.5)))
            p.co_deform+=Vector((delta.x/control.scale.x,delta.y/control.scale.y,delta.z/control.scale.z))
modifier=body.modifiers.new('Editable structural cage','LATTICE');modifier.object=control
scene.render.threads_mode='FIXED';scene.render.threads=6;scene.cycles.samples=16;scene.render.resolution_x=640;scene.render.resolution_y=800
scene.frame_set(1);bpy.context.view_layer.update()
# Record both topology and true non-adjacent triangle intersections in the
# changed torso: overlaps from adjacent triangles are not collisions.
eo=body.evaluated_get(bpy.context.evaluated_depsgraph_get());ev=eo.to_mesh();ev.calc_loop_triangles()
bm=bmesh.new();bm.from_mesh(ev)
stats={'vertices':len(ev.vertices),'triangles':len(ev.loop_triangles),'nonManifoldEdges':sum(not e.is_manifold for e in bm.edges),'boundaryEdges':sum(e.is_boundary for e in bm.edges),'degenerateFaces':sum(f.calc_area()<1e-12 for f in bm.faces)}
seen=set();components=0
for v in bm.verts:
    if v in seen:continue
    components+=1;stack=[v];seen.add(v)
    while stack:
        for edge in stack.pop().link_edges:
            for n in edge.verts:
                if n not in seen:seen.add(n);stack.append(n)
stats['connectedComponents']=components;bm.free()
tri=[tuple(t.vertices) for t in ev.loop_triangles]
tree=BVHTree.FromPolygons([v.co for v in ev.vertices],tri,all_triangles=True,epsilon=0)
pairs=tree.overlap(tree);hits=[]
for a,b in pairs:
    if a>=b or set(tri[a])&set(tri[b]):continue
    center=sum((ev.vertices[i].co for i in tri[a]+tri[b]),Vector())/6
    if abs(center.x)<.27 and .79<center.z<1.54:hits.append((a,b,tuple(round(x,6) for x in center)))
stats['nonAdjacentTriangleOverlapCandidatesTorso']=len(hits);stats['examples']=hits[:30]
eo.to_mesh_clear()
tag='final' if os.environ.get('FE_PASS')=='final' else 'preview'
(OUT/(tag+'-geometry.json')).write_text(json.dumps(stats,indent=2))
cam=scene.camera;cam.location=(3,-5,1.5);cam.rotation_euler=(Vector((0,0,.9))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=2.02
control.hide_set(True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/('connected-body.blend' if tag=='final' else 'connected-preview.blend')))
shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02)]
for name,pos,target,scale in shots:
    cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(tag+'-'+name+'.png'));bpy.ops.render.render(write_still=True)
print('CONNECTED_GEOMETRY',json.dumps(stats),flush=True)
