"""Final evidence set for Revision-E: neutral clay under raking light, textured
beauty views, detail close-ups, and an A/B hip comparison against Revision-D."""
import bpy
import sys
from pathlib import Path
from mathutils import Vector

ROOT = Path('/home/pablo/Projects/FootballEngine')
REV = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body'
OUT = REV / 'Revision-E/evidence'
OUT.mkdir(exist_ok=True)


def load(path):
    bpy.ops.wm.open_mainfile(filepath=str(path))
    scene = bpy.context.scene
    scene.frame_set(1)
    body = bpy.data.objects['FINAL_Body_Connected']
    keep = {'FINAL_Body_Connected'}
    for o in scene.objects:
        if o.type == 'MESH':
            ok = o.name.startswith(('GEO_Eyeball', 'GEO_Iris', 'GEO_Pupil', 'GEO_Brow', 'GEO_Nail', 'GEO_Hair'))
            o.hide_render = not (o.name in keep or ok)
            o.hide_set(o.hide_render)
    for o in list(scene.objects):
        if o.type == 'LIGHT':
            bpy.data.objects.remove(o, do_unlink=True)
    world = scene.world or bpy.data.worlds.new('W')
    scene.world = world
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs['Color'].default_value = (.04, .045, .055, 1)
    bg.inputs['Strength'].default_value = .45
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 40
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 700
    scene.render.resolution_y = 900
    cam = scene.camera or bpy.data.objects.new('Cam', bpy.data.cameras.new('Cam'))
    if cam.name not in scene.objects:
        scene.collection.objects.link(cam)
    scene.camera = cam
    return scene, body, cam


def light(scene, loc, energy, size, target=(0, 0, 1.2)):
    d = bpy.data.lights.new('L', 'AREA')
    d.energy = energy
    d.size = size
    l = bpy.data.objects.new('L', d)
    scene.collection.objects.link(l)
    l.location = loc
    l.rotation_euler = (Vector(target) - l.location).to_track_quat('-Z', 'Y').to_euler()
    return l


def shot(scene, cam, name, pos, target, scale, res=(700, 900)):
    cam.location = pos
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = scale
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = str(OUT / (name + '.png'))
    bpy.ops.render.render(write_still=True)


# --- Neutral clay, raking light. No texture, no accessories: this is the
# anatomy check, so nothing may hide a surface defect.
scene, body, cam = load(REV / 'Revision-E/final-body-source.blend')
for o in list(scene.objects):
    if o.type == 'MESH' and o.name != 'FINAL_Body_Connected':
        o.hide_render = True
clay = bpy.data.materials.new('Neutral_clay_evidence')
clay.use_nodes = True
bsdf = next(n for n in clay.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
bsdf.inputs['Base Color'].default_value = (.36, .36, .36, 1)
bsdf.inputs['Roughness'].default_value = .52
body.data.materials.clear()
body.data.materials.append(clay)
light(scene, (3.4, -1.0, 2.1), 380, .9, (0, 0, 1.05))
light(scene, (-2.4, -2.6, 1.2), 45, 2.5, (0, 0, 1.0))
light(scene, (.4, 3.0, 2.4), 90, 2.0, (0, 0, 1.1))
shot(scene, cam, 'neutral-front', (0, -5, .92), (0, 0, .92), 2.02)
shot(scene, cam, 'neutral-profile', (5, 0, .92), (0, 0, .92), 2.02)
shot(scene, cam, 'neutral-back', (0, 5, .92), (0, 0, .92), 2.02)
shot(scene, cam, 'neutral-three', (3.2, -4.6, 1.45), (0, 0, .92), 2.02)
shot(scene, cam, 'neutral-hip-three', (2.4, -2.4, .95), (.09, 0, .88), .72)
shot(scene, cam, 'neutral-head-three', (1.4, -2.0, 1.78), (0, 0, 1.69), .28)
shot(scene, cam, 'neutral-hand', (1.2, -3, 1.05), (.635, 0, .98), .34, (760, 620))
shot(scene, cam, 'neutral-foot', (1.2, -2.6, .35), (.13, 0, .06), .34, (760, 620))

# --- Textured beauty, three-point soft light.
scene, body, cam = load(REV / 'Revision-E/final-body-source.blend')
light(scene, (2.4, -2.6, 2.6), 240, 2.2)
light(scene, (-2.8, -3.2, 1.4), 55, 3.0)
light(scene, (1.2, 3.2, 2.4), 110, 2.0)
shot(scene, cam, 'beauty-front', (0, -5, .92), (0, 0, .92), 2.02)
shot(scene, cam, 'beauty-profile', (5, 0, .92), (0, 0, .92), 2.02)
shot(scene, cam, 'beauty-back', (0, 5, .92), (0, 0, .92), 2.02)
shot(scene, cam, 'beauty-three', (3.2, -4.6, 1.45), (0, 0, .92), 2.02)
shot(scene, cam, 'beauty-head-front', (0, -2, 1.69), (0, 0, 1.69), .26, (700, 780))
shot(scene, cam, 'beauty-head-three', (1.2, -2, 1.76), (0, 0, 1.69), .26, (700, 780))
shot(scene, cam, 'beauty-head-profile', (2, -.8, 1.69), (0, 0, 1.69), .26, (700, 780))
shot(scene, cam, 'beauty-hip-three', (2.4, -2.4, .95), (.09, 0, .88), .72)
shot(scene, cam, 'beauty-hand', (1.2, -3, 1.05), (.635, 0, .98), .34, (760, 620))
shot(scene, cam, 'beauty-foot', (1.2, -2.6, .35), (.13, 0, .06), .34, (760, 620))

# --- A/B: the same hip camera on Revision-D and Revision-E, both in clay.
for tag, path in [('revisionD', REV / 'Revision-D/final-body-source.blend'),
                  ('revisionE', REV / 'Revision-E/final-body-source.blend')]:
    if not path.exists():
        print('MISSING', path)
        continue
    scene, body, cam = load(path)
    for o in list(scene.objects):
        if o.type == 'MESH' and o.name != 'FINAL_Body_Connected':
            o.hide_render = True
    m = bpy.data.materials.new('Clay_AB')
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = (.36, .36, .36, 1)
    b.inputs['Roughness'].default_value = .52
    body.data.materials.clear()
    body.data.materials.append(m)
    light(scene, (3.4, -1.0, 2.1), 380, .9, (0, 0, 1.0))
    light(scene, (-2.4, -2.6, 1.2), 45, 2.5, (0, 0, 1.0))
    light(scene, (.4, 3.0, 2.4), 90, 2.0, (0, 0, 1.1))
    shot(scene, cam, 'ab-hip-' + tag, (2.3, -2.3, .96), (.08, 0, .88), .70)
    shot(scene, cam, 'ab-crotch-' + tag, (0, -3.0, .86), (0, 0, .86), .52, (700, 700))
print('EVIDENCE_OK')
