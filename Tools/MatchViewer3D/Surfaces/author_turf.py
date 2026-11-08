"""Original pitch turf: thousands of modelled grass blades rendered top-down into a seamless PBR tile.

    blender -b --factory-startup --python Tools/MatchViewer3D/Surfaces/author_turf.py -- [--res 1024] [--samples 32]

The tile covers 1.0 m. Blades are instanced by Geometry Nodes on a 3x3 grid of the same seeded point set, so
blades that cross an edge continue on the opposite side and the crop is seamless by construction.
Passes are lighting-independent (diffuse colour, normals, ambient occlusion), so nothing is baked twice at
runtime. Output: Tools/MatchViewer3D/Surfaces/_passes/{albedo,normal,ao}.exr; run turf_pack.py for the PNG sources.
"""

import math
import os
import sys

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
SEED = 9127
TILE = 1.0


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    res = int(a[a.index("--res") + 1]) if "--res" in a else 1024
    samples = int(a[a.index("--samples") + 1]) if "--samples" in a else 32
    return res, samples


def blade_mesh():
    """A 28 mm blade: tapered strip of 4 segments with a gentle bend, base at the origin."""
    me = bpy.data.meshes.new("blade")
    verts, faces = [], []
    seg = 4
    for i in range(seg + 1):
        t = i / seg
        w = 0.0017 * (1 - t) + 0.0002
        bend = 0.006 * t * t
        z = 0.028 * t
        verts += [(-w, bend, z), (w, bend, z)]
    for i in range(seg):
        a = i * 2
        faces.append((a, a + 1, a + 3, a + 2))
    me.from_pydata(verts, [], faces)
    obj = bpy.data.objects.new("blade", me)
    bpy.context.scene.collection.objects.link(obj)
    obj.hide_render = True
    mat = bpy.data.materials.new("blade")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    # Per-blade colour from the instance's random value: young bright blades, older yellowed ones, dark bases.
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_type = "INSTANCER"
    attr.attribute_name = "shade"
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    e = ramp.color_ramp.elements
    e[0].position, e[0].color = 0.0, (0.030, 0.085, 0.012, 1)
    e[1].position, e[1].color = 1.0, (0.085, 0.170, 0.030, 1)
    mid = e.new(0.7)
    mid.color = (0.065, 0.150, 0.022, 1)
    nt.links.new(attr.outputs["Fac"], ramp.inputs["Fac"])
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Object"], sep.inputs[0])
    tip = nt.nodes.new("ShaderNodeMapRange")
    tip.inputs["From Max"].default_value = 0.028
    tip.inputs["To Min"].default_value = 0.55
    tip.inputs["To Max"].default_value = 1.15
    nt.links.new(sep.outputs["Z"], tip.inputs["Value"])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs[0].default_value = 1.0
    nt.links.new(ramp.outputs["Color"], mix.inputs[6])
    comb = nt.nodes.new("ShaderNodeCombineColor")
    for i in range(3):
        nt.links.new(tip.outputs["Result"], comb.inputs[i])
    nt.links.new(comb.outputs[0], mix.inputs[7])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.78
    me.materials.append(mat)
    return obj


def soil():
    me = bpy.data.meshes.new("soil")
    h = TILE * 1.5
    me.from_pydata([(-h, -h, 0), (h, -h, 0), (h, h, 0), (-h, h, 0)], [], [(0, 1, 2, 3)])
    obj = bpy.data.objects.new("soil", me)
    bpy.context.scene.collection.objects.link(obj)
    mat = bpy.data.materials.new("thatch")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    n = nt.nodes.new("ShaderNodeTexNoise")
    n.inputs["Scale"].default_value = 60.0
    n.inputs["Detail"].default_value = 8.0
    r = nt.nodes.new("ShaderNodeValToRGB")
    r.color_ramp.elements[0].color = (0.020, 0.030, 0.010, 1)
    r.color_ramp.elements[1].color = (0.050, 0.060, 0.020, 1)
    nt.links.new(n.outputs["Fac"], r.inputs["Fac"])
    nt.links.new(r.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.95
    me.materials.append(mat)
    return obj


def blades(blade):
    """Seeded points on one tile, copied to the 3x3 neighbourhood, each instancing a randomly posed blade."""
    me = bpy.data.meshes.new("field")
    me.from_pydata([(-TILE / 2, -TILE / 2, 0), (TILE / 2, -TILE / 2, 0), (TILE / 2, TILE / 2, 0), (-TILE / 2, TILE / 2, 0)], [], [(0, 1, 2, 3)])
    obj = bpy.data.objects.new("field", me)
    bpy.context.scene.collection.objects.link(obj)
    gn = bpy.data.node_groups.new("turf", "GeometryNodeTree")
    gn.interface.new_socket("Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
    gn.interface.new_socket("Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
    N, L = gn.nodes, gn.links
    gi, go = N.new("NodeGroupInput"), N.new("NodeGroupOutput")
    dist = N.new("GeometryNodeDistributePointsOnFaces")
    # ~30k shoots per square metre: a dense, closely mown match surface.
    dist.inputs["Density"].default_value = 30000.0
    dist.inputs["Seed"].default_value = SEED
    L.new(gi.outputs[0], dist.inputs["Mesh"])
    join = N.new("GeometryNodeJoinGeometry")
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            tr = N.new("GeometryNodeTransform")
            tr.inputs["Translation"].default_value = (dx * TILE, dy * TILE, 0)
            L.new(dist.outputs["Points"], tr.inputs["Geometry"])
            L.new(tr.outputs[0], join.inputs[0])
    inst = N.new("GeometryNodeInstanceOnPoints")
    info = N.new("GeometryNodeObjectInfo")
    info.inputs["Object"].default_value = blade
    L.new(join.outputs[0], inst.inputs["Points"])
    L.new(info.outputs["Geometry"], inst.inputs["Instance"])
    # Random pose from the point id: the 3x3 copies share ids, so a blade crossing an edge matches its twin.
    idn = N.new("GeometryNodeInputID")
    rot = N.new("FunctionNodeRandomValue")
    rot.data_type = "FLOAT_VECTOR"
    rot.inputs["Min"].default_value = (-0.55, -0.55, 0.0)
    rot.inputs["Max"].default_value = (0.55, 0.55, 2 * math.pi)
    rot.inputs["Seed"].default_value = SEED + 1
    L.new(idn.outputs[0], rot.inputs["ID"])
    L.new(rot.outputs["Value"], inst.inputs["Rotation"])
    sc = N.new("FunctionNodeRandomValue")
    sc.data_type = "FLOAT"
    sc.inputs["Min"].default_value = 0.65
    sc.inputs["Max"].default_value = 1.25
    sc.inputs["Seed"].default_value = SEED + 2
    L.new(idn.outputs[0], sc.inputs["ID"])
    L.new(sc.outputs["Value"], inst.inputs["Scale"])
    shade = N.new("FunctionNodeRandomValue")
    shade.data_type = "FLOAT"
    shade.inputs["Seed"].default_value = SEED + 3
    L.new(idn.outputs[0], shade.inputs["ID"])
    store = N.new("GeometryNodeStoreNamedAttribute")
    store.data_type = "FLOAT"
    store.domain = "INSTANCE"
    store.inputs["Name"].default_value = "shade"
    L.new(inst.outputs[0], store.inputs["Geometry"])
    L.new(shade.outputs["Value"], store.inputs["Value"])
    L.new(store.outputs[0], go.inputs[0])
    obj.modifiers.new("turf", "NODES").node_group = gn
    return obj


def _albedo_as_emission():
    """Every material shows its base colour as emission: an exact, lighting-free albedo render."""
    for mat in bpy.data.materials:
        if not mat.use_nodes:
            continue
        nt = mat.node_tree
        bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        out = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), None)
        if bsdf is None or out is None:
            continue
        em = nt.nodes.new("ShaderNodeEmission")
        sock = bsdf.inputs["Base Color"]
        if sock.links:
            nt.links.new(sock.links[0].from_socket, em.inputs["Color"])
        else:
            em.inputs["Color"].default_value = sock.default_value
        nt.links.new(em.outputs[0], out.inputs["Surface"])


def _override(kind):
    mat = bpy.data.materials.new(f"override_{kind}")
    mat.use_nodes = True
    nt = mat.node_tree
    out = nt.nodes["Material Output"]
    em = nt.nodes.new("ShaderNodeEmission")
    if kind == "normal":
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        vm = nt.nodes.new("ShaderNodeVectorMath")
        vm.operation = "MULTIPLY_ADD"
        vm.inputs[1].default_value = (0.5, 0.5, 0.5)
        vm.inputs[2].default_value = (0.5, 0.5, 0.5)
        nt.links.new(geo.outputs["Normal"], vm.inputs[0])
        nt.links.new(vm.outputs[0], em.inputs["Color"])
    else:
        ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
        # Occlusion at blade scale: the crevices between shoots, not the whole tile.
        ao.inputs["Distance"].default_value = 0.02
        ao.samples = 16
        nt.links.new(ao.outputs["AO"], em.inputs["Color"])
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    return mat


def render(res, samples):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.seed = SEED
    scene.render.resolution_x = scene.render.resolution_y = res
    scene.view_settings.view_transform = "Standard"
    scene.render.image_settings.file_format = "OPEN_EXR"
    scene.render.image_settings.color_depth = "32"
    world = bpy.data.worlds.new("w")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.0
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = TILE
    cam.location = (0, 0, 1.0)
    scene.collection.objects.link(cam)
    scene.camera = cam
    vl = scene.view_layers[0]
    for kind in ("normal", "ao"):
        vl.material_override = _override(kind)
        scene.render.filepath = os.path.join(HERE, "_passes", f"{kind}.exr")
        bpy.ops.render.render(write_still=True)
    vl.material_override = None
    _albedo_as_emission()
    scene.render.filepath = os.path.join(HERE, "_passes", "albedo.exr")
    bpy.ops.render.render(write_still=True)


def main():
    res, samples = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    soil()
    blades(blade_mesh())
    render(res, samples)
    print("TURF_BUILT", res)


main()
