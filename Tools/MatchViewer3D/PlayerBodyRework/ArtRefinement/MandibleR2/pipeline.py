"""Run established baking/export validators exclusively in Mandible-R2."""
from pathlib import Path
import shutil
import sys

import bpy

ROOT=Path(__file__).resolve().parents[5]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
OUT=BASE/'Mandible-R2'
TOOLS=ROOT/'Tools/MatchViewer3D/PlayerBodyRework/ArtRefinement'
stage=sys.argv[sys.argv.index('--')+1]
if stage=='prepare':
    bpy.ops.wm.open_mainfile(filepath=str(OUT/'candidate.blend'))
    for c in list(bpy.data.collections):
        if c.get('fe_generated_body_lod'):
            for obj in list(c.objects):bpy.data.objects.remove(obj,do_unlink=True)
            bpy.data.collections.remove(c)
    body=bpy.data.objects['FINAL_Body_Connected']
    body.hide_set(False);body.hide_render=False
    bpy.context.scene.frame_set(1)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'final-body-source.blend'))
    shutil.copyfile(BASE/'final-body-authoring.json',OUT/'final-body-authoring.json')
else:
    scripts={'bake':'bake_final.py','export':'export_final.py','validate':'validate_final.py','verify':'verify_delivery.py','lod':'lod_distances.py'}
    source=TOOLS/scripts[stage]
    text=source.read_text()
    old='Final-Body/Art-Refinement'
    assert old in text
    text=text.replace(old,old+'/Mandible-R2')
    assert "Final-Body/Art-Refinement'" not in text
    exec(compile(text,str(source),'exec'),{'__file__':str(source),'__name__':'__main__'})
