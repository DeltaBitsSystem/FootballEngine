"""Quick neutral-gray inspection renders on a saved source; never edits the source."""
import bpy,sys,os
from pathlib import Path
from mathutils import Vector
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
blend=Path(args[0]);out=Path(args[1]);shots=args[2].split(',') if len(args)>2 else ['front','profile','back','three-quarter','head-front','head-three-quarter','hand','foot','foot-side','foot-three-quarter']
bpy.ops.wm.open_mainfile(filepath=str(blend));scene=bpy.context.scene;scene.frame_set(1)
body=bpy.data.objects['FINAL_Body_Connected']
def mat(name,value,rough=.62):
    m=bpy.data.materials.new(name);m.use_nodes=True
    b=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value=(value,value,value,1);b.inputs['Roughness'].default_value=rough
    return m
body.data.materials.clear();body.data.materials.append(mat('Preview_clay',.46))
for o in scene.objects:
    if o.type=='MESH' and o is not body:o.hide_render=True;o.hide_set(True)
world=scene.world or bpy.data.worlds.new('World');scene.world=world;world.use_nodes=True
bg=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs['Color'].default_value=(.035,.038,.045,1);bg.inputs['Strength'].default_value=.35
for o in list(scene.objects):
    if o.type=='LIGHT':bpy.data.objects.remove(o,do_unlink=True)
for name,loc,energy,size in [('K',(2.6,-2.2,2.4),420,2.4),('F',(-2.8,-3.2,1.2),90,3.0),('R',(1.2,3.4,2.2),160,2.0)]:
    d=bpy.data.lights.new(name,'AREA');d.energy=energy;d.size=size;l=bpy.data.objects.new(name,d);scene.collection.objects.link(l);l.location=loc
    l.rotation_euler=(Vector((0,0,1.15))-l.location).to_track_quat('-Z','Y').to_euler()
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.threads_mode='FIXED';scene.render.threads=6;scene.render.resolution_x=640;scene.render.resolution_y=800
cam=scene.camera or bpy.data.objects.new('Cam',bpy.data.cameras.new('Cam'))
if cam.name not in scene.objects:scene.collection.objects.link(cam)
scene.camera=cam
def shot(name,pos,target,scale):
    cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=scale
    scene.render.filepath=str(out/('preview-'+name+'.png'));bpy.ops.render.render(write_still=True)
S={'front':((0,-5,.92),(0,0,.92),2.02),'profile':((5,0,.92),(0,0,.92),2.02),'back':((0,5,.92),(0,0,.92),2.02),'three-quarter':((3.2,-4.6,1.45),(0,0,.92),2.02),
'head-front':((0,-2,1.70),(0,0,1.68),.30),'head-three-quarter':((1.2,-2,1.78),(0,0,1.68),.30),'head-profile':((2,-.7,1.70),(0,0,1.68),.30),
'hand':((.62,-2,1.04),(.62,0,1.04),.26),'hand-back':((.62,2,1.04),(.62,0,1.04),.26),
'foot':((.10,-2,.10),(.10,-.04,.10),.34),'foot-side':((2,-.02,.10),(.10,-.04,.10),.34),'foot-three-quarter':((1.6,-1.6,.35),(.10,-.04,.08),.36),
'foot-top':((.10,-.02,2),(.10,-.04,.05),.30),'pelvis':((1.5,-2,.93),(0,0,.93),.55)}
for n in shots:
    if n in S:shot(n,*S[n])
print('PREVIEW_OK',flush=True)
