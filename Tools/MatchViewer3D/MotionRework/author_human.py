"""Reproducible original kit refinement on a copy of the Landmark-Cage source.

Run in isolated Blender: blender -b --python-exit-code 1 --python this_file.py.
The source package and interactive Blender scene are never written.
"""
from pathlib import Path
import bpy, bmesh, math, json, hashlib, importlib.util
from mathutils import Vector, Quaternion

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / 'FootballEngine.Client/Assets/MatchViewer'
SOURCE = BASE / 'PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
OUT = BASE / 'PlayerBodyRework/Astra-Human'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE / 'player-runtime-source.blend'))
scene = bpy.context.scene
rig = bpy.data.objects['FINAL_Rig']
scene.frame_set(0)
meshes = [o for o in scene.objects if o.type == 'MESH' and o.parent == rig]
spec = importlib.util.spec_from_file_location('astra_anatomy_form', Path(__file__).with_name('anatomy_form.py'))
anatomy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(anatomy)
anatomy.refine(bpy.data.objects['FINAL_Body_Connected'], meshes)
rig.animation_data_clear()
for bone in rig.pose.bones:
    bone.matrix_basis.identity()
bpy.context.view_layer.objects.active = rig
rig.hide_set(False)
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
for side, sign in [('L', 1), ('R', -1)]:
    shoulder = Vector((sign * .19, 0, 1.455))
    rig.data.edit_bones['clavicle.' + side].head.z = 1.455
    rig.data.edit_bones['clavicle.' + side].tail = shoulder
    for name in ('upper_arm.', 'axilla_support.'):
        rig.data.edit_bones[name + side].head = shoulder
rig.data.edit_bones['neck'].head.z = 1.475
rig.data.edit_bones['neck'].tail.z = 1.605
rig.data.edit_bones['head'].head.z = 1.605
bpy.ops.object.mode_set(mode='OBJECT')

def smoothstep(a, b, value):
    t = max(0., min(1., (value - a) / (b - a)))
    return t * t * (3 - 2 * t)

# The revised shoulder pivot needs a continuous deltoid/axilla weight fan, not the old low-pivot seam.
for obj in meshes:
    if obj.name not in ('FINAL_Body_Connected', 'FE Jersey'):
        continue
    for v in obj.data.vertices:
        x, y, z = v.co
        if .13 < abs(x) < .34 and 1.30 < z < 1.53:
            side = 'L' if x > 0 else 'R'
            mixture = smoothstep(.14, .275, abs(x))
            transition = smoothstep(1.30, 1.34, z) * (1 - smoothstep(.31, .34, abs(x)))
            old = {obj.vertex_groups[g.group].name: g.weight for g in v.groups}
            desired = {'chest': 1 - mixture, 'upper_arm.' + side: mixture * .65, 'axilla_support.' + side: mixture * .35}
            weights = {name: old.get(name, 0) * (1 - transition) + desired.get(name, 0) * transition for name in old.keys() | desired.keys()}
            weights = sorted(((n, w) for n, w in weights.items() if w > 1e-7), key=lambda item: -item[1])[:4]
            total = sum(w for _, w in weights)
            for name in old:
                obj.vertex_groups[name].remove([v.index])
            for name, weight in weights:
                obj.vertex_groups[name].add([v.index], weight / total, 'REPLACE')

def smooth_boundary(mesh):
    bm = bmesh.new()
    bm.from_mesh(mesh)
    boundary = [v for v in bm.verts if v.is_boundary]
    # Extracted anatomical shells have stair-stepped cuts; averaging only their edge loops retains the fitted surface.
    for _ in range(5):
        targets = {}
        for v in boundary:
            adjacent = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
            if len(adjacent) == 2:
                targets[v] = v.co.lerp((adjacent[0].co + adjacent[1].co) * .5, .65)
        for v, p in targets.items():
            v.co = p
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()

for obj in meshes:
    if obj.name in ('FE Jersey', 'FE Shorts', 'FE Socks'):
        if obj.name == 'FE Jersey':
            bm = bmesh.new()
            bm.from_mesh(obj.data)
            for side, sign in [('L', 1), ('R', -1)]:
                bone = rig.data.bones['upper_arm.' + side]
                axis = (bone.tail_local - bone.head_local).normalized()
                cut = bone.head_local.lerp(bone.tail_local, .52)
                verts = [v for v in bm.verts if v.co.x * sign > .24]
                selected = set(verts)
                edges = [e for e in bm.edges if all(v in selected for v in e.verts)]
                faces = [f for f in bm.faces if all(v in selected for v in f.verts)]
                bmesh.ops.bisect_plane(bm, geom=verts + edges + faces, dist=.00001,
                    plane_co=cut, plane_no=axis, clear_outer=True, clear_inner=False)
            bm.to_mesh(obj.data)
            bm.free()
        smooth_boundary(obj.data)
        for v in obj.data.vertices:
            x, y, z = v.co
            if obj.name == 'FE Jersey':
                # Fabric bridges the abdominal waist rather than reproducing every muscular hollow.
                waist = math.exp(-((z - 1.13) / .19) ** 2)
                torso = max(0., min(1., (.25 - abs(x)) / .07))
                v.co.x += math.copysign(.020 * waist * torso, x)
                v.co.y += math.copysign(.014 * waist * torso, y)
                ease = .005 + .004 * (1. - torso)
                fold = .0023 * math.sin(z * 77 + x * 18) * waist * torso
                v.co += v.normal * (ease + fold)
            elif obj.name == 'FE Shorts':
                hem = max(0., min(1., (1.02 - z) / .26))
                centre = math.copysign(.10, x)
                radial = Vector((x - centre, y + .01, 0))
                if radial.length > .001:
                    v.co += radial.normalized() * (.007 + .014 * hem)
                v.co.z -= .035 * hem ** 2
                v.co += v.normal * (.002 * math.sin(math.atan2(y + .01, x - centre) * 9 + z * 6) * hem)
            else:
                v.co += v.normal * .001
        obj.data.update()
        # Independent cloth rims prevent paper-thin silhouettes at neck, cuff and hem.
        bpy.context.view_layer.objects.active = obj
        solid = obj.modifiers.new('Original stitched cloth thickness', 'SOLIDIFY')
        solid.thickness = .0015
        solid.offset = -1.
        bpy.ops.object.modifier_move_up(modifier=solid.name)
        bpy.ops.object.modifier_apply(modifier=solid.name)
        slot = {'FE Jersey': 0, 'FE Shorts': 1, 'FE Socks': 2}[obj.name]
        uv = obj.data.uv_layers.active
        for face in obj.data.polygons:
            coords = []
            for li in face.loop_indices:
                p = obj.data.vertices[obj.data.loops[li].vertex_index].co
                if slot == 0:
                    u = .5 + math.atan2(p.x, -p.y) / math.tau
                    v = (p.z - .99) / .53
                else:
                    centre = math.copysign(.10, p.x)
                    u = (.5 + math.atan2(p.x - centre, -p.y) / math.tau) * .48 + (.51 if p.x > 0 else .01)
                    v = (p.z - (.69 if slot == 1 else .10)) / (.36 if slot == 1 else .40)
                coords.append((li, u, v))
            if slot == 0 and max(c[1] for c in coords) - min(c[1] for c in coords) > .5:
                coords = [(li, 1. if u < .5 else u, v) for li, u, v in coords]
            for li, u, v in coords:
                uv.data[li].uv = ((slot + max(.008, min(.992, u))) / 4, .75 + max(.008, min(.992, v)) / 4)
    for p in obj.data.polygons:
        p.use_smooth = True

# All authoring pixels come from our existing baked skin and the companion seeded textile generator.
for mat in {m for o in meshes for m in o.data.materials if m}:
    if mat.use_nodes:
        for n in mat.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                name = 'player-normal-roughness.png' if 'normal' in n.image.name.lower() else 'player-albedo.png'
                image = bpy.data.images.load(str(OUT / name), check_existing=False)
                if name == 'player-normal-roughness.png':
                    image.colorspace_settings.name = 'Non-Color'
                image.pack()
                n.image = image
for obj in bpy.context.selected_objects:
    obj.select_set(False)
rig.hide_set(False)
rig.select_set(True)
for obj in meshes:
    obj.hide_set(False)
    obj.select_set(True)
bpy.context.view_layer.objects.active = rig
# Reuse the project's original motion generator with the revised bind geometry.
spec = importlib.util.spec_from_file_location('astra_original_motion', BASE / 'player_authoring.py')
motion = importlib.util.module_from_spec(spec)
spec.loader.exec_module(motion)
original_pose, original_leg, original_arm, original_aim = motion.pose_clip, motion.solve_leg, motion.solve_arm, motion.aim_bone
def aim(rig, name, head, tail, parent):
    if name.startswith('foot.'):
        rest = rig.data.bones[name]
        tail = head + rest.tail_local - rest.head_local
    return original_aim(rig, name, head, tail, parent)
def leg(rig, side, ankle):
    if side == 'L':
        rig.pose.bones['pelvis'].location.y -= .01173567
        bpy.context.view_layer.update()
    ankle = ankle.copy()
    ankle.z += rig.data.bones['foot.' + side].head_local.z - .08826446 - .01173567
    return original_leg(rig, side, ankle)
def arm(rig, side, wrist):
    wrist = wrist.copy()
    wrist.z -= .01173567
    return original_arm(rig, side, wrist)
def pose(rig, name, kind, phase, speed, duration):
    original_pose(rig, name, kind, phase, speed, duration)
    for bone in rig.pose.bones:
        bone.scale = (1, 1, 1)
    for side in ('L', 'R'):
        for helper, source in [('hip_support', 'thigh'), ('knee_support', 'shin'), ('elbow_support', 'forearm'), ('axilla_support', 'upper_arm')]:
            rig.pose.bones[helper + '.' + side].rotation_quaternion = Quaternion((1, 0, 0, 0)).slerp(rig.pose.bones[source + '.' + side].rotation_quaternion, .5)
    bpy.context.view_layer.update()
motion.aim_bone, motion.solve_leg, motion.solve_arm, motion.pose_clip = aim, leg, arm, pose
# These datablocks belong only to the isolated copy; their originals remain in the source blend.
for action in list(bpy.data.actions):
    bpy.data.actions.remove(action)
scene.render.fps = 60
clips = motion.author_clips(scene, rig)
scene.frame_set(0)
options = bpy.ops.export_scene.gltf.get_rna_type().properties
print('EXPORT_ENUMS', {key: [i.identifier for i in options[key].enum_items_static] for key in ('export_format', 'export_animation_mode')})
bpy.ops.export_scene.gltf(filepath=str(OUT / 'player.glb'), export_format='GLB', use_selection=True,
    use_active_scene=True, export_animations=True, export_skins=True, export_animation_mode='ACTIONS',
    export_force_sampling=True, export_frame_range=False, export_yup=True, export_apply=False,
    export_influence_nb=4, export_all_influences=False)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'human-editable.blend'))
manifest = json.loads((BASE / 'player-motion.json').read_text())
manifest['file']['sha256'] = hashlib.sha256((OUT / 'player.glb').read_bytes()).hexdigest()
manifest['clips'] = clips
manifest['qualityStatus'] = 'Astra original kit refinement; native acceptance in progress'
for item in manifest['textures']:
    item['sha256'] = hashlib.sha256((OUT / item['path']).read_bytes()).hexdigest()
(OUT / 'player-motion.json').write_text(json.dumps(manifest, indent=2))
(OUT / 'authoring.json').write_text(json.dumps({'source': str(SOURCE.relative_to(ROOT)),
    'sourceSha256': hashlib.sha256((SOURCE / 'player-runtime-source.blend').read_bytes()).hexdigest(),
    'seed': 20261031, 'originalContent': 'Existing project Landmark-Cage anatomy, rig and actions; original fitted cloth and seeded textile pixels',
    'meshes': {o.name: len(o.data.vertices) for o in meshes}}, indent=2))
print('ASTRA_HUMAN_EXPORTED', OUT, flush=True)
