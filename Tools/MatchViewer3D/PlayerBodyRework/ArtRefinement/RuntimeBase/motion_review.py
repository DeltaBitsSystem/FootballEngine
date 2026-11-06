"""Render the original procedural clips on the selected 60-bone body."""
from pathlib import Path
import sys
import importlib.util
import bpy
from mathutils import Vector
sys.dont_write_bytecode=True

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
helper=ROOT/'Tools/MatchViewer3D/PlayerBodyRework/sol_body_review.py'
spec=importlib.util.spec_from_file_location('review',helper)
review=importlib.util.module_from_spec(spec);spec.loader.exec_module(review)
draft='--draft' in sys.argv
path=OUT/('surface-kit-preview' if draft else 'runtime-preview')
review.render_views(OUT/('body-with-surface-kit.blend' if draft else 'player-runtime-source.blend'),path,samples=16,view='none',clay=False)
scene=bpy.context.scene;rig=bpy.data.objects['FINAL_Rig'];camera=scene.camera
for obj in scene.objects:
    if obj.type=='MESH' and obj.name.startswith(('FE Jersey','FE Shorts','FE Socks','FE Boots')):
        obj.hide_render=False;obj.hide_set(False)
camera.location=(3,-5,1.35);camera.rotation_euler=(Vector((0,0,.90))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.ortho_scale=1.95
for clip,frame in [('idle',0),('walk',15),('run',10),('sprint',8),('shot_right',31),('keeper_ready',30),('throw_in',33)]:
    rig.animation_data.action=bpy.data.actions[clip];scene.frame_set(frame)
    scene.render.filepath=str(path/(clip+'.png'))
    bpy.ops.render.render(write_still=True)
