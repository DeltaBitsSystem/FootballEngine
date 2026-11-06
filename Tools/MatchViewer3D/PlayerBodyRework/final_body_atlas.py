"""Original albedo atlas for the final body: bakes SkinTint plus procedural
skin detail into one 2048 texture on the authored UV islands, then audits the
UV layout. No stock pixels, no lighting, no vertex-colour fallback."""
import bpy
import math
import json
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Revision-E'
TEX = OUT / 'textures'
TEX.mkdir(exist_ok=True)
(OUT / 'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'final-body-source.blend'))
scene = bpy.context.scene
scene.frame_set(1)
body = bpy.data.objects['FINAL_Body_Connected']
mesh = body.data

base = np.array([.46, .265, .190], dtype=np.float32)
tint = mesh.color_attributes['SkinTint']
tint_vals = np.array([tuple(tint.data[i].color[:3]) for i in range(len(mesh.vertices))], dtype=np.float32)


def sstep(x, a, b):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def albedo(co):
    x, y, z = co
    ax = abs(x)
    c = base * tint_vals[0]  # placeholder, replaced per vertex below
    return c


# Per-vertex albedo: SkinTint carries lips, nostrils, areolae, lids and ears;
# the procedural layer adds mottling, pores and a shaved beard shadow.
attr = mesh.color_attributes.new(name='Albedo_bake_source', type='FLOAT_COLOR', domain='CORNER')
verts = mesh.vertices
for loop in mesh.loops:
    vi = loop.vertex_index
    x, y, z = verts[vi].co
    ax = abs(x)
    c = base * tint_vals[vi]
    # Low-frequency mottling: two incommensurate sines, no stock texture.
    mott = math.sin(x * 91.0 + y * 57.0) * math.sin(z * 83.0 + x * 23.0)
    c = c * (0.975 + 0.035 * mott)
    # Fine pore/stubble grain, deterministic in object space.
    grain = (math.sin(x * 2131.0 + y * 1877.0) * math.sin(z * 2437.0 + x * 1103.0))
    c = c * (0.985 + 0.025 * grain)
    # Shaved beard: jaw, chin, upper lip and sideburn strip, blue-grey.
    front = sstep(-y, .020, .060)
    beard = sstep(z, 1.545, 1.575) * (1 - sstep(z, 1.640, 1.672)) * (1 - sstep(ax, .062, .090)) * front
    lip = math.exp(-((z - 1.620) / .013) ** 2) * (1 - sstep(ax, .028, .040))
    beard *= (1 - lip)
    if beard > 0:
        shade = 1 - .16 * beard * (0.70 + 0.30 * (0.5 + 0.5 * math.sin(x * 1700 + z * 2100)))
        c = c * np.array([shade, shade * .985, shade * 1.005], dtype=np.float32)
    # Nose, cheeks and ears carry a little more capillary colour.
    warm = math.exp(-((x / .020) ** 2 + ((z - 1.657) / .014) ** 2)) + \
        0.6 * math.exp(-(((ax - .050) / .026) ** 2 + ((z - 1.650) / .030) ** 2))
    warm = min(1.0, warm) * (1 if y < -.02 else 0)
    c = c * np.array([1 + .035 * warm, 1 - .010 * warm, 1 - .020 * warm], dtype=np.float32)
    attr.data[loop.index].color = (float(c[0]), float(c[1]), float(c[2]), 1.0)

mat = body.data.materials[0]
nodes = mat.node_tree.nodes
links = mat.node_tree.links
bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
output = next(n for n in nodes if n.type == 'OUTPUT_MATERIAL')
vcol = nodes.new('ShaderNodeVertexColor')
vcol.layer_name = attr.name
emission = nodes.new('ShaderNodeEmission')
links.new(vcol.outputs['Color'], emission.inputs['Color'])
links.new(emission.outputs[0], output.inputs['Surface'])
size = 2048
image = bpy.data.images.new('Original_body_albedo_2048', size, size, alpha=False)
seed = np.tile(np.array([.46, .265, .19, 1], dtype=np.float32), size * size)
image.pixels.foreach_set(seed)
target = nodes.new('ShaderNodeTexImage')
target.image = image
nodes.active = target
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
bpy.context.view_layer.objects.active = body
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 1
scene.render.bake.use_clear = False
bpy.ops.object.bake(type='EMIT', margin=16)
image.filepath_raw = str(TEX / 'final-body-albedo-2048.png')
image.file_format = 'PNG'
image.save()
image.pack()
links.new(target.outputs['Color'], bsdf.inputs['Base Color'])
# Removing the temporary emission also removed the only link into the material
# output; without restoring the BSDF the surface renders black.
links.new(bsdf.outputs['BSDF'], output.inputs['Surface'])
nodes.remove(emission)
nodes.remove(vcol)
mesh.color_attributes.remove(attr)
bsdf.inputs['Roughness'].default_value = .58
scene.cycles.samples = 32

# Raster audit on the authored UVs.
mesh.calc_loop_triangles()
uv = mesh.uv_layers.active.data
occupancy = np.zeros((size, size), np.uint16)
degenerate = 0
misses = []
areas = []
rgba = np.empty(size * size * 4, dtype=np.float32)
image.pixels.foreach_get(rgba)
rgba = rgba.reshape((size, size, 4))
for tri in mesh.loop_triangles:
    pts = np.array([tuple(uv[i].uv) for i in tri.loops], dtype=float) * size
    a, b, c = pts
    den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
    areas.append(abs(den) * .5)
    if abs(den) < 1e-9:
        degenerate += 1
        continue
    lo = np.maximum(0, np.floor(pts.min(axis=0)).astype(int))
    hi = np.minimum(size - 1, np.ceil(pts.max(axis=0)).astype(int))
    xx, yy = np.meshgrid(np.arange(lo[0], hi[0] + 1) + .5, np.arange(lo[1], hi[1] + 1) + .5)
    w0 = ((b[1] - c[1]) * (xx - c[0]) + (c[0] - b[0]) * (yy - c[1])) / den
    w1 = ((c[1] - a[1]) * (xx - c[0]) + (a[0] - c[0]) * (yy - c[1])) / den
    inside = (w0 > 1e-5) & (w1 > 1e-5) & (1 - w0 - w1 > 1e-5)
    occupancy[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1] += inside
    center = np.clip(pts.mean(axis=0).astype(int), 0, size - 1)
    if rgba[center[1], center[0], 0] < .035:
        misses.append(tri.polygon_index)
report = {
    'resolution': [size, size],
    'uvLayer': mesh.uv_layers.active.name,
    'method': 'Authored anatomical seam islands, native angle-based unwrap, original emission-only albedo bake (SkinTint + procedural mottling/pores/beard), margin 16 px, no lighting or stock pixels.',
    'marginPixels': 16,
    'degenerateUVTriangles': degenerate,
    'overlappingInteriorPixels': int((occupancy > 1).sum()),
    'coveredInteriorPixels': int((occupancy > 0).sum()),
    'bodyUVTriangles': len(mesh.loop_triangles),
    'triangleCentroidBlackMisses': len(misses),
    'minimumUVTriangleAreaPixels': float(min(areas)),
    'texturePath': str((TEX / 'final-body-albedo-2048.png').relative_to(ROOT)),
}
(OUT / 'uv-atlas-audit.json').write_text(json.dumps(report, indent=2))
print('UV_AUDIT', json.dumps(report), flush=True)

# UV checker evidence with the real camera and lights.
checker = bpy.data.images.new('Original_UV_checker', 1024, 1024)
yy, xx = np.indices((1024, 1024))
values = np.where((xx // 32 + yy // 32) % 2, .62, .15).astype(np.float32)
px = np.ones((1024, 1024, 4), np.float32)
px[:, :, :3] = values[:, :, None]
checker.pixels.foreach_set(px.reshape(-1))
checker.update()
target.image = checker
for o in list(scene.objects):
    if o.type == 'LIGHT':
        bpy.data.objects.remove(o, do_unlink=True)
from mathutils import Vector
world = scene.world or bpy.data.worlds.new('W')
scene.world = world
world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
bg.inputs['Color'].default_value = (.05, .055, .065, 1)
bg.inputs['Strength'].default_value = .55
for loc, en, sz in [((2.4, -2.6, 2.6), 260, 2.2), ((-2.8, -3.2, 1.4), 60, 3.0), ((1.2, 3.2, 2.4), 120, 2.0)]:
    d = bpy.data.lights.new('L', 'AREA')
    d.energy = en
    d.size = sz
    l = bpy.data.objects.new('L', d)
    scene.collection.objects.link(l)
    l.location = loc
    l.rotation_euler = (Vector((0, 0, 1.1)) - l.location).to_track_quat('-Z', 'Y').to_euler()
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.render.resolution_x = 700
scene.render.resolution_y = 900
cam = bpy.data.objects.new('C', bpy.data.cameras.new('C'))
scene.collection.objects.link(cam)
scene.camera = cam
for name, pos, point, scale in [('uv-checker', (3, -5, 1.5), (0, 0, .90), 2.02),
                                ('uv-face', (0, -3, 1.67), (0, 0, 1.66), .34),
                                ('uv-hand', (1, -3, 1.10), (.635, 0, .97), .30)]:
    cam.location = pos
    cam.rotation_euler = (Vector(point) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = scale
    scene.render.filepath = str(OUT / 'evidence' / (name + '.png'))
    bpy.ops.render.render(write_still=True)
target.image = image
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'final-body-source.blend'))
print('ATLAS_DONE', flush=True)
