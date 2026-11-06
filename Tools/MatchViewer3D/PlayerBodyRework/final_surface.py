"""Final original albedo bake and body-matched garment; isolated staging only.

Run after skin_and_pose.py and before validate_export.py.
"""
import bpy
import bmesh
import math
import json
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'athlete-body-editable.blend'))
scene=bpy.context.scene;scene.frame_set(1)
body=bpy.data.objects['Athlete_ContinuousSkin_LOD0'];rig=bpy.data.objects['AthleteRig_Staging']

# Relax residual union creases, retaining finger tips and the face's authored relief.
bm=bmesh.new();bm.from_mesh(body.data)
for v in bm.verts:
    x,y,z=v.co
    if .07<abs(x)<.26 and 1.36<z<1.49:
        trapezius_roof=1.48-.36*abs(x)
        if z>trapezius_roof:v.co.z=trapezius_roof+.12*(z-trapezius_roof)
joint=[v for v in bm.verts if (1.295<v.co.z<1.445 and .12<abs(v.co.x)<.265)]
for _ in range(50):bmesh.ops.smooth_vert(bm,verts=joint,factor=.5,use_axis_x=True,use_axis_y=True,use_axis_z=True)
neck=[v for v in bm.verts if 1.48<v.co.z<1.58 and v.co.y>0]
for _ in range(20):bmesh.ops.smooth_vert(bm,verts=neck,factor=.4,use_axis_x=True,use_axis_y=True,use_axis_z=True)
bm.to_mesh(body.data);bm.free()
for p in body.data.polygons:p.use_smooth=True;p.material_index=0

# Re-solve on the finished shoulder shape rather than retaining weights from
# the exploratory surface. Neighbour smoothing reduces isolated heat-weight spikes.
for modifier in list(body.modifiers):
    if modifier.type=='ARMATURE':body.modifiers.remove(modifier)
body.vertex_groups.clear()
bpy.ops.object.select_all(action='DESELECT');body.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
bpy.context.view_layer.objects.active=body
group_modes=[i.identifier for i in bpy.ops.object.vertex_group_smooth.get_rna_type().properties['group_select_mode'].enum_items]
if 'ALL' in group_modes:bpy.ops.object.vertex_group_smooth(group_select_mode='ALL',factor=.6,repeat=8)
for vertex in body.data.vertices:
    influences=sorted([(g.group,g.weight) for g in vertex.groups if g.weight>1e-7],key=lambda p:-p[1])[:4]
    if not influences:raise RuntimeError('Final bone heat left an unweighted vertex')
    for group in body.vertex_groups:group.remove([vertex.index])
    total=sum(w for _,w in influences)
    for group,weight in influences:body.vertex_groups[group].add([vertex.index],weight/total,'REPLACE')

# Continuous vertex colour avoids stair-stepped face-material borders. These are
# original pigment fields, not lighting or photographic skin information.
attr=body.data.color_attributes.get('Original_Anatomical_Pigment') or body.data.color_attributes.new(name='Original_Anatomical_Pigment',type='FLOAT_COLOR',domain='CORNER')
def gauss(x,c,s):return math.exp(-((x-c)/s)**2)
for loop in body.data.loops:
    x,y,z=body.data.vertices[loop.vertex_index].co
    variation=1+.009*math.sin(x*81)*math.sin(y*67)*math.sin(z*95)
    color=Vector((.34,.172,.105))*variation
    if y<-.045 and z>1.54:
        lip=.70*gauss(x,0,.025)*gauss(z,1.588,.007)
        color=color.lerp(Vector((.245,.092,.072)),lip)
        arch=1.701-.12*(abs(x)-.031)
        brow=.9*gauss(abs(x),.035,.018)*gauss(z,arch,.002)
        color=color.lerp(Vector((.18,.095,.060)),brow)
    attr.data[loop.index].color=(*color,1)
mat=bpy.data.materials.new('Original_baked_skin_PBR');mat.use_nodes=True
body.data.materials.clear();body.data.materials.append(mat)
nodes=mat.node_tree.nodes;links=mat.node_tree.links
bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
output=next(n for n in nodes if n.type=='OUTPUT_MATERIAL')
bsdf.inputs['Roughness'].default_value=.66
color=nodes.new('ShaderNodeVertexColor');color.layer_name=attr.name
emission=nodes.new('ShaderNodeEmission');links.new(color.outputs['Color'],emission.inputs['Color']);links.new(emission.outputs[0],output.inputs['Surface'])
image=bpy.data.images.new('Original_skin_albedo_final',1024,1024,alpha=False)
image.pixels.foreach_set([.34,.172,.105,1]*(1024*1024))
target=nodes.new('ShaderNodeTexImage');target.image=image;nodes.active=target
bpy.ops.object.select_all(action='DESELECT');body.select_set(True);bpy.context.view_layer.objects.active=body
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=1
scene.render.bake.use_clear=False
bpy.ops.object.bake(type='EMIT',margin=2)
from array import array
pixels=array('f',[0])*(1024*1024*4)
image.pixels.foreach_get(pixels)
# Subpixel UV slivers can receive black miss pixels even without clearing the
# image. Black is outside this authored pigment palette, so fill misses with skin.
for i in range(0,len(pixels),4):
    if pixels[i]<.14:pixels[i:i+4]=array('f',[.34,.172,.105,1])
image.pixels.foreach_set(pixels)
image.filepath_raw=str(OUT/'skin-basecolor-original.png');image.file_format='PNG';image.save();image.pack()
links.new(bsdf.outputs[0],output.inputs['Surface']);links.new(color.outputs['Color'],bsdf.inputs['Base Color'])
nodes.remove(emission)
scene.cycles.samples=32

# Cut a real skin-derived compression garment with planar waist/leg hems.
old=bpy.data.objects['Separate_briefs'];cloth=old.data.materials[0]
bpy.data.objects.remove(old,do_unlink=True)
m=body.data.copy();bm=bmesh.new();bm.from_mesh(m)
faces=[f for f in bm.faces if abs(f.calc_center_median().x)>.25]
bmesh.ops.delete(bm,geom=faces,context='FACES')
for height,inner,outer in [(.835,True,False),(.993,False,True)]:
    bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=1e-6,plane_co=(0,0,height),plane_no=(0,0,1),clear_inner=inner,clear_outer=outer)
loose=[v for v in bm.verts if not v.link_faces]
if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
bm.normal_update()
for v in bm.verts:v.co+=v.normal*.006
bm.to_mesh(m);bm.free()
for attribute in list(m.color_attributes):m.color_attributes.remove(attribute)
brief=bpy.data.objects.new('Separate_briefs',m);scene.collection.objects.link(brief)
brief.data.materials.clear();brief.data.materials.append(cloth)
for p in brief.data.polygons:p.material_index=0;p.use_smooth=True
for g in body.vertex_groups:brief.vertex_groups.new(name=g.name)
mod=brief.modifiers.new('CanonicalNames_Skinning','ARMATURE');mod.object=rig;brief.parent=rig

report=json.loads((OUT/'validation.json').read_text())
report['textures']=[{'file':'skin-basecolor-original.png','size':[1024,1024],'channels':'sRGB albedo, emission-baked original pigment only; no lighting','source':'Original smooth numeric pigment field with lip/brow accents; authored geometry colour baked by Blender. No external pixels.','uv':'Smart-projected skin UV islands; 2-pixel bake margin, invalid background filled with original skin base; not the live athlete atlas.'}]
report['colorContract']='Authoring and GLB skin use original continuous vertex pigment (COLOR_0), avoiding small-UV-island bake artifacts. The standalone original albedo bake is retained as an editable intermediate, not the approved runtime atlas.'
report['skinning']='Blender bone heat re-solved on the finished anatomical surface; eight neighbour-smoothing iterations; four largest influences normalized; matching garment inherits interpolated weights; root non-deforming.'
report['limitations'].append('UV checker reveals varying density and small-island stretching. Standalone albedo bake still has seam/sliver artifacts; final skin uses vertex pigment instead. Runtime atlas rebake requires better UV topology.')
report['limitations'].append('Face is a medium-detail procedural adult study, without facial blendshapes, eyelashes or high-frequency skin detail; not a photoreal likeness.')
report['limitations']=list(dict.fromkeys(report['limitations']))
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);body.select_set(True);brief.select_set(True)
for o in scene.objects:
    if o.name.startswith(('Eye.','Iris.','Pupil.')):o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.gltf(filepath=str(OUT/'athlete-body-staging.glb'),export_format='GLB',use_selection=True,export_animations=False,export_skins=True,export_yup=True,export_apply=False)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-body-editable.blend'))
cam=scene.camera
shots=[('front',(0,-5,.92),(0,0,.90),2.02,1),('profile',(5,0,.92),(0,0,.90),2.02,1),('back',(0,5,.92),(0,0,.90),2.02,1),('three-quarter',(3,-5,1.5),(0,0,.90),2.02,1),('head-front',(0,-3,1.67),(0,0,1.66),.37,1),('head-profile',(3,-.8,1.67),(0,0,1.66),.37,1),('hand-closeup',(1,-3,1.10),(.635,0,.97),.30,1),('feet-closeup',(.7,-1.6,.55),(0,-.07,.105),.48,1),('arms-raised',(3,-5,1.3),(0,0,1.02),2.25,20),('elbows-flexed',(3,-5,1.3),(0,0,.98),2.12,40),('hip-knee-flexed',(4,-4,1.2),(0,0,.90),2.05,60),('running-stride',(4,-4,1.2),(0,0,.90),2.05,80)]
for name,pos,target,scale,frame in shots:
    scene.frame_set(frame);cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(name+'.png'));bpy.ops.render.render(write_still=True)
print('FINAL_SURFACE_COMPLETE')
