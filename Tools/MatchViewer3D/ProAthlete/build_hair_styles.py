"""Original hairstyle set for the live player pack.

    blender -b --factory-startup --python Tools/MatchViewer3D/ProAthlete/build_hair_styles.py -- [--preview DIR]

Each style is a shell grown from the scalp under the pack's own `FE Hair` area (so the sunken scalp is always
covered), shaped by a thickness profile and seeded noise, plus extra geometry where a style needs it (bun).
Weights and atlas UVs are transferred from `FE Hair`, so styles share the 60-joint skin and the hair tile.
Output: FootballEngine.Client/Assets/MatchViewer/player-hair.glb + player-hair.json.
"""

import hashlib
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector, noise
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
ASSETS = os.path.join(ROOT, "FootballEngine.Client", "Assets", "MatchViewer")
SEED = 7731


def style_thickness(style, co, head, crown, side):
    """Shell thickness in metres at a scalp point. `crown` 0 at the hairline .. 1 at the top, `side` |x| ratio."""
    # Clumps at ~3 cm and locks at ~1.2 cm: finer noise than the vertex spacing only produces faceted spikes.
    n = noise.noise(co * 30 + Vector((SEED, 0, 0)))
    fine = noise.noise(co * 80 + Vector((0, SEED, 0)))
    if style == "buzz":
        return 0.0025
    if style == "crop":
        return 0.004 + 0.011 * crown + 0.002 * n
    if style == "textured":
        return 0.006 + 0.028 * crown * (1 + 0.5 * n) + 0.001 * fine
    if style == "fade":
        # Faded sides and a raised crest along the midline: |x| below 3 cm keeps the volume.
        crest = max(0.0, 1.0 - abs(co.x - head.x) / 0.035)
        return 0.0012 + 0.026 * crest * crown * (1 + 0.3 * n)
    if style == "curly":
        return 0.024 + 0.014 * crown + 0.010 * n + 0.005 * fine
    if style == "bun":
        return 0.006 + 0.005 * crown + 0.0015 * fine
    raise ValueError(style)


STYLES = ["buzz", "crop", "textured", "fade", "curly", "bun"]


def import_pack():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(ASSETS, "player.glb"))
    objs = {o.name: o for o in bpy.data.objects}
    hair = next(o for o in objs.values() if o.type == "MESH" and o.name.startswith("FE Hair"))
    skin = next(o for o in objs.values() if o.type == "MESH" and o.name.startswith("FE Skin"))
    arm = next(o for o in objs.values() if o.type == "ARMATURE")
    return hair, skin, arm


def scalp_tree(skin):
    # The scalp is the skin *surface*: snapping to skin vertices would quantise the hair into facets.
    mw = skin.matrix_world
    verts = [mw @ v.co for v in skin.data.vertices]
    polys = [tuple(p.vertices) for p in skin.data.polygons]
    return BVHTree.FromPolygons(verts, polys), None, None


def build_style(style, hair, skin_kd, skin_pos, skin_nrm):
    obj = hair.copy()
    obj.data = hair.data.copy()
    obj.name = f"FE Hair {style}"
    obj.data.name = obj.name
    bpy.context.scene.collection.objects.link(obj)
    # Weights stay on the copy; drop the armature binding while editing positions, restore it afterwards.
    mods = [(m.name, m.object) for m in obj.modifiers if m.type == "ARMATURE"]
    obj.modifiers.clear()
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    # glTF import splits vertices along UV seams; weld them so a seam is not mistaken for a hairline edge.
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    # The pack's hair is a closed thin shell. Styles regrow from the scalp, so keep only the outer surface:
    # inner and rim faces would be pushed through each other and let the scalp show.
    bm.normal_update()
    skull = sum((v.co for v in bm.verts), Vector()) / len(bm.verts) - Vector((0, 0, 0.05))
    inner = [f for f in bm.faces if f.normal.dot((f.calc_center_median() - skull).normalized()) <= 0.25]
    bmesh.ops.delete(bm, geom=inner, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    # Only the outer hairline may stay open: inner holes (low ear-adjacent weights in the pack's cut) are
    # closed, otherwise their edges fade to zero thickness and the scalp shows through.
    loops, seen = [], set()
    for e in bm.edges:
        if e.is_boundary and e not in seen:
            stack, comp = [e], []
            while stack:
                cur = stack.pop()
                if cur in seen:
                    continue
                seen.add(cur)
                comp.append(cur)
                for v in cur.verts:
                    stack.extend(x for x in v.link_edges if x.is_boundary and x not in seen)
            loops.append(comp)
    loops.sort(key=len, reverse=True)
    for comp in loops[1:]:
        bmesh.ops.holes_fill(bm, edges=comp, sides=0)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    if style in ("textured", "curly", "fade"):
        # More vertices where the silhouette carries noise; weights/UVs interpolate through the subdivision.
        bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=1, use_grid_fill=True)
    # Relax the hairline: the pack's scalp cut follows faces and reads as a staircase against the skin.
    boundary = [v for v in bm.verts if v.is_boundary]
    for _ in range(8):
        moved = {}
        for v in boundary:
            ring = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
            if len(ring) == 2:
                moved[v] = v.co * 0.5 + (ring[0].co + ring[1].co) * 0.25
        for v, co in moved.items():
            v.co = co
    mw = obj.matrix_world
    inv = mw.inverted()
    world = [mw @ v.co for v in bm.verts]
    zs = [p.z for p in world]
    z_lo, z_hi = min(zs), max(zs)
    head = sum(world, Vector()) / len(world)
    # Thickness fades to nothing at the hairline so the edge reads as hair growing from skin, not a cap.
    edge = [mw @ v.co for v in bm.verts if v.is_boundary]
    edge_kd = KDTree(len(edge))
    for i, p in enumerate(edge):
        edge_kd.insert(p, i)
    edge_kd.balance()
    offsets = []
    fade_layer = bm.verts.layers.float.new("_fade")
    for v, p in zip(bm.verts, world):
        scalp, face_normal, _i, _d = skin_kd.find_nearest(p)
        # The pack's hair shell sits along the smooth scalp normal, so the shell point gives a smooth normal.
        normal = (p - scalp).normalized() if (p - scalp).length > 1e-5 else face_normal
        # Crown = distance inside the hairline, blended with height: volume sits on top and tapers to every
        # edge, instead of overhanging the forehead like a bowl (the front hairline is high on the head).
        inside = min(1.0, edge_kd.find(p)[2] / 0.075)
        crown = inside * (0.4 + 0.6 * (p.z - z_lo) / max(1e-6, z_hi - z_lo))
        side = abs(p.x - head.x) / 0.09
        fade = min(1.0, edge_kd.find(p)[2] / 0.022) ** 0.7
        v[fade_layer] = fade
        t = style_thickness(style, p, head, crown, side) * fade
        # The pack's scalp is uneven where it was sunk under the old cap; 5 mm clears it everywhere inside.
        offsets.append((v, scalp, normal, max(0.0004, max(t, 0.005 * fade))))
    # Smooth the thickness field over the mesh before placing vertices: neighbours agree, facets disappear.
    thick = {v: t for v, _s, _n, t in offsets}
    # Curls keep their bumps (one pass); the others are combed smooth.
    for _ in range({"curly": 1, "textured": 2, "fade": 3}.get(style, 1)):
        thick = {v: 0.5 * thick[v] + 0.5 * sum(thick[e.other_vert(v)] for e in v.link_edges) / max(1, len(v.link_edges))
                 for v in thick}
    for v, scalp, normal, _t in offsets:
        # Hair grows outward from the true (sunken) scalp, never inward through it.
        v.co = inv @ (scalp + normal * thick[v])
    if style == "bun":
        top_back = max(world, key=lambda q: q.z - 0.6 * q.y)
        add_bun(bm, inv @ (top_back + Vector((0, 0.035, -0.005))), inv)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for name, target in mods:
        m = obj.modifiers.new(name, "ARMATURE")
        m.object = target
    # Bun vertices have no weights or UVs yet: borrow them from the nearest original hair vertex.
    transfer = obj.modifiers.new("transfer", "DATA_TRANSFER")
    transfer.object = hair
    transfer.use_vert_data = True
    transfer.data_types_verts = {"VGROUP_WEIGHTS"}
    transfer.vert_mapping = "NEAREST"
    transfer.use_loop_data = True
    transfer.data_types_loops = {"UV"}
    transfer.loop_mapping = "POLYINTERP_NEAREST"
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.datalayout_transfer(modifier=transfer.name)
    bpy.ops.object.modifier_move_to_index(modifier=transfer.name, index=0)
    bpy.ops.object.modifier_apply(modifier=transfer.name)
    for p in obj.data.polygons:
        p.use_smooth = True
    # Imported glTF normals are custom split normals; after moving vertices they would shade the old shape.
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    try:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    except RuntimeError:
        pass
    return obj


def add_bun(bm, centre, inv):
    res = bmesh.ops.create_icosphere(bm, subdivisions=3, radius=0.046)
    for v in res["verts"]:
        d = v.co.normalized()
        # Wrapped strands: a twist of low-frequency bands reads as gathered hair, not a ball.
        wrap = 0.004 * math.sin(math.atan2(d.y, d.x) * 6 + d.z * 8)
        v.co = Vector(centre) + d * (0.046 + wrap) * Vector((1.0, 0.9, 0.85))


def main():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    hair, skin, arm = import_pack()
    kd, pos, nrm = scalp_tree(skin)
    styles = [build_style(s, hair, kd, pos, nrm) for s in STYLES]
    if "--preview" in a:
        preview(a[a.index("--preview") + 1], styles, hair, skin, arm)
        return
    bpy.ops.object.select_all(action="DESELECT")
    for o in styles:
        o.select_set(True)
    arm.select_set(True)
    out = os.path.join(ASSETS, "player-hair.glb")
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_skins=True, export_attributes=True,
                              export_animations=False, export_materials="PLACEHOLDER", export_yup=True)
    meta = {"schema": 1, "styles": [{"id": s, "node": o.name,
                                     "triangles": sum(len(p.vertices) - 2 for p in o.data.polygons)}
                                    for s, o in zip(STYLES, styles)],
            "replaces": "FE Hair", "uvTile": "hair (same atlas tile as FE Hair)", "seed": SEED,
            "files": {"player-hair.glb": hashlib.sha256(open(out, "rb").read()).hexdigest()},
            "source": "Tools/MatchViewer3D/ProAthlete/build_hair_styles.py",
            "provenance": "Original styles grown from the pack's own scalp area; shapes, profiles and noise are "
                          "authored in the script. Weights/UVs transferred from the pack's FE Hair."}
    json.dump(meta, open(os.path.join(ASSETS, "player-hair.json"), "w"), indent=1)
    print("exported", out, [m["triangles"] for m in meta["styles"]])


def preview(out_dir, styles, hair, skin, arm):
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 420, 420
    scene.view_settings.view_transform = "AgX"
    w = bpy.data.worlds.new("w")
    scene.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.45, 0.5, 0.55, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)
    hm = bpy.data.materials.new("hair_preview")
    hm.use_nodes = True
    hm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.05, 0.03, 0.02, 1)
    hm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.55
    sm = bpy.data.materials.new("skin_preview")
    sm.use_nodes = True
    sm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.36, 0.26, 1)
    skin.data.materials.clear()
    skin.data.materials.append(sm)
    head_z = max((skin.matrix_world @ v.co).z for v in skin.data.vertices) - 0.12
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 85
    scene.collection.objects.link(cam)
    scene.camera = cam
    for o in bpy.data.objects:
        if o.type == "MESH" and o not in (skin,):
            o.hide_render = True
    for s, o in zip(STYLES, styles):
        o.hide_render = False
        o.data.materials.clear()
        o.data.materials.append(hm)
        for view, loc in (("front", (0.35, -1.1, head_z + 0.12)), ("back", (-0.45, 1.0, head_z + 0.2))):
            cam.location = loc
            cam.rotation_euler = (Vector((0, 0, head_z)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = os.path.join(out_dir, f"{s}_{view}.png")
            bpy.ops.render.render(write_still=True)
        o.hide_render = True


main()
