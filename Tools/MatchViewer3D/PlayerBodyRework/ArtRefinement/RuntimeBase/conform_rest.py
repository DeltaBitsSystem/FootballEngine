"""Move semantic rest joints within the unchanged selected surface."""
from pathlib import Path
import json, math
import bpy
import numpy as np
from mathutils.bvhtree import BVHTree

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'body-consolidated.blend'))
rig=bpy.data.objects['FINAL_Rig']; body=bpy.data.objects['FINAL_Body_Connected']
rig.data.pose_position='REST'
def evaluated():
    obj=body.evaluated_get(bpy.context.evaluated_depsgraph_get()); mesh=obj.to_mesh()
    p=np.array([tuple(obj.matrix_world@v.co) for v in mesh.vertices]); obj.to_mesh_clear(); return p
before=evaluated()
bvh=BVHTree.FromPolygons([v.co for v in body.data.vertices],[tuple(p.vertices) for p in body.data.polygons])
positions={}
for side in ('L','R'):
    thigh=rig.data.bones['thigh.'+side]; shin=rig.data.bones['shin.'+side]
    knee=shin.head_local.copy()
    hip=knee+(thigh.head_local-knee).normalized()*0.4402556
    ankle=knee+(shin.tail_local-knee).normalized()*0.4352585
    for p in (hip,knee,ankle):
        nearest,normal,_,distance=bvh.find_nearest(p)
        assert (p-nearest).dot(normal)<0 and distance>.005, (side,tuple(p),distance)
    positions[side]=(hip,knee,ankle)
bpy.ops.object.select_all(action='DESELECT');rig.hide_set(False);rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
for side,(hip,knee,ankle) in positions.items():
    eb=rig.data.edit_bones
    thigh=eb['thigh.'+side];shin=eb['shin.'+side];foot=eb['foot.'+side]
    shin.use_connect=False;foot.use_connect=False
    thigh.head=hip;thigh.tail=knee
    shin.head=knee;shin.tail=ankle
    foot.head=ankle
    shin.use_connect=True;foot.use_connect=True
    helper=eb['hip_support.'+side];helper.head=hip;helper.tail=knee
    helper=eb['knee_support.'+side];helper.head=knee;helper.tail=ankle
bpy.ops.object.mode_set(mode='OBJECT');bpy.context.view_layer.update()
after=evaluated();error=float(np.max(np.linalg.norm(after-before,axis=1))*1000)
assert error<.001
for side in ('L','R'):
    for name,target in [('thigh',.4402556),('shin',.4352585)]:
        assert abs(rig.data.bones[name+'.'+side].length-target)<1e-6
rig.data.pose_position='POSE'
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'body-conformed.blend'))
report={'maxRestSurfaceDifferenceMm':error,'targetThighM':.4402556,'targetShinM':.4352585,
        'joints':{s:{k:list(v) for k,v in zip(('hip','knee','ankle'),p)} for s,p in positions.items()},
        'note':'Nearest-surface normal is an anatomical containment heuristic, not a clinical landmark test.'}
(OUT/'rest-conformation.json').write_text(json.dumps(report,indent=2))
print('REST_CONFORMATION',json.dumps(report),flush=True)
