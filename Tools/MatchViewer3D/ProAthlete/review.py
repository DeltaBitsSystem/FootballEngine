"""Review renders of a ProAthlete stage blend: rest views, face and deformation stress poses.

FE_REVIEW_BLEND=<blend> FE_REVIEW_OUT=<dir> blender -b --python-exit-code 1 --python review.py
Never saves the reviewed blend. Renders are EEVEE previews, not runtime evidence.
"""
import math, os
import bpy
from mathutils import Vector, Quaternion

SRC, OUT = os.environ['FE_REVIEW_BLEND'], os.environ['FE_REVIEW_OUT']
POSES = os.environ.get('FE_REVIEW_POSES', 'rest,stress').split(',')
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=SRC)
sc = bpy.context.scene
rig = bpy.data.objects['FINAL_Rig']
if rig.animation_data:
    rig.animation_data.action = None
for o in sc.objects:
    if o.type in ('LIGHT', 'CAMERA'):
        bpy.data.objects.remove(o, do_unlink=True)
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 640, 960
sc.view_settings.view_transform = 'AgX'
w = bpy.data.worlds.new('review'); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes['Background']; bg.inputs[0].default_value = (.32, .36, .42, 1); bg.inputs[1].default_value = .6
def light(kind, energy, rot, size=1):
    l = bpy.data.objects.new(kind, bpy.data.lights.new(kind, kind)); sc.collection.objects.link(l)
    l.data.energy = energy; l.rotation_euler = [math.radians(a) for a in rot]
    if kind == 'AREA': l.data.size = size
    return l
light('SUN', 3.2, (48, 0, 28))
rim = light('SUN', 1.2, (60, 0, 200))
floor = bpy.data.objects.new('floor', bpy.data.meshes.new('floor')); sc.collection.objects.link(floor)
floor.data.from_pydata([(-4, -4, 0), (4, -4, 0), (4, 4, 0), (-4, 4, 0)], [], [(0, 1, 2, 3)])
fm = bpy.data.materials.new('floor'); fm.use_nodes = True
fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.12, .2, .1, 1); floor.data.materials.append(fm)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
ATLAS = os.environ.get('FE_REVIEW_ATLAS')
if ATLAS:
    # Runtime normals use glTF V (down); Blender's normal-map node expects +Y along Blender V, so green flips.
    alb = bpy.data.images.load(os.path.join(ATLAS, 'player-albedo.png'))
    det = bpy.data.images.load(os.path.join(ATLAS, 'player-normal-roughness.png')); det.colorspace_settings.name = 'Non-Color'
    det.alpha_mode = 'CHANNEL_PACKED'
    for m in bpy.data.materials:
        if not m.name.startswith('FE '):
            continue
        m.use_nodes = True
        nt = m.node_tree; nt.nodes.clear()
        out = nt.nodes.new('ShaderNodeOutputMaterial'); bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
        uv = nt.nodes.new('ShaderNodeUVMap'); uv.uv_map = 'Atlas'
        ta = nt.nodes.new('ShaderNodeTexImage'); ta.image = alb
        td = nt.nodes.new('ShaderNodeTexImage'); td.image = det
        sep = nt.nodes.new('ShaderNodeSeparateColor'); inv = nt.nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1
        comb = nt.nodes.new('ShaderNodeCombineColor'); nmap = nt.nodes.new('ShaderNodeNormalMap'); nmap.uv_map = 'Atlas'
        L = nt.links.new
        L(uv.outputs['UV'], ta.inputs['Vector']); L(uv.outputs['UV'], td.inputs['Vector'])
        L(ta.outputs['Color'], bsdf.inputs['Base Color'])
        L(td.outputs['Color'], sep.inputs['Color']); L(sep.outputs[1], inv.inputs[1])
        L(sep.outputs[0], comb.inputs[0]); L(inv.outputs[0], comb.inputs[1]); L(sep.outputs[2], comb.inputs[2])
        L(comb.outputs['Color'], nmap.inputs['Color']); L(nmap.outputs['Normal'], bsdf.inputs['Normal'])
        L(td.outputs['Alpha'], bsdf.inputs['Roughness'])
        if m.name == 'FE skin':
            bsdf.inputs['Subsurface Weight'].default_value = .15
            bsdf.inputs['Subsurface Radius'].default_value = (1, .35, .2)
        L(bsdf.outputs['BSDF'], out.inputs['Surface'])
# Stage blends before the bake carry blank materials; flat preview colours make the forms readable.
PREVIEW = {'jersey': (.55, .04, .05), 'shorts': (.02, .03, .08), 'socks': (.55, .04, .05), 'skin': (.62, .40, .30),
           'hair': (.05, .035, .025), 'boots': (.02, .02, .02), 'trim': (.85, .85, .82), 'eyes': (.85, .85, .85)}
for m in bpy.data.materials:
    key = m.name.replace('FE ', '')
    if key in PREVIEW and m.use_nodes and not any(n.type == 'TEX_IMAGE' for n in m.node_tree.nodes):
        p = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        p.inputs['Base Color'].default_value = (*PREVIEW[key], 1)
        p.inputs['Roughness'].default_value = .45 if key == 'skin' else .8

def shot(name, loc, target, lens):
    cam.location = Vector(loc); cam.data.lens = lens
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(OUT, name + '.png')
    bpy.ops.render.render(write_still=True)

def reset():
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()

def rot(name, axis, deg):
    pb = rig.pose.bones[name]
    pb.rotation_quaternion = Quaternion(axis, math.radians(deg)) @ pb.rotation_quaternion
    for s in ('L', 'R'):
        for helper, source in (('hip_support', 'thigh'), ('knee_support', 'shin'), ('elbow_support', 'forearm'), ('axilla_support', 'upper_arm')):
            rig.pose.bones[f'{helper}.{s}'].rotation_quaternion = Quaternion().slerp(rig.pose.bones[f'{source}.{s}'].rotation_quaternion, .5)

if 'rest' in POSES:
    reset()
    shot('front', (0, -6.2, 1.0), (0, 0, .92), 50)
    shot('profile', (6.2, 0, 1.0), (0, 0, .92), 50)
    shot('three-quarter', (3.8, -4.8, 1.25), (0, 0, .92), 50)
    head = rig.data.bones['head'].head_local + Vector((0, -.03, .09))
    shot('face', head + Vector((.32, -1.05, .02)), head, 85)
    shot('face-profile', head + Vector((1.1, -.02, .02)), head, 85)
    shot('back', (1.5, 5.8, 1.1), (0, 0, .92), 50)
if 'clips' in POSES:
    rig.animation_data_create()
    for clip, phase in (('idle', .5), ('run', .25), ('sprint', .6), ('shot_right', .5), ('tackle_left', .5), ('keeper_ready', .5), ('pass_left', .5), ('throw_in', .5)):
        action = bpy.data.actions[clip]
        rig.animation_data.action = action
        if hasattr(rig.animation_data, 'action_slot') and action.slots:
            rig.animation_data.action_slot = action.slots[0]
        start, end = action.frame_range
        sc.frame_set(int(start + (end - start) * phase))
        shot('clip-' + clip, (3.2, -4.0, 1.2), (0, 0, .85), 50)
    rig.animation_data.action = None
    sc.frame_set(0)
if 'stress' in POSES:
    reset()
    for s, sign in (('L', 1), ('R', -1)):
        rot(f'upper_arm.{s}', (0, 1, 0), -70 * sign)
        rot(f'forearm.{s}', (1, 0, 0), 60)
    shot('stress-arms', (2.5, -4.5, 1.3), (0, 0, 1.1), 50)
    reset()
    rot('thigh.L', (1, 0, 0), 70); rot('shin.L', (1, 0, 0), -100)
    rot('thigh.R', (1, 0, 0), -25); rot('shin.R', (1, 0, 0), -30)
    rot('upper_arm.L', (1, 0, 0), -35); rot('upper_arm.R', (1, 0, 0), 35)
    shot('stress-stride', (4.6, -2.0, 1.0), (0, 0, .85), 50)
