"""Run with Blender --background --factory-startup --python this_file.

Imports the authoritative GLB into a new authoring asset. Never edits the user's live
Blender or overwrites the older linked-library scene, whose missing meshes prevent a faithful reload.
"""
import hashlib
import json
import math
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
ASSET = ROOT/'FootballEngine.Client/Assets/MatchViewer'
LIGHTING = ASSET/'Lighting'
manifest = json.loads((LIGHTING/'manifest.json').read_text())


def enum_value(owner, property_name, desired):
    items = owner.bl_rna.properties[property_name].enum_items
    return next(item.identifier for item in items if item.identifier.casefold() == desired.casefold())


def node(tree, node_type, label):
    value = tree.nodes.new(node_type)
    value.label = label
    return value


def math_node(tree, operation, first=None, second=None):
    result = node(tree, 'ShaderNodeMath', operation)
    result.operation = enum_value(result, 'operation', operation)
    for index, value in enumerate((first, second)):
        if value is not None:
            if isinstance(value, (float, int)):
                result.inputs[index].default_value = value
            else:
                tree.links.new(value, result.inputs[index])
    return result.outputs[0]


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(ASSET/'stadium.glb'))
scene = bpy.context.scene
scene.name = 'FE Matchday / Daylight'
world = bpy.data.worlds.new('FE exported stadium environment')
world.use_nodes = True
scene.world = world
tree = world.node_tree
tree.nodes.clear()

# Explicit lat-long coordinates avoid relying on Blender's different Z-up environment-map convention.
coordinate = node(tree, 'ShaderNodeTexCoord', 'World ray: Blender Z-up')
separate = node(tree, 'ShaderNodeSeparateXYZ', 'Runtime axes: x, z, -y')
direction = node(tree, 'ShaderNodeVectorMath', 'World background normals point back toward the viewer')
direction.operation = enum_value(direction, 'operation', 'SCALE')
direction.inputs[3].default_value = -1.0
tree.links.new(coordinate.outputs[1], direction.inputs[0])
tree.links.new(direction.outputs[0], separate.inputs[0])
longitude = math_node(tree, 'ARCTAN2', math_node(tree, 'MULTIPLY', separate.outputs[1], -1.0), separate.outputs[0])
u = math_node(tree, 'ADD', math_node(tree, 'DIVIDE', longitude, 2*math.pi), 0.5)
latitude = math_node(tree, 'ARCCOSINE', separate.outputs[2])
v = math_node(tree, 'SUBTRACT', 1.0, math_node(tree, 'DIVIDE', latitude, math.pi))
uv = node(tree, 'ShaderNodeCombineXYZ', 'Shared WebGPU sky UV convention')
tree.links.new(u, uv.inputs[0])
tree.links.new(v, uv.inputs[1])
backgrounds = []
presentation = manifest['presentation']
for name, label in [('sky-lighting.exr', 'Original solar-disc-free environment lighting'), ('sky-visible.exr', 'Original Blender atmosphere and clouds')]:
    entry = next(item for item in manifest['authoringFiles'] if item['path'] == name)
    path = LIGHTING/name
    if hashlib.sha256(path.read_bytes()).hexdigest() != entry['sha256']:
        raise ValueError(f'Unverified authoring sky: {name}')
    image = bpy.data.images.load(str(path), check_existing=False)
    image.pack()
    texture = node(tree, 'ShaderNodeTexImage', label)
    texture.image = image
    texture.interpolation = enum_value(texture, 'interpolation', 'Linear')
    tree.links.new(uv.outputs[0], texture.inputs[0])
    background = node(tree, 'ShaderNodeBackground', label)
    background.inputs[1].default_value = presentation['environmentIntensity']
    tree.links.new(texture.outputs[0], background.inputs[0])
    backgrounds.append(background)
path = node(tree, 'ShaderNodeLightPath', 'Keep the authored solar disc out of diffuse/specular IBL')
mix = node(tree, 'ShaderNodeMixShader', 'Camera sky / lighting environment')
tree.links.new(path.outputs[0], mix.inputs[0])
tree.links.new(backgrounds[0].outputs[0], mix.inputs[1])
tree.links.new(backgrounds[1].outputs[0], mix.inputs[2])
output = node(tree, 'ShaderNodeOutputWorld', 'Exported stadium world')
tree.links.new(mix.outputs[0], output.inputs[0])

# Hide the old authored night lights in this daylight preset, retaining their geometry for future floodlights.
for obj in scene.objects:
    if obj.type == 'LIGHT':
        obj.hide_render = True
sun_type = next(item.identifier for item in bpy.types.Light.bl_rna.properties['type'].enum_items if item.identifier.casefold() == 'sun')
sun_data = bpy.data.lights.new('FE exported daylight sun', sun_type)
sun = bpy.data.objects.new('FE exported daylight sun', sun_data)
scene.collection.objects.link(sun)
sun_direction = Vector(manifest['sunDirection'])
blender_sun = Vector((sun_direction.x, -sun_direction.z, sun_direction.y))
sun.rotation_euler = (-blender_sun).to_track_quat('-Z', 'Y').to_euler()
sun_data.energy = max(presentation['sunRadiance'])
sun_data.color = tuple(value/sun_data.energy for value in presentation['sunRadiance'])
sun_data.angle = math.radians(manifest['sunAngularDiameterDegrees'])

camera_data = bpy.data.cameras.new('FE review behind goal')
camera = bpy.data.objects.new('FE review behind goal', camera_data)
scene.collection.objects.link(camera)
camera.location = (73.0, 0.0, 19.0)
target = Vector((0.0, 0.0, 0.7))
camera.rotation_euler = (target-camera.location).to_track_quat('-Z', 'Y').to_euler()
camera_data.sensor_fit = enum_value(camera_data, 'sensor_fit', 'VERTICAL')
camera_data.angle = math.radians(48)
scene.camera = camera
scene.render.resolution_x = 1160
scene.render.resolution_y = 698
scene.render.resolution_percentage = 100
scene.view_settings.exposure = presentation['exposureStops']

metadata = {
    'schema': 2,
    'authoringFile': 'stadium-original-daylight.blend',
    'stadiumSha256': hashlib.sha256((ASSET/'stadium.glb').read_bytes()).hexdigest(),
    'environmentDirectory': 'Lighting',
    'environmentManifestSha256': hashlib.sha256((LIGHTING/'manifest.json').read_bytes()).hexdigest(),
    'world': world.name,
    'exposureStops': presentation['exposureStops'],
    'environmentIntensity': presentation['environmentIntensity'],
    'sunDirection': manifest['sunDirection'],
    'sunRadiance': presentation['sunRadiance'],
    'skySource': manifest['source'],
    'authorship': manifest['authorship'],
    'projection': 'atan2(runtime.z,runtime.x), acos(runtime.y); Blender axes converted explicitly',
}
world['fe_environment'] = json.dumps(metadata, sort_keys=True)
scene['fe_runtime_environment'] = 'stadium.environment.json'
bpy.ops.wm.save_as_mainfile(filepath=str(ASSET/'stadium-original-daylight.blend'))
(ASSET/'stadium.environment.json').write_text(json.dumps(metadata, indent=2, sort_keys=True)+'\n')

# The review image is evidence only; the application still performs its own WebGPU rendering.
try:
    scene.render.engine = 'CYCLES'
except TypeError as error:
    raise RuntimeError(f'Cycles is required for the offline stadium review: {error}')
scene.cycles.samples = 16
scene.render.filepath = '/tmp/opencode/visual-blender-stadium-daylight.png'
bpy.ops.render.render(write_still=True)
print('STADIUM_ENVIRONMENT_AUTHORED='+json.dumps(metadata))
