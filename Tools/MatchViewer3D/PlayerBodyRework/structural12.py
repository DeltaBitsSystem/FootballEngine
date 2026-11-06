"""Structural form study: original connected mesh, measured native proportional edits."""
import bpy, bmesh, json, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'Structural-13';OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Sculpt-11/athlete-sculpt-study.blend'))
scene=bpy.context.scene;body=bpy.data.objects['B_connected_0'];mesh=body.data
names=json.loads((BASE/'Iteration2/cage-construction.json').read_text())['regionNames']
regions=[names[v.value] for v in mesh.attributes['Anatomical_region_id'].data]
before=[v.co.copy() for v in mesh.vertices]
original=body.copy();original.data=mesh.copy();scene.collection.objects.link(original);original.name='Structural_A_original';original.hide_render=True;original.hide_set(True)
for obj in scene.objects:
    if obj.name.startswith('A_connected'):obj.hide_render=True;obj.hide_set(True)
    if obj.name=='B_connected_1':obj.hide_render=True;obj.hide_set(True)
bpy.ops.object.select_all(action='DESELECT');body.hide_set(False);body.select_set(True);bpy.context.view_layer.objects.active=body
scene.tool_settings.mesh_select_mode=(True,False,False)
landmarks={
 'ankle lateral':((.13,0,.11),'leg.L'),'knee anterior':((.10,-.06,.50),'leg.L'),
 'trochanter lateral':((.18,0,.82),'leg.L'),'pubis':((0,-.10,.90),'torso'),
 'umbilical level':((0,-.10,1.10),'torso'),'sternum':((0,-.11,1.32),'torso'),
 'acromion':((.22,0,1.40),'arm.L'),'neck base':((.045,0,1.48),'torso'),
 'chin':((0,-.07,1.57),'head')}
indices={}
for name,(target,r) in landmarks.items():
    candidates=[v for v in mesh.vertices if regions[v.index]==r]
    v=min(candidates,key=lambda v:(v.co-Vector(target)).length);indices[name]=v.index
def measure(coords):
    out={name:{'vertex':i,'xyz_m':[round(x,6) for x in coords[i]]} for name,i in indices.items()}
    out['floor']={'z_m':min(v.z for v in coords)};out['crown']={'z_m':max(v.z for v in coords)}
    out['height_m']=out['crown']['z_m']-out['floor']['z_m']
    for name,z in [('neck',1.49),('chest',1.31),('waist',1.10),('pelvis',.90),('knee',.50),('calf',.34)]:
        ids=[i for i,p in enumerate(coords) if abs(p.z-z)<.006 and (regions[i]=='torso' if z>.89 else regions[i]=='leg.L')]
        p=[coords[i] for i in ids]
        if p:out[name+'_section_m']={'z':z,'width':max(v.x for v in p)-min(v.x for v in p),'depth':max(v.y for v in p)-min(v.y for v in p),'count':len(p)}
    return out
audit=[]
def move(label,target,delta,radius,predicate):
    target=Vector(target);v=min((v for v in mesh.vertices if predicate(regions[v.index],v.co)),key=lambda v:(v.co-target).length)
    distance=(v.co-target).length
    if distance>.03:raise RuntimeError('Anchor miss '+label+': '+str(distance))
    for p in mesh.polygons:p.select=False
    for e in mesh.edges:e.select=False
    for p in mesh.vertices:p.select=False
    v.select=True;i=v.index;old=[p.co.copy() for p in mesh.vertices]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.transform.translate(value=delta,use_proportional_edit=True,use_proportional_connected=True,proportional_size=radius,proportional_edit_falloff='SMOOTH')
    bpy.ops.object.mode_set(mode='OBJECT')
    m=[(p.co-q).length for p,q in zip(mesh.vertices,old)]
    if max(m)<.0001:raise RuntimeError('No structural displacement '+label)
    audit.append({'landmark':label,'index':i,'anchorErrorMm':distance*1000,'radiusMm':radius*1000,'maximumMm':max(m)*1000,'changed':sum(x>.0001 for x in m)})
for s,sf in [(-1,'R'),(1,'L')]:
    t=lambda r,p:r=='torso';leg=lambda r,p:r=='leg.'+sf;arm=lambda r,p:r=='arm.'+sf
    move('lateral neck '+sf,(s*.045,0,1.48),(s*.012,0,-.006),.08,t)
    move('upper shoulder '+sf,(s*.23,0,1.38),(0,0,.006),.08,arm)
    move('anterior shoulder '+sf,(s*.21,-.04,1.36),(0,-.012,.003),.075,arm)
    move('clavicle '+sf,(s*.11,-.06,1.39),(0,-.008,.006),.07,t)
    move('pectoral '+sf,(s*.085,-.105,1.30),(0,-.008,0),.10,t)
    move('back thorax '+sf,(s*.09,.095,1.31),(0,.012,0),.10,t)
    move('pelvic posterior '+sf,(s*.10,.085,.94),(0,.021,-.002),.095,t)
    move('gluteal posterior '+sf,(s*.10,.09,.84),(0,.025,0),.11,leg)
    move('patella '+sf,(s*.10,-.06,.50),(0,-.011,.003),.055,leg)
    move('distal hamstring '+sf,(s*.10,.05,.55),(0,.009,0),.085,leg)
    move('upper calf '+sf,(s*.10,.06,.38),(0,.008,.012),.105,leg)
    move('lower calf taper '+sf,(s*.10,.04,.24),(0,.005,.006),.075,leg)
bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table()
transitions=[v for v in bm.verts if (1.34<v.co.z<1.425 and .15<abs(v.co.x)<.26) or (.80<v.co.z<.92 and .12<abs(v.co.x)<.21)]
prior={v:v.co.copy() for v in transitions}
for _ in range(12):bmesh.ops.smooth_vert(bm,verts=transitions,factor=.20,use_axis_x=True,use_axis_y=True,use_axis_z=True)
audit.append({'landmark':'shoulder and hip transition native smoothing','changed':len(transitions),'maximumMm':max((v.co-p).length for v,p in prior.items())*1000})
bm.to_mesh(mesh);bm.free()
after=[v.co.copy() for v in mesh.vertices]
(OUT/'landmarks.json').write_text(json.dumps({'units':'meters','method':'Nearest explicitly targeted mesh vertex, identical index before/after; sections are vertex bands +/- 6mm, not anthropometric landmarks or mass estimates. Anatomical positions on this stylized surface are approximate.','before':measure(before),'after':measure(after),'operations':audit,'references':['https://www.physio-pedia.com/Pelvic_Landmarks','https://anatomy4sculptors.com/blog/forming-the-head-simple-to-complex/']},indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'body-structural-editable.blend'))
shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02)]
for label,obj in [('A',original),('B',body)]:
    body.hide_render=obj!=body;original.hide_render=obj!=original
    for name,pos,target,scale in shots:
        camera=scene.camera;camera.location=pos;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale
        scene.render.filepath=str(OUT/'evidence'/(label+'-'+name+'.png'));bpy.ops.render.render(write_still=True)
print('STRUCTURAL_COMPLETE',flush=True)
