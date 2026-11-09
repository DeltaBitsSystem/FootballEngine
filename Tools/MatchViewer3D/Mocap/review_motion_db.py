"""Render the runtime player posed from motion-db frames, as an isolated Blender batch job (never the live scene).

The pose is rebuilt from the database the way the runtime will: root at the ground point with the stored yaw,
pelvis offset in that yaw frame, driven joints from their stored locals, every other joint from its bind local.
Each Blender bone then gets P = C · W · W0⁻¹ · C⁻¹ · B0 in armature space, which does not depend on how the glTF
importer re-orients bone axes, and its basis is solved analytically so no depsgraph update is needed per level.

Usage: blender -b --factory-startup --python review_motion_db.py -- <player.glb> <motion-db.bin> <out.png> clip:frame ...
Tiles are written next to <out.png>; contact_sheet.py joins them outside Blender, whose Python has no imaging library.
"""

import math
import pathlib
import struct
import sys

import bpy
import numpy as np
from mathutils import Matrix

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from build_motion_db import Rig, euler_xyz, quat_to_matrix  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1 :]
glb, database, output = args[0], args[1], pathlib.Path(args[2])
requests = [(item.split(":")[0], int(item.split(":")[1])) for item in args[3:]]


def read_database(path):
    data = pathlib.Path(path).read_bytes()
    if data[:8] != b"FEMODB01":
        raise SystemExit("not a motion database")
    offset = 8
    _, _, joints = struct.unpack_from("<IHH", data, offset)
    offset += 8

    def string():
        nonlocal offset
        (length,) = struct.unpack_from("<H", data, offset)
        offset += 2
        text = data[offset : offset + length].decode("utf-8")
        offset += length
        return text

    names = [string() for _ in range(joints)]
    (count,) = struct.unpack_from("<I", data, offset)
    offset += 4
    clips = {}
    for _ in range(count):
        name, source, family = string(), string(), string()
        (frames,) = struct.unpack_from("<I", data, offset)
        offset += 4
        records = []
        for _ in range(frames):
            head = struct.unpack_from("<6fB", data, offset)
            offset += 25
            quats = np.array(struct.unpack_from(f"<{joints * 4}h", data, offset), float).reshape(joints, 4) / 32767.0
            offset += joints * 8
            records.append((head, quats / np.linalg.norm(quats, axis=1, keepdims=True)))
        clips[name] = (source, records)
    return names, clips


rig = Rig(glb)
names, clips = read_database(database)
driven = {rig.index[name]: i for i, name in enumerate(names)}


def joint_worlds(record):
    (gx, gz, yaw, px, py, pz, _), quats = record
    yaw_rotation = euler_xyz(0.0, yaw, 0.0)
    ground = np.array([gx, 0.0, gz])
    worlds = [None] * len(rig.names)
    for joint in rig.order:
        parent = rig.parent[joint]
        matrix = np.eye(4)
        if parent < 0:
            matrix[:3, :3] = yaw_rotation
            matrix[:3, 3] = ground
        elif rig.names[joint] == "pelvis":
            matrix[:3, :3] = yaw_rotation @ quat_to_matrix(quats[driven[joint]])
            matrix[:3, 3] = ground + yaw_rotation @ np.array([px, py, pz])
        else:
            local = np.eye(4)
            local[:3, :3] = quat_to_matrix(quats[driven[joint]]) if joint in driven else rig.bind_rotation[joint]
            local[:3, 3] = rig.bind_translation[joint]
            matrix = worlds[parent] @ local
        worlds[joint] = matrix
    return worlds


rest = []
for joint in range(len(rig.names)):
    matrix = np.eye(4)
    matrix[:3, :3] = rig.rest_rotation[joint]
    matrix[:3, 3] = rig.rest_position[joint]
    rest.append(matrix)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
armature = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
# Actions imported with the asset would re-key the pose at render time and hide the database pose.
for item in bpy.context.scene.objects:
    item.animation_data_clear()
C = Matrix.Rotation(math.radians(90.0), 4, "X")


def apply(record, offset_x):
    worlds = joint_worlds(record)
    desired = {}
    for joint, name in enumerate(rig.names):
        delta = Matrix((worlds[joint] @ np.linalg.inv(rest[joint])).tolist())
        desired[name] = C @ delta @ C.inverted() @ armature.data.bones[name].matrix_local
    for bone in armature.pose.bones:
        rest_bone = bone.bone
        if bone.name not in desired:
            continue
        if rest_bone.parent is None:
            relative = rest_bone.matrix_local
            parent_pose = Matrix.Identity(4)
        else:
            relative = rest_bone.parent.matrix_local.inverted() @ rest_bone.matrix_local
            parent_pose = desired.get(rest_bone.parent.name, rest_bone.parent.matrix_local)
        bone.matrix_basis = relative.inverted() @ parent_pose.inverted() @ desired[bone.name]
    armature.location = (offset_x, 0.0, 0.0)


scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "MATERIAL"
scene.render.resolution_x = 360
scene.render.resolution_y = 480
camera_data = bpy.data.cameras.new("review")
camera_data.type = "ORTHO"
camera_data.ortho_scale = 2.4
camera = bpy.data.objects.new("review", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
floor = bpy.data.meshes.new("floor")
floor.from_pydata([(-50, -50, 0), (50, -50, 0), (50, 50, 0), (-50, 50, 0)], [], [(0, 1, 2, 3)])
scene.collection.objects.link(bpy.data.objects.new("floor", floor))

tiles = []
for index, (clip, frame) in enumerate(requests):
    source, records = clips[clip]
    record = records[min(frame, len(records) - 1)]
    apply(record, 0.0)
    (gx, gz, yaw, *_), _ = record
    # Side view of the athlete: look along his left-right axis so stride and kick read clearly.
    # glTF (x, y, z) is Blender (x, -z, y); the athlete's left axis R_yaw · +X lands at (cos yaw, sin yaw).
    target_x, target_y = gx, -gz
    side = (math.cos(yaw), math.sin(yaw))
    camera.location = (target_x + side[0] * 6.0, target_y + side[1] * 6.0, 1.0)
    camera.rotation_euler = (math.radians(90.0), 0.0, math.atan2(side[0], -side[1]))
    path = output.with_name(f"{output.stem}-{index:02d}.png")
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    tiles.append((path, f"{clip}:{frame}"))

print("tiles " + " ".join(f"{path}={label}" for path, label in tiles))
