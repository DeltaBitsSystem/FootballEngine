"""FootballEngine-owned anatomical mesh, canonical rig and authored motion curves.

All dimensions are metres. Blender uses Z up and -Y forward; the glTF exporter
produces Y up and +Z forward. Clip roots remain in place: authoritative football
movement is never imported from an animation. This is an editable first corpus,
not a claim of scanned-human or motion-capture quality.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

FPS = 60
SEGMENTS = 24
SCENE_NAME = "FootballEngine Player Authoring"
MATERIALS = {
    "jersey": (0.82, 0.84, 0.78),
    "shorts": (0.025, 0.03, 0.04),
    "socks": (0.82, 0.84, 0.78),
    "skin": (0.52, 0.265, 0.145),
    "hair": (0.025, 0.016, 0.012),
    "boots": (0.012, 0.016, 0.025),
    "trim": (0.56, 0.015, 0.023),
    "eyes": (0.009, 0.007, 0.006),
}


def enum_value(properties, key, requested):
    prop = properties[key]
    values = [item.identifier for item in prop.enum_items]
    # Some Blender dynamic enums are empty in RNA: their assignment is checked by Blender.
    if values and requested not in values:
        raise ValueError(f"{key}: {requested!r} not supported; available: {values}")
    return requested


def material(name, color):
    result = bpy.data.materials.new("FE Player " + name)
    result.use_nodes = True
    shader = next(node for node in result.node_tree.nodes if node.type == "BSDF_PRINCIPLED")
    shader.inputs["Base Color"].default_value = (*color, 1.0)
    shader.inputs["Roughness"].default_value = 0.78
    result.diffuse_color = (*color, 1.0)
    return result


class MeshAuthor:
    def __init__(self, scene):
        self.scene = scene
        self.vertices = []
        self.faces = []
        self.material_indices = []
        self.weights = []
        self.material_names = list(MATERIALS)

    def loft(self, rings, material_name, influences, basis=None, segments=SEGMENTS, caps=True):
        """Contour rings support deliberate anatomical shape and joint topology.

        rings: (centre, transverse radius, depth radius). influences is a function
        returning normalized bone weights per ring; no automatic heat weighting.
        """
        basis = basis or (Vector((1, 0, 0)), Vector((0, 1, 0)))
        offset = len(self.vertices)
        for index, (centre, width, depth) in enumerate(rings):
            centre = Vector(centre)
            weights = influences(index, len(rings))
            for segment in range(segments):
                angle = segment * math.tau / segments
                self.vertices.append(tuple(centre + basis[0] * (width * math.cos(angle)) + basis[1] * (depth * math.sin(angle))))
                self.weights.append(weights)
        for ring in range(len(rings) - 1):
            for segment in range(segments):
                a = offset + ring * segments + segment
                b = offset + ring * segments + (segment + 1) % segments
                c = b + segments
                d = a + segments
                self.faces.append((a, b, c, d))
                self.material_indices.append(self.material_names.index(material_name))
        if caps:
            self.faces.append(tuple(offset + index for index in reversed(range(segments))))
            self.material_indices.append(self.material_names.index(material_name))
            last = offset + (len(rings) - 1) * segments
            self.faces.append(tuple(last + index for index in range(segments)))
            self.material_indices.append(self.material_names.index(material_name))

    def segment(self, start, end, contours, material_name, bone):
        start, end = Vector(start), Vector(end)
        direction = (end - start).normalized()
        transverse = Vector((1, 0, 0))
        if abs(direction.dot(transverse)) > 0.9:
            transverse = Vector((0, 0, 1))
        transverse = (transverse - direction * transverse.dot(direction)).normalized()
        depth = direction.cross(transverse).normalized()
        rings = [(start.lerp(end, t), width, radius) for t, width, radius in contours]
        self.loft(rings, material_name, lambda *_: {bone: 1.0}, (transverse, depth))

    def finish(self, rig):
        mesh = bpy.data.meshes.new("FE Anatomical Player Mesh")
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        obj = bpy.data.objects.new("FE Player", mesh)
        self.scene.collection.objects.link(obj)
        for name in self.material_names:
            mesh.materials.append(material(name, MATERIALS[name]))
        for polygon, index in zip(mesh.polygons, self.material_indices):
            polygon.material_index = index
            polygon.use_smooth = True
        groups = {bone.name: obj.vertex_groups.new(name=bone.name) for bone in rig.data.bones}
        for vertex, weights in enumerate(self.weights):
            total = sum(weights.values())
            if total <= 0 or len(weights) > 4:
                raise ValueError(f"Invalid skin weights at vertex {vertex}")
            for name, weight in weights.items():
                groups[name].add([vertex], weight / total, "REPLACE")
        modifier = obj.modifiers.new("FE Skin", "ARMATURE")
        modifier.object = rig
        obj.parent = rig
        return obj


def make_rig(scene):
    data = bpy.data.armatures.new("FE Canonical Skeleton v1")
    rig = bpy.data.objects.new("FE Skeleton", data)
    scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    definitions = [
        ("root", (0, 0, 0), (0, 0, 0.20), None),
        ("pelvis", (0, 0, 0.97), (0, 0, 1.11), "root"),
        ("spine", (0, 0, 1.11), (0, 0, 1.33), "pelvis"),
        ("chest", (0, 0, 1.33), (0, 0, 1.52), "spine"),
        ("neck", (0, 0, 1.52), (0, 0, 1.65), "chest"),
        ("head", (0, 0, 1.65), (0, 0, 1.87), "neck"),
    ]
    for sign, suffix in [(-1, "R"), (1, "L")]:
        definitions.extend([
            (f"clavicle.{suffix}", (0, 0, 1.48), (sign * 0.19, 0, 1.48), "chest"),
            (f"upper_arm.{suffix}", (sign * 0.19, 0, 1.48), (sign * 0.42, 0, 1.29), f"clavicle.{suffix}"),
            (f"forearm.{suffix}", (sign * 0.42, 0, 1.29), (sign * 0.60, 0, 1.12), f"upper_arm.{suffix}"),
            (f"hand.{suffix}", (sign * 0.60, 0, 1.12), (sign * 0.67, 0, 1.06), f"forearm.{suffix}"),
            (f"thigh.{suffix}", (sign * 0.10, 0, 0.97), (sign * 0.10, -0.015, 0.53), "pelvis"),
            (f"shin.{suffix}", (sign * 0.10, -0.015, 0.53), (sign * 0.10, 0, 0.095), f"thigh.{suffix}"),
            (f"foot.{suffix}", (sign * 0.10, 0, 0.095), (sign * 0.10, -0.16, 0.065), f"shin.{suffix}"),
            (f"toe.{suffix}", (sign * 0.10, -0.16, 0.065), (sign * 0.10, -0.23, 0.045), f"foot.{suffix}"),
        ])
    for name, head, tail, parent in definitions:
        bone = data.edit_bones.new(name)
        bone.head, bone.tail = head, tail
        if parent:
            bone.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    for bone in rig.pose.bones:
        bone.rotation_mode = "QUATERNION"
    rig.show_in_front = True
    rig["fe_rig_schema"] = 1
    rig["fe_forward"] = "-Y"
    rig["fe_units"] = "metres"
    return rig


def build_mesh(scene, rig):
    mesh = MeshAuthor(scene)
    torso = [(0.98, 0.137, 0.101), (1.06, 0.14, 0.098), (1.16, 0.148, 0.102),
             (1.28, 0.172, 0.112), (1.39, 0.195, 0.12), (1.47, 0.192, 0.108),
             (1.52, 0.14, 0.092), (1.56, 0.062, 0.055)]
    def torso_weights(index, count):
        t = index / (count - 1)
        if t < 0.3:
            return {"pelvis": 1 - t / 0.3, "spine": t / 0.3}
        if t < 0.7:
            return {"spine": 1 - (t - 0.3) / 0.4, "chest": (t - 0.3) / 0.4}
        return {"chest": 1.0}
    mesh.loft([((0, 0, z), width, depth) for z, width, depth in torso], "jersey", torso_weights)
    mesh.loft([((0, 0, z), width, depth) for z, width, depth in
               [(0.89, 0.153, 0.10), (0.96, 0.155, 0.108), (1.04, 0.141, 0.102)]], "shorts", lambda *_: {"pelvis": 1.0})
    mesh.loft([((0, 0, z), width, depth) for z, width, depth in
               [(1.54, 0.058, 0.055), (1.60, 0.057, 0.056), (1.66, 0.051, 0.05)]], "skin", lambda *_: {"neck": 1.0})
    # Jaw, cheek, brow and cranium contours make an editable head rather than a sphere.
    mesh.loft([((0, y, z), width, depth) for z, y, width, depth in
               [(1.64, -0.013, 0.031, 0.04), (1.68, -0.013, 0.063, 0.066),
                (1.72, 0, 0.082, 0.08), (1.77, 0.005, 0.089, 0.085),
                (1.82, 0.008, 0.087, 0.085), (1.87, 0.012, 0.074, 0.075),
                (1.91, 0.012, 0.049, 0.05), (1.93, 0.012, 0.005, 0.007)]], "skin", lambda *_: {"head": 1.0})
    mesh.loft([((0, y, z), width, depth) for z, y, width, depth in
               [(1.82, 0.014, 0.09, 0.085), (1.87, 0.014, 0.078, 0.079),
                (1.91, 0.014, 0.053, 0.055), (1.936, 0.014, 0.003, 0.004)]], "hair", lambda *_: {"head": 1.0})
    mesh.segment((0, -0.082, 1.785), (0, -0.115, 1.746),
                 [(0, 0.009, 0.008), (0.5, 0.012, 0.009), (1, 0.017, 0.01)], "skin", "head")
    for sign, suffix in [(-1, "R"), (1, "L")]:
        shoulder, elbow, wrist = (sign * 0.19, 0, 1.48), (sign * 0.42, 0, 1.29), (sign * 0.60, 0, 1.12)
        mesh.segment(shoulder, Vector(shoulder).lerp(Vector(elbow), 0.43),
                     [(0, 0.055, 0.057), (0.25, 0.060, 0.062), (0.75, 0.059, 0.060), (1, 0.052, 0.054)], "jersey", f"upper_arm.{suffix}")
        # A continuous weighted elbow avoids the gap between two independently capped rigid tubes.
        upper_start, joint, lower_end = Vector(shoulder), Vector(elbow), Vector(wrist)
        direction = (lower_end - upper_start).normalized()
        transverse = Vector((0, 1, 0))
        depth = direction.cross(transverse).normalized()
        arm_rings = [
            (upper_start.lerp(joint, 0.40), 0.048, 0.048, 0.0),
            (upper_start.lerp(joint, 0.65), 0.049, 0.047, 0.0),
            (upper_start.lerp(joint, 0.88), 0.041, 0.040, 0.15),
            (joint, 0.037, 0.037, 0.5),
            (joint.lerp(lower_end, 0.12), 0.040, 0.039, 0.85),
            (joint.lerp(lower_end, 0.35), 0.042, 0.041, 1.0),
            (joint.lerp(lower_end, 0.65), 0.035, 0.034, 1.0),
            (lower_end, 0.025, 0.025, 1.0),
        ]
        mesh.loft([(centre, width, radius) for centre, width, radius, _ in arm_rings], "skin",
                  lambda i, _, suffix=suffix: {f"upper_arm.{suffix}": 1 - arm_rings[i][3], f"forearm.{suffix}": arm_rings[i][3]},
                  (transverse, depth))
        mesh.segment(wrist, (sign * 0.67, 0, 1.06), [(0, 0.023, 0.024), (0.35, 0.029, 0.036), (0.8, 0.025, 0.03), (1, 0.008, 0.012)], "skin", f"hand.{suffix}")
        # Continuous leg contours blend across the knee; the material split does not split skin weights.
        for rings, mat in [
            ([(0.97, 0.0, 0.084, 0.087), (0.9, -0.003, 0.088, 0.086), (0.80, -0.006, 0.081, 0.08), (0.75, -0.007, 0.072, 0.075)], "shorts"),
            ([(0.755, -0.007, 0.067, 0.07), (0.68, -0.01, 0.065, 0.068), (0.60, -0.014, 0.058, 0.057), (0.53, -0.015, 0.047, 0.052), (0.49, -0.01, 0.048, 0.046)], "skin"),
            ([(0.50, -0.011, 0.05, 0.047), (0.43, -0.004, 0.055, 0.058), (0.34, 0.002, 0.055, 0.062), (0.24, 0.002, 0.044, 0.05), (0.15, 0.0, 0.031, 0.034), (0.08, 0.0, 0.03, 0.032)], "socks"),
        ]:
            def leg_weights(index, count, rings=rings, suffix=suffix):
                z = rings[index][0]
                shin = max(0, min(1, (0.60 - z) / 0.14))
                return {f"thigh.{suffix}": 1 - shin, f"shin.{suffix}": shin}
            mesh.loft([((sign * 0.10, y, z), width, depth) for z, y, width, depth in reversed(rings)], mat,
                      lambda i, n, f=leg_weights: f(n - 1 - i, n))
        mesh.loft([((sign * 0.10, y, z), width, height) for y, z, width, height in
                   [(0.05, 0.065, 0.035, 0.034), (0.015, 0.062, 0.044, 0.049),
                    (-0.055, 0.046, 0.049, 0.034), (-0.13, 0.039, 0.049, 0.026),
                    (-0.21, 0.031, 0.038, 0.023), (-0.24, 0.027, 0.005, 0.012)]], "boots",
                  lambda *_: {f"foot.{suffix}": 1.0}, (Vector((1, 0, 0)), Vector((0, 0, -1))))
        mesh.segment((sign * 0.034, -0.081, 1.787), (sign * 0.052, -0.078, 1.787),
                     [(0, 0.004, 0.003), (0.5, 0.006, 0.004), (1, 0.002, 0.002)], "eyes", "head")
    return mesh.finish(rig)


def smooth(t):
    t = max(0, min(1, t))
    return t * t * (3 - 2 * t)


def aim_bone(rig, name, head, tail, parent_matrix):
    rest = rig.data.bones[name]
    rotation = (rest.tail_local - rest.head_local).rotation_difference(Vector(tail) - Vector(head))
    world_matrix = Matrix.Translation(Vector(head)) @ rotation.to_matrix().to_4x4() @ rest.matrix_local.to_3x3().to_4x4()
    rig.pose.bones[name].matrix = world_matrix
    # Matrix assignment derives local pose from the evaluated parent, so update before solving a child.
    bpy.context.view_layer.update()
    return world_matrix


def solve_leg(rig, suffix, ankle):
    thigh = rig.data.bones[f"thigh.{suffix}"]
    shin = rig.data.bones[f"shin.{suffix}"]
    hip = rig.pose.bones["pelvis"].matrix @ (rig.data.bones["pelvis"].matrix_local.inverted() @ thigh.head_local)
    ankle = Vector(ankle)
    upper = (thigh.tail_local - thigh.head_local).length
    lower = (shin.tail_local - shin.head_local).length
    direction = ankle - hip
    if direction.length > upper + lower + 0.001:
        raise ValueError(f"Authored ankle target exceeds leg reach: {suffix}: {direction.length:.6f} > {upper + lower:.6f}")
    length = min(upper + lower - 0.0001, max(abs(upper - lower) + 0.0001, direction.length))
    direction.normalize()
    ankle = hip + direction * length
    along = (upper * upper - lower * lower + length * length) / (2 * length)
    perpendicular = Vector((0, -1, 0))
    perpendicular = (perpendicular - direction * perpendicular.dot(direction)).normalized()
    knee = hip + direction * along + perpendicular * math.sqrt(max(0, upper * upper - along * along))
    aim_bone(rig, f"thigh.{suffix}", hip, knee, None)
    aim_bone(rig, f"shin.{suffix}", knee, ankle, None)
    aim_bone(rig, f"foot.{suffix}", ankle, ankle + Vector((0, -0.16, -0.03)), None)


def solve_arm(rig, suffix, wrist):
    upper = rig.data.bones[f"upper_arm.{suffix}"]
    lower = rig.data.bones[f"forearm.{suffix}"]
    parent = rig.data.bones[f"clavicle.{suffix}"]
    shoulder = rig.pose.bones[parent.name].matrix @ (parent.matrix_local.inverted() @ upper.head_local)
    wrist = Vector(wrist)
    first = (upper.tail_local - upper.head_local).length
    second = (lower.tail_local - lower.head_local).length
    direction = wrist - shoulder
    if direction.length > first + second + 0.001:
        raise ValueError(f"Authored wrist target exceeds arm reach: {suffix}")
    length = max(abs(first - second) + 0.0001, min(first + second - 0.0001, direction.length))
    direction.normalize()
    wrist = shoulder + direction * length
    along = (first * first - second * second + length * length) / (2 * length)
    sign = 1 if suffix == "L" else -1
    # Elbows trail the torso rather than flare sideways like a static display pose.
    pole = Vector((sign * 0.25, 0.5, -1))
    pole = (pole - direction * pole.dot(direction)).normalized()
    elbow = shoulder + direction * along + pole * math.sqrt(max(0, first * first - along * along))
    aim_bone(rig, upper.name, shoulder, elbow, None)
    aim_bone(rig, lower.name, elbow, wrist, None)
    # A neutral wrist continues the forearm; fixing it to world-down creates a broken-looking hinge.
    aim_bone(rig, f"hand.{suffix}", wrist, wrist + (wrist - elbow).normalized() * 0.092, None)


def pose_swing_arm(rig, suffix, shoulder_angle, elbow_flexion, abduction=0.065):
    upper = rig.data.bones[f"upper_arm.{suffix}"]
    lower = rig.data.bones[f"forearm.{suffix}"]
    parent = rig.data.bones[f"clavicle.{suffix}"]
    parent_pose = rig.pose.bones[parent.name].matrix
    shoulder = parent_pose @ (parent.matrix_local.inverted() @ upper.head_local)
    torso_rotation = (parent_pose @ parent.matrix_local.inverted()).to_3x3()
    sign = 1 if suffix == "L" else -1
    # Explicit sagittal flexion keeps the elbow hinge anatomical across the whole swing, unlike wrist-only IK.
    upper_direction = Vector((sign * math.sin(abduction), -math.sin(shoulder_angle) * math.cos(abduction), -math.cos(shoulder_angle) * math.cos(abduction)))
    lower_angle = shoulder_angle + elbow_flexion
    lower_direction = Vector((sign * 0.025, -math.sin(lower_angle), -math.cos(lower_angle))).normalized()
    elbow = shoulder + torso_rotation @ upper_direction * upper.length
    wrist = elbow + torso_rotation @ lower_direction * lower.length
    aim_bone(rig, upper.name, shoulder, elbow, None)
    aim_bone(rig, lower.name, elbow, wrist, None)
    aim_bone(rig, f"hand.{suffix}", wrist, wrist + torso_rotation @ lower_direction * 0.092, None)


CLIPS = [
    ("idle", 2.0, 0.0, "idle"),
    ("walk", 1.0, 1.2, "locomotion"),
    ("run", 0.70, 3.8, "locomotion"),
    ("sprint", 0.56, 6.5, "locomotion"),
    ("turn_left", 0.9, 0.0, "turn"),
    ("turn_right", 0.9, 0.0, "turn"),
    ("shield_left", 1.0, 0.0, "shield"),
    ("shield_right", 1.0, 0.0, "shield"),
    ("pass_left", 0.85, 0.0, "pass"),
    ("pass_right", 0.85, 0.0, "pass"),
    ("shot_left", 1.05, 0.0, "shot"),
    ("shot_right", 1.05, 0.0, "shot"),
    ("receive_left", 0.75, 0.0, "receive"),
    ("receive_right", 0.75, 0.0, "receive"),
    ("tackle_left", 1.1, 0.0, "tackle"),
    ("tackle_right", 1.1, 0.0, "tackle"),
    ("keeper_ready", 1.5, 0.0, "keeper"),
    ("throw_in", 1.1, 0.0, "throw"),
]


def pose_clip(rig, name, kind, phase, speed, duration):
    for bone in rig.pose.bones:
        bone.matrix_basis = Matrix.Identity(4)
    pelvis = rig.pose.bones["pelvis"]
    legs = {"L": Vector((0.10, 0, 0.095)), "R": Vector((-0.10, 0, 0.095))}
    hands = {"L": Vector((0.23, -0.04, 0.95)), "R": Vector((-0.23, -0.04, 0.95))}
    arm_angles = {suffix: (0.0, math.radians(12)) for suffix in ("L", "R")}
    if kind == "locomotion":
        # Stride lengths are an authoring envelope, not a licence to move Core's root.
        stride = min(0.72, speed * duration * 0.5)
        stance = 0.60 if speed < 2 else 0.48
        pelvis.location.y = -0.11 + 0.012 * (1 - math.cos(phase * math.tau * 2))
        rig.pose.bones["spine"].rotation_quaternion = Quaternion((1, 0, 0), 0.035 if speed < 2 else 0.10)
        rig.pose.bones["chest"].rotation_quaternion = Quaternion((0, 1, 0), math.sin(phase * math.tau) * (0.035 if speed < 2 else 0.07))
        for suffix, offset in [("L", 0), ("R", 0.5)]:
            cycle = (phase + offset) % 1
            if cycle < stance:
                travel = cycle / stance
                legs[suffix].y = -stride * 0.5 + stride * travel
            else:
                swing = (cycle - stance) / (1 - stance)
                legs[suffix].y = stride * 0.5 - stride * smooth(swing)
                legs[suffix].z += math.sin(swing * math.pi) * (0.085 if speed < 2 else 0.16)
            swing = -math.cos((phase + offset) * math.tau)
            amplitude = math.radians(18 if speed < 2 else (34 if speed < 6 else 42))
            flexion = math.radians(24 if speed < 2 else 82)
            arm_angles[suffix] = (amplitude * swing, flexion + math.radians(6) * swing)
    elif kind in ("turn", "shield"):
        sign = 1 if name.endswith("left") else -1
        amount = math.sin(phase * math.pi)
        rig.pose.bones["chest"].rotation_quaternion = Quaternion((0, 1, 0), sign * amount * 0.36)
        pelvis.location.y = -0.06 * amount
        legs["L"].x += 0.05 * amount
        legs["R"].x -= 0.05 * amount
        legs["L" if sign > 0 else "R"].y += 0.11 * amount
        for suffix in ("L", "R"):
            protecting = suffix == ("L" if sign > 0 else "R")
            arm_angles[suffix] = (
                -0.12 * amount if protecting else 0.15 * amount,
                math.radians(12 + (45 if protecting else 20) * amount),
                0.065 + (0.60 if kind == "shield" and protecting else 0.20) * amount)
    elif kind in ("pass", "shot", "receive", "tackle"):
        foot = "L" if name.endswith("left") else "R"
        # Every one-shot has a named contact at phase 0.5; the engine chooses its time.
        reach = 0.28 if kind in ("pass", "receive") else 0.43
        if phase < 0.25:
            legs[foot].y = 0.14 * smooth(phase / 0.25)
        elif phase <= 0.5:
            legs[foot].y = 0.14 - (0.14 + reach) * smooth((phase - 0.25) / 0.25)
        else:
            legs[foot].y = -reach * (1 - smooth((phase - 0.5) / 0.5))
        legs[foot].z += 0.12 * math.sin(phase * math.pi)
        if kind == "receive":
            legs[foot].y = -0.20 * math.sin(phase * math.pi)
            legs[foot].z = 0.095 + 0.045 * math.sin(phase * math.pi)
        pelvis.location.y = -0.045 * math.sin(phase * math.pi)
        rig.pose.bones["chest"].rotation_quaternion = Quaternion((0, 1, 0), (0.15 if foot == "L" else -0.15) * math.sin(phase * math.pi))
        balance = math.sin(phase * math.pi)
        for suffix in ("L", "R"):
            opposite = suffix != foot
            arm_angles[suffix] = (
                (0.30 if opposite else -0.20) * balance,
                math.radians(12 + 30 * balance),
                0.065 + (0.55 if kind in ("shot", "tackle") else 0.25) * balance)
    elif kind == "keeper":
        arm_angles = {}
        pelvis.location.y = -0.10
        legs["L"].x += 0.09
        legs["R"].x -= 0.09
        for suffix in ("L", "R"):
            hands[suffix].y = -0.28
            hands[suffix].z = 1.17
    elif kind == "throw":
        arm_angles = {}
        for suffix in ("L", "R"):
            lift = math.sin(phase * math.pi)
            hands[suffix].x = 0.15 if suffix == "L" else -0.15
            hands[suffix].y = -0.06 - 0.18 * lift
            hands[suffix].z = 1.07 + 0.78 * lift
    else:
        rig.pose.bones["chest"].scale = (1, 1 + 0.004 * math.sin(phase * math.tau), 1)
    bpy.context.view_layer.update()
    for suffix in ("L", "R"):
        solve_leg(rig, suffix, legs[suffix])
        if suffix in arm_angles:
            pose_swing_arm(rig, suffix, *arm_angles[suffix])
        else:
            solve_arm(rig, suffix, hands[suffix])
        bpy.context.view_layer.update()


def author_clips(scene, rig):
    metadata = []
    rig.animation_data_create()
    for name, duration, speed, kind in CLIPS:
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data.action = action
        count = round(duration * FPS)
        for frame in range(count + 1):
            # Evaluating existing keys during IK overwrites authored transforms with the previous pose.
            rig.animation_data.action = None
            scene.frame_set(frame)
            pose_clip(rig, name, kind, frame / count, speed, duration)
            rig.animation_data.action = action
            for bone in rig.pose.bones:
                bone.keyframe_insert("location", frame=frame, group=bone.name)
                bone.keyframe_insert("rotation_quaternion", frame=frame, group=bone.name)
                bone.keyframe_insert("scale", frame=frame, group=bone.name)
        metadata.append({
            "id": name, "durationSeconds": count / FPS, "kind": kind,
            "nominalSpeedMetersPerSecond": speed,
            "loop": kind in ("idle", "locomotion", "keeper"),
            "contact": None if kind in ("idle", "locomotion", "keeper", "turn", "shield") else {"normalizedTime": 0.5, "limb": "bothHands" if kind == "throw" else ("leftFoot" if name.endswith("left") else "rightFoot")},
        })
    rig.animation_data.action = bpy.data.actions.get("idle")
    scene.frame_set(0)
    return metadata


def validate_mesh(obj, rig):
    if len(rig.data.bones) != 22:
        raise ValueError(f"Canonical rig expected 22 bones, found {len(rig.data.bones)}")
    for vertex in obj.data.vertices:
        total = sum(group.weight for group in vertex.groups)
        if not math.isfinite(total) or abs(total - 1) > 0.00001 or len(vertex.groups) > 4:
            raise ValueError(f"Invalid vertex weights: {vertex.index}: {total}")
    for coordinate in (v for vertex in obj.data.vertices for v in vertex.co):
        if not math.isfinite(coordinate):
            raise ValueError("Non-finite mesh position")


def glb_document(path):
    content = path.read_bytes()
    magic, version, length = struct.unpack_from("<III", content)
    if magic != 0x46546C67 or version != 2 or length != len(content):
        raise ValueError("Malformed GLB header")
    size, kind = struct.unpack_from("<II", content, 12)
    if kind != 0x4E4F534A:
        raise ValueError("GLB lacks its JSON chunk")
    return json.loads(content[20:20 + size])


def validate_export(path, metadata):
    document = glb_document(path)
    if len(document.get("skins", [])) != 1:
        raise ValueError("Player export must contain exactly one skin")
    clips = {clip["name"] for clip in document.get("animations", [])}
    required = {clip["id"] for clip in metadata}
    if clips != required:
        raise ValueError(f"Animation export mismatch: missing={required - clips}, unexpected={clips - required}")
    for mesh in document["meshes"]:
        for primitive in mesh["primitives"]:
            if not {"JOINTS_0", "WEIGHTS_0"}.issubset(primitive["attributes"]):
                raise ValueError("Exported primitive has no skinning attributes")
    return document


def setup_preview(scene):
    camera_data = bpy.data.cameras.new("FE Authoring Camera")
    camera = bpy.data.objects.new("FE Authoring Camera", camera_data)
    scene.collection.objects.link(camera)
    camera.location = (3.0, -6.0, 2.6)
    camera.rotation_euler = (Vector((0, 0, 0.95)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera_data.type = enum_value(camera_data.bl_rna.properties, "type", "ORTHO")
    camera_data.ortho_scale = 2.25
    scene.camera = camera
    for name, location, energy in [("key", (2, -4, 4), 500), ("fill", (-3, -1, 3), 250), ("rim", (1, 3, 4), 400)]:
        data = bpy.data.lights.new("FE " + name, enum_value(bpy.types.Light.bl_rna.properties, "type", "AREA"))
        data.energy, data.size = energy, 4
        light = bpy.data.objects.new("FE " + name, data)
        scene.collection.objects.link(light)
        light.location = location
        light.rotation_euler = (Vector((0, 0, 0.95)) - light.location).to_track_quat("-Z", "Y").to_euler()
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except TypeError as error:
        raise RuntimeError(f"This authoring tool requires Eevee: {error}") from error
    scene.render.resolution_x, scene.render.resolution_y = 640, 768
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    settings = scene.render.image_settings
    settings.file_format = enum_value(settings.bl_rna.properties, "file_format", "PNG")
    settings.color_mode = enum_value(settings.bl_rna.properties, "color_mode", "RGBA")


def build(output, previews):
    from human_authoring import build_human
    output.mkdir(parents=True, exist_ok=True)
    scene = bpy.data.scenes.new(SCENE_NAME)
    bpy.context.window.scene = scene
    scene.render.fps = FPS
    scene.render.fps_base = 1.0
    rig = make_rig(scene)
    obj = build_human(scene, rig, output)
    meshes = [object for object in scene.objects if object.type == "MESH" and object.parent == rig]
    for mesh in meshes:
        validate_mesh(mesh, rig)
    metadata = author_clips(scene, rig)
    setup_preview(scene)
    for selected in bpy.context.selected_objects:
        selected.select_set(False)
    rig.select_set(True)
    for mesh in meshes:
        mesh.select_set(True)
    bpy.context.view_layer.objects.active = rig
    properties = bpy.ops.export_scene.gltf.get_rna_type().properties
    glb = output / "player.glb"
    bpy.ops.export_scene.gltf(
        filepath=str(glb), check_existing=False,
        export_format=enum_value(properties, "export_format", "GLB"),
        use_selection=True, use_active_scene=True, export_animations=True, export_skins=True,
        export_animation_mode=enum_value(properties, "export_animation_mode", "ACTIONS"),
        export_force_sampling=True, export_frame_range=False,
        export_yup=True, export_extras=True, export_apply=False,
        export_influence_nb=4, export_all_influences=False,
    )
    document = validate_export(glb, metadata)
    manifest = {
        "schemaVersion": 1, "rigSchemaVersion": 1,
        "author": "FootballEngine anatomical adaptation, clothing, textures and motion",
        "source": ["generate_players.py", "player_authoring.py", "human_authoring.py", "athlete_equipment.py", "Sources/MakeHuman/sources.json"],
        "thirdPartyGeometryOrMotion": True,
        "thirdPartyGeometryLicense": "CC0-1.0; MakeHuman hm08 and anatomical targets",
        "thirdPartyMotion": False,
        "equipment": [{"object": mesh.name, "category": mesh.get("fe_category", "body"),
                       "coverage": mesh.get("fe_coverage", "none"), "independentMesh": True}
                      for mesh in meshes],
        "textures": [{"path": name, "sha256": hashlib.sha256((output / name).read_bytes()).hexdigest()}
                     for name in ["player-albedo.png", "player-normal-roughness.png"]],
        "blenderVersion": bpy.app.version_string,
        "coordinates": {"units": "metres", "up": "+Y", "forward": "+Z"},
        "rootMotion": "in-place; match engine owns world transform",
        "file": {"path": glb.name, "sha256": hashlib.sha256(glb.read_bytes()).hexdigest()},
        "joints": len(document["skins"][0]["joints"]),
        "clips": metadata,
        "qualityStatus": "first-party authoring corpus; gameplay/contact acceptance pending",
    }
    (output / "player-motion.json").write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n")
    # The runtime only uploads verified mips; colour-space and normal filtering belong to asset authoring.
    import importlib.util
    mip_builder_path = Path(__file__).resolve().parents[3] / "Tools/MatchViewer3D/build_athlete_mips.py"
    spec = importlib.util.spec_from_file_location("athlete_mips", mip_builder_path)
    mip_builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mip_builder)
    mip_builder.build(output)
    if previews:
        for name in ["idle", "run", "shield_right", "pass_right", "shot_right"]:
            rig.animation_data.action = bpy.data.actions[name]
            scene.frame_set(round(next(duration for clip, duration, *_ in CLIPS if clip == name) * FPS * 0.5))
            scene.render.filepath = str(output / f"authoring-{name}.png")
            bpy.ops.render.render(write_still=True, scene=scene.name)
    rig.animation_data.action = bpy.data.actions["idle"]
    scene.frame_set(0)
    scene.frame_end = FPS * 2
    # A clean authoring invocation writes only the new source scene; no duplicated legacy sprite scenes.
    if bpy.app.background:
        for previous in list(bpy.data.scenes):
            if previous != scene:
                bpy.data.scenes.remove(previous)
    bpy.ops.wm.save_as_mainfile(filepath=str(output / "players-anatomical.blend"), check_existing=False)
    print(json.dumps({"player": str(glb), "joints": manifest["joints"], "clips": len(metadata), "sha256": manifest["file"]["sha256"]}))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, default=Path(__file__).resolve().parent)
    parser.add_argument("--previews", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    build(args.output_dir.resolve(), args.previews)
