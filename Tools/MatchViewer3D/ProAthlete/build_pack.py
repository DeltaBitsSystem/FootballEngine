"""Step 4: author the 18 procedural clips on the ProAthlete rig and export the runtime character pack.

blender -b --python-exit-code 1 --python Tools/MatchViewer3D/ProAthlete/build_pack.py
Reads Pro-Athlete/uv.blend and the painted atlas; writes player.glb, player-motion.json (schema 3) and mips
into Pro-Athlete/. The live MatchViewer assets are only replaced by promote.py.
"""
import hashlib, importlib.util, json, sys
from pathlib import Path
import bpy
from mathutils import Quaternion, Vector

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('pa_common', Path(__file__).with_name('common.py'))
C = importlib.util.module_from_spec(spec); spec.loader.exec_module(C)


def load(name, relative):
    # Shared tooling may still be uncommitted in the main checkout; prefer this tree, then the asset root.
    path = next(p for p in (C.ROOT / relative, C.ASSET_ROOT / relative) if p.exists())
    s = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(s); s.loader.exec_module(m)
    return m, path


bpy.ops.wm.open_mainfile(filepath=str(C.OUT / 'uv.blend'))
scene = bpy.context.scene
rig = bpy.data.objects['FINAL_Rig']
MESHES = ['FE Skin', 'FE Eyes', 'FE Hair', 'FE Jersey', 'FE Shorts', 'FE Socks', 'FE Boots']
motion, motion_path = load('pa_motion', 'FootballEngine.Client/Assets/MatchViewer/player_authoring.py')

# The generator's stance targets put the ankle at 0.095 m; this rig's rest ankle includes the boot sole.
ANKLE_SHIFT = rig.data.bones['foot.L'].head_local.z - 0.095
original_leg, original_aim, original_pose, original_swing = motion.solve_leg, motion.aim_bone, motion.pose_clip, motion.pose_swing_arm


def aim(rig, name, head, tail, parent):
    if name.startswith('foot.'):
        rest = rig.data.bones[name]
        # A rigid foot keeps the rest sole orientation; the generator's generic vector would pitch the boot.
        tail = Vector(head) + rest.tail_local - rest.head_local
    return original_aim(rig, name, head, tail, parent)


def leg(rig, side, ankle):
    ankle = Vector(ankle).copy()
    ankle.z += ANKLE_SHIFT
    return original_leg(rig, side, ankle)


def swing(rig, suffix, shoulder_angle, elbow_flexion, abduction=0.065):
    # The athletic torso and loose jersey are wider than the generator's original body; hands clear the hips.
    return original_swing(rig, suffix, shoulder_angle, elbow_flexion, abduction + PACK['extraArmAbduction'])


def pose(rig, name, kind, phase, speed, duration):
    original_pose(rig, name, kind, phase, speed, duration)
    for bone in rig.pose.bones:
        bone.scale = (1, 1, 1)
    for side in C.SIDES:
        for helper, source in C.SUPPORTS:
            rig.pose.bones[f'{helper}.{side}'].rotation_quaternion = Quaternion((1, 0, 0, 0)).slerp(
                rig.pose.bones[f'{source}.{side}'].rotation_quaternion, .5)
    bpy.context.view_layer.update()


PACK = json.loads(C.PARAMS_PATH.read_text())['pack']
motion.aim_bone, motion.solve_leg, motion.pose_clip, motion.pose_swing_arm = aim, leg, pose, swing
for action in list(bpy.data.actions):
    bpy.data.actions.remove(action)
for pb in rig.pose.bones:
    pb.rotation_mode = 'QUATERNION'
scene.render.fps = 60
clips = motion.author_clips(scene, rig)
scene.frame_set(0)

# Runtime takes colour from the atlas; materials only carry identity and a flat base colour.
for m in bpy.data.materials:
    if m.name.startswith('FE '):
        m.use_nodes = True
        for n in list(m.node_tree.nodes):
            if n.type == 'TEX_IMAGE':
                m.node_tree.nodes.remove(n)
# Runtime material identity comes from the atlas tile under each triangle, so the boot sole keeps its trim-tile
# UVs while sharing the boot material: rig_geometry reads the boot as exactly one primitive.
boots = bpy.data.objects['FE Boots']
for poly in boots.data.polygons:
    poly.material_index = 0
while len(boots.data.materials) > 1:
    boots.data.materials.pop(index=len(boots.data.materials) - 1)
bpy.ops.object.select_all(action='DESELECT')
for name in MESHES + ['FINAL_Rig']:
    o = bpy.data.objects[name]
    o.hide_set(False); o.select_set(True)
for o in scene.objects:
    if o.name not in MESHES + ['FINAL_Rig']:
        o.select_set(False)
bpy.context.view_layer.objects.active = rig
glb = C.OUT / 'player.glb'
bpy.ops.export_scene.gltf(filepath=str(glb), export_format='GLB', use_selection=True, use_active_scene=True,
                          export_animations=True, export_skins=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True, export_frame_range=False, export_yup=True, export_apply=False,
                          export_influence_nb=4, export_all_influences=False, export_morph=False,
                          export_texcoords=True, export_normals=True, export_attributes=False, export_image_format='NONE')
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(C.OUT / 'player-source.blend'))

sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
manifest = {
    'schemaVersion': 3, 'rigSchemaVersion': 2,
    'author': 'FootballEngine ProAthlete: MPFB CC0 anatomical base, project-authored rig mapping, kit, hair and atlas pixels',
    'qualityStatus': 'ProAthlete semi-realistic base; native acceptance in progress',
    'atlas': {'columns': 4, 'homeRows': 2, 'materials': C.ATLAS_MATERIALS},
    'coordinates': {'forward': '+Z', 'units': 'metres', 'up': '+Y'},
    'joints': len(rig.data.bones),
    'clips': clips,
    'file': {'path': 'player.glb', 'sha256': sha(glb)},
    'textures': [{'path': n, 'sha256': sha(C.OUT / n)} for n in ('player-albedo.png', 'player-normal-roughness.png')],
}
(C.OUT / 'player-motion.json').write_text(json.dumps(manifest, indent=2, sort_keys=True) + '\n')
mips, _ = load('pa_mips', 'Tools/MatchViewer3D/build_athlete_mips.py')
mips.build(C.OUT)
geometry_mod, _ = load('pa_rig_geometry', 'Tools/MatchViewer3D/MotionRework/rig_geometry.py')
geometry = geometry_mod.derive(glb)
manifest = json.loads((C.OUT / 'player-motion.json').read_text())
manifest.update(schemaVersion=3, rigSchemaVersion=2, rigGeometry=geometry)
(C.OUT / 'player-motion.json').write_text(json.dumps(manifest, indent=2, sort_keys=True) + '\n')
print('PACK_BUILT', json.dumps({'clips': len(clips), 'glbBytes': glb.stat().st_size, 'sockets': geometry['sockets'],
                                'motionSource': str(motion_path)}), flush=True)
