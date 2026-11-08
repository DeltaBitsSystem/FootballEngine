"""Original match ball: truncated-icosahedron panels with stitched grooves and a region attribute.

    blender -b --factory-startup --python Tools/MatchViewer3D/Ball/build_ball.py -- [--preview DIR]

The 32-panel layout is plain geometry (an Archimedean solid); colours are runtime palette slots, so no printed
design or brand is baked in. Output: FootballEngine.Client/Assets/MatchViewer/Ball/{ball.glb, ball.json, ball.blend}.
"""

import hashlib
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "FootballEngine.Client", "Assets", "MatchViewer", "Ball")
RADIUS = 0.11  # FIFA size 5: 68-70 cm circumference
GROOVE = 0.0012  # stitched seams sit ~1 mm below the panel crown

PANEL, ACCENT, SEAM = 0, 1, 2


def build(lod):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=0, radius=RADIUS)
    # Cutting every vertex at a third of its edges turns the icosahedron into 12 pentagons + 20 hexagons.
    bmesh.ops.bevel(bm, geom=list(bm.verts), offset=33.333, offset_type="PERCENT", affect="VERTICES", segments=1)
    seam = bm.faces.layers.float.new("_seam")
    accent = bm.faces.layers.float.new("_accent")
    for f in bm.faces:
        f[accent] = 1.0 if len(f.verts) == 5 else 0.0
    bmesh.ops.inset_individual(bm, faces=list(bm.faces), thickness=0.0028 if lod == 0 else 0.0035)
    # The inset ring is the stitched seam: thin quads, two orders of magnitude smaller than any panel.
    for f in bm.faces:
        if f.calc_area() < 1e-3:
            f[seam] = 1.0
    me = bpy.data.meshes.new(f"ball.lod{lod}")
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(me.name, me)
    bpy.context.scene.collection.objects.link(obj)
    sub = obj.modifiers.new("sub", "SUBSURF")
    # Simple (not Catmull-Clark) subdivision: smoothing would widen the thin seam ring into a band.
    sub.subdivision_type = "SIMPLE"
    sub.levels = 2 if lod == 0 else 1
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    for m in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    seam = bm.faces.layers.float["_seam"]
    accent = bm.faces.layers.float["_accent"]

    def region_of(f):
        # Zones come from the face each subdivided face descends from, never from a distance guess.
        if f[seam] > 0.5:
            return SEAM
        return ACCENT if f[accent] > 0.5 else PANEL

    regions = {f: region_of(f) for f in bm.faces}
    # Split where zones meet so each vertex belongs to one zone: a per-vertex id then has hard colour edges.
    boundary = [e for e in bm.edges if len(e.link_faces) == 2 and regions[e.link_faces[0]] != regions[e.link_faces[1]]]
    bmesh.ops.split_edges(bm, edges=boundary)
    reg_layer = bm.verts.layers.float.new("_region")
    for f in bm.faces:
        r = region_of(f)
        for v in f.verts:
            v[reg_layer] = float(r)
    for v in bm.verts:
        v.co = v.co.normalized() * RADIUS
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    for name in ("_seam", "_accent"):
        me.attributes.remove(me.attributes[name])
    return obj


def main():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT, exist_ok=True)
    objs = [build(0), build(1)]
    if "--preview" in a:
        preview(a[a.index("--preview") + 1], objs[0])
        return
    glb = os.path.join(OUT, "ball.glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_yup=True, export_attributes=True,
                              export_materials="NONE")
    meta = {"schema": 1, "radiusMetres": RADIUS, "regions": ["panel", "accent", "seam"],
            "lods": [{"node": o.name, "triangles": sum(len(p.vertices) - 2 for p in o.data.polygons)} for o in objs],
            "files": {"ball.glb": hashlib.sha256(open(glb, "rb").read()).hexdigest()},
            "source": {"generator": "Tools/MatchViewer3D/Ball/build_ball.py", "blender": bpy.app.version_string},
            "provenance": "Original FootballEngine content: geometry from a truncated icosahedron built in this "
                          "script; colours are runtime palette slots, no printed graphics."}
    json.dump(meta, open(os.path.join(OUT, "ball.json"), "w"), indent=1)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "ball.blend"), compress=True)
    print("exported", glb, [l["triangles"] for l in meta["lods"]])


def preview(out_dir, obj):
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = scene.render.resolution_y = 600
    scene.view_settings.view_transform = "AgX"
    w = bpy.data.worlds.new("w")
    scene.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.4, 0.45, 0.5, 1)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4
    sun.rotation_euler = (math.radians(40), 0, math.radians(40))
    scene.collection.objects.link(sun)
    mat = bpy.data.materials.new("ball")
    mat.use_nodes = True
    nt = mat.node_tree
    at = nt.nodes.new("ShaderNodeAttribute")
    at.attribute_name = "_region"
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = "CONSTANT"
    e = ramp.color_ramp.elements
    e[0].color = (0.85, 0.85, 0.83, 1)
    e[1].position = 0.4
    e[1].color = (0.03, 0.05, 0.12, 1)
    s = e.new(0.8)
    s.color = (0.25, 0.25, 0.26, 1)
    m = nt.nodes.new("ShaderNodeMath")
    m.operation = "DIVIDE"
    m.inputs[1].default_value = 2.4
    nt.links.new(at.outputs["Fac"], m.inputs[0])
    nt.links.new(m.outputs[0], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    nt.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.35
    obj.data.materials.append(mat)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 85
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.location = (0.45, -0.6, 0.35)
    cam.rotation_euler = (-cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(out_dir, "ball.png")
    bpy.ops.render.render(write_still=True)


main()
