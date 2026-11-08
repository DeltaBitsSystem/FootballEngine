"""Assemble a stadium from the reference layout and the kit, then render it from the match cameras.

    blender -b --python assemble_preview.py -- <scenario> <out_dir> [primary #RRGGBB] [secondary #RRGGBB]

This is look-development for the runtime assembler: same layout, same kit, same construction rules. It is not
shipped; the game builds the stadium in F# from `stadium_layout` semantics.
"""

import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import build_kit as bk  # noqa: E402
import kit_pieces as kp  # noqa: E402
import stadium_layout as sl  # noqa: E402
from kit_geo import shade_auto_smooth  # noqa: E402


def hex_rgb(h):
    h = h.lstrip("#")
    srgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb)


# ---- stand geometry ---------------------------------------------------------------------------------


def frames(st, step=2.0):
    return [(Vector((x, y, 0)), Vector((nx, ny, 0))) for (x, y), (nx, ny) in sl.front_frames(st, step)]


def tier_profile(t, upper):
    return sl.tier_profile(t, upper)


def extrude(bm, uv, st, profile, mat_index):
    fr = frames(st)
    rings = []
    for p, n in fr:
        rings.append([bm.verts.new(p + n * o + Vector((0, 0, z))) for (o, z) in profile])
    for k in range(len(rings) - 1):
        for i in range(len(profile) - 1):
            f = bm.faces.new((rings[k][i], rings[k][i + 1], rings[k + 1][i + 1], rings[k + 1][i]))
            f.material_index = mat_index
    if st["kind"] == "straight" and len(profile) > 2:
        for ring in (rings[0], rings[-1]):
            try:
                f = bm.faces.new(ring)
                f.material_index = mat_index
            except ValueError:
                pass
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for loop in f.loops:
            co = loop.vert.co
            u, v = [(co.y, co.z), (co.x, co.z), (co.x, co.y)][ax]
            loop[uv].uv = (u / 2.0, v / 2.0)


def seat_points(st, t, seated):
    return [(Vector((x, y, z)), math.radians(yaw), row) for (x, y, z, yaw, row) in sl.seat_positions(st, t)]


def build_stands(layout, mats, colours):
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    concrete = mats["concrete"]
    seat_pos = []
    for st in layout["stands"]:
        for ti, t in enumerate(st["tiers"]):
            extrude(bm, uv, st, tier_profile(t, ti > 0), 0)
            if ti + 1 < len(st["tiers"]):
                # Close the void between tiers: glazed hospitality where the layout asks for it, else a dark
                # concourse wall. Without it the sky shows through the middle of the stand.
                up = st["tiers"][ti + 1]
                back = t["front_offset"] + t["rows"] * t["row_depth"] + 0.6
                z0 = t["base_height"] + t["rows"] * t["riser"] + 1.1
                z1 = up["base_height"] - 1.4
                if z1 > z0:
                    extrude(bm, uv, st, [(back, z0), (back, z1)], 1 if up["vip_band"] else 2)
            for pos, yaw, row in seat_points(st, t, st["seated"]):
                if layout["seat_colours"] == "tiered":
                    c = colours[ti % 2]
                elif layout["seat_colours"] == "blocks":
                    c = colours[1] if row < 3 or row >= t["rows"] - 2 else colours[0]
                else:
                    c = colours[0]
                seat_pos.append((pos, yaw, c))
    me = bpy.data.meshes.new("stands")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(concrete)
    me.materials.append(mats["glass"])
    me.materials.append(mats["painted_steel:charcoal"])
    obj = bpy.data.objects.new("stands", me)
    bpy.context.scene.collection.objects.link(obj)
    return seat_pos


def instance_seats(seat_pos, seat_obj):
    """Seats as a point cloud with Instance-on-Points: 60k seats stay cheap for EEVEE."""
    me = bpy.data.meshes.new("seat_points")
    me.from_pydata([tuple(p) for p, _, _ in seat_pos], [], [])
    yaw = me.attributes.new("yaw", "FLOAT", "POINT")
    col = me.attributes.new("seat_col", "FLOAT_COLOR", "POINT")
    for i, (_, y, c) in enumerate(seat_pos):
        yaw.data[i].value = y
        col.data[i].color = (*c, 1.0)
    obj = bpy.data.objects.new("seats", me)
    bpy.context.scene.collection.objects.link(obj)
    gn = bpy.data.node_groups.new("seat_instancer", "GeometryNodeTree")
    gn.interface.new_socket("Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
    gn.interface.new_socket("Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
    nodes, links = gn.nodes, gn.links
    gi = nodes.new("NodeGroupInput")
    go = nodes.new("NodeGroupOutput")
    inst = nodes.new("GeometryNodeInstanceOnPoints")
    info = nodes.new("GeometryNodeObjectInfo")
    info.inputs["Object"].default_value = seat_obj
    yaw_attr = nodes.new("GeometryNodeInputNamedAttribute")
    yaw_attr.data_type = "FLOAT"
    yaw_attr.inputs["Name"].default_value = "yaw"
    comb = nodes.new("ShaderNodeCombineXYZ")
    links.new(yaw_attr.outputs["Attribute"], comb.inputs["Z"])
    col_attr = nodes.new("GeometryNodeInputNamedAttribute")
    col_attr.data_type = "FLOAT_COLOR"
    col_attr.inputs["Name"].default_value = "seat_col"
    store = nodes.new("GeometryNodeStoreNamedAttribute")
    store.data_type = "FLOAT_COLOR"
    store.domain = "INSTANCE"
    store.inputs["Name"].default_value = "seat_col"
    links.new(gi.outputs[0], inst.inputs["Points"])
    links.new(info.outputs["Geometry"], inst.inputs["Instance"])
    links.new(comb.outputs[0], inst.inputs["Rotation"])
    links.new(inst.outputs[0], store.inputs["Geometry"])
    links.new(col_attr.outputs["Attribute"], store.inputs["Value"])
    links.new(store.outputs[0], go.inputs[0])
    mod = obj.modifiers.new("seats", "NODES")
    mod.node_group = gn


def seat_material_from_attribute(mat):
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_type = "INSTANCER"
    attr.attribute_name = "seat_col"
    mix = next(n for n in nt.nodes if n.type == "MIX" and n.blend_type == "MULTIPLY")
    nt.links.new(attr.outputs["Color"], mix.inputs[7])


# ---- roofs, facades and pieces ----------------------------------------------------------------------


def place(src, matrix, name):
    obj = bpy.data.objects.new(name, src.data)
    obj.matrix_world = matrix
    bpy.context.scene.collection.objects.link(obj)
    return obj


def build_roofs(layout, mats):
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    for st in layout["stands"]:
        for quad in sl.roof_sheets(st):
            f = bm.faces.new([bm.verts.new(v) for v in quad])
            for loop in f.loops:
                loop[uv].uv = (loop.vert.co.x, loop.vert.co.y)
    me = bpy.data.meshes.new("roof_sheets")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mats["roof_sheet"])
    obj = bpy.data.objects.new("roof_sheets", me)
    bpy.context.scene.collection.objects.link(obj)


def build_pieces(layout, kit):
    for pc in layout["pieces"]:
        src = kit.get(f"{pc['piece']}.lod0")
        if src is None:
            continue
        sx, sy, sz = pc.get("scale", (1, 1, 1))
        m = Matrix.Translation((pc["x"], pc["y"], pc["z"])) @ Matrix.Rotation(math.radians(pc["yaw"]), 4, "Z") \
            @ Matrix.Diagonal((sx, sy, sz, 1))
        place(src, m, pc["piece"])


def build_pitch():
    me = bpy.data.meshes.new("pitch")
    hx, hy = sl.PITCH_LENGTH / 2 + 14, sl.PITCH_WIDTH / 2 + 12
    me.from_pydata([(-hx, -hy, 0), (hx, -hy, 0), (hx, hy, 0), (-hx, hy, 0)], [], [(0, 1, 2, 3)])
    mat = bpy.data.materials.new("pitch")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Object"], sep.inputs[0])
    stripe = nt.nodes.new("ShaderNodeMath")
    stripe.operation = "PINGPONG"
    div = nt.nodes.new("ShaderNodeMath")
    div.operation = "DIVIDE"
    div.inputs[1].default_value = 5.25
    nt.links.new(sep.outputs[0], div.inputs[0])
    flo = nt.nodes.new("ShaderNodeMath")
    flo.operation = "FLOORED_MODULO"
    flo.inputs[1].default_value = 2.0
    nt.links.new(div.outputs[0], flo.inputs[0])
    st = nt.nodes.new("ShaderNodeMath")
    st.operation = "FLOOR"
    nt.links.new(flo.outputs[0], st.inputs[0])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.inputs[6].default_value = (0.055, 0.17, 0.035, 1)
    mix.inputs[7].default_value = (0.075, 0.22, 0.05, 1)
    nt.links.new(st.outputs[0], mix.inputs[0])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    me.materials.append(mat)
    obj = bpy.data.objects.new("pitch", me)
    bpy.context.scene.collection.objects.link(obj)
    # Touchlines and goal lines only: enough to judge scale and framing in look-dev.
    lines = bmesh.new()
    w = 0.12
    L, W = sl.PITCH_LENGTH / 2, sl.PITCH_WIDTH / 2
    for (x0, y0, x1, y1) in ((-L, -W, L, -W), (-L, W, L, W), (-L, -W, -L, W), (L, -W, L, W), (0, -W, 0, W)):
        dx, dy = x1 - x0, y1 - y0
        ln = math.hypot(dx, dy)
        nx, ny = -dy / ln * w / 2, dx / ln * w / 2
        vs = [lines.verts.new((x0 + nx, y0 + ny, 0.01)), lines.verts.new((x1 + nx, y1 + ny, 0.01)),
              lines.verts.new((x1 - nx, y1 - ny, 0.01)), lines.verts.new((x0 - nx, y0 - ny, 0.01))]
        lines.faces.new(vs)
    seg = 64
    ring = [(math.cos(2 * math.pi * i / seg), math.sin(2 * math.pi * i / seg)) for i in range(seg)]
    outer = [lines.verts.new((c * 9.21, s * 9.21, 0.01)) for c, s in ring]
    inner = [lines.verts.new((c * 9.09, s * 9.09, 0.01)) for c, s in ring]
    for i in range(seg):
        j = (i + 1) % seg
        lines.faces.new((outer[i], outer[j], inner[j], inner[i]))
    lm = bpy.data.meshes.new("lines")
    lines.to_mesh(lm)
    lines.free()
    wm = bpy.data.materials.new("chalk")
    wm.use_nodes = True
    wm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.85, 0.85, 0.82, 1)
    lm.materials.append(wm)
    bpy.context.scene.collection.objects.link(bpy.data.objects.new("lines", lm))


# Night look target for the runtime: one spot per kit emitter, a 2 kW-class luminaire (~150,000 lm) focused into
# a 22-degree half-angle beam. Blender spot power is specified as if radiated over the full sphere (the cone only
# masks it), so the beam's real intensity Phi/Omega needs watts = lm / 683 * 4pi / Omega. That yields ~2,000-3,000
# lux on the pitch with a few hundred lamps: broadcast-grade lighting.
LUMENS = 150000.0
BEAM_HALF_ANGLE = 20.0
BEAM_SOLID_ANGLE = 2 * math.pi * (1 - math.cos(math.radians(BEAM_HALF_ANGLE)))
LUMINAIRE_WATTS = LUMENS / 683.0 * 4 * math.pi / BEAM_SOLID_ANGLE


def add_floodlights(layout):
    kit = json.load(open(os.path.join(bk.OUT, "kit.json")))
    emitters = {p["id"]: p["emitters"] for p in kit["pieces"] if p["emitters"]}
    lamps = sl.lamp_positions(layout["pieces"], emitters)
    targets = sl.aiming_targets(lamps)
    beams = sl.beam_half_angles(lamps, targets)
    bpy.context.scene.eevee.shadow_pool_size = "1024"
    for pos, tgt, half in zip(lamps, targets, beams):
        aim = (Vector(tgt) - Vector(pos)).normalized()
        lamp = bpy.data.lights.new("lamp", "SPOT")
        omega = 2 * math.pi * (1 - math.cos(math.radians(half)))
        lamp.energy = LUMENS / 683.0 * 4 * math.pi / omega
        lamp.spot_size = math.radians(half * 2)
        lamp.spot_blend = 0.8
        lamp.color = (1.0, 0.97, 0.93)
        lamp.shadow_soft_size = 0.3
        lo = bpy.data.objects.new("lamp", lamp)
        lo.location = pos
        lo.rotation_euler = aim.to_track_quat("-Z", "Y").to_euler()
        bpy.context.scene.collection.objects.link(lo)
    print("floodlights", len(lamps))


def setup_world(night=False):
    scene = bpy.context.scene
    if night:
        scene.render.engine = "BLENDER_EEVEE"
        scene.view_settings.view_transform = "AgX"
        # Broadcast cameras open up about one stop for floodlit play relative to daylight.
        scene.view_settings.exposure = 1.0
        world = bpy.data.worlds.new("night")
        scene.world = world
        world.use_nodes = True
        env = world.node_tree.nodes.new("ShaderNodeTexEnvironment")
        env.image = bpy.data.images.load(os.path.join(bk.OUT, "..", "OriginalSky", "sky-visible-night.exr"))
        world.node_tree.links.new(env.outputs[0], world.node_tree.nodes["Background"].inputs[0])
        scene.eevee.use_shadows = True
        try:
            scene.eevee.use_raytracing = True
        except AttributeError:
            pass
        return
    scene.render.engine = "BLENDER_EEVEE"
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.use_nodes = True
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    sky.sun_elevation = math.radians(38)
    sky.sun_rotation = math.radians(150)
    world.node_tree.links.new(sky.outputs[0], world.node_tree.nodes["Background"].inputs[0])
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.25
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4.5
    sun.data.angle = math.radians(0.6)
    sun.rotation_euler = (math.radians(52), 0, math.radians(150 + 90))
    scene.collection.objects.link(sun)
    scene.eevee.use_shadows = True
    try:
        scene.eevee.use_raytracing = True
        scene.eevee.ray_tracing_options.resolution_scale = "2"
    except AttributeError:
        pass


def cameras(layout):
    g = next(pc for pc in layout["pieces"] if pc["piece"] == "camera_gantry")
    return {
        "tv": ((g["x"], g["y"] + 0.8, g["z"] + 1.6), (0.0, 8.0, 0.0), 24.0),
        "aerial": ((-170.0, -190.0, 150.0), (0.0, 0.0, 0.0), 32.0),
        "goal": ((-sl.PITCH_LENGTH / 2 - 25, -6.0, 16.0), (10.0, 0.0, 2.0), 24.0),
        "pitch": ((-20.0, -28.0, 1.7), (20.0, 30.0, 12.0), 20.0),
        "bench": ((-14.0, -12.0, 6.0), (0.0, -40.0, 1.5), 24.0),
        "goals": ((0.0, -6.0, 75.0), (0.0, 0.0, 0.0), 22.0),
        "goal_close": ((40.0, -14.0, 5.0), (54.0, 0.0, 1.0), 28.0),
    }


def render(out_dir, layout, res=(1600, 900), which=None):
    scene = bpy.context.scene
    scene.render.resolution_x, scene.render.resolution_y = res
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.clip_end = 2000
    for name, (pos, look, lens) in cameras(layout).items():
        if which and name not in which:
            continue
        cam.location = pos
        cam.rotation_euler = (Vector(look) - Vector(pos)).to_track_quat("-Z", "Y").to_euler()
        cam.data.lens = lens
        scene.render.filepath = os.path.join(out_dir, f"{name}.png")
        bpy.ops.render.render(write_still=True)


def main():
    a = sys.argv[sys.argv.index("--") + 1:]
    scenario, out_dir = a[0], a[1]
    primary = hex_rgb(a[2]) if len(a) > 2 else hex_rgb("#E2353B")
    secondary = hex_rgb(a[3]) if len(a) > 3 else hex_rgb("#FFFFFF")
    which = a[4].split(",") if len(a) > 4 and a[4] else None
    night = len(a) > 5 and a[5] == "night"
    name, rep, lvl, seed = next(g for g in sl.GOLDEN if g[0] == scenario)
    from dataclasses import asdict

    layout = asdict(sl.generate(rep, lvl, seed))
    bk.TINTS["club_primary"] = primary
    bk.TINTS["club"] = (1.0, 1.0, 1.0)
    mats, _meta, _lib = bk.build({"res": 1024, "skip_bake": True, "preview": None, "pieces": None})
    kit = {o.name: o for o in bpy.data.objects if o.get("fe_lod") is not None}
    for o in kit.values():
        o.hide_render = True
        o.hide_viewport = True
    seat_mat = next(m for k, m in mats.items() if k.startswith("seat_plastic"))
    seat_material_from_attribute(seat_mat)
    if "concrete" not in mats:
        mats["concrete"] = bk.textured_material("concrete", os.path.join(bk.OUT, "Textures"))
    for extra in ("glass", "painted_steel:charcoal"):
        if extra not in mats:
            mats[extra] = bk.textured_material(extra, os.path.join(bk.OUT, "Textures"))
    if "roof_sheet" not in mats:
        mats["roof_sheet"] = bk.textured_material("roof_sheet:white", os.path.join(bk.OUT, "Textures"))
    build_pitch()
    seats = build_stands(layout, mats, (primary, secondary))
    instance_seats(seats, kit["seat.lod1"])
    build_roofs(layout, mats)
    build_pieces(layout, kit)
    setup_world(night)
    if night:
        add_floodlights(layout)
    os.makedirs(out_dir, exist_ok=True)
    render(out_dir, layout, which=which)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, f"{scenario}.blend"))
    print("seats", len(seats), "capacity", layout["capacity"])


main()
