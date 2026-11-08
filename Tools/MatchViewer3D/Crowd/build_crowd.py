"""Original stadium crowd: low-poly spectators with baked vertex-animation clips.

    blender -b --factory-startup --python Tools/MatchViewer3D/Crowd/build_crowd.py -- [--preview DIR]

Bodies come from a skeleton graph through Blender's Skin modifier (a tool, not an imported asset), are rigged
with our own armature and posed procedurally. Every clip is baked to per-vertex positions/normals (VAT), so the
runtime animates tens of thousands of instances with one texture fetch per vertex and no skinning.
Output: FootballEngine.Client/Assets/MatchViewer/Crowd/{crowd.glb, crowd-vat.bin, crowd.json}.
"""

import hashlib
import json
import math
import os
import struct
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "FootballEngine.Client", "Assets", "MatchViewer", "Crowd")
FPS = 15

# Region ids written to the colour attribute; the runtime maps each to a per-instance palette slot.
SKIN, TOP, SLEEVE, TROUSERS, SHOES, HAIR, SCARF = range(7)

# Skeleton graph (name, position in metres for a 1.0-scale body, skin radius x/y, parent).
JOINTS = [
    ("pelvis", (0, 0, 0.96), (0.15, 0.11), None),
    ("spine", (0, 0, 1.13), (0.15, 0.11), "pelvis"),
    ("chest", (0, 0, 1.33), (0.17, 0.12), "spine"),
    ("neck", (0, 0, 1.50), (0.055, 0.055), "chest"),
    ("head", (0, 0.01, 1.62), (0.085, 0.10), "neck"),
    ("crown", (0, 0, 1.75), (0.07, 0.08), "head"),
    ("shoulder.L", (-0.19, 0, 1.43), (0.065, 0.06), "chest"),
    ("elbow.L", (-0.27, 0, 1.17), (0.048, 0.048), "shoulder.L"),
    ("wrist.L", (-0.33, 0.02, 0.94), (0.036, 0.03), "elbow.L"),
    ("hand.L", (-0.35, 0.03, 0.86), (0.04, 0.025), "wrist.L"),
    ("shoulder.R", (0.19, 0, 1.43), (0.065, 0.06), "chest"),
    ("elbow.R", (0.27, 0, 1.17), (0.048, 0.048), "shoulder.R"),
    ("wrist.R", (0.33, 0.02, 0.94), (0.036, 0.03), "elbow.R"),
    ("hand.R", (0.35, 0.03, 0.86), (0.04, 0.025), "wrist.R"),
    ("hip.L", (-0.10, 0, 0.92), (0.085, 0.085), "pelvis"),
    ("knee.L", (-0.11, 0.01, 0.50), (0.06, 0.06), "hip.L"),
    ("ankle.L", (-0.11, 0, 0.09), (0.045, 0.045), "knee.L"),
    ("toe.L", (-0.11, 0.15, 0.04), (0.045, 0.035), "ankle.L"),
    ("hip.R", (0.10, 0, 0.92), (0.085, 0.085), "pelvis"),
    ("knee.R", (0.11, 0.01, 0.50), (0.06, 0.06), "hip.R"),
    ("ankle.R", (0.11, 0, 0.09), (0.045, 0.045), "knee.R"),
    ("toe.R", (0.11, 0.15, 0.04), (0.045, 0.035), "ankle.R"),
]

# Bones: (name, head joint, tail joint, parent bone, roll axis). Roll fixes local axes so procedural angles
# have one meaning: for limbs hanging down, +X rotation swings forward and +Z (left) / -Z (right) abducts.
BONES = [
    ("root", "pelvis", "spine", None, (0, -1, 0)),
    ("chest", "spine", "neck", "root", (0, -1, 0)),
    ("neck", "neck", "head", "chest", (0, -1, 0)),
    ("head", "head", "crown", "neck", (0, -1, 0)),
    ("upper_arm.L", "shoulder.L", "elbow.L", "chest", (0, 1, 0)),
    ("forearm.L", "elbow.L", "wrist.L", "upper_arm.L", (0, 1, 0)),
    ("hand.L", "wrist.L", "hand.L", "forearm.L", (0, 1, 0)),
    ("upper_arm.R", "shoulder.R", "elbow.R", "chest", (0, 1, 0)),
    ("forearm.R", "elbow.R", "wrist.R", "upper_arm.R", (0, 1, 0)),
    ("hand.R", "wrist.R", "hand.R", "forearm.R", (0, 1, 0)),
    ("thigh.L", "hip.L", "knee.L", "root", (0, 1, 0)),
    ("shin.L", "knee.L", "ankle.L", "thigh.L", (0, 1, 0)),
    ("foot.L", "ankle.L", "toe.L", "shin.L", (0, 0, 1)),
    ("thigh.R", "hip.R", "knee.R", "root", (0, 1, 0)),
    ("shin.R", "knee.R", "ankle.R", "thigh.R", (0, 1, 0)),
    ("foot.R", "ankle.R", "toe.R", "shin.R", (0, 0, 1)),
]

# Body variants: overall scale, width multiplier, region rules.
VARIANTS = {
    "a": dict(scale=1.00, width=1.00, sleeves="long", hair="short"),
    "b": dict(scale=0.94, width=0.88, sleeves="short", hair="long"),
    "c": dict(scale=1.03, width=1.18, sleeves="short", hair="cap"),
    "d": dict(scale=0.97, width=0.95, sleeves="long", hair="hood"),
}


def build_body(name, v, lod=0):
    me = bpy.data.meshes.new(name)
    pos = {j: Vector(p) for j, p, _, _ in JOINTS}
    idx = {j: i for i, (j, _, _, _) in enumerate(JOINTS)}
    edges = [(idx[j], idx[par]) for j, _, _, par in JOINTS if par]
    verts = [tuple(Vector((p.x * v["width"], p.y, p.z)) * v["scale"]) for p in pos.values()]
    me.from_pydata(verts, edges, [])
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    skin = obj.modifiers.new("skin", "SKIN")
    skin.use_smooth_shade = True
    skin.branch_smoothing = 0.5
    for i, (_, _, r, _) in enumerate(JOINTS):
        sv = me.skin_vertices[0].data[i]
        sv.radius = (r[0] * v["scale"] * v["width"], r[1] * v["scale"])
    me.skin_vertices[0].data[idx["pelvis"]].use_root = True
    if lod == 0:
        # LOD0 rounds the skin hull for the near rows; LOD1 keeps the raw hull (~4x fewer triangles).
        sub = obj.modifiers.new("sub", "SUBSURF")
        sub.levels = 1
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    for m in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)
    return obj


def build_armature(name, v):
    arm = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    pos = {j: Vector((p[0] * v["width"], p[1], p[2])) * v["scale"] for j, p, _, _ in JOINTS}
    for bname, h, t, parent, roll in BONES:
        b = arm.edit_bones.new(bname)
        b.head, b.tail = pos[h], pos[t]
        b.align_roll(Vector(roll))
        if parent:
            b.parent = arm.edit_bones[parent]
            b.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def region_of(co, v):
    """Clothing region from rest-pose position; one rule set per body variant."""
    s = v["scale"]
    z, x, y = co.z / s, abs(co.x) / (s * v["width"]), co.y / s
    # Hair covers the crown and the back of the head; the face (front, y > 0.03) stays skin below the hairline.
    if z > 1.72 or (z > 1.64 and y < 0.03):
        return HAIR
    if v["hair"] == "long" and z > 1.42 and y < -0.02:
        return HAIR
    if v["hair"] == "hood" and 1.44 < z < 1.70 and y < -0.03:
        return HAIR
    if 1.44 <= z <= 1.53 and x < 0.11:
        return SCARF
    if z > 1.44:
        return SKIN
    if x > 0.2:  # arms
        if z > 1.12:
            return TOP
        if z > 0.98:
            return SLEEVE if v["sleeves"] == "long" else SKIN
        return SKIN
    if z > 0.88:
        return TOP
    if z > 0.12:
        return TROUSERS
    return SHOES


def rig(body, arm, v):
    body.parent = arm
    mod = body.modifiers.new("arm", "ARMATURE")
    mod.object = arm
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    # A plain float attribute (glTF `_REGION`): colour attributes are sRGB-encoded and would shift the ids.
    # The exporter may split or reorder vertices; `_vid` is the VAT column each exported vertex must read.
    vid = body.data.attributes.new("_vid", "FLOAT", "POINT")
    for i in range(len(body.data.vertices)):
        vid.data[i].value = float(i)
    reg = body.data.attributes.new("_region", "FLOAT", "POINT")
    for i, vert in enumerate(body.data.vertices):
        reg.data[i].value = float(region_of(vert.co, v))


# ---- procedural clips ---------------------------------------------------------------------------------
# Each clip is f(t, seconds) -> {bone: (rx, ry, rz) degrees, "root_loc": (x, y, z)}; t loops over its length.

def _sit(p):
    # Seated base: pelvis on the seat (0.46 m) slightly behind the seat centre, thighs level, shins vertical.
    p["root_loc"] = (0, -0.06, -0.50)
    p["thigh.L"] = (88, 0, 4)
    p["thigh.R"] = (88, 0, -4)
    p["shin.L"] = (-86, 0, 0)
    p["shin.R"] = (-86, 0, 0)
    p["foot.L"] = (10, 0, 0)
    p["foot.R"] = (10, 0, 0)
    return p


def _arms_rest(p, sit):
    p["upper_arm.L"] = (25 if sit else 4, 0, 6)
    p["upper_arm.R"] = (25 if sit else 4, 0, -6)
    p["forearm.L"] = (40 if sit else 12, 0, 0)
    p["forearm.R"] = (40 if sit else 12, 0, 0)
    return p


def _clap(p, t, rate):
    c = 0.5 + 0.5 * math.sin(2 * math.pi * rate * t)
    p["upper_arm.L"] = (55, 0, -8 + 14 * c)
    p["upper_arm.R"] = (55, 0, 8 - 14 * c)
    p["forearm.L"] = (70, 0, -30 + 20 * c)
    p["forearm.R"] = (70, 0, 30 - 20 * c)
    return p


def clip_sit_idle(t):
    p = _arms_rest(_sit({}), True)
    p["chest"] = (-4 + 2 * math.sin(2 * math.pi * t / 4), 0, 0)
    p["head"] = (0, 0, 12 * math.sin(2 * math.pi * t / 4 + 1))
    return p


def clip_sit_clap(t):
    p = _clap(_sit({}), t, 2.0)
    p["chest"] = (-6, 0, 0)
    return p


def clip_sit_tense(t):
    p = _sit({})
    p["chest"] = (-22 - 3 * math.sin(2 * math.pi * t / 3), 0, 0)
    p["upper_arm.L"] = (75, 0, -10)
    p["upper_arm.R"] = (75, 0, 10)
    p["forearm.L"] = (110, 0, -30)
    p["forearm.R"] = (110, 0, 30)
    p["head"] = (-10, 0, 0)
    return p


def clip_stand_idle(t):
    p = _arms_rest({"root_loc": (0, 0.12, 0)}, False)
    p["root"] = (0, 0, 3 * math.sin(2 * math.pi * t / 4))
    p["head"] = (0, 0, 10 * math.sin(2 * math.pi * t / 4 + 0.7))
    return p


def clip_stand_clap(t):
    p = _clap({"root_loc": (0, 0.12, 0.01 * abs(math.sin(2 * math.pi * t)))}, t, 2.0)
    return p


def clip_stand_chant(t):
    # Fist pump on the beat: right arm punches up twice a second, left hand holds a scarf at chest height.
    c = 0.5 + 0.5 * math.sin(2 * math.pi * 2.0 * t)
    p = {"root_loc": (0, 0.12, 0.02 * c)}
    p["upper_arm.R"] = (150 * c + 20, 0, -10)
    p["forearm.R"] = (40 * (1 - c), 0, 0)
    p["upper_arm.L"] = (50, 0, 10)
    p["forearm.L"] = (80, 0, -20)
    p["head"] = (8 * c, 0, 0)
    return p


def clip_celebrate(t):
    # Jumping with both arms high; a 1.5 Hz hop is what a packed stand does after a goal.
    c = abs(math.sin(math.pi * 1.5 * t))
    p = {"root_loc": (0, 0.12, 0.16 * c)}
    for side, s in (("L", 1), ("R", -1)):
        # Past vertical the abduction axis flips, so the V opens with a negative angle.
        p[f"upper_arm.{side}"] = (160, 0, -25 * s)
        p[f"forearm.{side}"] = (10 + 20 * c, 0, 0)
        p[f"thigh.{side}"] = (20 * (1 - c), 0, 0)
        p[f"shin.{side}"] = (-30 * (1 - c), 0, 0)
    p["chest"] = (8 * c, 0, 0)
    p["head"] = (12, 0, 0)
    return p


def clip_despair(t):
    p = {"root_loc": (0, 0.12, 0)}
    for side, s in (("L", 1), ("R", -1)):
        p[f"upper_arm.{side}"] = (120, 0, 55 * s)
        p[f"forearm.{side}"] = (125, 0, 0)
    p["chest"] = (-10, 0, 4 * math.sin(2 * math.pi * t / 3))
    p["head"] = (-25 + 5 * math.sin(2 * math.pi * t / 3), 0, 0)
    return p


def clip_protest(t):
    # Arm thrown out at the referee, then back: an angry, readable gesture at broadcast distance.
    c = 0.5 + 0.5 * math.sin(2 * math.pi * 1.0 * t)
    p = {"root_loc": (0, 0.12, 0)}
    p["upper_arm.R"] = (70 + 50 * c, 0, -20)
    p["forearm.R"] = (20, 0, 0)
    p["upper_arm.L"] = (30, 0, 15)
    p["forearm.L"] = (60, 0, 0)
    p["chest"] = (-6 * c, 0, 0)
    p["head"] = (6 * c, 0, 0)
    return p


CLIPS = [
    ("sit_idle", clip_sit_idle, 4.0),
    ("sit_clap", clip_sit_clap, 2.0),
    ("sit_tense", clip_sit_tense, 3.0),
    ("stand_idle", clip_stand_idle, 4.0),
    ("stand_clap", clip_stand_clap, 2.0),
    ("stand_chant", clip_stand_chant, 2.0),
    ("celebrate", clip_celebrate, 2.0),
    ("despair", clip_despair, 3.0),
    ("protest", clip_protest, 2.0),
]


def apply_pose(arm, pose):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        rx, ry, rz = pose.get(pb.name, (0, 0, 0))
        pb.rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
        pb.location = (0, 0, 0)
    # Root translation is expressed in world metres; the root bone points up, so convert into its local axes.
    loc = Vector(pose.get("root_loc", (0, 0, 0)))
    arm.pose.bones["root"].location = (loc.x, loc.z, -loc.y)
    bpy.context.view_layer.update()


def bake(body, arm):
    frames = []
    for name, fn, seconds in CLIPS:
        n = int(round(seconds * FPS))
        start = len(frames)
        for f in range(n):
            apply_pose(arm, fn(f / FPS))
            dg = bpy.context.evaluated_depsgraph_get()
            ev = body.evaluated_get(dg)
            me = ev.to_mesh()
            me.calc_normals_split() if hasattr(me, "calc_normals_split") else None
            p = np.empty(len(me.vertices) * 3, np.float32)
            me.vertices.foreach_get("co", p)
            nrm = np.empty(len(me.vertices) * 3, np.float32)
            me.vertices.foreach_get("normal", nrm)
            frames.append((p.reshape(-1, 3), nrm.reshape(-1, 3)))
            ev.to_mesh_clear()
        yield name, start, n, seconds
    bake.frames = frames


def main():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    preview = a[a.index("--preview") + 1] if "--preview" in a else None
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT, exist_ok=True)
    meta = {"schema": 1, "fps": FPS, "regions": ["skin", "top", "sleeve", "trousers", "shoes", "hair", "scarf"],
            "origin": "seat reference: tread floor under the seat centre; spectators face glTF -Z (Blender +Y)",
            "variants": [], "clips": []}
    blobs = []
    offset = 0
    bodies = []
    for key, v, lod in [(k, v, lod) for lod in (0, 1) for k, v in VARIANTS.items()]:
        body = build_body(f"spectator_{key}.lod{lod}", v, lod)
        arm = build_armature(f"rig_{key}_{lod}", v)
        rig(body, arm, v)
        bake.frames = []
        clips = list(bake(body, arm))
        frames = bake.frames
        nv = len(body.data.vertices)
        # Positions and normals as half floats, frame-major: the runtime indexes [frame][vertex].
        pos = np.stack([f[0] for f in frames]).astype(np.float16)
        nrm = np.stack([f[1] for f in frames]).astype(np.float16)
        data = pos.tobytes() + nrm.tobytes()
        blobs.append(data)
        tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
        meta["variants"].append({"id": key, "lod": lod, "node": body.name, "vertices": nv, "triangles": tris,
                                 "frames": len(frames), "byteOffset": offset,
                                 "positionBytes": pos.nbytes, "normalBytes": nrm.nbytes})
        offset += len(data)
        if not meta["clips"]:
            meta["clips"] = [{"id": n, "startFrame": s, "frames": c, "seconds": sec, "loop": True}
                             for n, s, c, sec in clips]
        apply_pose(arm, {})
        bodies.append((body, arm))
        print("variant", key, lod, nv, "verts", tris, "tris", len(frames), "frames")
    if preview:
        render_preview(preview, [b for b in bodies if b[0].name.endswith("lod0")])
        return
    vat = os.path.join(OUT, "crowd-vat.bin")
    with open(vat, "wb") as fh:
        for b in blobs:
            fh.write(b)
    # The GLB carries rest meshes with the region attribute only; animation lives in the VAT.
    for body, arm in bodies:
        body.modifiers.clear()
        body.parent = None
        bpy.data.objects.remove(arm)
    glb = os.path.join(OUT, "crowd.glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_yup=True, export_attributes=True,
                              export_all_vertex_colors=True, export_materials="NONE", export_skins=False,
                              export_animations=False)
    sha = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()
    meta["files"] = {"crowd.glb": sha(glb), "crowd-vat.bin": sha(vat)}
    meta["vatEncoding"] = "float16 little-endian; per variant: positions[frame][vertex][xyz] then normals[frame][vertex][xyz], Blender Z-up metres (convert to glTF Y-up: x, z, -y)"
    meta["source"] = {"generator": "Tools/MatchViewer3D/Crowd/build_crowd.py", "blender": bpy.app.version_string}
    meta["provenance"] = "Original FootballEngine content: skeleton graph, rig, poses and clips authored in this script."
    json.dump(meta, open(os.path.join(OUT, "crowd.json"), "w"), indent=1)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "crowd.blend"), compress=True)
    print("exported", glb)


def render_preview(out_dir, bodies):
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 1200, 500
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.55, 0.6, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3
    sun.rotation_euler = (math.radians(45), 0, math.radians(30))
    scene.collection.objects.link(sun)
    palette = [(0.62, 0.42, 0.30), (0.7, 0.05, 0.08), (0.55, 0.04, 0.06), (0.06, 0.08, 0.16), (0.04, 0.04, 0.04),
               (0.08, 0.05, 0.03), (0.95, 0.95, 0.95)]
    mat = bpy.data.materials.new("region")
    mat.use_nodes = True
    nt = mat.node_tree
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "_region"
    scale = nt.nodes.new("ShaderNodeMath")
    scale.operation = "MULTIPLY_ADD"
    scale.inputs[1].default_value = 1 / 8
    scale.inputs[2].default_value = 1 / 16
    nt.links.new(attr.outputs["Fac"], scale.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT"
    els = ramp.color_ramp.elements
    els[0].position = 0.0
    els[0].color = (*palette[0], 1)
    els[1].position = 1 / 8
    els[1].color = (*palette[1], 1)
    for i in range(2, 7):
        e = els.new(i / 8)
        e.color = (*palette[i], 1)
    nt.links.new(scale.outputs[0], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 35
    for clip_name, fn, seconds in CLIPS:
        for i, (body, arm) in enumerate(bodies):
            body.data.materials.clear()
            body.data.materials.append(mat)
            arm.location = (i * 1.1, 0, 0)
            apply_pose(arm, fn(seconds * 0.3))
        for view, loc in (("front", (1.65, 6.2, 1.4)), ("side", (-4.2, 2.0, 1.3))):
            cam.location = loc
            cam.rotation_euler = (Vector((1.65 if view == "front" else 0.0, 0, 0.85)) - cam.location).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = os.path.join(out_dir, f"{clip_name}_{view}.png")
            bpy.ops.render.render(write_still=True)


main()
