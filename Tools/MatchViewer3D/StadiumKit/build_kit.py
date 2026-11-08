"""Build the FootballEngine stadium kit: bake materials, build pieces with LODs, export GLB + kit.json.

    blender -b --python Tools/MatchViewer3D/StadiumKit/build_kit.py -- [--res 1024] [--skip-bake] [--preview DIR]

Outputs to FootballEngine.Client/Assets/MatchViewer/StadiumKit/. Deterministic: no randomness without a seed,
no downloads. The .blend saved next to the GLB is the editable source of record.
"""

import hashlib
import json
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import kit_materials as km  # noqa: E402
import kit_pieces as kp  # noqa: E402
from kit_geo import shade_auto_smooth  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
OUT = os.path.join(ROOT, "FootballEngine.Client", "Assets", "MatchViewer", "StadiumKit")

# Variants share textures; the runtime multiplies albedo by the tint, or by a club colour for `club*` roles.
TINTS = {
    "white": (1.0, 1.0, 1.0),
    "charcoal": (0.055, 0.06, 0.068),
    "club_primary": (0.62, 0.06, 0.07),
    "club": (0.08, 0.22, 0.62),
}
CLUB_ROLES = {"club_primary": "clubPrimary", "club": "clubSeat"}

# Untextured materials: (base colour, roughness, metallic, transmission, emissive strength)
PLAIN = {
    "glass": ((0.55, 0.62, 0.66), 0.05, 0.0, 0.85, 0.0),
    "polycarbonate": ((0.62, 0.68, 0.72), 0.12, 0.0, 0.7, 0.0),
    "lamp_lens": ((1.0, 0.97, 0.92), 0.1, 0.0, 0.0, 6.0),
    "flag_cloth": ((0.95, 0.85, 0.1), 0.8, 0.0, 0.0, 0.0),
}


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {"res": 1024, "skip_bake": False, "preview": None, "pieces": None}
    i = 0
    while i < len(a):
        if a[i] == "--res":
            opts["res"] = int(a[i + 1]); i += 1
        elif a[i] == "--skip-bake":
            opts["skip_bake"] = True
        elif a[i] == "--preview":
            opts["preview"] = a[i + 1]; i += 1
        elif a[i] == "--pieces":
            opts["pieces"] = a[i + 1].split(","); i += 1
        i += 1
    return opts


def textured_material(full_id, tex_dir):
    base, _, variant = full_id.partition(":")
    mat = bpy.data.materials.new("kit." + full_id)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    out = nt.nodes["Material Output"]
    tint = TINTS.get(variant, (1.0, 1.0, 1.0))
    mat["fe_material"] = full_id
    if base in PLAIN:
        col, rough, metal, trans, emit = PLAIN[base]
        bsdf.inputs["Base Color"].default_value = (*col, 1)
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        bsdf.inputs["Transmission Weight"].default_value = trans
        if emit:
            bsdf.inputs["Emission Color"].default_value = (*col, 1)
            bsdf.inputs["Emission Strength"].default_value = emit
        if trans:
            bsdf.inputs["Alpha"].default_value = 1.0 - trans * 0.6
        return mat
    tc = nt.nodes.new("ShaderNodeTexCoord")
    if base == "led_screen":
        # Preview content only: a gradient stands in for runtime content. The subpixel grid is analytic in the
        # runtime shader (UV x screen size / pixel pitch), so nothing is textured here.
        bsdf.inputs["Base Color"].default_value = (0.02, 0.02, 0.02, 1)
        grad = nt.nodes.new("ShaderNodeTexGradient")
        nt.links.new(tc.outputs["UV"], grad.inputs["Vector"])
        cr = nt.nodes.new("ShaderNodeMix")
        cr.data_type = "RGBA"
        cr.inputs[6].default_value = (0.05, 0.35, 1.0, 1)
        cr.inputs[7].default_value = (1.0, 0.85, 0.1, 1)
        nt.links.new(grad.outputs["Fac"], cr.inputs[0])
        nt.links.new(cr.outputs[2], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 4.0
        return mat
    tex_base = {"net": "net_mask"}.get(base, base)

    def img(kind, colour):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images.load(os.path.join(tex_dir, f"{tex_base}_{kind}.png"), check_existing=True)
        n.image.colorspace_settings.name = "sRGB" if colour else "Non-Color"
        nt.links.new(tc.outputs["UV"], n.inputs["Vector"])
        return n

    alb = img("albedo", True)
    orm = img("orm", False)
    nrm = img("normal", False)
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs[0].default_value = 1.0
    mix.inputs[7].default_value = (*tint, 1)
    nt.links.new(alb.outputs["Color"], mix.inputs[6])
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(orm.outputs["Color"], sep.inputs[0])
    nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    if base == "net":
        bsdf.inputs["Base Color"].default_value = (0.92, 0.92, 0.92, 1)
        nt.links.new(alb.outputs["Color"], bsdf.inputs["Alpha"])
        mat.surface_render_method = "DITHERED"
    else:
        nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return mat


def material_entry(full_id, lib):
    base, _, variant = full_id.partition(":")
    e = {"id": full_id}
    if base in PLAIN:
        col, rough, metal, trans, emit = PLAIN[base]
        e.update(baseColor=list(col), roughness=rough, metallic=metal, transmission=trans, emissive=emit)
        if base == "flag_cloth":
            e["tintRole"] = "clubPrimary"
        return e
    if base == "led_screen":
        e.update(baseColor=[0.02, 0.02, 0.02], roughness=0.25, metallic=0.0, emissiveContent="runtime",
                 ledPixelPitchMetres=0.01)
        return e
    tex_base = {"net": "net_mask"}.get(base, base)
    e.update(textures={k: f"Textures/{tex_base}_{k}.png" for k in ("albedo", "normal", "orm")},
             tileMetres=lib[tex_base]["tileMetres"], tint=list(TINTS.get(variant, (1, 1, 1))))
    if variant in CLUB_ROLES:
        e["tintRole"] = CLUB_ROLES[variant]
    if base == "net":
        e["alphaFromAlbedoRed"] = True
    return e


LOD_DISTANCE = {0: 0.0, 1: 40.0, 2: 110.0}
RUNTIME_MATERIALS = ("concrete", "roof_sheet:white", "glass", "painted_steel:charcoal")


def build(opts):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    tex_dir = os.path.join(OUT, "Textures")
    lib_path = os.path.join(OUT, "materials-lib.json")
    if opts["skip_bake"] and os.path.exists(lib_path):
        lib = json.load(open(lib_path))
    else:
        lib = km.bake_library(tex_dir, opts["res"])
        json.dump(lib, open(lib_path, "w"), indent=1)
    tex_dir_ = tex_dir
    # Surfaces the runtime builds itself (terraces, tier bands, roof sheets) still use kit materials.
    mats = {mid: textured_material(mid, tex_dir_) for mid in RUNTIME_MATERIALS}
    coll = bpy.data.collections.new("StadiumKit")
    bpy.context.scene.collection.children.link(coll)
    pieces_meta = []
    names = opts["pieces"] or list(kp.PIECES)
    for pid in names:
        maker = kp.PIECES[pid]
        lods = []
        for lod in (0, 1, 2):
            b = maker(lod)
            if lod == 0:
                screens = [{"widthMetres": w, "heightMetres": h} for (w, h) in getattr(b, "screens", [])]
            for mid in b.materials:
                if mid not in mats:
                    mats[mid] = textured_material(mid, tex_dir)
            b.name = f"{pid}.lod{lod}"
            obj = b.build(mats, coll)
            shade_auto_smooth(obj, 40.0)
            obj["fe_piece"] = pid
            obj["fe_lod"] = lod
            tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
            if lod == 0:
                lo = Vector(map(min, *[v.co for v in obj.data.vertices]))
                hi = Vector(map(max, *[v.co for v in obj.data.vertices]))
                piece_mats = sorted({s.material["fe_material"] for s in obj.material_slots})
            lods.append({"lod": lod, "node": obj.name, "triangles": tris, "fromDistance": LOD_DISTANCE[lod]})
        pieces_meta.append({"id": pid, "lods": lods, "screens": screens,
                            "boundsMin": [round(c, 4) for c in lo], "boundsMax": [round(c, 4) for c in hi],
                            "materials": piece_mats})
        print("piece", pid, [l["triangles"] for l in lods])
    return mats, pieces_meta, lib


def export(mats, pieces_meta, lib):
    os.makedirs(OUT, exist_ok=True)
    glb = os.path.join(OUT, "stadium-kit.glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=False, export_apply=True,
                              export_materials="PLACEHOLDER", export_yup=True, export_extras=True,
                              export_normals=True, export_texcoords=True, export_tangents=False)
    blend = os.path.join(OUT, "stadium-kit.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend, compress=True)
    sha = lambda p: hashlib.sha256(open(p, "rb").read()).hexdigest()
    textures = {}
    for f in sorted(os.listdir(os.path.join(OUT, "Textures"))):
        textures[f"Textures/{f}"] = sha(os.path.join(OUT, "Textures", f))
    kit = {
        "schema": 1,
        "units": "metres",
        "axes": "glTF Y-up; every piece faces the pitch along glTF -Z (Blender +Y)",
        "lodRule": "use the highest lod whose fromDistance <= camera distance to the instance bounds centre",
        "pieces": pieces_meta,
        "materials": [material_entry(m, lib) for m in sorted(mats)],
        "files": {"stadium-kit.glb": sha(glb), **textures},
        "source": {"blend": "stadium-kit.blend", "generator": "Tools/MatchViewer3D/StadiumKit/build_kit.py",
                   "blender": bpy.app.version_string},
        "provenance": "Original FootballEngine content: geometry and every texture pixel are generated by the "
                      "scripts above from Blender procedural nodes. No third-party meshes, images or scans.",
    }
    json.dump(kit, open(os.path.join(OUT, "kit.json"), "w"), indent=1)
    print("exported", glb)


def preview(out_dir, names):
    """One EEVEE thumbnail per piece (LOD0) under a sun and sky, framed from its bounds."""
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 480
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.use_nodes = True
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    world.node_tree.links.new(sky.outputs[0], world.node_tree.nodes["Background"].inputs[0])
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.35
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4.0
    sun.data.angle = math.radians(1.0)
    sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    scene.collection.objects.link(sun)
    ground = bpy.data.objects.new("ground", bpy.data.meshes.new("g"))
    ground.data.from_pydata([(-200, -200, 0), (200, -200, 0), (200, 200, 0), (-200, 200, 0)], [], [(0, 1, 2, 3)])
    gm = bpy.data.materials.new("ground")
    gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.09, 0.2, 0.06, 1)
    ground.data.materials.append(gm)
    scene.collection.objects.link(ground)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    objs = [o for o in bpy.data.objects if o.get("fe_lod") is not None]
    for o in objs:
        o.hide_render = True
    for pid in names:
        o = bpy.data.objects[f"{pid}.lod0"]
        o.hide_render = False
        lo = Vector(map(min, *[v.co for v in o.data.vertices]))
        hi = Vector(map(max, *[v.co for v in o.data.vertices]))
        c = (lo + hi) / 2
        r = max((hi - lo).length / 2, 0.3)
        d = Vector((0.75, 1.0, 0.55)).normalized()
        fov = cam.data.angle_y
        cam.location = c + d * (r / math.sin(fov / 2)) * 1.05
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        cam.data.clip_start = r * 0.01
        cam.data.clip_end = r * 40
        scene.render.filepath = os.path.join(out_dir, f"{pid}.png")
        bpy.ops.render.render(write_still=True)
        o.hide_render = True


if __name__ == "__main__":
    o = args()
    m, p, lib = build(o)
    if o["preview"]:
        preview(o["preview"], o["pieces"] or list(kp.PIECES))
    else:
        export(m, p, lib)
