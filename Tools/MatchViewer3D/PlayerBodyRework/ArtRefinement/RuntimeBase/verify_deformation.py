"""Measure body edge deformation in representative regenerated clip poses."""
from pathlib import Path
import hashlib,json
import bpy
import numpy as np

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'player-runtime-source.blend'))
scene=bpy.context.scene;rig=bpy.data.objects['FINAL_Rig'];body=bpy.data.objects['FINAL_Body_Connected']
def points():
    ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
    data=np.array([v.co[:] for v in mesh.vertices]);ev.to_mesh_clear();return data
edges=np.array([e.vertices[:] for e in body.data.edges])
rig.data.pose_position='REST';bpy.context.view_layer.update();rest=points()
length=np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1)
assert np.min(length)>0
rig.data.pose_position='POSE'
result={}
for name in ('idle','walk','run','sprint','shot_right','tackle_left','keeper_ready','throw_in'):
    action=bpy.data.actions[name];rig.animation_data.action=action
    end=round(action.frame_range[1])
    for frame in sorted({0,round(end*.25),round(end*.5),round(end*.75),end}):
        scene.frame_set(frame);p=points();assert np.isfinite(p).all()
        ratio=np.linalg.norm(p[edges[:,0]]-p[edges[:,1]],axis=1)/length
        result[f'{name}:{frame}']={'edgeRatioMin':float(ratio.min()),'edgeRatioP01':float(np.quantile(ratio,.01)),
                                 'edgeRatioP99':float(np.quantile(ratio,.99)),'edgeRatioMax':float(ratio.max())}
report={'sourceSha256':hashlib.sha256((OUT/'player-runtime-source.blend').read_bytes()).hexdigest(),
        'samples':result,'note':'Edge ratios are indicators, not an automatic anatomical or collision acceptance test.'}
(OUT/'deformation-conformation-validation.json').write_text(json.dumps(report,indent=2))
print('DEFORMATION_CHECK',json.dumps(report),flush=True)
