"""Original manually blocked torso cage, with previous body preserved for A/B."""
import bpy,bmesh,json,os
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'Reconstruction-Study';OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Sculpt-11/athlete-sculpt-study.blend'))
scene=bpy.context.scene;source=bpy.data.objects['B_connected_0']
original=source.copy();original.data=source.data.copy();scene.collection.objects.link(original);original.name='A_original_body';original.hide_render=True;original.hide_set(True)
context=source.copy();context.data=source.data.copy();scene.collection.objects.link(context);context.name='B_original_limb_head_context'
source.hide_render=True;source.hide_set(True)
names=json.loads((BASE/'Iteration2/cage-construction.json').read_text())['regionNames']
regions=[names[a.value] for a in context.data.attributes['Anatomical_region_id'].data]
bm=bmesh.new();bm.from_mesh(context.data);bm.verts.ensure_lookup_table();bm.faces.ensure_lookup_table()
remove=[f for f in bm.faces if all(regions[v.index]=='torso' for v in f.verts)]
bmesh.ops.delete(bm,geom=remove,context='FACES')
loose=[v for v in bm.verts if not v.link_faces]
if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
bm.to_mesh(context.data);bm.free()
for o in scene.objects:
    if o.name.startswith('A_connected') or o.name=='B_connected_1':o.hide_render=True;o.hide_set(True)
    elif o.name.startswith('B_connected_') and o!=source:o.hide_render=False;o.hide_set(False)

# Each row is authored from front plane through side to rear, not an ellipse.
# Coordinates: z, half width, front central, front lateral, side, back lateral, back central.
rows=[
 (.805,.133,-.050,-.046,.001,.085,.090),(.845,.151,-.067,-.055,.003,.115,.130),
 (.885,.157,-.084,-.071,.002,.124,.141),(.930,.157,-.092,-.080,0,.116,.127),
 (.975,.151,-.096,-.082,-.002,.104,.110),(1.025,.140,-.097,-.083,-.004,.090,.098),
 (1.080,.128,-.096,-.084,-.004,.086,.092),(1.135,.131,-.100,-.087,-.003,.092,.099),
 (1.190,.147,-.105,-.094,-.003,.100,.109),(1.245,.161,-.115,-.101,-.002,.108,.118),
 (1.300,.175,-.124,-.108,-.001,.115,.125),(1.345,.181,-.123,-.106,0,.110,.117),
 (1.385,.181,-.105,-.092,0,.091,.096),(1.415,.156,-.075,-.066,0,.071,.075),
 (1.445,.103,-.051,-.049,.001,.055,.059),(1.475,.061,-.045,-.044,.004,.051,.055),
 (1.510,.048,-.040,-.040,.006,.046,.050),(1.535,.041,-.039,-.038,.006,.043,.046)]
verts=[]
for z,w,f,fl,side,bl,b in rows:
    # Sixteen control vertices per ring; anterior is intentionally flat,
    # lateral faces turn abruptly, posterior is a broad scapular/pelvic plane.
    samples=[(0,f),(.40*w,f),(.76*w,fl),(w,side-.035),(w,side),(w,side+.035),(.76*w,bl),(.40*w,b),(0,b),(-.40*w,b),(-.76*w,bl),(-w,side+.035),(-w,side),(-w,side-.035),(-.76*w,fl),(-.40*w,f)]
    verts.extend((x,y,z) for x,y in samples)
faces=[]
for i in range(len(rows)-1):
    for j in range(16):faces.append((i*16+j,i*16+(j+1)%16,(i+1)*16+(j+1)%16,(i+1)*16+j))
data=bpy.data.meshes.new('Anatomical_planes_control_cage');data.from_pydata(verts,[],faces);data.update()
torso=bpy.data.objects.new('B_new_torso_control_cage',data);scene.collection.objects.link(torso)
data.materials.append(source.data.materials[0]);sub=torso.modifiers.new('Editable native subdivision','SUBSURF');sub.levels=2;sub.render_levels=2
for p in data.polygons:p.use_smooth=True
bm=bmesh.new();bm.from_mesh(data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(data);bm.free()
scene.render.threads_mode='FIXED';scene.render.threads=6;scene.cycles.samples=16
scene.render.resolution_x=640;scene.render.resolution_y=800
if os.environ.get('FE_REFINEMENT')=='1':
    # Refine actual cage vertices after examining the first preview: broaden
    # neck root and avoid a concave notch where the cage meets each shoulder.
    for i in [13,14,15]:
        for j in range(16):
            p=data.vertices[i*16+j].co
            p.x*=1.08 if i==14 else 1.04
    # Deliberate overlap with the preserved arm/hip context conceals open
    # boundaries for form study; this is not a welded animation surface.
    for i,factor in [(0,1.34),(1,1.22),(2,1.12),(10,1.08),(11,1.20),(12,1.23),(13,1.10)]:
        for j in range(16):data.vertices[i*16+j].co.x*=factor
    # Reduce over-projected upper chest while retaining independent rib depth.
    for i in [9,10,11]:
        for j in [0,1,2,14,15]:data.vertices[i*16+j].co.y+=.009
    data.update()
scene.frame_set(1)
for o in [context,torso]:o.hide_render=False;o.hide_set(False)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/('torso-rebuild-working.blend' if os.environ.get('FE_REFINEMENT')=='1' else 'torso-rebuild-preview.blend')))
shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02),('torso-three-quarter',(3,-5,1.4),(0,0,1.17),.85)]
for name,pos,target,scale in shots:
    camera=scene.camera;camera.location=pos;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(('final-' if os.environ.get('FE_REFINEMENT')=='1' else 'preview-')+name+'.png'));bpy.ops.render.render(write_still=True)
print('NEW_TORSO_CAGE',len(data.vertices),len(data.polygons),flush=True)
