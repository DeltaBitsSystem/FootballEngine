"""Apply the user's selected cage without applying the skin or changing weights."""
import hashlib,json
from pathlib import Path
import bpy,bmesh
import numpy as np

ROOT=Path(__file__).resolve().parents[5]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
SOURCE=BASE/'Landmark-Cage/cage-form.blend'
OUT=BASE/'Runtime-Base'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
s=bpy.context.scene;s.frame_set(1)
body=bpy.data.objects['FINAL_Body_Connected'];rig=bpy.data.objects['FINAL_Rig']
assert len(rig.data.bones)==60
def evaluated():
    e=body.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh()
    p=np.array([v.co[:] for v in m.vertices]);e.to_mesh_clear();return p
before=evaluated()
bone_names={b.name for b in rig.data.bones}
def weights():
    return [[(body.vertex_groups[g.group].name,g.weight) for g in v.groups if body.vertex_groups[g.group].name in bone_names] for v in body.data.vertices]
pre_weights=weights()
uv0=np.array([d.uv[:] for d in body.data.uv_layers.active.data])
mods=[m for m in body.modifiers if m.type=='LATTICE']
assert len(mods)==1 and body.modifiers[0]==mods[0]
bpy.ops.object.select_all(action='DESELECT');body.hide_set(False);body.select_set(True);bpy.context.view_layer.objects.active=body
mask_name=mods[0].vertex_group
bpy.ops.object.modifier_apply(modifier=mods[0].name)
mask=body.vertex_groups.get(mask_name)
if mask:body.vertex_groups.remove(mask)
bpy.context.view_layer.update()
after=evaluated()
err=float(np.max(np.linalg.norm(after-before,axis=1))*1000)
assert err<.001, f'Cage apply altered evaluated geometry by {err}mm'
assert weights()==pre_weights and np.array_equal(uv0,np.array([d.uv[:] for d in body.data.uv_layers.active.data]))
ws=weights();counts=[len(w) for w in ws]
assert min(counts)>0 and max(counts)<=4
bm=bmesh.new();bm.from_mesh(body.data)
assert all(e.is_manifold for e in bm.edges)
bm.free()
for collection in list(bpy.data.collections):
    if collection.get('fe_generated_body_lod'):
        for obj in list(collection.objects):bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.collections.remove(collection)
body.hide_render=False;body.hide_set(False)
bpy.context.preferences.filepaths.save_version=0
path=OUT/'body-consolidated.blend';bpy.ops.wm.save_as_mainfile(filepath=str(path))
report={'input':str(SOURCE),'inputSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        'output':str(path),'outputSha256':hashlib.sha256(path.read_bytes()).hexdigest(),
        'maxApplyErrorMm':err,'vertices':len(body.data.vertices),'bones':len(rig.data.bones),
        'maxBoneInfluences':max(counts),'unweighted':sum(n==0 for n in counts),'boneWeightsUnchanged':True,'uvUnchanged':True,
        'note':'Only consolidated body; no runtime garment, atlas or motion pack yet.'}
(OUT/'consolidation.json').write_text(json.dumps(report,indent=2))
print('CONSOLIDATED',json.dumps(report),flush=True)
