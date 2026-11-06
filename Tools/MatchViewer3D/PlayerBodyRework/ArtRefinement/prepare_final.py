"""Promote the visually selected surface and remove only inherited LODs."""
from pathlib import Path
import bpy

ROOT=Path(__file__).resolve().parents[4]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'face-refinement.blend'))
for collection in list(bpy.data.collections):
    if collection.get('fe_generated_body_lod'):
        for obj in list(collection.objects):
            bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.collections.remove(collection)
body=bpy.data.objects['FINAL_Body_Connected']
body.hide_set(False)
body.hide_render=False
bpy.context.scene.frame_set(1)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'final-body-source.blend'))
print('SELECTED_FACE_VARIANT_OLD_LODS_REMOVED',flush=True)
