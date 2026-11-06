"""Original daylight atmosphere; run only in an isolated Blender background process.

blender -b --factory-startup -t 8 --python Tools/MatchViewer3D/OriginalSky/author_sky.py -- --preview
Omit --preview for the two 2048x1024 scene-linear float EXRs and editable source.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector

ARGS = argparse.ArgumentParser()
ARGS.add_argument('--preview', action='store_true')
args = ARGS.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
ROOT = Path(__file__).resolve().parents[3]
SOURCE = Path(__file__).resolve().parent
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/OriginalSky'
OUT.mkdir(parents=True, exist_ok=True)
SEED = 31871
SUN_R = Vector((-0.48, 0.78, 0.40)).normalized()
SUN_B = Vector((SUN_R.x, -SUN_R.z, SUN_R.y))


def enum_set(owner, key, value):
    allowed = {i.identifier for i in owner.bl_rna.properties[key].enum_items}
    # OCIO and engine-backed dynamic enums can report only a placeholder in RNA.
    try:
        setattr(owner, key, value)
    except (TypeError, ValueError) as exc:
        raise ValueError(f'{key}: {value} unavailable; RNA={allowed}; {exc}') from exc


# A newly created scene prevents accidental mutation even if invoked on a loaded file.
scene = bpy.data.scenes.new('OriginalSky-Daylight-31871')
bpy.context.window.scene = scene
try:
    scene.render.engine = 'CYCLES'
except TypeError as exc:
    raise RuntimeError(f'Cycles required: {exc}') from exc
scene.cycles.samples = 16 if args.preview else 64
scene.cycles.seed = SEED
scene.cycles.use_denoising = True
scene.cycles.use_adaptive_sampling = True
scene.cycles.adaptive_threshold = 0.035 if args.preview else 0.012
scene.cycles.volume_bounces = 2
scene.cycles.volume_step_rate = 0.75
scene.cycles.volume_max_steps = 1024
scene.render.resolution_x = 768 if args.preview else 2048
scene.render.resolution_y = scene.render.resolution_x // 2
scene.render.resolution_percentage = 100
enum_set(scene.view_settings, 'view_transform', 'AgX')
scene.view_settings.exposure = -1.0
scene.view_settings.gamma = 1.0

world = bpy.data.worlds.new('WORLD-original-physical-atmosphere')
world.use_nodes = True
scene.world = world
nodes, links = world.node_tree.nodes, world.node_tree.links
nodes.clear()
sky = nodes.new('ShaderNodeTexSky')
sky.label = 'Physical atmosphere; solar disc toggle owns visible/lighting separation'
sky.location = (-400, 0)
sky_types = {e.identifier for e in sky.bl_rna.properties['sky_type'].enum_items}
enum_set(sky, 'sky_type', 'MULTIPLE_SCATTERING' if 'MULTIPLE_SCATTERING' in sky_types else 'NISHITA')
sky.sun_elevation = math.asin(SUN_B.z)
# Blender's physical sky azimuth is measured from +Y toward +X, unlike atan2(y,x).
sky.sun_rotation = math.atan2(SUN_B.x, SUN_B.y)
sky.sun_size = math.radians(0.545)
sky.sun_intensity = 1.0
sky.altitude = 150.0
sky.air_density = 1.0
sky.ozone_density = 1.0
if hasattr(sky, 'dust_density'):
    sky.dust_density = 0.65
if hasattr(sky, 'aerosol_density'):
    sky.aerosol_density = 0.65
if hasattr(sky, 'ground_albedo'):
    sky.ground_albedo = 0.18
background = nodes.new('ShaderNodeBackground')
background.inputs['Strength'].default_value = 0.1
background.location = (-100, 0)
output = nodes.new('ShaderNodeOutputWorld')
output.location = (160, 0)
links.new(sky.outputs['Color'], background.inputs['Color'])
links.new(background.outputs[0], output.inputs['Surface'])

# Camera rays see a sun-bearing atmosphere; all scattering rays see the disc-free
# atmosphere so the explicit SUN is the only direct source in the cloud volume.
visible = nodes.new('ShaderNodeTexSky')
for prop in sky.bl_rna.properties:
    if prop.identifier in ('sky_type', 'sun_elevation', 'sun_rotation', 'sun_size',
                           'sun_intensity', 'altitude', 'air_density', 'ozone_density',
                           'dust_density', 'aerosol_density', 'ground_albedo'):
        setattr(visible, prop.identifier, getattr(sky, prop.identifier))
visible.location = (-400, 260)
visible.sun_disc = True
sky.sun_disc = False
light_path = nodes.new('ShaderNodeLightPath')
light_path.location = (-400, -260)
disc_scale = nodes.new('ShaderNodeMath')
enum_set(disc_scale, 'operation', 'MULTIPLY')
disc_scale.inputs[1].default_value = 0.25
disc_scale.label = 'Camera-only solar radiance calibration / binary16 headroom'
disc_scale.location = (-150, -240)
links.new(light_path.outputs['Is Camera Ray'], disc_scale.inputs[0])
mix = nodes.new('ShaderNodeMixRGB')
mix.location = (-100, 220)
links.new(disc_scale.outputs[0], mix.inputs[0])
links.new(sky.outputs['Color'], mix.inputs[1])
links.new(visible.outputs['Color'], mix.inputs[2])
links.new(mix.outputs[0], background.inputs['Color'])

sun_data = bpy.data.lights.new('SUN-direct-cloud-scattering-only', 'SUN')
sun_data.energy = 3.2
sun_data.angle = math.radians(0.545)
sun_data.color = (1.0, 0.966, 0.90)
sun = bpy.data.objects.new(sun_data.name, sun_data)
scene.collection.objects.link(sun)
sun.rotation_euler = (-SUN_B).to_track_quat('-Z', 'Y').to_euler()

# A continuous elevated density field, not sphere piles: coarse coherent weather
# islands, fractal edge erosion, and an altitude envelope make flat-based cloud banks.
bpy.ops.mesh.primitive_cube_add(size=2, location=(0, 0, 2850))
cloud = bpy.context.object
cloud.name = 'VOL-distant-fractal-cloud-layer-2400-to-3300m'
cloud.scale = (24000, 24000, 450)
mat = bpy.data.materials.new('MAT-original-cloud-density-seed-31871')
mat.use_nodes = True
cloud.data.materials.append(mat)
n, l = mat.node_tree.nodes, mat.node_tree.links
n.clear()


def node(kind, label, x, y):
    v = n.new(kind)
    v.label = label
    v.location = (x, y)
    return v


def math_node(op, a, b, label, x, y):
    v = node('ShaderNodeMath', label, x, y)
    enum_set(v, 'operation', op)
    for index, item in enumerate((a, b)):
        if isinstance(item, (int, float)):
            v.inputs[index].default_value = item
        else:
            l.new(item, v.inputs[index])
    return v.outputs[0]


geom = node('ShaderNodeNewGeometry', 'World-space metres', -1200, 300)
mapping = node('ShaderNodeVectorMath', 'Weather scale / wind elongation', -1000, 300)
enum_set(mapping, 'operation', 'MULTIPLY')
mapping.inputs[1].default_value = (1 / 1900, 1 / 1400, 1 / 650)
l.new(geom.outputs['Position'], mapping.inputs[0])
noise = node('ShaderNodeTexNoise', 'Coherent cloud banks, fixed 4D seed', -780, 350)
enum_set(noise, 'noise_dimensions', '4D')
noise.inputs['W'].default_value = SEED / 1000
noise.inputs['Scale'].default_value = 1.0
noise.inputs['Detail'].default_value = 5.0
noise.inputs['Roughness'].default_value = 0.62
noise.inputs['Distortion'].default_value = 0.22
l.new(mapping.outputs[0], noise.inputs['Vector'])
separate = node('ShaderNodeSeparateXYZ', 'Altitude', -1000, -200)
l.new(geom.outputs['Position'], separate.inputs[0])
height = math_node('SUBTRACT', separate.outputs['Z'], 2810, 'Layer centre', -780, -180)
height = math_node('DIVIDE', height, 450, 'Layer half-depth', -600, -180)
height = math_node('ABSOLUTE', height, 0, 'Altitude distance', -420, -180)
height = math_node('POWER', height, 2, 'Soft cap / base envelope', -240, -180)
height = math_node('MULTIPLY', height, 0.30, 'Erode toward boundaries', -60, -180)
density = math_node('SUBTRACT', noise.outputs['Fac'], height, 'Altitude-eroded fBm', -420, 350)
density = math_node('SUBTRACT', density, 0.54, 'Broken coverage threshold', -240, 350)
density = math_node('MAXIMUM', density, 0, 'Clear gaps', -60, 350)
density = math_node('MULTIPLY', density, 0.055, 'Extinction per metre', 120, 350)
volume = node('ShaderNodeVolumePrincipled', 'Water droplets / forward scatter', 320, 250)
l.new(density, volume.inputs['Density'])
volume.inputs['Color'].default_value = (0.98, 0.99, 1.0, 1)
volume.inputs['Anisotropy'].default_value = 0.55
out = node('ShaderNodeOutputMaterial', 'Volume only; no opaque cloud surface', 600, 250)
l.new(volume.outputs['Volume'], out.inputs['Volume'])

camera_data = bpy.data.cameras.new('CAM-runtime-Y-up-equirectangular')
enum_set(camera_data, 'type', 'PANO')
enum_set(camera_data, 'panorama_type', 'EQUIRECTANGULAR')
camera_data.clip_end = 100000
camera = bpy.data.objects.new(camera_data.name, camera_data)
scene.collection.objects.link(camera)
camera.location = (0, 0, 2)
camera.rotation_euler = Matrix(((0, 0, -1), (-1, 0, 0), (0, 1, 0))).to_euler()
scene.camera = camera

settings = dict(seed=SEED, blender=bpy.app.version_string, skyModel=sky.sky_type,
    sunDirectionRuntime=list(SUN_R), sunDirectionBlender=list(SUN_B),
    projection='Top row zenith. phi=(u-.5)*2pi, theta=v*pi; runtime direction=(sin(theta)*cos(phi),cos(theta),sin(theta)*sin(phi)).',
    coordinates='Blender (x,y,z) -> runtime (x,z,-y), right-handed rotation.',
    directSun=dict(energy=3.2, color=list(sun_data.color), angularDiameterDegrees=0.545),
    atmosphereStrength=0.1, cameraSolarDiscRadianceScale=0.25,
    cloudAltitudeMetres=[2400, 3300], cloudDomainMetres=[48000,48000,900],
    samples=scene.cycles.samples, resolution=[scene.render.resolution_x, scene.render.resolution_y],
    previewDisplay='AgX, exposure -1 EV, gamma 1; no glare, grading or downloaded pixels',
    originalContent='Original procedural density field and atmosphere authored for FootballEngine. Blender built-in physical sky/noise/Cycles are tools, not imported assets.',
    solarSeparation='Camera-only sky disc in visible. Disc absent in lighting. SUN lights cloud scattering in both; do not add visible map to IBL. Runtime direct sun remains separate.',
    limitations=['Not runtime validated; no runtime files modified.', 'Cloud field is an art-directed stationary procedural volume, not a meteorological simulation.', '48km finite cloud domain; below-horizon atmosphere is the physical sky model, not stadium geometry.'])
(SOURCE / ('preview-settings.json' if args.preview else 'settings.json')).write_text(json.dumps(settings, indent=2) + '\n')
if not args.preview:
    text = bpy.data.texts.new('author_sky.py')
    text.write(Path(__file__).read_text())
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'daylight-sky.blend'))

for name, disc in [('sky-visible', True), ('sky-lighting', False)]:
    visible.sun_disc = disc
    enum_set(scene.render.image_settings, 'file_format', 'OPEN_EXR')
    enum_set(scene.render.image_settings, 'color_depth', '32')
    enum_set(scene.render.image_settings, 'color_mode', 'RGB')
    enum_set(scene.render.image_settings, 'exr_codec', 'ZIP')
    path = (SOURCE if args.preview else OUT) / (name + ('-preview.exr' if args.preview else '.exr'))
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    enum_set(scene.render.image_settings, 'file_format', 'PNG')
    enum_set(scene.render.image_settings, 'color_depth', '8')
    preview_path = SOURCE / (name + ('-draft.png' if args.preview else '-preview.png'))
    bpy.data.images['Render Result'].save_render(str(preview_path), scene=scene)
    print('OUTPUT', path, hashlib.sha256(path.read_bytes()).hexdigest(), flush=True)

if not args.preview:
    visible.sun_disc = True
    enum_set(camera_data, 'type', 'PERSP')
    camera_data.lens = 24
    scene.render.resolution_x = 1024
    scene.render.resolution_y = 576
    scene.cycles.samples = 32
    for name, azimuth, elevation in [('horizon', 0.0, 16.0),
                                    ('sunward', math.atan2(SUN_B.y, SUN_B.x), 34.0)]:
        e = math.radians(elevation)
        direction = Vector((math.cos(azimuth) * math.cos(e),
                            math.sin(azimuth) * math.cos(e), math.sin(e)))
        camera.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(SOURCE / f'view-{name}.png')
        bpy.ops.render.render(write_still=True)
