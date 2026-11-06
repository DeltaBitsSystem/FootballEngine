"""Form pass 01 on the complete connected quad body.

Isolated batch: opens the archived Iteration2 staging file, never the live scene.
The original mesh stays in the file as the A reference; all edits are made on a
duplicated body so both can be rendered with identical cameras and lights.
"""
import bpy
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT = BASE / 'Final-Body'
(OUT / 'evidence').mkdir(parents=True, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=str(BASE / 'Iteration2/athlete-iteration2.blend'))
scene = bpy.context.scene
scene.frame_set(1)

source = bpy.data.objects['GEO_AthleteBody_Iteration2']
regions_names = json.loads((BASE / 'Iteration2/cage-construction.json').read_text())['regionNames']

# A reference copy keeps the untouched connected body in the same file.
reference = source.copy()
reference.data = source.data.copy()
scene.collection.objects.link(reference)
reference.name = 'FORM_A_connected_reference'
body = source
body.name = 'FORM_B_connected_body'
for obj in scene.objects:
    if obj.type in {'MESH', 'ARMATURE'} and obj not in {body, reference}:
        obj.hide_render = True
        obj.hide_set(True)
for obj in (body, reference):
    obj.hide_render = obj is not body
    obj.hide_set(obj is not body)

mesh = body.data
ref_mesh = reference.data
regions = [regions_names[d.value] for d in mesh.attributes['Anatomical_region_id'].data]
ref_regions = [regions_names[d.value] for d in ref_mesh.attributes['Anatomical_region_id'].data]
original = [v.co.copy() for v in mesh.vertices]


def smoothstep(x, a, b):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def band(x, a, b, c, d):
    return smoothstep(x, a, b) * (1 - smoothstep(x, c, d))


def interp_table(table, z):
    if z <= table[0][0]:
        return table[0][1:]
    if z >= table[-1][0]:
        return table[-1][1:]
    for (z0, *v0), (z1, *v1) in zip(table, table[1:]):
        if z0 <= z <= z1:
            t = (z - z0) / (z1 - z0)
            t = t * t * (3 - 2 * t)
            return [a + (b - a) * t for a, b in zip(v0, v1)]
    return table[-1][1:]


# Current torso silhouette measured from the source cage, used only as the
# divisor for the target table; this keeps the edit authored against real
# geometry instead of assumed coordinates.
bins = {}
for index, region in enumerate(ref_regions):
    if region != 'torso':
        continue
    p = ref_mesh.vertices[index].co
    key = round(p.z / 0.01) * 0.01
    b = bins.setdefault(key, [1e9, -1e9, 1e9, -1e9])
    b[0] = min(b[0], p.x)
    b[1] = max(b[1], p.x)
    b[2] = min(b[2], p.y)
    b[3] = max(b[3], p.y)
current = []
for z in sorted(bins):
    b = bins[z]
    current.append((z, max(abs(b[0]), b[1]), -b[2], b[3]))

# Athletic male silhouette at 1.80 m. Widths and depths are deliberately kept
# lean: the body reads through planes and transitions, not body-builder mass.
target = [
    (0.90, 0.160, 0.100, 0.128),
    (0.92, 0.160, 0.099, 0.130),
    (0.96, 0.156, 0.098, 0.120),
    (1.00, 0.151, 0.097, 0.107),
    (1.04, 0.146, 0.096, 0.099),
    (1.08, 0.140, 0.096, 0.093),
    (1.12, 0.142, 0.098, 0.094),
    (1.16, 0.150, 0.102, 0.098),
    (1.20, 0.160, 0.108, 0.104),
    (1.24, 0.172, 0.116, 0.112),
    (1.28, 0.180, 0.121, 0.116),
    (1.32, 0.185, 0.120, 0.113),
    (1.35, 0.190, 0.113, 0.108),
    (1.38, 0.195, 0.092, 0.100),
    (1.40, 0.195, 0.080, 0.092),
    (1.42, 0.135, 0.062, 0.080),
    (1.44, 0.100, 0.058, 0.072),
    (1.46, 0.090, 0.055, 0.062),
]

changes = {name: 0 for name in ['torso', 'arm', 'leg', 'head', 'neck', 'hand', 'foot']}
for vertex in mesh.vertices:
    index = vertex.index
    region = regions[index]
    p = vertex.co
    if region == 'torso':
        ax, front, back = interp_table(current, p.z)
        tx, tf, tb = interp_table(target, p.z)
        # Section scaling follows the real ring, so loop spacing is preserved.
        if 0.895 < p.z < 1.455:
            p.x *= tx / max(ax, 1e-5)
            if p.y < 0:
                p.y *= tf / max(front, 1e-5)
            else:
                p.y *= tb / max(back, 1e-5)
        changes['torso'] += 1

# Re-measure the edited torso so planar edits use the new surface.
mesh.update()

# --- Torso planes -----------------------------------------------------------
for vertex in mesh.vertices:
    if regions[vertex.index] != 'torso':
        continue
    p = vertex.co
    x, y, z = p.x, p.y, p.z
    ax = abs(x)
    if y >= -0.02 or not (1.14 < z < 1.455):
        continue
    anterior = 1 - smoothstep(y, -0.075, -0.02)
    # Pectoral plane: medial fibres run to the sternum, lateral fibres insert
    # under the deltoid; the lower edge turns sharply into the costal wall.
    pec = band(ax, 0.018, 0.050, 0.135, 0.180) * band(z, 1.225, 1.255, 1.355, 1.390) * anterior
    if pec:
        plane = -0.121 + 0.26 * (ax - 0.075) + 0.30 * (z - 1.305)
        p.y = p.y * (1 - 0.82 * pec) + plane * 0.82 * pec
        # A narrow costal furrow gives the pectoral a real inferior border.
        furrow = band(ax, 0.015, 0.045, 0.140, 0.175) * band(z, 1.215, 1.232, 1.242, 1.258)
        p.y += 0.0075 * furrow
    # Sternum is a shallow vertical valley between the two plates.
    sternum = (1 - smoothstep(ax, 0.006, 0.020)) * band(z, 1.235, 1.270, 1.395, 1.430) * anterior
    p.y += 0.0045 * sternum
    # Clavicle ridge and the hollow above it.
    clav_z = 1.438 - 0.16 * ax
    clav = band(ax, 0.005, 0.035, 0.155, 0.196) * (1 - smoothstep(abs(z - clav_z), 0.006, 0.016)) * anterior
    p.y -= 0.0045 * clav
    hollow = band(ax, 0.045, 0.075, 0.130, 0.160) * band(z, clav_z + 0.010, clav_z + 0.018, clav_z + 0.040, clav_z + 0.052) * anterior
    p.y += 0.0035 * hollow
    # Rectus plane: flat, with a soft linea alba and only two shallow
    # transverse valleys; no body-builder segmentation.
    rect = (1 - smoothstep(ax, 0.045, 0.072)) * band(z, 1.020, 1.045, 1.185, 1.225) * anterior
    if rect:
        plane = -0.109 - 0.010 * smoothstep(z, 1.03, 1.16)
        p.y = p.y * (1 - 0.55 * rect) + plane * 0.55 * rect
    linea = (1 - smoothstep(ax, 0.004, 0.014)) * band(z, 1.015, 1.045, 1.20, 1.235) * anterior
    p.y += 0.0028 * linea
    for crease_z in (1.118, 1.168):
        crease = (1 - smoothstep(ax, 0.012, 0.060)) * (1 - smoothstep(abs(z - crease_z), 0.003, 0.010)) * anterior
        p.y += 0.0018 * crease
    # Costal margin forms an inverted V from the sternum toward the flank.
    margin_z = 1.190 + 0.24 * ax
    margin = (1 - smoothstep(abs(z - margin_z), 0.004, 0.014)) * band(ax, 0.015, 0.040, 0.120, 0.155) * anterior
    p.y += 0.0030 * margin
    # Iliac crest and ASIS are ridges, not a horizontal box.
    crest_z = 1.000 - 0.24 * max(0.0, ax - 0.035)
    crest = (1 - smoothstep(abs(z - crest_z), 0.004, 0.014)) * band(ax, 0.030, 0.060, 0.125, 0.155)
    p.y -= 0.0032 * crest
    asis = (1 - smoothstep((Vector((ax, z)) - Vector((0.138, 0.962))).length, 0.010, 0.030))
    p.y -= 0.0028 * asis
    # Inguinal crease runs from ASIS to the pubic crest.
    ing_z = 0.955 - 0.55 * max(0.0, 0.135 - ax)
    ing = (1 - smoothstep(abs(z - ing_z), 0.003, 0.011)) * band(ax, 0.030, 0.055, 0.130, 0.152) * anterior
    p.y += 0.0025 * ing
    # Posterior: scapular plate, spine of scapula, erector ridges, spine groove.
    posterior = smoothstep(y, 0.025, 0.075)
    if posterior:
        scap = band(ax, 0.030, 0.055, 0.135, 0.175) * band(z, 1.235, 1.270, 1.365, 1.400)
        if scap:
            plane = 0.108 - 0.20 * (ax - 0.08) - 0.10 * (z - 1.31)
            p.y = p.y * (1 - 0.70 * scap) + plane * 0.70 * scap
            changes['torso'] += 1
        spine_scap = band(ax, 0.045, 0.065, 0.155, 0.185) * (1 - smoothstep(abs(z - (1.335 + 0.16 * ax)), 0.004, 0.013))
        p.y += 0.0032 * spine_scap
        groove = (1 - smoothstep(ax, 0.004, 0.016)) * band(z, 1.045, 1.080, 1.395, 1.430)
        p.y -= 0.0035 * groove
        erector = (1 - smoothstep(abs(ax - 0.030), 0.008, 0.022)) * band(z, 1.020, 1.050, 1.380, 1.420)
        p.y += 0.0024 * erector
        lat = band(ax, 0.100, 0.125, 0.165, 0.195) * band(z, 1.120, 1.160, 1.300, 1.350)
        p.y += 0.0030 * lat

# --- Shoulder girdle / deltoid ---------------------------------------------
for side in (1, -1):
    sf = 'L' if side > 0 else 'R'
    shoulder = Vector((side * 0.1766529, 0, 1.376033))
    wrist = Vector((side * 0.5578512, 0, 1.041322))
    axis = (wrist - shoulder).normalized()
    for vertex in mesh.vertices:
        region = regions[vertex.index]
        if region not in ('arm.' + sf, 'torso'):
            continue
        p = vertex.co
        if region == 'torso':
            if p.x * side < 0.13 or not (1.27 < p.z < 1.455):
                continue
            t = 0.06
        else:
            t = (p - shoulder).dot(axis) / (wrist - shoulder).length
            if t < 0 or t > 0.30:
                continue
        # The deltoid wraps the humeral head: a cap that is widest at the
        # acromion and inserts at the deltoid tuberosity, not a tube sleeve.
        cap = band(t, -0.10, 0.02, 0.20, 0.30)
        if cap <= 0:
            continue
        lateral = 1.0 - smoothstep(abs(p.y), 0.035, 0.090)
        p.x += side * 0.016 * cap
        p.z += 0.010 * cap
        p.y += (0.007 if p.y > 0 else -0.007) * cap * (1 - lateral * 0.6)
        changes['arm'] += 1

# --- Arm masses -------------------------------------------------------------
for side in (1, -1):
    sf = 'L' if side > 0 else 'R'
    shoulder = Vector((side * 0.1766529, 0, 1.376033))
    wrist = Vector((side * 0.5578512, 0, 1.041322))
    axis = (wrist - shoulder).normalized()
    length = (wrist - shoulder).length
    for vertex in mesh.vertices:
        if regions[vertex.index] != 'arm.' + sf:
            continue
        p = vertex.co
        t = (p - shoulder).dot(axis) / length
        radial = (p - (shoulder + axis * (p - shoulder).dot(axis)))
        if t < 0.06 or t > 0.97:
            continue
        # Biceps and triceps are directional masses, not a radial scale.
        front = max(0.0, -p.y) / max(radial.length, 1e-5)
        back = max(0.0, p.y) / max(radial.length, 1e-5)
        upper = band(t, 0.18, 0.27, 0.43, 0.52)
        p.y -= 0.0042 * front * upper
        p.y += 0.0032 * back * upper
        # Brachioradialis caps the forearm near the elbow.
        fore = band(t, 0.50, 0.56, 0.72, 0.80)
        p.x += side * 0.0022 * fore * (0.5 + 0.5 * abs(p.y) / max(radial.length, 1e-5))
        # Olecranon projects posteriorly at the elbow.
        olecranon = band(t, 0.46, 0.49, 0.53, 0.57) * max(0.0, p.y / max(radial.length, 1e-5))
        p.y += 0.0030 * olecranon

# --- Leg masses -------------------------------------------------------------
for side in (1, -1):
    sf = 'L' if side > 0 else 'R'
    hip_x = side * 0.100
    for vertex in mesh.vertices:
        region = regions[vertex.index]
        if region != 'leg.' + sf and region != 'torso':
            continue
        p = vertex.co
        if p.z > 0.87 or p.z < 0.10:
            continue
        if region == 'torso' and (p.x * side < 0.02 or p.y < 0.02):
            continue
        ax = abs(p.x)
        # Gluteal masses: upper projection fades into the lumbar back and ends
        # in the infragluteal fold, with a real intergluteal cleft.
        if p.y > 0.02:
            glute = band(p.z, 0.785, 0.835, 0.935, 0.985) * band(ax, 0.012, 0.032, 0.145, 0.175)
            if glute:
                p.y += 0.030 * glute
                p.z -= 0.004 * glute
            cleft = (1 - smoothstep(ax, 0.004, 0.016)) * band(p.z, 0.760, 0.790, 0.930, 0.975)
            p.y -= 0.013 * cleft
            fold = band(p.z, 0.770, 0.786, 0.798, 0.822) * band(ax, 0.020, 0.040, 0.135, 0.165)
            p.y -= 0.0045 * fold
        if region != 'leg.' + sf:
            continue
        # Thigh: quadriceps mass anteriorly, adductor medially, hamstring
        # posteriorly; all taper into the knee instead of staying cylindrical.
        thigh = band(p.z, 0.505, 0.555, 0.795, 0.860)
        if thigh:
            p.x += (p.x - hip_x) * 0.09 * thigh
            if p.y < 0:
                p.y -= 0.006 * thigh * band(p.z, 0.560, 0.620, 0.760, 0.820)
            else:
                p.y += 0.004 * thigh * band(p.z, 0.560, 0.610, 0.730, 0.790)
            # Vastus lateralis produces the lateral thigh silhouette.
            p.x += side * 0.0045 * thigh * smoothstep(p.x * side, 0.115, 0.160)
        # Patella plate and condyles.
        patella = (1 - smoothstep((Vector((abs(p.x) - 0.100, p.z - 0.492))).length, 0.010, 0.030)) * max(0.0, -p.y)
        p.y -= 0.0045 * patella
        condyle = band(p.z, 0.455, 0.470, 0.500, 0.515) * (1 - smoothstep(abs(abs(p.x) - 0.100), 0.020, 0.042))
        p.x += side * 0.0025 * condyle * (1 if abs(p.x) > 0.100 else -1)
        # Gastrocnemius: medial head lower and fuller than lateral.
        calf = band(p.z, 0.235, 0.270, 0.380, 0.430) * max(0.0, p.y / max((p - Vector((hip_x, 0, p.z))).length, 1e-5))
        p.y += 0.0060 * calf
        p.x += (p.x - hip_x) * 0.05 * band(p.z, 0.250, 0.300, 0.400, 0.450)
        # Tibial ridge and Achilles tendon.
        shin = (1 - smoothstep(abs(p.x - side * 0.095), 0.008, 0.022)) * band(p.z, 0.140, 0.180, 0.360, 0.410) * max(0.0, -p.y)
        p.y -= 0.0020 * shin
        achilles = (1 - smoothstep(abs(p.x - side * 0.100), 0.010, 0.026)) * band(p.z, 0.105, 0.130, 0.220, 0.260) * max(0.0, p.y)
        p.y -= 0.0025 * achilles

# --- Neck / trapezius -------------------------------------------------------
for vertex in mesh.vertices:
    region = regions[vertex.index]
    p = vertex.co
    if region == 'torso' and 1.435 < p.z < 1.515:
        # Trapezius rises from the acromion to the neck, closing the coat-hanger
        # gap without an inflated collar.
        trap = band(abs(p.x), 0.030, 0.055, 0.115, 0.165) * smoothstep(p.z, 1.435, 1.500)
        p.y += 0.008 * trap
        if abs(p.x) < 0.065:
            p.x *= 1.06
    if region == 'head' and 1.500 < p.z < 1.560:
        # Sternocleidomastoid ridges descend toward the sternal notch.
        ax = abs(p.x)
        scm = band(ax, 0.024, 0.040, 0.058, 0.070) * band(p.z, 1.500, 1.520, 1.575, 1.600) * max(0.0, -p.y)
        p.y -= 0.0025 * scm
    # Slightly fuller cranium and jaw, keeping the 1.80 m height.
    if region == 'head' and p.z > 1.600:
        skull = smoothstep(p.z, 1.66, 1.74) * (1 - smoothstep(p.z, 1.79, 1.80))
        p.x *= 1 + 0.035 * skull
        if p.y > 0:
            p.y *= 1 + 0.030 * skull

mesh.update()

# Metrics computed after every edit and on the final surface.
ref_coords = [v.co.copy() for v in ref_mesh.vertices]
deltas = [(v.co - ref_coords[v.index]).length for v in mesh.vertices]
by_region = {}
for index, d in enumerate(deltas):
    if d > 1e-6:
        key = regions[index].split('.')[0]
        by_region[key] = by_region.get(key, 0) + 1


def section(objmesh, region_list, z, region_prefix='torso'):
    values = [objmesh.vertices[i].co for i, r in enumerate(region_list)
              if r == region_prefix and abs(objmesh.vertices[i].co.z - z) < 0.008]
    if not values:
        return None
    return {
        'halfWidth': round(max(abs(v.x) for v in values), 4),
        'front': round(-min(v.y for v in values), 4),
        'back': round(max(v.y for v in values), 4),
    }


report = {
    'source': 'Iteration2/athlete-iteration2.blend',
    'heightMeters': round(max(v.co.z for v in mesh.vertices), 4),
    'changedVertices': {k: v for k, v in sorted(by_region.items())},
    'maxControlDisplacementMm': round(max(deltas) * 1000, 3),
    'sectionsBefore': {str(z): section(ref_mesh, ref_regions, z) for z in (0.92, 1.08, 1.20, 1.28, 1.35, 1.40)},
    'sectionsAfter': {str(z): section(mesh, regions, z) for z in (0.92, 1.08, 1.20, 1.28, 1.35, 1.40)},
    'note': 'Metrics are control-cage measurements after all edits; no rig, UV or export yet.',
}
(OUT / 'form-pass01-metrics.json').write_text(json.dumps(report, indent=2))

# --- Neutral clay studio ----------------------------------------------------
def clay(name, value):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (value, value, value, 1)
    bsdf.inputs['Roughness'].default_value = 0.62
    return mat


clay_mat = clay('Anatomy_clay_grey', 0.46)
for obj in (body, reference):
    obj.data.materials.clear()
    obj.data.materials.append(clay_mat)

world = scene.world or bpy.data.worlds.new('World')
scene.world = world
world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
bg.inputs['Color'].default_value = (0.035, 0.038, 0.045, 1)
bg.inputs['Strength'].default_value = 0.35

for obj in list(scene.objects):
    if obj.type == 'LIGHT':
        bpy.data.objects.remove(obj, do_unlink=True)
for name, location, energy, size, color in [
    ('FORM_key_side', (2.6, -2.2, 2.4), 420, 2.4, (1.0, 0.98, 0.95)),
    ('FORM_fill_front', (-2.8, -3.2, 1.2), 90, 3.0, (0.9, 0.94, 1.0)),
    ('FORM_rim_back', (1.2, 3.4, 2.2), 160, 2.0, (0.95, 0.97, 1.0)),
]:
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = energy
    data.size = size
    data.color = color
    light = bpy.data.objects.new(name, data)
    scene.collection.objects.link(light)
    light.location = location
    light.rotation_euler = (Vector((0, 0, 1.15)) - light.location).to_track_quat('-Z', 'Y').to_euler()

scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.cycles.denoiser = 'OPENIMAGEDENOISE'
scene.render.threads_mode = 'FIXED'
scene.render.threads = 6
scene.render.resolution_x = 640
scene.render.resolution_y = 800
scene.render.film_transparent = False

if 'Studio_floor_NOT_exported' in bpy.data.objects:
    floor = bpy.data.objects['Studio_floor_NOT_exported']
    floor.hide_render = False
    floor.hide_set(False)


def aim(camera, position, target, ortho):
    camera.location = position
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = ortho


camera = scene.camera
views = {
    'front': ((0, -5, 0.92), (0, 0, 0.92), 2.02),
    'profile': ((5, 0, 0.92), (0, 0, 0.92), 2.02),
    'back': ((0, 5, 0.92), (0, 0, 0.92), 2.02),
    'three-quarter': ((3.2, -4.6, 1.45), (0, 0, 0.92), 2.02),
    'torso-front': ((0, -5, 1.24), (0, 0, 1.24), 0.78),
    'torso-profile': ((5, 0, 1.24), (0, 0, 1.24), 0.78),
    'torso-back': ((0, 5, 1.24), (0, 0, 1.24), 0.78),
    'torso-three-quarter': ((3.2, -4.6, 1.45), (0, 0, 1.24), 0.78),
}
for prefix, obj in (('A', reference), ('B', body)):
    body.hide_render = obj is not body
    reference.hide_render = obj is not reference
    for name, (position, target, ortho) in views.items():
        aim(camera, position, target, ortho)
        scene.render.filepath = str(OUT / 'evidence' / f'{prefix}-{name}.png')
        bpy.ops.render.render(write_still=True)

body.hide_render = False
reference.hide_render = True
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'form-pass01.blend'))
print('FORM01', json.dumps(report), flush=True)
