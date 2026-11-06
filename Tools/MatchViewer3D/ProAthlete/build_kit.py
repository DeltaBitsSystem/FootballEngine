"""Step 2: original kit, boots and hair fitted to the ProAthlete body; hides fully covered skin.

blender -b --python-exit-code 1 --python Tools/MatchViewer3D/ProAthlete/build_kit.py
Reads Pro-Athlete/body.blend, writes Pro-Athlete/kit.blend. Every garment is generated here from the body surface.
"""
import importlib.util, json, math, sys
from pathlib import Path
import bpy, bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('pa_common', Path(__file__).with_name('common.py'))
C = importlib.util.module_from_spec(spec); spec.loader.exec_module(C)
K = json.loads(C.PARAMS_PATH.read_text())['kit']

bpy.ops.wm.open_mainfile(filepath=str(C.OUT / 'body.blend'))
rig = bpy.data.objects['FINAL_Rig']
skin = bpy.data.objects['FE Skin']
B = rig.data.bones
report = {}

ARM = ('upper_arm', 'forearm', 'hand', 'elbow_support') + tuple(f'{f}_0{i}' for f in C.FINGERS for i in (1, 2, 3))
LEG = ('thigh', 'shin', 'foot', 'toe', 'knee_support')


def weights_of(obj):
    names = {g.index: g.name for g in obj.vertex_groups}
    return [{names[g.group]: g.weight for g in v.groups} for v in obj.data.vertices]


def chain(w, parts, side):
    return sum(w.get(f'{p}.{side}', 0) for p in parts)


def duplicate(name):
    data = skin.data.copy(); data.name = name + ' Mesh'
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    # Mesh data stores weights by group index; the copy needs the same names in the same order.
    for g in skin.vertex_groups:
        obj.vertex_groups.new(name=g.name)
    obj.parent = rig
    data.materials.clear()
    return obj


def cut(bm, co, no, keep_vert=None):
    """Remove the side of a plane that `no` points to; `keep_vert` limits which geometry the plane may touch."""
    deform = bm.verts.layers.deform.active
    verts = [v for v in bm.verts if keep_vert is None or keep_vert(v, deform)]
    vs = set(verts)
    edges = [e for e in bm.edges if all(v in vs for v in e.verts)]
    faces = [f for f in bm.faces if all(v in vs for v in f.verts)]
    bmesh.ops.bisect_plane(bm, geom=verts + edges + faces, dist=1e-6, plane_co=co, plane_no=no, clear_outer=True)


def longest(bm):
    return max((e.calc_length() for e in bm.edges), default=0.0)


def keep_largest(bm, count=1, seeds=None):
    bm.verts.ensure_lookup_table()
    seen, parts = set(), []
    for v in bm.verts:
        if v in seen:
            continue
        stack, part = [v], []
        seen.add(v)
        while stack:
            x = stack.pop(); part.append(x)
            for e in x.link_edges:
                o = e.other_vert(x)
                if o not in seen:
                    seen.add(o); stack.append(o)
        parts.append(part)
    if seeds:
        # Dense faces outnumber larger regions, so a garment is chosen by the anatomy it must contain.
        chosen = [min(parts, key=lambda part: min((v.co - s).length for v in part)) for s in seeds]
        parts = chosen + [p for p in parts if not any(p is c for c in chosen)]
        count = len(chosen)
    else:
        parts.sort(key=len, reverse=True)
    for part in parts[count:]:
        bmesh.ops.delete(bm, geom=part, context='VERTS')


def relax(bm, iterations, factor, boundary_factor=None, pin=lambda v: False):
    """Laplacian relaxation removes muscular relief that cloth bridges; boundary loops relax along themselves."""
    for _ in range(iterations):
        new = {}
        for v in bm.verts:
            if pin(v):
                continue
            if v.is_boundary:
                if boundary_factor is None:
                    continue
                n = [e.other_vert(v) for e in v.link_edges if e.is_boundary]
                # A cut that grazes a vertex can join two unrelated loops at it; averaging across them throws
                # the vertex across the garment, so only short, same-loop neighbours are trusted.
                if len(n) == 2 and all((o.co - v.co).length < .05 for o in n):
                    new[v] = v.co.lerp((n[0].co + n[1].co) / 2, boundary_factor)
                continue
            n = [e.other_vert(v) for e in v.link_edges]
            if n:
                avg = sum((o.co for o in n), Vector()) / len(n)
                new[v] = v.co.lerp(avg, factor)
        for v, p in new.items():
            v.co = p
        bm.normal_update()


SKIN_BVH = None


def skin_bvh():
    global SKIN_BVH
    if SKIN_BVH is None:
        SKIN_BVH = BVHTree.FromPolygons([v.co.copy() for v in skin.data.vertices], [tuple(p.vertices) for p in skin.data.polygons])
    return SKIN_BVH


def clear_skin(bm, gap, bvh=None):
    """Push every garment vertex out of the body (or an inner layer) by at least `gap`, along its nearest normal."""
    bvh = bvh or skin_bvh()
    worst = 0.0
    for v in bm.verts:
        hit, normal, _, dist = bvh.find_nearest(v.co)
        if hit is None:
            continue
        signed = (v.co - hit).dot(normal)
        if signed < gap:
            worst = max(worst, gap - signed)
            v.co = hit + normal * gap + (v.co - hit - normal * signed)
    return worst


def finish(obj, bm, material, thickness, rim_material=None):
    bm.normal_update()
    bm.to_mesh(obj.data); bm.free()
    obj.data.update()
    for p in obj.data.polygons:
        p.use_smooth = True
    obj.data.materials.append(MATS[material])
    if rim_material:
        obj.data.materials.append(MATS[rim_material])
    bpy.context.view_layer.objects.active = obj
    sol = obj.modifiers.new('cloth thickness', 'SOLIDIFY')
    sol.thickness, sol.offset, sol.use_rim = thickness, -1.0, True
    sol.use_even_offset = True
    # Even offset scales by 1/cos of the fold angle; on a near-folded crease it threw a vertex 0.4 m, so the
    # offset is clamped to the neighbouring edge lengths.
    sol.thickness_clamp = 1.0
    if rim_material:
        sol.material_offset_rim = 1
    with bpy.context.temp_override(object=obj, active_object=obj):
        bpy.ops.object.modifier_apply(modifier=sol.name)
    arm = obj.modifiers.new('Runtime skin', 'ARMATURE'); arm.object = rig
    obj.parent = rig


MATS = {}
for name in C.ATLAS_MATERIALS:
    m = bpy.data.materials.new('FE ' + name); m.use_nodes = True
    MATS[name] = m
skin.data.materials.clear(); skin.data.materials.append(MATS['skin'])
eyes = bpy.data.objects['FE Eyes']; eyes.data.materials.clear(); eyes.data.materials.append(MATS['eyes'])

def bone_point(name, t):
    b = B[name]
    return b.head_local.lerp(b.tail_local, t), (b.tail_local - b.head_local).normalized()

hip_z = B['thigh.L'].head_local.z
ankle_z = B['foot.L'].head_local.z
neck_z = B['neck'].head_local.z
# A jersey hem below the crotch follows the inner thighs and reads as a leotard; the hem and the hidden shorts
# waist are placed relative to the measured crotch so every body type gets an untucked tube.
gen_mask = skin.data.attributes['fe_genitals'].data
crotch_z = min(v.co.z for v in skin.data.vertices
               if abs(v.co.x) < .015 and .4 < v.co.z < hip_z + .05 and gen_mask[v.index].value < .02)
K['jerseyHemZ'] = max(K['jerseyHemZ'], crotch_z + K['hemAboveCrotch'])
K['shortsWaistZ'] = max(K['shortsWaistZ'], K['jerseyHemZ'] + K['shortsUnderJersey'])
report['crotchZ'], report['jerseyHemZ'] = crotch_z, K['jerseyHemZ']

gid = {g.name: g.index for g in skin.vertex_groups}
deform = None
def wsum(v, names):
    d = v[deform]
    return sum(d.get(gid[n], 0) for n in names if n in gid)

# ---------------- Shorts ----------------
# Built first: the untucked jersey must hang outside the shorts, so the jersey clears against them.
shorts = duplicate('FE Shorts')
bm = bmesh.new(); bm.from_mesh(shorts.data); deform = bm.verts.layers.deform.active
arm_all = [f'{p}.{s}' for p in ARM for s in C.SIDES]
bmesh.ops.delete(bm, geom=[v for v in bm.verts if wsum(v, arm_all) > .3], context='VERTS')
cut(bm, Vector((0, 0, K['shortsWaistZ'])), Vector((0, 0, 1)))
for s in C.SIDES:
    co, axis = bone_point(f'thigh.{s}', K['shortsT'])
    leg = [f'{p}.{s}' for p in LEG] + [f'hip_support.{s}']
    cut(bm, co, axis, lambda v, d, names=leg: wsum(v, names) > .3)
keep_largest(bm)
relax(bm, K['shortsRelax'], .5, boundary_factor=.5)
for v in bm.verts:
    # Shorts flare toward the hem the way a loose woven leg opening does, without tracing the gluteal fold.
    hem = max(0., min(1., (K['shortsWaistZ'] - v.co.z) / (K['shortsWaistZ'] - B['thigh.L'].head_local.lerp(B['thigh.L'].tail_local, K['shortsT']).z)))
    v.co += v.normal * (K['shortsEase'] + K['shortsFlare'] * hem ** 1.5)
report['shortsMaxPushMm'] = clear_skin(bm, K['shortsGap']) * 1000
relax(bm, 2, .3, boundary_factor=.6)
clear_skin(bm, K['shortsGap'])
finish(shorts, bm, 'shorts', K['clothThickness'], rim_material='trim')
shorts_bvh = BVHTree.FromPolygons([v.co.copy() for v in shorts.data.vertices], [tuple(p.vertices) for p in shorts.data.polygons])

# ---------------- Jersey ----------------
jersey = duplicate('FE Jersey')
bm = bmesh.new(); bm.from_mesh(jersey.data)
deform = bm.verts.layers.deform.active
for s in C.SIDES:
    co, axis = bone_point(f'upper_arm.{s}', K['sleeveT'])
    arm_names = [f'{p}.{s}' for p in ARM] + [f'axilla_support.{s}']
    cut(bm, co, axis, lambda v, d, names=arm_names: wsum(v, names) > .15)
cut(bm, Vector((0, 0, K['jerseyHemZ'])), Vector((0, 0, -1)))
report['jerseyEdgeAfterCuts'] = longest(bm)
# Crew neckline: an elliptical opening on a plane that dips at the front, so it never clips the shoulders.
z_front, z_back = neck_z + K['collarFrontDz'], neck_z + K['collarBackDz']
# The opening is sized from the measured neck just above the neckline: a fixed ellipse failed to encircle a
# more muscular neck, which left the head attached to the jersey.
# Probed above the trapezius slope so the shoulders do not widen the opening into a boat neck.
band = [v.co for v in skin.data.vertices if z_back + K['collarProbeLow'] < v.co.z < z_back + K['collarProbeHigh']
        and abs(v.co.x) < .10 and abs(v.co.y - B['neck'].head_local.y) < .12]
neck_y = (max(p.y for p in band) + min(p.y for p in band)) / 2 + K['collarDy']
ca = max(K['collarA'], max(abs(p.x) for p in band) + K['collarMargin'])
cb = max(K['collarB'], (max(p.y for p in band) - min(p.y for p in band)) / 2 + K['collarMargin'])
report['collarEllipse'] = {'a': ca, 'b': cb, 'centreY': neck_y}
def collar_z(y):
    return z_front + (z_back - z_front) * max(0., min(1., (y - (neck_y - cb)) / (2 * cb)))
def in_neck(co):
    return (co.x / ca) ** 2 + ((co.y - neck_y) / cb) ** 2 < 1 and co.z > collar_z(co.y)
bmesh.ops.delete(bm, geom=[f for f in bm.faces if any(in_neck(v.co) for v in f.verts)], context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
keep_largest(bm, seeds=[B['chest'].head_local + Vector((0, -.12, 0))])
report['jerseyEdgeAfterNeck'] = longest(bm)
for v in bm.verts:
    if v.is_boundary and v.co.z > K['jerseyHemZ'] + .3 and abs(v.co.x) < ca * 1.6:
        r = math.sqrt((v.co.x / ca) ** 2 + ((v.co.y - neck_y) / cb) ** 2)
        v.co.x /= r; v.co.y = neck_y + (v.co.y - neck_y) / r; v.co.z = collar_z(v.co.y)
relax(bm, K['jerseyRelax'], .5, boundary_factor=.5)
report['jerseyEdgeAfterRelax'] = longest(bm)
for v in bm.verts:
    # Fabric hangs straight from the chest and shoulder blades instead of tracing the waist.
    waist = math.exp(-((v.co.z - K['waistZ']) / .16) ** 2)
    v.co += v.normal * (K['jerseyEase'] + K['jerseyWaistEase'] * waist)
report['jerseyMaxPushMm'] = clear_skin(bm, K['jerseyGap']) * 1000
relax(bm, 2, .3, boundary_factor=.6)
report['jerseyMaxPush2Mm'] = clear_skin(bm, K['jerseyGap']) * 1000
report['jerseyOverShortsPushMm'] = clear_skin(bm, K['jerseyOverShortsGap'], shorts_bvh) * 1000

def collar_band(bm):
    """Snap the neckline to a low-order smooth curve, then add a ribbed collar band like a real kit's."""
    deform = bm.verts.layers.deform.active
    ring = [v for v in bm.verts if v.is_boundary and v.co.z > K['jerseyHemZ'] + .3 and abs(v.co.x) < .16]
    cx = 0.0; cy = sum(v.co.y for v in ring) / len(ring)
    th = np.array([math.atan2(v.co.x - cx, -(v.co.y - cy)) for v in ring])
    r = np.array([math.hypot(v.co.x - cx, v.co.y - cy) for v in ring]); z = np.array([v.co.z for v in ring])
    basis = lambda t: np.stack([np.ones_like(t)] + [f(k * t) for k in range(1, 5) for f in (np.cos, np.sin)], axis=1)
    cr = np.linalg.lstsq(basis(th), r, rcond=None)[0]; cz = np.linalg.lstsq(basis(th), z, rcond=None)[0]
    for v, t in zip(ring, th):
        rr, zz = basis(np.array([t])) @ cr, basis(np.array([t])) @ cz
        v.co = Vector((cx + rr[0] * math.sin(t), cy - rr[0] * math.cos(t), zz[0]))
    n = 72
    loops = []
    height, inset = K['collarBandHeight'], K['collarBandInset']
    for level, (dz, dr) in enumerate(((-.002, .002), (height * .5, -inset * .5 + .001), (height, -inset))):
        loop = []
        for i in range(n):
            t = -math.pi + 2 * math.pi * i / n
            rr = (basis(np.array([t])) @ cr)[0] + dr; zz = (basis(np.array([t])) @ cz)[0] + dz
            nv = bm.verts.new((cx + rr * math.sin(t), cy - rr * math.cos(t), zz))
            src = min(ring, key=lambda v: (v.co - nv.co).length)
            for k, w in src[deform].items():
                nv[deform][k] = w
            loop.append(nv)
        loops.append(loop)
    for a, b in zip(loops, loops[1:]):
        for i in range(n):
            f = bm.faces.new((a[i], a[(i + 1) % n], b[(i + 1) % n], b[i]))
            f.material_index = 1
    bm.normal_update()
    return len(ring)

report['jerseyEdgeBeforeBand'] = longest(bm)
report['collarRingVertices'] = collar_band(bm)
report['jerseyEdgeAfterBand'] = longest(bm)
finish(jersey, bm, 'jersey', K['clothThickness'], rim_material='trim')

# ---------------- Socks ----------------
socks = duplicate('FE Socks')
bm = bmesh.new(); bm.from_mesh(socks.data); deform = bm.verts.layers.deform.active
leg_all = [f'{p}.{s}' for p in ('shin', 'foot', 'toe', 'knee_support') for s in C.SIDES]
bmesh.ops.delete(bm, geom=[v for v in bm.verts if wsum(v, leg_all) < .5], context='VERTS')
for s in C.SIDES:
    co, axis = bone_point(f'shin.{s}', K['sockTopT'])
    cut(bm, co, -axis, lambda v, d, sx=1 if s == 'L' else -1: v.co.x * sx > 0)
cut(bm, Vector((0, 0, K['sockBottomZ'])), Vector((0, 0, -1)))
keep_largest(bm, count=2)
relax(bm, 3, .4, boundary_factor=.5)
for v in bm.verts:
    v.co += v.normal * K['sockEase']
clear_skin(bm, K['sockGap'])
finish(socks, bm, 'socks', .0015, rim_material='trim')

# ---------------- Boots ----------------
boots = duplicate('FE Boots')
bm = bmesh.new(); bm.from_mesh(boots.data); deform = bm.verts.layers.deform.active
cut(bm, Vector((0, 0, ankle_z + K['bootCollarDz'])), Vector((0, 0, 1)))
keep_largest(bm, count=2)
# Toes and arch are smoothed into a last; a boot is a rigid shell, not a sock over each toe.
relax(bm, K['bootRelax'], .5, boundary_factor=.4)
for v in bm.verts:
    toe = max(0., min(1., (B['foot.L'].head_local.y - v.co.y - .06) / .12))
    v.co += v.normal * (K['bootEase'] + .004 * toe)
clear_skin(bm, K['bootGap'])
relax(bm, 2, .3, boundary_factor=.5)
clear_skin(bm, K['bootGap'])
for v in bm.verts:
    if v.co.z < K['soleFlattenZ']:
        v.co.z = 0.0
    elif v.co.z < K['soleFlattenZ'] + .01:
        v.co.z = (v.co.z - K['soleFlattenZ']) / .01 * (K['soleFlattenZ'] + .01)
for s, sx in (('L', 1), ('R', -1)):
    side = [v for v in bm.verts if v.co.x * sx > 0]
    tip = min(v.co.y for v in side)
    # rig_geometry needs a stable toe edge; a 1.5 mm cap is invisible but defines the ball-contact point.
    for v in side:
        if v.co.y < tip + .0015:
            v.co.y = tip
bm.normal_update()
bm.to_mesh(boots.data); bm.free(); boots.data.update()
for p in boots.data.polygons:
    p.use_smooth = True
boots.data.materials.append(MATS['boots']); boots.data.materials.append(MATS['trim'])
for p in boots.data.polygons:
    if all(boots.data.vertices[i].co.z < .004 for i in p.vertices):
        p.material_index = 1
arm = boots.modifiers.new('Runtime skin', 'ARMATURE'); arm.object = rig

# ---------------- Hair ----------------
hair = duplicate('FE Hair')
bm = bmesh.new(); bm.from_mesh(hair.data); deform = bm.verts.layers.deform.active
top = max(v.co.z for v in bm.verts)
centre = Vector((0, B['head'].head_local.y + K['hairCentreDy'], 0))
# Hairline drop below the crown by azimuth from the face: temples recede, it dips over the ears and down the nape.
HAIRLINE_AZ = [0, 30, 50, 75, 100, 135, 180]
def hairline(co):
    az = abs(math.degrees(math.atan2(co.x - centre.x, -(co.y - centre.y))))
    return top - float(np.interp(az, HAIRLINE_AZ, K['hairlineDrop']))
ears = bm.verts.layers.float['fe_ears']
def in_hair(v):
    # Ears are excluded by their anatomical mask; a head-weight threshold notched the temples.
    return v.co.z > hairline(v.co) and v[ears] < .3
bmesh.ops.delete(bm, geom=[f for f in bm.faces if not all(in_hair(v) for v in f.verts)], context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
keep_largest(bm)
# Low ear-adjacent head weights punch small holes inside the cut; only the outer hairline may stay open.
bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=30)
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
relax(bm, 6, .4, boundary_factor=.8)
report['hairline'] = {'top': top, 'centreY': centre.y, 'azimuthDeg': HAIRLINE_AZ, 'drop': K['hairlineDrop']}
edge = [v.co.copy() for v in bm.verts if v.is_boundary]
kd = KDTree(len(edge))
for i, p in enumerate(edge):
    kd.insert(p, i)
kd.balance()
fade_layer = bm.verts.layers.float.new('fe_hair_fade')
from mathutils import noise as mnoise
for v in bm.verts:
    line = hairline(v.co)
    rise = max(0., min(1., (v.co.z - line) / max(1e-4, top - line)))
    # Thickness fades to nothing at the hairline so the cut reads as hair growing from skin, not a cap edge.
    fade = max(0., min(1., kd.find(v.co)[2] / K['hairTaper'])) ** .7
    v[fade_layer] = fade
    # Short-back-and-sides: crown volume broken into clumps, sides kept close to the scalp.
    crown = rise ** 1.6
    clump = 1 + K['hairClump'] * crown * mnoise.noise(v.co * 55)
    thickness = (K['hairSideThickness'] + (K['hairTopThickness'] - K['hairSideThickness']) * crown) * clump
    v.co += v.normal * fade * thickness
    v.co.y -= K['hairFringeForward'] * crown * max(0., -(v.co.y - centre.y)) / .1 * fade
relax(bm, 3, .35, boundary_factor=.5)
clear_skin(bm, .0008)
finish(hair, bm, 'hair', .0012)
# Relaxed hair faces dip between their vertices on convex scalp; sinking the covered scalp keeps skin from
# showing through while the hairline itself stays flush.
ears_attr = skin.data.attributes['fe_ears'].data
sunk = 0
for v in skin.data.vertices:
    depth = v.co.z - hairline(v.co)
    if depth > 0 and ears_attr[v.index].value < .3:
        v.co -= v.normal * K['scalpSink'] * min(1., depth / .01)
        sunk += 1
skin.data.update()
report['scalpSunkVertices'] = sunk

# ---------------- Hide covered skin ----------------
garments = [jersey, shorts, socks, boots]
hidden = 0
bvhs, kds = [], []
for g in garments:
    me = g.data
    bvhs.append(BVHTree.FromPolygons([v.co.copy() for v in me.vertices], [tuple(p.vertices) for p in me.polygons]))
    bm = bmesh.new(); bm.from_mesh(me)
    edge_pts = [v.co.copy() for v in bm.verts if v.is_boundary]
    bm.free()
    kd = KDTree(max(1, len(edge_pts)))
    for i, p in enumerate(edge_pts):
        kd.insert(p, i)
    kd.balance(); kds.append((kd, len(edge_pts)))
covered = np.zeros(len(skin.data.vertices), bool)
for v in skin.data.vertices:
    for bvh, (kd, n) in zip(bvhs, kds):
        hit = bvh.find_nearest(v.co, K['hideDistance'])
        if hit[0] is None:
            continue
        far_from_edge = n == 0 or kd.find(v.co)[2] > K['hideEdgeMargin']
        if far_from_edge:
            covered[v.index] = True; break
bm = bmesh.new(); bm.from_mesh(skin.data)
doomed = [f for f in bm.faces if all(covered[v.index] for v in f.verts)]
hidden = len(doomed)
bmesh.ops.delete(bm, geom=doomed, context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
bm.to_mesh(skin.data); bm.free(); skin.data.update()
report['hiddenSkinFaces'] = hidden

for g in garments + [hair, skin, eyes]:
    longest = max((g.data.vertices[e.vertices[0]].co - g.data.vertices[e.vertices[1]].co).length for e in g.data.edges)
    report[g.name] = {'vertices': len(g.data.vertices), 'triangles': sum(len(p.vertices) - 2 for p in g.data.polygons),
                      'longestEdgeM': longest}
torn = {k: r['longestEdgeM'] for k, r in report.items() if isinstance(r, dict) and r.get('longestEdgeM', 0) >= K['maxEdgeM']}
(C.OUT / 'kit-report.json').write_text(json.dumps(report, indent=2))
# A garment edge this long is a torn spike, never cloth; exporting it would show a needle in game.
assert not torn, f'torn garment edges: {torn}'
report['totalTriangles'] = sum(r['triangles'] for k, r in report.items() if isinstance(r, dict) and 'triangles' in r)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(C.OUT / 'kit.blend'))
(C.OUT / 'kit-report.json').write_text(json.dumps(report, indent=2))
print('KIT_BUILT', json.dumps(report), flush=True)
