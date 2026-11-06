"""Draft conforming kit shells from the selected weighted body itself."""
from pathlib import Path
import json
import bpy,bmesh

ROOT=Path(__file__).resolve().parents[5]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
OUT=BASE/'Runtime-Base'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'body-with-motion.blend'))
scene=bpy.context.scene;scene.frame_set(0)
body=bpy.data.objects['FINAL_Body_Connected'];rig=bpy.data.objects['FINAL_Rig']
names=json.loads((BASE/'final-body-authoring.json').read_text())['regionNames']
ids=body.data.attributes['Anatomical_region_id'].data
region=[names[x.value] for x in ids]
specs=[('FE Jersey','jersey',(0.83,0.84,0.79),0),('FE Shorts','shorts',(0.035,0.045,0.065),1),
       ('FE Socks','socks',(0.83,0.84,0.79),2),('FE Boots','boots',(0.02,0.025,0.04),5)]
def selected(category,p,rs):
    z=p.z
    if category=='jersey':
        return (any(r=='torso' for r in rs) and 1.005<z<1.495) or (any(r.startswith('arm.') for r in rs) and z>1.255 and abs(p.x)>.15)
    if category=='shorts':return .735<z<1.035 and any(r=='torso' or r=='perineum' or r.startswith('leg.') for r in rs)
    if category=='socks':return .105<z<.49 and any(r.startswith('leg.') for r in rs)
    return z<.105 and any(r.startswith(('foot.','heel.','forefoot.','toe')) for r in rs)
for name,category,color,slot in specs:
    if category=='boots':
        continue
    obj=body.copy();obj.data=body.data.copy();scene.collection.objects.link(obj);obj.name=name
    bm=bmesh.new();bm.from_mesh(obj.data);bm.faces.ensure_lookup_table()
    remove=[]
    for f in bm.faces:
        rs=[region[v.index] for v in f.verts]
        if not selected(category,f.calc_center_median(),rs):remove.append(f)
    bmesh.ops.delete(bm,geom=remove,context='FACES')
    loose=[v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm,geom=loose,context='VERTS')
    bm.normal_update()
    for v in bm.verts:v.co+=v.normal*(.006 if category=='jersey' else .003)
    bm.to_mesh(obj.data);bm.free();obj.data.update()
    obj.data.materials.clear()
    mat=bpy.data.materials.new('Runtime '+category);mat.use_nodes=True
    shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    shader.inputs['Base Color'].default_value=(*color,1);shader.inputs['Roughness'].default_value=.72
    obj.data.materials.append(mat)
    uv=obj.data.uv_layers.active
    for loop in uv.data:
        u=max(.002,min(.998,loop.uv.x));v=max(.002,min(.998,loop.uv.y))
        loop.uv=((slot%4+u)/4,1-(slot//4+1-v)/4)
    obj.parent=rig;obj.hide_render=False;obj.hide_set(False)
    obj['fe_category']=category
    print('SURFACE_KIT',name,len(obj.data.vertices),len(obj.data.polygons),flush=True)
# Reuse the original project-authored boot mesh rather than painting toes.
with bpy.data.libraries.load(str(OUT/'body-with-kit.blend'),link=False) as (source,target):
    assert 'FE Boots' in source.objects
    target.objects=['FE Boots']
boot=target.objects[0];scene.collection.objects.link(boot)
boot.name='FE Boots';boot.parent=rig
for modifier in boot.modifiers:
    if modifier.type=='ARMATURE':modifier.object=rig
for v in boot.data.vertices:
    v.co.z=.005+(v.co.z-.005)*1.18
    centre=.10 if v.co.x>=0 else -.10
    v.co.x=centre+(v.co.x-centre)*1.08
boot.hide_render=False;boot.hide_set(False)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'body-with-surface-kit.blend'))
