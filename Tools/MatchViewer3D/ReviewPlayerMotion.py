"""Render complete motion cycles from a frozen authoring file, without touching the live Blender scene.

blender --background PACK/players.blend --python Tools/MatchViewer3D/ReviewPlayerMotion.py -- --output-dir PATH
"""
import argparse
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

parser = argparse.ArgumentParser()
parser.add_argument("--output-dir", type=Path, required=True)
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
args.output_dir.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
rig = next(obj for obj in scene.objects if obj.type == "ARMATURE")
camera = scene.camera
scene.render.resolution_x = 320
scene.render.resolution_y = 384
camera.data.ortho_scale = 2.3
scene.render.film_transparent = False
scene.world = bpy.data.worlds.new("Motion Review Background")
scene.world.color = (0.045, 0.045, 0.045)

for clip in ("walk", "run", "sprint", "shield_right", "shot_right"):
    action = next(action for action in bpy.data.actions if action.name == clip)
    rig.animation_data.action = action
    duration = int(action.frame_range[1])
    for view, angle in (("front", 0), ("side", math.pi / 2), ("threequarter", math.pi / 4)):
        camera.location = (6 * math.sin(angle), -6 * math.cos(angle), 1.35)
        camera.rotation_euler = (Vector((0, 0, 0.98)) - camera.location).to_track_quat("-Z", "Y").to_euler()
        for sample in range(8):
            scene.frame_set(round(duration * sample / 8))
            scene.render.filepath = str(args.output_dir / f"{clip}-{view}-{sample:02d}.png")
            bpy.ops.render.render(write_still=True)
