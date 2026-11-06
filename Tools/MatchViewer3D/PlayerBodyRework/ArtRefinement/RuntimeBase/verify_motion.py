"""Sample regenerated actions, support joints and boot ground clearance."""
from pathlib import Path
import hashlib,json, math
import bpy
import numpy as np
from mathutils import Quaternion

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'body-with-surface-kit.blend'))
scene=bpy.context.scene;rig=bpy.data.objects['FINAL_Rig'];boots=bpy.data.objects['FE Boots']
clips=json.loads((OUT/'procedural-clips.json').read_text())
side_indices={side:[v.index for v in boots.data.vertices if (v.co.x>0)==(side=='L')] for side in ('L','R')}
report={};helper_error=0.;max_scale_error=0.;max_quat_error=0.;max_stance_height=0.
for clip in clips:
    action=bpy.data.actions[clip['id']];rig.animation_data.action=action
    end=round(clip['durationSeconds']*60)
    frames=list(range(end+1))
    clear={'L':[],'R':[]}; first=None;last=None
    for frame in frames:
        scene.frame_set(frame)
        for bone in rig.pose.bones:
            max_scale_error=max(max_scale_error,max(abs(x-1) for x in bone.scale))
            max_quat_error=max(max_quat_error,abs(bone.rotation_quaternion.magnitude-1))
        for side in ('L','R'):
            for support,child in [('hip_support','thigh'),('knee_support','shin'),('elbow_support','forearm'),('axilla_support','upper_arm')]:
                expected=Quaternion((1,0,0,0)).slerp(rig.pose.bones[child+'.'+side].rotation_quaternion,.5)
                actual=rig.pose.bones[support+'.'+side].rotation_quaternion
                helper_error=max(helper_error,expected.rotation_difference(actual).angle)
        ev=boots.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
        p=np.array([tuple(boots.matrix_world@v.co) for v in mesh.vertices]);ev.to_mesh_clear()
        for side in ('L','R'):
            z=float(np.min(p[side_indices[side],2]));clear[side].append(z)
            if clip['kind']=='locomotion':
                cycle=(frame/end+(0 if side=='L' else .5))%1
                stance=.60 if clip['nominalSpeedMetersPerSecond']<2 else .48
                if cycle<stance:max_stance_height=max(max_stance_height,abs(z))
        if frame==0:first=p
        if frame==end:last=p
    report[clip['id']]={'sampledFrames':len(frames),'bootMinZMetres':{s:min(z) for s,z in clear.items()},
                       'bootMaxOfMinZMetres':{s:max(z) for s,z in clear.items()},
                       'loopBootMaxEndpointDifferenceMm':float(np.linalg.norm(first-last,axis=1).max()*1000) if clip['loop'] else None}
assert helper_error<.001 and max_scale_error<.001 and max_quat_error<.001
assert max_stance_height<.002
assert min(min(v['bootMinZMetres'].values()) for v in report.values())>-.002
result={'sampling':'every frame at 60 fps','sourceSha256':hashlib.sha256((OUT/'body-with-surface-kit.blend').read_bytes()).hexdigest(),
        'clips':report,'maxLocomotionStanceSoleHeightAbsM':max_stance_height,'maxHelperQuaternionAngleErrorRad':helper_error,
        'maxPoseScaleDeviation':max_scale_error,'maxQuaternionNormError':max_quat_error,
        'note':'Minimum Z is against Blender z=0; stance/contact is not inferred and boot clearance alone does not prove correct foot contact.'}
(OUT/'motion-conformation-validation.json').write_text(json.dumps(result,indent=2))
print('MOTION_CHECK',json.dumps(result),flush=True)
