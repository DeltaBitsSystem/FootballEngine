"""Reuse project procedural motion on the selected rig, including support joints."""
from pathlib import Path
import importlib.util,json,sys
import bpy
from mathutils import Quaternion
sys.dont_write_bytecode=True

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
script=ROOT/'FootballEngine.Client/Assets/MatchViewer/player_authoring.py'
spec=importlib.util.spec_from_file_location('read_only_player_authoring',script)
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'body-conformed.blend'))
rig=bpy.data.objects['FINAL_Rig'];scene=bpy.context.scene
scene.render.fps=60;scene.render.fps_base=1
original_solve_leg=module.solve_leg
original_solve_arm=module.solve_arm
original_aim_bone=module.aim_bone
def aim_bone(rig,name,head,tail,parent):
    if name.startswith('foot.'):
        rest=rig.data.bones[name]
        tail=head+(rest.tail_local-rest.head_local)
    return original_aim_bone(rig,name,head,tail,parent)
def solve_leg(rig,side,ankle):
    if side=='L':
        rig.pose.bones['pelvis'].location.y-=0.01173567
        bpy.context.view_layer.update()
    ankle=ankle.copy()
    # The generator's targets are expressed for the previous ankle pivot.
    ankle.z+=rig.data.bones['foot.'+side].head_local.z-0.08826446-0.01173567
    return original_solve_leg(rig,side,ankle)
def solve_arm(rig,side,wrist):
    wrist=wrist.copy();wrist.z-=0.01173567
    return original_solve_arm(rig,side,wrist)
module.aim_bone=aim_bone
module.solve_leg=solve_leg
module.solve_arm=solve_arm
original=module.pose_clip
def pose(rig,name,kind,phase,speed,duration):
    original(rig,name,kind,phase,speed,duration)
    for bone in rig.pose.bones:
        bone.scale=(1,1,1)
    for side in ('L','R'):
        for support,child in [('hip_support','thigh'),('knee_support','shin'),('elbow_support','forearm'),('axilla_support','upper_arm')]:
            rig.pose.bones[support+'.'+side].rotation_quaternion=Quaternion((1,0,0,0)).slerp(rig.pose.bones[child+'.'+side].rotation_quaternion,.5)
    bpy.context.view_layer.update()
module.pose_clip=pose
metadata=module.author_clips(scene,rig)
(OUT/'procedural-clips.json').write_text(json.dumps(metadata,indent=2))
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'body-with-motion.blend'))
print('MOTION_AUTHORED',len(metadata),flush=True)
