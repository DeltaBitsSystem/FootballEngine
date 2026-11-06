"""Isolated, repeatable review of an existing body source; never opens the live scene."""
import argparse
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body'


def render_views(source, output, samples=16, view=None, clay=False):
    bpy.ops.wm.open_mainfile(filepath=str(source))
    scene = bpy.context.scene
    scene.frame_set(1)
    rig=bpy.data.objects['FINAL_Rig']
    for obj in scene.objects:
        if obj.type == 'MESH':
            visible = obj.name == 'Studio_floor_NOT_exported' or (obj.parent==rig and (obj.name == 'FINAL_Body_Connected' or obj.name.startswith('GEO_')))
            obj.hide_render = not visible
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.threads_mode = 'FIXED'
    scene.render.threads = 6
    scene.render.resolution_x = 600
    scene.render.resolution_y = 760
    scene.render.resolution_percentage = 100
    scene.view_settings.exposure=0
    # Every revision uses the same neutral rig so A/B changes are geometric,
    # not inherited differences between earlier authoring scenes.
    for obj in list(scene.objects):
        if obj.type=='LIGHT':
            bpy.data.objects.remove(obj,do_unlink=True)
    for name,location,energy,size in [
        ('Review_Key',(2.4,-2.6,2.6),300,2.2),
        ('Review_Fill',(-2.8,-3.2,1.4),70,3),
        ('Review_Rim',(1.2,3.2,2.4),120,2),
    ]:
        light_type=next(i.identifier for i in bpy.types.Light.bl_rna.properties['type'].enum_items if i.identifier.casefold()=='area')
        data=bpy.data.lights.new(name,light_type)
        data.energy=energy
        data.size=size
        obj=bpy.data.objects.new(name,data)
        scene.collection.objects.link(obj)
        obj.location=location
        obj.rotation_euler=(Vector((0,0,1.1))-obj.location).to_track_quat('-Z','Y').to_euler()
    if scene.world and scene.world.use_nodes:
        background=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
        background.inputs['Color'].default_value=(.035,.038,.045,1)
        background.inputs['Strength'].default_value=.35
    if clay:
        material=bpy.data.materials.new('Review neutral geometry only')
        material.use_nodes=True
        shader=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        shader.inputs['Base Color'].default_value=(.35,.35,.35,1)
        shader.inputs['Roughness'].default_value=.7
        body=bpy.data.objects['FINAL_Body_Connected']
        body.data.materials.clear()
        body.data.materials.append(material)
        for obj in scene.objects:
            if obj.type=='MESH' and obj.parent==rig and obj.name.startswith(('GEO_Ear','GEO_Nail')):
                obj.data.materials.clear()
                obj.data.materials.append(material)
    output.mkdir(parents=True, exist_ok=True)
    shots = [
        ('front', (0,-5,1.0), (0,0,.90), 1.95, 1),
        ('body', (3, -5, 1.35), (0, 0, .90), 1.95, 1),
        ('profile', (4, 0, 1.1), (0, 0, .90), 1.95, 1),
        ('back', (0,5,1.0), (0,0,.90), 1.95, 1),
        ('head', (2, -5, 1.7), (0, -.005, 1.679), .30, 1),
        ('head_profile', (4,0,1.7), (0,0,1.679), .30, 1),
        ('hip', (3, -5, 1.25), (0, 0, .85), .78, 60),
        ('raised_arms', (3,-5,1.35), (0,0,1.23), 1.5, 20),
        ('hand', (.85,-2,1.2), (.62,-.005,.97), .25, 1),
        ('grip', (.85,-2,1.2), (.62,-.005,.97), .25, 120),
        ('squat', (3,-5,1.3), (0,-.10,.70), 1.7, 160),
        ('twist', (3,-5,1.3), (0,0,.90), 1.95, 180),
        ('right_hip', (-3,-5,1.25), (0,0,.85), .85, 190),
        ('foot', (.45,-.6,.35), (.095,-.055,.08), .32, 1),
    ]
    for name, position, target, scale, frame in shots:
        if view and name not in ([view] if isinstance(view,str) else view):
            continue
        scene.frame_set(frame)
        camera = scene.camera
        camera.location = position
        camera.rotation_euler = (Vector(target) - camera.location).to_track_quat('-Z', 'Y').to_euler()
        camera.data.ortho_scale = scale
        scene.render.filepath = str(output / (name + '.png'))
        bpy.ops.render.render(write_still=True)
    scene.frame_set(1)
    (output / 'review-state.json').write_text(json.dumps({
        'source': str(source),
        'visibleMeshes': [o.name for o in scene.objects if o.type == 'MESH' and not o.hide_render],
        'blender': bpy.app.version_string,
    }, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--revision', default='Revision-F')
    parser.add_argument('--samples', type=int, default=16)
    parser.add_argument('--view')
    parser.add_argument('--views',nargs='+')
    parser.add_argument('--clay',action='store_true')
    parser.add_argument('--output')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    output=Path(args.output) if args.output else BASE / args.revision / 'sol-review'
    render_views(BASE / args.revision / 'final-body-source.blend', output, args.samples, args.views or args.view, args.clay)
