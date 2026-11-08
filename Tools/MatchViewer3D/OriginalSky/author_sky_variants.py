"""Original dusk and night environments, built on the daylight sky's construction (author_sky.py).

blender -b --factory-startup -t 8 --python Tools/MatchViewer3D/OriginalSky/author_sky_variants.py -- --variant dusk [--preview]
blender -b --factory-startup -t 8 --python Tools/MatchViewer3D/OriginalSky/author_sky_variants.py -- --variant night [--preview]

Same projection, coordinates and visible/lighting split as the daylight pair (see README.md). Night adds an
original procedural star field and a warm city/stadium light dome that also under-lights the cloud deck;
floodlights themselves are runtime lights, never baked here.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

ARGS = argparse.ArgumentParser()
ARGS.add_argument('--preview', action='store_true')
ARGS.add_argument('--variant', choices=['dusk', 'night'], required=True)
args = ARGS.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
ROOT = Path(__file__).resolve().parents[3]
SOURCE = Path(__file__).resolve().parent
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/OriginalSky'
OUT.mkdir(parents=True, exist_ok=True)

# elevation/azimuth in degrees (runtime azimuth: from +X toward +Z), sun energy/colour, atmosphere strength,
# cloud coverage threshold (lower = more cloud), seed.
PRESETS = {
    # Low western sun behind the far stand: long shadows across the pitch and warm cloud undersides.
    'dusk': dict(elev=4.5, azim=200.0, sun_energy=1.7, sun_colour=(1.0, 0.70, 0.46), strength=0.1,
                 coverage=0.585, seed=47123, stars=0.0, dome=0.0),
    # Sun 16 degrees below the horizon: the physical sky is effectively black, so the stars and the light
    # dome carry the visible sky; the stadium floodlights are the real light sources at runtime.
    'night': dict(elev=-16.0, azim=200.0, sun_energy=0.0, sun_colour=(1.0, 1.0, 1.0), strength=0.1,
                  coverage=0.56, seed=58231, stars=1.0, dome=1.0),
}
P = PRESETS[args.variant]
SEED = P['seed']
e, a = math.radians(P['elev']), math.radians(P['azim'])
SUN_R = Vector((math.cos(e) * math.cos(a), math.sin(e), math.cos(e) * math.sin(a))).normalized()
SUN_B = Vector((SUN_R.x, -SUN_R.z, SUN_R.y))


def enum_set(owner, key, value):
    try:
        setattr(owner, key, value)
    except (TypeError, ValueError) as exc:
        allowed = {i.identifier for i in owner.bl_rna.properties[key].enum_items}
        raise ValueError(f'{key}: {value} unavailable; RNA={allowed}; {exc}') from exc


scene = bpy.data.scenes.new(f'OriginalSky-{args.variant}-{SEED}')
bpy.context.window.scene = scene
scene.render.engine = 'CYCLES'
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
enum_set(scene.view_settings, 'view_transform', 'AgX')
scene.view_settings.exposure = -1.0 if args.variant == 'dusk' else 5.0

world = bpy.data.worlds.new(f'WORLD-original-{args.variant}')
scene.world = world
world.use_nodes = True
wn, wl = world.node_tree.nodes, world.node_tree.links
wn.clear()


def sky_node(disc):
    s = wn.new('ShaderNodeTexSky')
    types = {i.identifier for i in s.bl_rna.properties['sky_type'].enum_items}
    enum_set(s, 'sky_type', 'MULTIPLE_SCATTERING' if 'MULTIPLE_SCATTERING' in types else 'NISHITA')
    s.sun_elevation = math.asin(max(-1.0, min(1.0, SUN_B.z)))
    s.sun_rotation = math.atan2(SUN_B.x, SUN_B.y)
    s.sun_size = math.radians(0.545)
    s.sun_intensity = 1.0
    s.altitude = 150.0
    for prop, value in (('dust_density', 0.9), ('aerosol_density', 0.9), ('ground_albedo', 0.18)):
        if hasattr(s, prop):
            setattr(s, prop, value)
    s.sun_disc = disc
    return s


lighting_sky, visible_sky = sky_node(False), sky_node(True)
light_path = wn.new('ShaderNodeLightPath')
disc_scale = wn.new('ShaderNodeMath')
disc_scale.operation = 'MULTIPLY'
disc_scale.inputs[1].default_value = 0.25
wl.new(light_path.outputs['Is Camera Ray'], disc_scale.inputs[0])
mix = wn.new('ShaderNodeMixRGB')
wl.new(disc_scale.outputs[0], mix.inputs[0])
wl.new(lighting_sky.outputs['Color'], mix.inputs[1])
wl.new(visible_sky.outputs['Color'], mix.inputs[2])
sky_colour = mix.outputs[0]

# Night extras are emission in the world: they are part of what both the camera and the IBL see.
if P['stars'] or P['dome']:
    geo = wn.new('ShaderNodeTexCoord')
    sep = wn.new('ShaderNodeSeparateXYZ')
    wl.new(geo.outputs['Generated'], sep.inputs[0])
    up = sep.outputs['Z']  # world direction z: +1 zenith, 0 horizon
    clamp_up = wn.new('ShaderNodeClamp')
    wl.new(up, clamp_up.inputs['Value'])
    # Stars: a 3D Voronoi lattice on the unit sphere; a cell's random value decides whether it holds a star
    # and its magnitude. Sizes stay below a pixel at 2048 so stars read as points, not discs.
    vor = wn.new('ShaderNodeTexVoronoi')
    vor.feature = 'F1'
    vor.inputs['Scale'].default_value = 420.0
    vor.inputs['Randomness'].default_value = 1.0
    wl.new(geo.outputs['Generated'], vor.inputs['Vector'])
    near = wn.new('ShaderNodeMath')
    near.operation = 'LESS_THAN'
    near.inputs[1].default_value = 0.035
    wl.new(vor.outputs['Distance'], near.inputs[0])
    rnd = wn.new('ShaderNodeSeparateColor')
    wl.new(vor.outputs['Color'], rnd.inputs[0])
    present = wn.new('ShaderNodeMath')
    present.operation = 'GREATER_THAN'
    present.inputs[1].default_value = 0.82
    wl.new(rnd.outputs['Red'], present.inputs[0])
    mag = wn.new('ShaderNodeMath')
    mag.operation = 'POWER'
    mag.inputs[1].default_value = 6.0
    wl.new(rnd.outputs['Green'], mag.inputs[0])
    star = wn.new('ShaderNodeMath')
    star.operation = 'MULTIPLY'
    wl.new(near.outputs[0], star.inputs[0])
    wl.new(present.outputs[0], star.inputs[1])
    star2 = wn.new('ShaderNodeMath')
    star2.operation = 'MULTIPLY'
    wl.new(star.outputs[0], star2.inputs[0])
    wl.new(mag.outputs[0], star2.inputs[1])
    # Light pollution hides faint stars near the horizon; only the zenith keeps the full field.
    fade = wn.new('ShaderNodeMath')
    fade.operation = 'MULTIPLY'
    wl.new(star2.outputs[0], fade.inputs[0])
    wl.new(clamp_up.outputs[0], fade.inputs[1])
    star_strength = wn.new('ShaderNodeMath')
    star_strength.operation = 'MULTIPLY'
    star_strength.inputs[1].default_value = 0.05 * P['stars']
    wl.new(fade.outputs[0], star_strength.inputs[0])
    # Light dome: warm-neutral glow, strongest at the horizon, from the city and the stadium's own lights.
    dome = wn.new('ShaderNodeMath')
    dome.operation = 'SUBTRACT'
    dome.inputs[0].default_value = 1.0
    wl.new(clamp_up.outputs[0], dome.inputs[1])
    dome_pow = wn.new('ShaderNodeMath')
    dome_pow.operation = 'POWER'
    dome_pow.inputs[1].default_value = 7.0
    wl.new(dome.outputs[0], dome_pow.inputs[0])
    dome_s = wn.new('ShaderNodeMath')
    dome_s.operation = 'MULTIPLY_ADD'
    dome_s.inputs[1].default_value = 0.004 * P['dome']
    dome_s.inputs[2].default_value = 0.0  # the airglow floor is added above the horizon only, via `floor`
    # Below the horizon the dome fades out: that hemisphere is the stadium and the ground, not glowing air.
    below = wn.new('ShaderNodeMath')
    below.operation = 'MULTIPLY_ADD'
    below.inputs[1].default_value = 12.0
    below.inputs[2].default_value = 1.0
    below.use_clamp = True
    wl.new(up, below.inputs[0])
    dome_gate = wn.new('ShaderNodeMath')
    dome_gate.operation = 'MULTIPLY'
    wl.new(dome_pow.outputs[0], dome_gate.inputs[0])
    wl.new(below.outputs[0], dome_gate.inputs[1])
    wl.new(dome_gate.outputs[0], dome_s.inputs[0])
    floor = wn.new('ShaderNodeMath')
    floor.operation = 'MULTIPLY_ADD'
    floor.inputs[1].default_value = 0.00035 * P['dome']  # airglow so the zenith is never pure black
    wl.new(below.outputs[0], floor.inputs[0])
    wl.new(dome_s.outputs[0], floor.inputs[2])
    dome_s = floor
    dome_col = wn.new('ShaderNodeMixRGB')
    dome_col.blend_type = 'MULTIPLY'
    dome_col.inputs[0].default_value = 1.0
    dome_col.inputs[1].default_value = (1.0, 0.82, 0.62, 1)
    dome_comb = wn.new('ShaderNodeCombineColor')
    for i in range(3):
        wl.new(dome_s.outputs[0], dome_comb.inputs[i])
    wl.new(dome_comb.outputs[0], dome_col.inputs[2])
    star_comb = wn.new('ShaderNodeCombineColor')
    for i in range(3):
        wl.new(star_strength.outputs[0], star_comb.inputs[i])
    add1 = wn.new('ShaderNodeMixRGB')
    add1.blend_type = 'ADD'
    add1.inputs[0].default_value = 1.0
    wl.new(sky_colour, add1.inputs[1])
    wl.new(dome_col.outputs[0], add1.inputs[2])
    add2 = wn.new('ShaderNodeMixRGB')
    add2.blend_type = 'ADD'
    add2.inputs[0].default_value = 1.0
    wl.new(add1.outputs[0], add2.inputs[1])
    wl.new(star_comb.outputs[0], add2.inputs[2])
    sky_colour = add2.outputs[0]

bg = wn.new('ShaderNodeBackground')
bg.inputs['Strength'].default_value = P['strength']
out = wn.new('ShaderNodeOutputWorld')
wl.new(sky_colour, bg.inputs['Color'])
wl.new(bg.outputs[0], out.inputs['Surface'])
# The world strength scales the atmosphere only; emission terms were authored in final radiance, so undo it.
if P['stars'] or P['dome']:
    bg.inputs['Strength'].default_value = 1.0
    for s in (lighting_sky, visible_sky):
        s.sun_intensity = P['strength']

if P['sun_energy'] > 0:
    sun = bpy.data.objects.new('SUN-cloud-scattering-only', bpy.data.lights.new('sun', 'SUN'))
    sun.data.energy = P['sun_energy']
    sun.data.color = P['sun_colour']
    sun.data.angle = math.radians(0.545)
    scene.collection.objects.link(sun)
    sun.rotation_euler = (-SUN_B).to_track_quat('-Z', 'Y').to_euler()

bpy.ops.mesh.primitive_cube_add(size=2, location=(0, 0, 2850))
cloud = bpy.context.object
cloud.scale = (24000, 24000, 450)
mat = bpy.data.materials.new(f'MAT-original-cloud-{SEED}')
mat.use_nodes = True
cloud.data.materials.append(mat)
n, l = mat.node_tree.nodes, mat.node_tree.links
n.clear()


def mnode(op, a, b):
    v = n.new('ShaderNodeMath')
    v.operation = op
    for i, item in enumerate((a, b)):
        if isinstance(item, (int, float)):
            v.inputs[i].default_value = item
        else:
            l.new(item, v.inputs[i])
    return v.outputs[0]


g = n.new('ShaderNodeNewGeometry')
mp = n.new('ShaderNodeVectorMath')
mp.operation = 'MULTIPLY'
mp.inputs[1].default_value = (1 / 1900, 1 / 1400, 1 / 650)
l.new(g.outputs['Position'], mp.inputs[0])
noise = n.new('ShaderNodeTexNoise')
noise.noise_dimensions = '4D'
noise.inputs['W'].default_value = SEED / 1000
noise.inputs['Detail'].default_value = 5.0
noise.inputs['Roughness'].default_value = 0.62
noise.inputs['Distortion'].default_value = 0.22
l.new(mp.outputs[0], noise.inputs['Vector'])
sz = n.new('ShaderNodeSeparateXYZ')
l.new(g.outputs['Position'], sz.inputs[0])
h = mnode('MULTIPLY', mnode('POWER', mnode('ABSOLUTE', mnode('DIVIDE', mnode('SUBTRACT', sz.outputs['Z'], 2810), 450), 0), 2), 0.30)
density = mnode('MULTIPLY', mnode('MAXIMUM', mnode('SUBTRACT', mnode('SUBTRACT', noise.outputs['Fac'], h), P['coverage']), 0), 0.055)
vol = n.new('ShaderNodeVolumePrincipled')
vol.inputs['Color'].default_value = (0.98, 0.99, 1.0, 1)
vol.inputs['Anisotropy'].default_value = 0.55
l.new(density, vol.inputs['Density'])
if P['dome']:
    # Clouds over a lit city glow faintly orange from below; emission stands in for that diffuse up-light.
    vol.inputs['Emission Color'].default_value = (1.0, 0.78, 0.55, 1)
    l.new(mnode('MULTIPLY', density, 0.0012), vol.inputs['Emission Strength'])
mo = n.new('ShaderNodeOutputMaterial')
l.new(vol.outputs['Volume'], mo.inputs['Volume'])

cam_data = bpy.data.cameras.new('CAM-runtime-Y-up-equirectangular')
enum_set(cam_data, 'type', 'PANO')
enum_set(cam_data, 'panorama_type', 'EQUIRECTANGULAR')
cam_data.clip_end = 100000
camera = bpy.data.objects.new(cam_data.name, cam_data)
scene.collection.objects.link(camera)
camera.location = (0, 0, 2)
camera.rotation_euler = Matrix(((0, 0, -1), (-1, 0, 0), (0, 1, 0))).to_euler()
scene.camera = camera

settings = dict(variant=args.variant, seed=SEED, blender=bpy.app.version_string, preset=P,
                sunDirectionRuntime=list(SUN_R),
                projection='Same as the daylight pair: top row zenith; phi=(u-.5)*2pi, theta=v*pi.',
                solarSeparation='Camera-only disc in visible, none in lighting; runtime keeps one directional sun '
                                '(dusk) or none (night, floodlights only).',
                originalContent='Procedural atmosphere, clouds, stars and light dome authored for FootballEngine; '
                                'Blender physical sky, noise and Cycles are tools, not imported assets.')
suffix = '-preview' if args.preview else ''
(SOURCE / f'{args.variant}-settings{suffix}.json').write_text(json.dumps(settings, indent=2) + '\n')
if not args.preview:
    text = bpy.data.texts.new('author_sky_variants.py')
    text.write(Path(__file__).read_text())
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / f'{args.variant}-sky.blend'))

for name, disc in (('sky-visible', True), ('sky-lighting', False)):
    visible_sky.sun_disc = disc
    enum_set(scene.render.image_settings, 'file_format', 'OPEN_EXR')
    enum_set(scene.render.image_settings, 'color_depth', '32')
    enum_set(scene.render.image_settings, 'color_mode', 'RGB')
    enum_set(scene.render.image_settings, 'exr_codec', 'ZIP')
    path = (SOURCE if args.preview else OUT) / f'{name}-{args.variant}{suffix}.exr'
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    enum_set(scene.render.image_settings, 'file_format', 'PNG')
    enum_set(scene.render.image_settings, 'color_depth', '8')
    bpy.data.images['Render Result'].save_render(str(SOURCE / f'{name}-{args.variant}{suffix}.png'), scene=scene)
    print('OUTPUT', path, hashlib.sha256(path.read_bytes()).hexdigest(), flush=True)
