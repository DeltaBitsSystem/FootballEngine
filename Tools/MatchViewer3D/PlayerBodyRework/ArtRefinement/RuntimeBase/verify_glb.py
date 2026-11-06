"""Real GLB reimport and pose/UV validation, isolated from the live scene."""
from pathlib import Path
import hashlib,json
import bpy,numpy as np
from mathutils.kdtree import KDTree

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
source=OUT/'player-runtime-source.blend'
bpy.ops.wm.open_mainfile(filepath=str(source))
s=bpy.context.scene;rig=bpy.data.objects['FINAL_Rig'];body=bpy.data.objects['FINAL_Body_Connected']
samples=[('idle',0),('walk',15),('run',10),('sprint',8),('shot_right',31),('throw_in',33)]
def points(obj):
    ev=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
    data=np.array([tuple(obj.matrix_world@v.co) for v in mesh.vertices],dtype=np.float64)
    ev.to_mesh_clear();return data
source_points={}
for name,frame in samples:
    rig.animation_data.action=bpy.data.actions[name];s.frame_set(frame);source_points[(name,frame)]=points(body)
source_uv=[[] for _ in body.data.vertices]
for loop in body.data.loops:source_uv[loop.vertex_index].append(tuple(body.data.uv_layers.active.data[loop.index].uv))
bpy.ops.wm.read_factory_settings(use_empty=True)
s=bpy.context.scene;s.render.fps=60;s.render.fps_base=1
bpy.ops.import_scene.gltf(filepath=str(OUT/'player.glb'))
rig=next(o for o in s.objects if o.type=='ARMATURE');body=bpy.data.objects['FINAL_Body_Connected']
assert len(rig.data.bones)==60
for name in ['pelvis','chest','head']+[f'{part}.{side}' for side in ('L','R') for part in ('thigh','shin','foot','toe','upper_arm','forearm','hand')]:
    assert name in rig.data.bones, name
rig.animation_data.action=bpy.data.actions['idle'];s.frame_set(0)
ref=source_points[('idle',0)];p=points(body);kd=KDTree(len(ref))
for i,x in enumerate(ref):kd.insert(x,i)
kd.balance();mapping=[kd.find(x)[1] for x in p]
uv_error=0.
for loop in body.data.loops:
    a=body.data.uv_layers.active.data[loop.index].uv
    uv_error=max(uv_error,min(max(abs(a.x-u),abs(a.y-v)) for u,v in source_uv[mapping[loop.vertex_index]]))
pose_errors={}
for name,frame in samples:
    rig.animation_data.action=bpy.data.actions[name];s.frame_set(frame);x=points(body)
    err=np.linalg.norm(x-source_points[(name,frame)][mapping],axis=1)
    pose_errors[f'{name}:{frame}']={'maxMm':float(np.max(err)*1000),'p99Mm':float(np.quantile(err,.99)*1000)}
    assert np.all(np.isfinite(x))
all_mesh=[o for o in s.objects if o.type=='MESH' and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)]
uv_range=[float(min(d.uv.y for o in all_mesh for d in o.data.uv_layers.active.data)),float(max(d.uv.y for o in all_mesh for d in o.data.uv_layers.active.data))]
print('IMPORTED_UV_RANGE',uv_range,[(o.name,min(d.uv.y for d in o.data.uv_layers.active.data),max(d.uv.y for d in o.data.uv_layers.active.data)) for o in all_mesh if min(d.uv.y for d in o.data.uv_layers.active.data)<.5-1e-6],flush=True)
assert uv_range[0]>=.5-1e-6 and uv_range[1]<=1+1e-6
report={'file':str(OUT/'player.glb'),'sha256':hashlib.sha256((OUT/'player.glb').read_bytes()).hexdigest(),
        'reimportedBones':len(rig.data.bones),'reimportedMeshes':len(all_mesh),
        'bodyUvMaxError':uv_error,'blenderUvYRange':uv_range,'poseErrors':pose_errors,
        'note':'Correspondence uses nearest posed point at idle. This is not a gameplay or clothing-collision test.'}
(OUT/'glb-reimport.json').write_text(json.dumps(report,indent=2))
print('GLB_REIMPORT',json.dumps(report,indent=2),flush=True)
