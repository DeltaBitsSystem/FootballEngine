"""Blender-only visual check of the second atlas half, never saved to source."""
from pathlib import Path
import importlib.util
import sys
import bpy
from mathutils import Vector
sys.dont_write_bytecode=True

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
helper=ROOT/'Tools/MatchViewer3D/PlayerBodyRework/sol_body_review.py'
spec=importlib.util.spec_from_file_location('review',helper)
review=importlib.util.module_from_spec(spec);spec.loader.exec_module(review)
path=OUT/'runtime-preview'
review.render_views(OUT/'player-runtime-source.blend',path,samples=16,view='none',clay=False)
s=bpy.context.scene;rig=bpy.data.objects['FINAL_Rig'];rig.animation_data.action=bpy.data.actions['idle'];s.frame_set(0)
for obj in s.objects:
    if obj.type=='MESH' and obj.parent==rig and obj.data.uv_layers.active:
        for loop in obj.data.uv_layers.active.data:loop.uv.y-=.5
        if obj.name.startswith(('FE Jersey','FE Shorts','FE Socks','FE Boots')):obj.hide_render=False;obj.hide_set(False)
cam=s.camera;cam.location=(3,-5,1.35);cam.rotation_euler=(Vector((0,0,.90))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=1.95
s.render.filepath=str(path/'away-idle.png');bpy.ops.render.render(write_still=True)
