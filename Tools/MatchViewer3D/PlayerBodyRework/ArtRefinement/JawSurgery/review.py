from pathlib import Path
import importlib.util,sys
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[5]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
OUT=BASE/'Jaw-Surgery'
helper=ROOT/'Tools/MatchViewer3D/PlayerBodyRework/sol_body_review.py'
spec=importlib.util.spec_from_file_location('review',helper)
review=importlib.util.module_from_spec(spec);spec.loader.exec_module(review)
variant=sys.argv[sys.argv.index('--')+1]
source={'before':BASE/'Contour-Study/contour-form.blend','surgery':OUT/'jaw-form.blend'}[variant]
directory=OUT/'comparison'/variant
review.render_views(source,directory,samples=16,view=['head','head_profile','body'],clay=True)
s=bpy.context.scene;s.camera.location=(0,-5,1.679)
s.camera.rotation_euler=(Vector((0,-.005,1.679))-s.camera.location).to_track_quat('-Z','Y').to_euler()
s.camera.data.ortho_scale=.30;s.render.filepath=str(directory/'head_front.png')
bpy.ops.render.render(write_still=True)
