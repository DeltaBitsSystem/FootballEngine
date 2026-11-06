"""Deterministic surface/garment finishing and re-rendering of the isolated body.

Run after author_body.py and before validate_export.py. No external image data.
"""
import bpy
import bmesh
import json
import math
import random
from pathlib import Path

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'athlete-body-editable.blend'))
scene=bpy.context.scene
body=bpy.data.objects['Athlete_ContinuousSkin_LOD0']
rig=bpy.data.objects['AthleteRig_Staging']
scene.frame_set(1)
skin=body.data.materials[0]
bsdf=next(n for n in skin.node_tree.nodes if n.type=='BSDF_PRINCIPLED')

# Low-amplitude albedo variation has no baked illumination, pores or borrowed pixels.
rng=random.Random(81723)
size=512
img=bpy.data.images.new('Original_skin_albedo_seed81723',size,size,alpha=False)
pixels=[]
for y in range(size):
    for x in range(size):
        variation=.985+.012*math.sin(x*.061)*math.sin(y*.049)+rng.uniform(-.006,.006)
        pixels.extend((.42*variation,.235*variation,.145*variation,1))
img.pixels.foreach_set(pixels)
img.filepath_raw=str(OUT/'skin-basecolor-original.png')
img.file_format='PNG';img.save();img.pack()
tex=skin.node_tree.nodes.new('ShaderNodeTexImage');tex.image=img
skin.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])

# Use the actual continuous body surface as a separate compression-short shell.
# This avoids a funnel-shaped proxy cutting into the gluteal and upper-thigh contours.
brief=bpy.data.objects['Separate_briefs']
cloth=brief.data.materials[0]
bpy.data.objects.remove(brief,do_unlink=True)
m=body.data.copy()
bm=bmesh.new();bm.from_mesh(m)
remove=[f for f in bm.faces if not (.835<f.calc_center_median().z<.993 and abs(f.calc_center_median().x)<.25)]
bmesh.ops.delete(bm,geom=remove,context='FACES')
loose=[v for v in bm.verts if not v.link_faces]
if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
bm.normal_update()
for v in bm.verts:v.co+=v.normal*.0035
bm.to_mesh(m);bm.free()
brief=bpy.data.objects.new('Separate_briefs',m);scene.collection.objects.link(brief)
brief.data.materials.clear();brief.data.materials.append(cloth)
# Mesh copies retain deform data; recreating the same group ordering resolves those indices.
for g in body.vertex_groups:brief.vertex_groups.new(name=g.name)
mod=brief.modifiers.new('CanonicalNames_Skinning','ARMATURE');mod.object=rig;brief.parent=rig

# The socket surface is the eyelid: sclera sits behind it, not as a protruding sphere.
for suffix in ['L','R']:
    for prefix in ['Eye','Iris','Pupil']:
        o=bpy.data.objects[prefix+'.'+suffix]
        for v in o.data.vertices:v.co.y+=.011

report=json.loads((OUT/'validation.json').read_text())
report['textures']=[{'file':'skin-basecolor-original.png','size':[512,512],'channels':'sRGB albedo, no baked lighting','source':'Original deterministic sine and pseudorandom pixel field, seed 81723; amplitude under 3%.','uv':'Smart-projected non-overlapping UV islands; not the live athlete atlas.'}]
report['limitations']=[s for s in report['limitations'] if 'constant PBR' not in s]
report['limitations'].append('Minimal original skin albedo only; no production micro-normal, body hair or variant face library.')
(OUT/'validation.json').write_text(json.dumps(report,indent=2))

bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);body.select_set(True);brief.select_set(True)
for o in scene.objects:
    if o.name.startswith(('Eye.','Iris.','Pupil.')):o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.gltf(filepath=str(OUT/'athlete-body-staging.glb'),export_format='GLB',use_selection=True,export_animations=False,export_skins=True,export_yup=True,export_apply=False)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-body-editable.blend'))

cam=scene.camera
from mathutils import Vector
def render(name,pos,target,scale,frame=1):
    scene.frame_set(frame);cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(name+'.png'))
    bpy.ops.render.render(write_still=True)
for name,pos in [('front',(0,-5,.92)),('profile',(5,0,.92)),('back',(0,5,.92)),('three-quarter',(3,-5,1.5))]:render(name,pos,(0,0,.90),2.02)
render('head-front',(0,-3,1.67),(0,0,1.66),.37)
render('head-profile',(3,-.8,1.67),(0,0,1.66),.37)
render('hand-closeup',(1,-3,1.10),(.635,0,.97),.30)
render('feet-closeup',(.7,-1.6,.55),(0,-.07,.105),.48)
render('arms-raised',(2,-5,1.3),(0,0,1.02),2.25,20)
render('elbows-flexed',(2,-5,1.3),(0,0,.98),2.12,40)
render('hip-knee-flexed',(4,-4,1.2),(0,0,.90),2.05,60)
render('running-stride',(4,-4,1.2),(0,0,.90),2.05,80)
print('REFINEMENT_COMPLETE')
