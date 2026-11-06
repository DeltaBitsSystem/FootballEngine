"""Same-camera anatomical review of an isolated editable character file."""
from pathlib import Path
import bpy, sys, math
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
source, destination = Path(args[0]), Path(args[1])
destination.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source))
rig = bpy.data.objects['FINAL_Rig']
assert 'REST' in [i.identifier for i in rig.data.bl_rna.properties['pose_position'].enum_items]
rig.data.pose_position = 'REST'
scene = bpy.data.scenes.new('Astra anatomical review')
bpy.context.window.scene = scene
scene.collection.objects.link(rig)
mat = bpy.data.materials.new('Neutral anatomical clay')
mat.use_nodes = True
shader = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
shader.inputs['Base Color'].default_value = (.38, .35, .31, 1)
shader.inputs['Roughness'].default_value = .7
for obj in list(bpy.data.objects):
    if obj.type == 'MESH' and obj.parent == rig and not obj.name.startswith('FE '):
        scene.collection.objects.link(obj)
        obj.hide_render = False
        obj.data.materials.clear()
        obj.data.materials.append(mat)
world = bpy.data.worlds.new('Review neutral world')
world.use_nodes = True
next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND').inputs['Color'].default_value = (.12, .12, .12, 1)
scene.world = world
def aim(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()
for name, position, power, size in [('Key', (2, -3, 3), 450, 2), ('Fill', (-3, -1, 2), 240, 2), ('Back', (1, 3, 3), 350, 2)]:
    data = bpy.data.lights.new(name, 'AREA')
    data.energy, data.size = power, size
    light = bpy.data.objects.new(name, data)
    scene.collection.objects.link(light)
    light.location = position
    aim(light, (0, 0, 1))
camera = bpy.data.objects.new('Anatomical orthographic camera', bpy.data.cameras.new('Anatomical orthographic camera'))
scene.collection.objects.link(camera)
assert 'ORTHO' in [i.identifier for i in camera.data.bl_rna.properties['type'].enum_items]
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 2.0
scene.camera = camera
try:
    scene.render.engine = 'BLENDER_EEVEE'
except TypeError as error:
    raise RuntimeError(str(error))
scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 720, 960, 100
assert 'PNG' in [i.identifier for i in scene.render.image_settings.bl_rna.properties['file_format'].enum_items]
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
for name, position in [('front', (0, -5, .91)), ('side', (5, 0, .91)), ('back', (0, 5, .91)), ('three-quarter', (3.5, -3.5, .91))]:
    camera.location = position
    aim(camera, (0, 0, .91))
    scene.render.filepath = str(destination / (name + '.png'))
    bpy.ops.render.render(write_still=True)
if '--poses' in args:
    assert 'POSE' in [i.identifier for i in rig.data.bl_rna.properties['pose_position'].enum_items]
    rig.data.pose_position = 'POSE'
    rig.animation_data_create()
    camera.data.ortho_scale = 2.1
    camera.location = (3.5, -3.5, .91)
    aim(camera, (0, 0, .91))
    for name, frame in [('run', 12), ('shot_left', 32), ('shot_right', 32), ('throw_in', 22)]:
        action = bpy.data.actions[name]
        rig.animation_data.action = action
        if action.slots:
            rig.animation_data.action_slot = action.slots[0]
        scene.frame_set(frame)
        scene.render.filepath = str(destination / ('pose-' + name + '.png'))
        bpy.ops.render.render(write_still=True)
    rig.data.pose_position = 'REST'
camera.data.ortho_scale = .34
for name, position in [('head-front', (0, -5, 1.65)), ('head-side', (5, 0, 1.65)), ('head-three-quarter', (3.5, -3.5, 1.65))]:
    camera.location = position
    aim(camera, (0, 0, 1.65))
    scene.render.filepath = str(destination / (name + '.png'))
    bpy.ops.render.render(write_still=True)
