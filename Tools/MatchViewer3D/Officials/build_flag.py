"""Assistant referee's flag: 0.5 m handle and a 0.4 x 0.3 m cloth quartered in two colours.

    blender -b --factory-startup --python Tools/MatchViewer3D/Officials/build_flag.py

Origin is the grip point; the handle runs along +Z so the runtime parents it to the hand bone with a fixed
offset. The cloth carries `_REGION` (0 handle, 1 colour A, 2 colour B) and `_WAVE` (0 at the pole, 1 at the
free edge) so the shader can flutter it without a simulation.
Output: FootballEngine.Client/Assets/MatchViewer/Officials/{flag.glb, flag.json}.
"""
import hashlib
import json
import math
import os

import bmesh
import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "FootballEngine.Client", "Assets", "MatchViewer", "Officials")

bpy.ops.wm.read_factory_settings(use_empty=True)
os.makedirs(OUT, exist_ok=True)
bm = bmesh.new()
region = bm.verts.layers.float.new("_region")
wave = bm.verts.layers.float.new("_wave")
res = bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.012, radius2=0.010, depth=0.5)
for v in res["verts"]:
    v.co.z += 0.15  # grip 10 cm above the handle's bottom end
    v[region] = 0.0
nu, nv = 8, 6
grid = []
for j in range(nv + 1):
    row = []
    for i in range(nu + 1):
        u, t = i / nu, j / nv
        v = bm.verts.new((0.012 + 0.40 * u, 0.0, 0.40 - 0.30 * t))
        # Quartered design, a common officials' pattern made of two plain colours.
        v[region] = 1.0 if (u < 0.5) == (t < 0.5) else 2.0
        v[wave] = u
        row.append(v)
    grid.append(row)
for j in range(nv):
    for i in range(nu):
        bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
me = bpy.data.meshes.new("assistant_flag")
bm.to_mesh(me)
bm.free()
obj = bpy.data.objects.new("assistant_flag", me)
bpy.context.scene.collection.objects.link(obj)
glb = os.path.join(OUT, "flag.glb")
bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_yup=True, export_attributes=True,
                          export_materials="NONE")
meta = {"schema": 1, "node": "assistant_flag", "grip": "origin; handle along +Y (glTF)", "regions": ["handle", "colourA", "colourB"],
        "triangles": sum(len(p.vertices) - 2 for p in me.polygons), "doubleSided": True,
        "files": {"flag.glb": hashlib.sha256(open(glb, "rb").read()).hexdigest()},
        "source": "Tools/MatchViewer3D/Officials/build_flag.py", "provenance": "Original FootballEngine content."}
json.dump(meta, open(os.path.join(OUT, "flag.json"), "w"), indent=1)
print("exported", glb, meta["triangles"])
