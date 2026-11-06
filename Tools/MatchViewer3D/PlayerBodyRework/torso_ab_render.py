"""Render the preserved original with the exact reconstruction camera/light."""
import bpy
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Reconstruction-Study'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'torso-rebuild-working.blend'))
scene=bpy.context.scene
bpy.data.objects['A_original_body'].hide_render=False
bpy.data.objects['B_original_limb_head_context'].hide_render=True
bpy.data.objects['B_new_torso_control_cage'].hide_render=True
shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02),('torso-three-quarter',(3,-5,1.4),(0,0,1.17),.85)]
for name,pos,target,scale in shots:
    cam=scene.camera;cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/('original-'+name+'.png'));bpy.ops.render.render(write_still=True)
