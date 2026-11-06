"""Inspect the actual retained export surfaces and UV checker in a clean process."""
import json
from pathlib import Path

import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Revision-Sol'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'final-body-source.blend'))
scene=bpy.context.scene
scene.frame_set(1)
scene.render.engine='CYCLES'
scene.cycles.samples=24
scene.cycles.use_denoising=True
scene.render.threads_mode='FIXED'
scene.render.threads=6
scene.render.resolution_x=450
scene.render.resolution_y=600
scene.render.resolution_percentage=100
evidence=OUT/'lod-review'
evidence.mkdir(exist_ok=True)
camera=scene.camera


def shot(name,position,target,scale):
    camera.location=position
    camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=scale
    scene.render.filepath=str(evidence/(name+'.png'))
    bpy.ops.render.render(write_still=True)


for obj in scene.objects:
    if obj.type=='MESH':
        obj.hide_render=obj.name!='Studio_floor_NOT_exported'
for level in range(3):
    for index in range(3):
        collection=bpy.data.collections[f'Runtime_LOD{index}']
        collection.hide_render=index!=level
        for obj in collection.objects:
            obj.hide_render=index!=level
            obj.hide_set(False)
    shot(f'lod{level}-body',(3,-5,1.35),(0,0,.90),1.95)
    shot(f'lod{level}-head',(2,-5,1.70),(0,-.005,1.679),.30)
    # Same projection at a lower physical pixel count approximates the size
    # of a player on the broadcast camera, not a performance measurement.
    scene.render.resolution_x=90
    scene.render.resolution_y=120
    shot(f'lod{level}-small',(3,-5,1.35),(0,0,.90),1.95)
    scene.render.resolution_x=450
    scene.render.resolution_y=600

for index in range(3):
    bpy.data.collections[f'Runtime_LOD{index}'].hide_render=True
rig=bpy.data.objects['FINAL_Rig']
for obj in scene.objects:
    if obj.type=='MESH' and obj.parent==rig and (obj.name=='FINAL_Body_Connected' or obj.name.startswith('GEO_')):
        obj.hide_render=False
        obj.hide_set(False)
body=bpy.data.objects['FINAL_Body_Connected']
material=body.data.materials[0].copy()
body.data.materials[0]=material
nodes,links=material.node_tree.nodes,material.node_tree.links
bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
coordinates=nodes.new('ShaderNodeTexCoord')
checker=nodes.new('ShaderNodeTexChecker')
checker.inputs['Color1'].default_value=(.12,.12,.12,1)
checker.inputs['Color2'].default_value=(.65,.65,.65,1)
checker.inputs['Scale'].default_value=32
links.new(coordinates.outputs['UV'],checker.inputs['Vector'])
links.new(checker.outputs['Color'],bsdf.inputs['Base Color'])
shot('uv-body',(3,-5,1.35),(0,0,.90),1.95)
shot('uv-head',(2,-5,1.70),(0,-.005,1.679),.30)
shot('uv-hand',(.85,-2,1.2),(.62,-.005,.97),.25)
record={'sourceReopened':True,'rigBones':len(rig.data.bones),
        'packedBodyImages':[{'name':n.image.name,'size':list(n.image.size),'packed':bool(n.image.packed_file),'colorSpace':n.image.colorspace_settings.name}
                            for n in body.data.materials[0].node_tree.nodes if n.type=='TEX_IMAGE' and n.image],
        'retainedLods':[{'name':bpy.data.collections[f'Runtime_LOD{i}'].name,
                         'objects':len(bpy.data.collections[f'Runtime_LOD{i}'].objects)} for i in range(3)],
        'reviewOnlyChangesSaved':False}
(OUT/'sol-reopen-verification.json').write_text(json.dumps(record,indent=2))
print('SOL_LOD_REVIEW_COMPLETE',flush=True)
