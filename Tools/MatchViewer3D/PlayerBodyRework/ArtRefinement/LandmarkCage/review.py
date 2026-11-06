"""Identical neutral lateral light and orthographic cameras, reloaded from disk."""
from pathlib import Path
import importlib.util,sys
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[5]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
OUT=BASE/'Landmark-Cage'
helper=ROOT/'Tools/MatchViewer3D/PlayerBodyRework/sol_body_review.py'
spec=importlib.util.spec_from_file_location('review',helper)
review=importlib.util.module_from_spec(spec);spec.loader.exec_module(review)
variant=sys.argv[sys.argv.index('--')+1]
source={'before':BASE/'Contour-Study/contour-form.blend','cage':OUT/'cage-form.blend'}[variant]
directory=OUT/'comparison'/variant
review.render_views(source,directory,samples=32,view='none',clay=True)
s=bpy.context.scene
for obj in list(s.objects):
    if obj.type=='LIGHT':bpy.data.objects.remove(obj,do_unlink=True)
for label,loc,power,size in [('Side', (2.8,-1.0,2.5),220,.8),('Fill',(-2.4,-3,1.7),38,2.2),('Back',(-1,3,2.1),55,1.5)]:
    dtype=next(x.identifier for x in bpy.types.Light.bl_rna.properties['type'].enum_items if x.identifier.casefold()=='area')
    light=bpy.data.lights.new(label,dtype);light.energy=power;light.size=size
    ob=bpy.data.objects.new(label,light);s.collection.objects.link(ob);ob.location=loc
    ob.rotation_euler=(Vector((0,0,1.60))-ob.location).to_track_quat('-Z','Y').to_euler()
cam=s.camera
for name,position,target,scale in [('front',(0,-5,1.679),(0,-.005,1.679),.30),('profile',(4,0,1.7),(0,0,1.679),.30),('three-quarter',(2,-5,1.7),(0,-.005,1.679),.30),('body',(3,-5,1.35),(0,0,.90),1.95)]:
    cam.location=position;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=scale;s.render.filepath=str(directory/(name+'.png'))
    bpy.ops.render.render(write_still=True)
