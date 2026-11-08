"""Original stadium material library.

Every tile is generated from Blender procedural nodes authored here, baked with Cycles and made seamless
offline, so the runtime pixels are project-owned. Run through build_kit.py; nothing here downloads.
"""

import os

import bpy
import numpy as np

RES = 1024


def _nodes(mat):
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return nt, bsdf


def _coord(nt, scale):
    # Texture space spans one tile; `scale` converts it to metres so feature sizes are physical.
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (scale, scale, scale)
    nt.links.new(tc.outputs["UV"], mp.inputs["Vector"])
    return mp.outputs["Vector"]


def _noise(nt, vec, scale, detail=6.0, rough=0.55, dim=3):
    n = nt.nodes.new("ShaderNodeTexNoise")
    n.noise_dimensions = "3D"
    n.inputs["Scale"].default_value = scale
    n.inputs["Detail"].default_value = detail
    n.inputs["Roughness"].default_value = rough
    nt.links.new(vec, n.inputs["Vector"])
    return n.outputs["Fac"]


def _ramp(nt, fac, stops):
    r = nt.nodes.new("ShaderNodeValToRGB")
    els = r.color_ramp.elements
    while len(els) > 2:
        els.remove(els[-1])
    for i, (pos, col) in enumerate(stops):
        e = els[i] if i < len(els) else els.new(pos)
        e.position = pos
        e.color = col
    nt.links.new(fac, r.inputs["Fac"])
    return r.outputs["Color"]


def _math(nt, op, a, b=None):
    m = nt.nodes.new("ShaderNodeMath")
    m.operation = op
    for i, v in enumerate((a, b)):
        if v is None:
            continue
        if isinstance(v, (int, float)):
            m.inputs[i].default_value = v
        else:
            nt.links.new(v, m.inputs[i])
    return m.outputs[0]


def _bump(nt, height, strength, distance):
    b = nt.nodes.new("ShaderNodeBump")
    b.inputs["Strength"].default_value = strength
    b.inputs["Distance"].default_value = distance
    nt.links.new(height, b.inputs["Height"])
    return b.outputs["Normal"]


def _mix(nt, a, b, fac):
    m = nt.nodes.new("ShaderNodeMix")
    m.data_type = "RGBA"
    for i, v in ((0, fac), (6, a), (7, b)):
        if isinstance(v, (int, float)):
            m.inputs[i].default_value = v
        elif isinstance(v, tuple):
            m.inputs[i].default_value = v
        else:
            nt.links.new(v, m.inputs[i])
    return m.outputs[2]


# ---- material recipes -------------------------------------------------------------------------------
# Each returns (tile_metres, metallic, builder) where builder wires the principled node.


def concrete(nt, bsdf, tile):
    v = _coord(nt, tile)
    big = _noise(nt, v, 0.7, 4, 0.5)
    mid = _noise(nt, v, 4.0, 8, 0.6)
    pores = _noise(nt, v, 90.0, 2, 0.4)
    stain = _ramp(nt, big, [(0.3, (0.40, 0.395, 0.38, 1)), (0.55, (0.55, 0.54, 0.51, 1)), (0.8, (0.64, 0.63, 0.60, 1))])
    speck = _ramp(nt, mid, [(0.3, (0.9, 0.9, 0.9, 1)), (0.7, (1.05, 1.04, 1.02, 1))])
    m = nt.nodes.new("ShaderNodeMix")
    m.data_type = "RGBA"
    m.blend_type = "MULTIPLY"
    m.inputs[0].default_value = 1.0
    nt.links.new(stain, m.inputs[6])
    nt.links.new(speck, m.inputs[7])
    pit = _math(nt, "GREATER_THAN", pores, 0.68)
    dark = _mix(nt, m.outputs[2], (0.2, 0.2, 0.2, 1), _math(nt, "MULTIPLY", pit, 0.55))
    nt.links.new(dark, bsdf.inputs["Base Color"])
    rough = _math(nt, "ADD", _math(nt, "MULTIPLY", mid, 0.25), 0.72)
    nt.links.new(rough, bsdf.inputs["Roughness"])
    h = _math(nt, "SUBTRACT", _math(nt, "MULTIPLY", mid, 0.6), _math(nt, "MULTIPLY", pit, 0.8))
    nt.links.new(_bump(nt, h, 1.0, 0.02), bsdf.inputs["Normal"])


def painted_steel(nt, bsdf, tile):
    # Neutral near-white base: the runtime tints it with club or architectural colours.
    v = _coord(nt, tile)
    peel = _noise(nt, v, 220.0, 2, 0.5)
    grime = _noise(nt, v, 2.5, 6, 0.6)
    col = _ramp(nt, grime, [(0.45, (0.80, 0.80, 0.80, 1)), (0.8, (0.86, 0.86, 0.86, 1))])
    nt.links.new(col, bsdf.inputs["Base Color"])
    nt.links.new(_math(nt, "ADD", _math(nt, "MULTIPLY", grime, 0.15), 0.32), bsdf.inputs["Roughness"])
    nt.links.new(_bump(nt, peel, 0.35, 0.004), bsdf.inputs["Normal"])


def galvanized(nt, bsdf, tile):
    v = _coord(nt, tile)
    vor = nt.nodes.new("ShaderNodeTexVoronoi")
    vor.inputs["Scale"].default_value = 45.0
    nt.links.new(v, vor.inputs["Vector"])
    spangle = vor.outputs["Distance"]
    col = _ramp(nt, spangle, [(0.0, (0.55, 0.56, 0.57, 1)), (0.6, (0.70, 0.71, 0.72, 1))])
    nt.links.new(col, bsdf.inputs["Base Color"])
    bsdf.inputs["Metallic"].default_value = 1.0
    nt.links.new(_math(nt, "ADD", _math(nt, "MULTIPLY", spangle, 0.2), 0.3), bsdf.inputs["Roughness"])


def seat_plastic(nt, bsdf, tile):
    # White base so one seat batch carries any club colour by multiplication.
    v = _coord(nt, tile)
    grain = _noise(nt, v, 400.0, 2, 0.5)
    sun = _noise(nt, v, 3.0, 4, 0.5)
    col = _ramp(nt, sun, [(0.3, (0.86, 0.86, 0.86, 1)), (0.8, (0.93, 0.93, 0.93, 1))])
    nt.links.new(col, bsdf.inputs["Base Color"])
    nt.links.new(_math(nt, "ADD", _math(nt, "MULTIPLY", sun, 0.2), 0.3), bsdf.inputs["Roughness"])
    nt.links.new(_bump(nt, grain, 0.25, 0.002), bsdf.inputs["Normal"])


def roof_sheet(nt, bsdf, tile):
    # Trapezoidal profiled sheet at 0.2 m pitch: the ribs live in the normal map, not the geometry.
    v = _coord(nt, tile)
    wave = nt.nodes.new("ShaderNodeTexWave")
    wave.wave_type = "BANDS"
    wave.bands_direction = "X"
    wave.wave_profile = "SAW"
    wave.inputs["Scale"].default_value = 1.0
    wave.inputs["Distortion"].default_value = 0.0
    # Five ribs per metre; tile spans an integer number of ribs so it stays seamless.
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (5.0 / 1.0, 1, 1)
    nt.links.new(v, mp.inputs["Vector"])
    nt.links.new(mp.outputs[0], wave.inputs["Vector"])
    rib = _math(nt, "PINGPONG", wave.outputs["Fac"], 0.5)
    streak = _noise(nt, v, 1.5, 6, 0.6)
    col = _ramp(nt, streak, [(0.4, (0.74, 0.75, 0.76, 1)), (0.8, (0.82, 0.83, 0.84, 1))])
    nt.links.new(col, bsdf.inputs["Base Color"])
    nt.links.new(_math(nt, "ADD", _math(nt, "MULTIPLY", streak, 0.2), 0.4), bsdf.inputs["Roughness"])
    nt.links.new(_bump(nt, rib, 1.0, 0.02), bsdf.inputs["Normal"])


def rubber_floor(nt, bsdf, tile):
    v = _coord(nt, tile)
    chips = _noise(nt, v, 160.0, 2, 0.5)
    col = _ramp(nt, chips, [(0.4, (0.05, 0.05, 0.055, 1)), (0.65, (0.09, 0.09, 0.1, 1))])
    nt.links.new(col, bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    nt.links.new(_bump(nt, chips, 0.8, 0.01), bsdf.inputs["Normal"])


def led_grid(nt, bsdf, tile):
    # Subpixel triads at 10 mm pitch; the runtime multiplies its emissive content by this mask.
    v = _coord(nt, tile)
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (100.0, 100.0, 1)
    nt.links.new(v, mp.inputs["Vector"])
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(mp.outputs[0], sep.inputs[0])
    fx = _math(nt, "FRACT", sep.outputs[0])
    fy = _math(nt, "FRACT", sep.outputs[1])
    cell = _math(nt, "MULTIPLY", _math(nt, "LESS_THAN", fy, 0.82), _math(nt, "GREATER_THAN", fy, 0.12))
    comb = nt.nodes.new("ShaderNodeCombineColor")
    for i, (lo, hi) in enumerate(((0.05, 0.31), (0.36, 0.62), (0.67, 0.93))):
        band = _math(nt, "MULTIPLY", _math(nt, "GREATER_THAN", fx, lo), _math(nt, "LESS_THAN", fx, hi))
        nt.links.new(_math(nt, "MULTIPLY", band, cell), comb.inputs[i])
    nt.links.new(comb.outputs[0], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.25


def net_mask(nt, bsdf, tile):
    # Knotted square mesh, 120 mm cells and 4 mm twine; alpha is carried in the albedo's red channel.
    v = _coord(nt, tile)
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (1 / 0.12, 1 / 0.12, 1)
    nt.links.new(v, mp.inputs["Vector"])
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(mp.outputs[0], sep.inputs[0])
    fx = _math(nt, "FRACT", sep.outputs[0])
    fy = _math(nt, "FRACT", sep.outputs[1])
    w = 0.004 / 0.12
    lx = _math(nt, "LESS_THAN", _math(nt, "ABSOLUTE", _math(nt, "SUBTRACT", fx, 0.5)), w)
    ly = _math(nt, "LESS_THAN", _math(nt, "ABSOLUTE", _math(nt, "SUBTRACT", fy, 0.5)), w)
    twine = _math(nt, "MAXIMUM", lx, ly)
    nt.links.new(twine, bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8


LIBRARY = {
    # id: (recipe, tile metres, metallic)
    "concrete": (concrete, 2.0, 0.0),
    "painted_steel": (painted_steel, 1.0, 0.0),
    "galvanized": (galvanized, 1.0, 1.0),
    "seat_plastic": (seat_plastic, 0.5, 0.0),
    "roof_sheet": (roof_sheet, 1.0, 0.0),
    "rubber_floor": (rubber_floor, 1.0, 0.0),
    "net_mask": (net_mask, 0.48, 0.0),
}


def build_material(mid):
    recipe, tile, _metal = LIBRARY[mid]
    mat = bpy.data.materials.get("kit." + mid) or bpy.data.materials.new("kit." + mid)
    nt, bsdf = _nodes(mat)
    recipe(nt, bsdf, tile)
    mat["fe_material"] = mid
    mat["fe_tile_m"] = tile
    return mat


# ---- baking -----------------------------------------------------------------------------------------


def _seamless(img):
    """Cross-fade with a half-period roll: edges come from the rolled copy's continuous interior."""
    h, w = img.shape[:2]
    rolled = np.roll(np.roll(img, h // 2, 0), w // 2, 1)
    y = np.abs(np.linspace(-1, 1, h))[:, None]
    x = np.abs(np.linspace(-1, 1, w))[None, :]
    edge = np.clip((np.maximum(x, y) - 0.55) / 0.4, 0, 1)
    edge = edge * edge * (3 - 2 * edge)
    return img * (1 - edge[..., None]) + rolled * edge[..., None]


def _bsdf_source(nt, socket_name):
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    sock = bsdf.inputs[socket_name]
    return (sock.links[0].from_socket if sock.links else None), sock.default_value


def _bake(obj, mat, kind, res):
    """Colour and roughness go through an emission shader: a DIFFUSE bake would zero the colour of metals."""
    img = bpy.data.images.new(f"bake_{kind}", res, res, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    nt = mat.node_tree
    node = nt.nodes.new("ShaderNodeTexImage")
    node.image = img
    nt.nodes.active = node
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    surface = out.inputs["Surface"].links[0].from_socket
    if kind == "normal":
        bpy.context.scene.render.bake.normal_space = "TANGENT"
        bpy.ops.object.bake(type="NORMAL")
    else:
        src, default = _bsdf_source(nt, "Base Color" if kind == "color" else "Roughness")
        emit = nt.nodes.new("ShaderNodeEmission")
        if src is not None:
            nt.links.new(src, emit.inputs["Color"])
        elif kind == "color":
            emit.inputs["Color"].default_value = default
        else:
            emit.inputs["Color"].default_value = (default, default, default, 1)
        nt.links.new(emit.outputs[0], out.inputs["Surface"])
        bpy.ops.object.bake(type="EMIT")
        nt.links.new(surface, out.inputs["Surface"])
        nt.nodes.remove(emit)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(res, res, 4)
    nt.nodes.remove(node)
    bpy.data.images.remove(img)
    return px


def bake_library(out_dir, res=RES):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 8
    bpy.ops.mesh.primitive_plane_add(size=1.0)
    plane = bpy.context.active_object
    os.makedirs(out_dir, exist_ok=True)
    manifest = {}
    for mid, (_r, tile, metal) in LIBRARY.items():
        mat = build_material(mid)
        plane.data.materials.clear()
        plane.data.materials.append(mat)
        color = _seamless(_bake(plane, mat, "color", res))
        rough = _seamless(_bake(plane, mat, "rough", res))
        normal = _bake(plane, mat, "normal", res)
        # Normals are blended in vector space then renormalised so the seam stays a valid unit normal.
        n = _seamless(normal[..., :3] * 2 - 1)
        n /= np.linalg.norm(n, axis=-1, keepdims=True) + 1e-8
        normal[..., :3] = n * 0.5 + 0.5
        write_png(os.path.join(out_dir, f"{mid}_albedo.png"), color, srgb=True)
        orm = np.stack([np.ones((res, res)), rough[..., 0], np.full((res, res), metal), np.ones((res, res))], -1)
        write_png(os.path.join(out_dir, f"{mid}_orm.png"), orm, srgb=False)
        write_png(os.path.join(out_dir, f"{mid}_normal.png"), normal, srgb=False)
        manifest[mid] = {"tileMetres": tile, "metallic": metal}
        print("baked", mid)
    bpy.data.objects.remove(plane)
    return manifest


def write_png(path, px, srgb):
    res_y, res_x = px.shape[:2]
    data = np.clip(px, 0, 1)
    if srgb:
        rgb = data[..., :3]
        data = data.copy()
        data[..., :3] = np.where(rgb <= 0.0031308, rgb * 12.92, 1.055 * np.power(rgb, 1 / 2.4) - 0.055)
    img = bpy.data.images.new("out", res_x, res_y, alpha=True)
    img.colorspace_settings.name = "Non-Color"
    img.pixels[:] = data.astype(np.float32).ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
