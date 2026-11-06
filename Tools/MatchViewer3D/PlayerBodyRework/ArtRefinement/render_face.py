"""Render identical Sol review lighting and orthographic cameras from disk."""
import importlib.util
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[4]
BASE = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
helper_path = ROOT / 'Tools/MatchViewer3D/PlayerBodyRework/sol_body_review.py'
spec = importlib.util.spec_from_file_location('sol_review_readonly', helper_path)
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)
args = sys.argv[sys.argv.index('--')+1:]
variant, style = args
sources = {'before':'staging-source.blend', 'after':'face-refinement.blend', 'profile':'profile-refinement.blend', 'final':'final-body-source.blend'}
source = BASE / sources[variant]
output = BASE / 'comparison' / variant / style
helper.render_views(source, output, samples=16, view=['head', 'head_profile'], clay=style == 'clay')
scene = bpy.context.scene
scene.camera.location = (0, -5, 1.679)
scene.camera.rotation_euler = (Vector((0, -.005, 1.679))-scene.camera.location).to_track_quat('-Z', 'Y').to_euler()
scene.camera.data.ortho_scale = .30
scene.render.filepath = str(output/'head_front.png')
bpy.ops.render.render(write_still=True)
print('RENDERED_FROM_DISK', str(source), str(output), flush=True)
