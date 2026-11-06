"""Render either on-disk source under the identical neutral review setup."""
from pathlib import Path
import importlib.util
import sys
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[5]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
OUT=BASE/'Mandible-R2'
helper=ROOT/'Tools/MatchViewer3D/PlayerBodyRework/sol_body_review.py'
spec=importlib.util.spec_from_file_location('review',helper)
review=importlib.util.module_from_spec(spec)
spec.loader.exec_module(review)
variant,style=sys.argv[sys.argv.index('--')+1:]
source={'before':BASE/'final-body-source.blend','candidate':OUT/'candidate.blend','final':OUT/'final-body-source.blend'}[variant]
directory=OUT/'comparison'/variant/style
review.render_views(source,directory,samples=16,view=['head','head_profile','body','squat','twist'],clay=style=='clay')
scene=bpy.context.scene
camera=scene.camera
camera.location=(0,-5,1.679)
camera.rotation_euler=(Vector((0,-.005,1.679))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.ortho_scale=.30
scene.render.filepath=str(directory/'head_front.png')
bpy.ops.render.render(write_still=True)
