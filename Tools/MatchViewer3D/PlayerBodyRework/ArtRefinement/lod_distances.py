"""Render retained LODs at equal, explicit perspective distances in meters."""
import importlib.util
import json
from pathlib import Path
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[4]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
src=ROOT/'Tools/MatchViewer3D/PlayerBodyRework/sol_body_review.py'
spec=importlib.util.spec_from_file_location('review',src)
review=importlib.util.module_from_spec(spec)
spec.loader.exec_module(review)
output=OUT/'lod-distances'
review.render_views(OUT/'final-body-source.blend',output,samples=8,view='no_shots',clay=False)
scene=bpy.context.scene
camera=scene.camera
camera.data.type='PERSP'
camera.data.lens=50
camera.data.sensor_width=36
camera.data.dof.use_dof=False
scene.render.resolution_x=960
scene.render.resolution_y=540
scene.render.resolution_percentage=100
for obj in scene.objects:
    if obj.type=='MESH':obj.hide_render=obj.name!='Studio_floor_NOT_exported'
target=Vector((0,0,.9))
direction=Vector((.45,-.893,.015)).normalized()
for level in range(3):
    for i in range(3):
        collection=bpy.data.collections[f'Runtime_LOD{i}']
        collection.hide_render=i!=level
        for obj in collection.objects:
            obj.hide_render=i!=level
            obj.hide_set(False)
    for distance in (10,25,50):
        camera.location=target+direction*distance
        camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(output/f'lod{level}-{distance}m.png')
        bpy.ops.render.render(write_still=True)
(output/'settings.json').write_text(json.dumps({'distancesM':[10,25,50],'lensMm':50,'sensorWidthMm':36,'imagePixels':[960,540],'cameraTargetM':[0,0,.9],'note':'Perspective distances, not performance or runtime screenshots.'},indent=2))
