"""Original edge-graph Skin blockout; retained graph and explicitly edited cage."""
import bpy,bmesh,json,math,os
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'Reconstruction-Study/Branched-Cage';OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Sculpt-11/athlete-sculpt-study.blend'))
scene=bpy.context.scene
for obj in scene.objects:
    if obj.type in {'MESH','ARMATURE'} and obj.name!='Studio_floor_NOT_exported':obj.hide_render=True;obj.hide_set(True)
old=bpy.data.objects['B_connected_0'];old.name='A_previous_body_reference';old.hide_render=True;old.hide_set(True)
# Anatomical metres, Z up; forward is -Y. Edge graph is entirely new, no
# vertices/faces are read or copied from the reference body.
points=[(0,.010,.89),(0,0,1.06),(0,.005,1.24),(0,.01,1.38),(0,.01,1.46),(0,.008,1.52),
        (.16,.006,1.375),(.235,.005,1.35),(.315,.005,1.28),
        (-.16,.006,1.375),(-.235,.005,1.35),(-.315,.005,1.28),
        (.105,.015,.86),(.105,.012,.77),(.10,.005,.68),
        (-.105,.015,.86),(-.105,.012,.77),(-.10,.005,.68)]
edges=[(0,1),(1,2),(2,3),(3,4),(4,5),(3,6),(6,7),(7,8),(3,9),(9,10),(10,11),(0,12),(12,13),(13,14),(0,15),(15,16),(16,17)]
radii=[(.105,.085),(.112,.085),(.158,.107),(.15,.085),(.055,.05),(.044,.045),
       (.078,.063),(.055,.054),(.049,.047),(.078,.063),(.055,.054),(.049,.047),
       (.078,.075),(.073,.073),(.065,.067),(.078,.075),(.073,.073),(.065,.067)]
data=bpy.data.meshes.new('Original_edge_graph_data');data.from_pydata(points,edges,[]);data.update()
graph=bpy.data.objects.new('B_source_branch_graph',data);scene.collection.objects.link(graph)
bpy.ops.object.select_all(action='DESELECT');graph.select_set(True);bpy.context.view_layer.objects.active=graph
skin=graph.modifiers.new('Native five branch Skin','SKIN');bpy.context.view_layer.update()
skin_data=graph.data.skin_vertices[0].data
for i,(rx,ry) in enumerate(radii):skin_data[i].radius=(rx,ry);skin_data[i].use_root=i==0
skin.use_smooth_shade=True
graph.hide_render=True;graph.hide_set(True)
cage=graph.copy();cage.data=graph.data.copy();scene.collection.objects.link(cage);cage.name='B_new_original_skin_cage';cage.hide_render=False;cage.hide_set(False)
bpy.ops.object.select_all(action='DESELECT');cage.select_set(True);bpy.context.view_layer.objects.active=cage
bpy.ops.object.modifier_apply(modifier=next(m.name for m in cage.modifiers if m.type=='SKIN'))
initial={'vertices':len(cage.data.vertices),'edges':len(cage.data.edges),'faces':len(cage.data.polygons)}

# Edit the native Skin cage in place. The coarse polygons remain editable;
# local planar treatments replace the rectangular uniform Skin silhouette.
for v in cage.data.vertices:
    p=v.co;x,y,z=p;ax=abs(x)
    if .98<z<1.37 and ax<.19:
        # Move anterior thoracic plane without a separate pectoral sphere.
        if y<-.025:p.y-=.014*(1 if z>1.16 else .45)
        if y>.025:p.y+=.010
    if .82<z<.98 and ax<.19:
        if y>.025:p.y+=.018
        if y<-.025:p.y-=.006
    if 1.37<z<1.48 and ax<.12:
        p.x*=1.08
for p in cage.data.polygons:p.use_smooth=True
bm=bmesh.new();bm.from_mesh(cage.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(cage.data);bm.free()
mat=bpy.data.materials.new('Branched_cage_neutral_gray');mat.use_nodes=True
shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');shader.inputs['Base Color'].default_value=(.32,.32,.32,1);shader.inputs['Roughness'].default_value=.68
cage.data.materials.append(mat)
sub=cage.modifiers.new('Editable native subdivision','SUBSURF');sub.levels=2;sub.render_levels=2
scene.render.threads_mode='FIXED';scene.render.threads=6;scene.cycles.samples=16
scene.render.resolution_x=640;scene.render.resolution_y=800
scene.camera.location=(3,-5,1.35);scene.camera.rotation_euler=(Vector((0,0,1.1))-scene.camera.location).to_track_quat('-Z','Y').to_euler();scene.camera.data.ortho_scale=1.05
scene.frame_set(1)
if os.environ.get('FE_REFINED')=='1':
    # Post-preview edits on the Skin-derived control cage: shorten the broad
    # shoulder plate and increase the posterior pelvic transition, not a shader.
    for v in cage.data.vertices:
        p=v.co
        if 1.30<p.z<1.43 and abs(p.x)<.24:
            p.x*=.94
            if p.y<0:p.y+=.006
        if .80<p.z<.96 and p.y>.02:p.y+=.012
    cage.data.update()
    path=OUT/'branched-cage-editable.blend'
else:path=OUT/'branched-cage-preview.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(path))
stats={'graphVertices':len(points),'graphEdges':len(edges),'rootVertex':0,'endpoints':sum(sum(i in e for e in edges)==1 for i in range(len(points))),'skinAppliedCageBeforeEdit':initial,'skinAppliedCageAfterEdit':{'vertices':len(cage.data.vertices),'edges':len(cage.data.edges),'faces':len(cage.data.polygons)},'referenceUsedAsGeometry':False,'contextConnected':False}
(OUT/('final-counts.json' if os.environ.get('FE_REFINED')=='1' else 'preview-counts.json')).write_text(json.dumps(stats,indent=2))
shots=[('front',(0,-4,1.1),(0,0,1.1),1.05),('profile',(4,0,1.1),(0,0,1.1),1.05),('three-quarter',(3,-5,1.35),(0,0,1.1),1.05)]
for name,pos,target,scale in shots:
    cam=scene.camera;cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(('final-' if os.environ.get('FE_REFINED')=='1' else 'preview-')+name+'.png'));bpy.ops.render.render(write_still=True)
print('BRANCHED_SKIN',json.dumps(stats),flush=True)
