"""Step 1: athletic body from MPFB's CC0 base, bound to the 60-joint runtime rig.

blender -b --python-exit-code 1 --python Tools/MatchViewer3D/ProAthlete/build_body.py
Requires the MPFB extension and the CC0 makehuman_system_assets pack. Writes Pro-Athlete/body.blend only.
"""
import importlib.util, json, math, sys
from pathlib import Path
import bpy, addon_utils
import numpy as np
from mathutils import Vector

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('pa_common', Path(__file__).with_name('common.py'))
C = importlib.util.module_from_spec(spec); spec.loader.exec_module(C)

addon_utils.enable('bl_ext.blender_org.mpfb', default_set=True, persistent=False)
from bl_ext.blender_org.mpfb.services.humanservice import HumanService
from bl_ext.blender_org.mpfb.services.targetservice import TargetService
from bl_ext.blender_org.mpfb.services.locationservice import LocationService

PARAMS = json.loads(C.PARAMS_PATH.read_text())
# Boots stand the body on their sole; the bind pose therefore floats the bare foot by the sole thickness.
SOLE_M = PARAMS['bootSoleM']

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

macro = TargetService.get_default_macro_info_dict()
macro.update({k: v for k, v in PARAMS['macro'].items() if k != 'race'})
macro['race'] = PARAMS['macro']['race']
human = HumanService.create_human(scale=0.1, macro_detail_dict=macro)
data_dir = Path(LocationService.get_mpfb_data()) / 'targets'
for name, weight in PARAMS['targets'].items():
    found = list(data_dir.rglob(name + '.target.gz'))
    assert len(found) == 1, f'target {name}: {found}'
    TargetService.load_target(human, str(found[0]), weight=weight, name=name)
HumanService.add_builtin_rig(human, 'game_engine', import_weights=True)
ge = human.parent
assert ge and ge.type == 'ARMATURE'

eyes_path = Path(LocationService.get_user_data()) / 'eyes/low-poly/low-poly.mhclo'
eyes = HumanService.add_mhclo_asset(str(eyes_path), human, asset_type='Eyes', subdiv_levels=0, material_type='MAKESKIN')

# Shape keys become plain geometry: runtime meshes carry no morph targets.
for obj in (human, eyes):
    bpy.context.view_layer.objects.active = obj
    if obj.data.shape_keys:
        obj.shape_key_add(name='FE_mix', from_mix=True)
        keys = obj.data.shape_keys.key_blocks
        mix = np.empty(len(obj.data.vertices) * 3); keys['FE_mix'].data.foreach_get('co', mix)
        obj.shape_key_clear(); obj.data.vertices.foreach_set('co', mix); obj.data.update()

# Joint cubes are helper geometry, so facial landmarks must be measured before helpers are deleted.
LANDMARK_GROUPS = ['joint-l-eye', 'joint-r-eye', 'joint-l-upperlid', 'joint-r-upperlid', 'joint-l-lowerlid',
                   'joint-r-lowerlid', 'joint-mouth', 'joint-jaw', 'joint-head', 'joint-head-2', 'joint-neck']
raw_landmarks = {}
for name in LANDMARK_GROUPS:
    gi = human.vertex_groups[name].index
    pts = [v.co.copy() for v in human.data.vertices if any(g.group == gi and g.weight > .5 for g in v.groups)]
    raw_landmarks[name] = sum(pts, Vector()) / len(pts)
# Anatomical masks become point attributes; the runtime skin keeps none of MakeHuman's helper groups.
MASK_GROUPS = {'fe_lips': 'lips', 'fe_ears': 'ears', 'fe_scalp': 'scalp', 'fe_nails': ('fingernails', 'toenails'),
               'fe_nipple': 'nipple', 'fe_genitals': 'genitals'}
for attr, groups in MASK_GROUPS.items():
    groups = (groups,) if isinstance(groups, str) else groups
    ids = {human.vertex_groups[g].index for g in groups}
    values = [max((g.weight for g in v.groups if g.group in ids), default=0.0) for v in human.data.vertices]
    a = human.data.attributes.new(attr, 'FLOAT', 'POINT'); a.data.foreach_set('value', values)

# MakeHuman ships helper geometry (tights, skirt, hair proxies) outside the 'body' group.
body_group = human.vertex_groups['body'].index
helpers = [v.index for v in human.data.vertices if all(g.group != body_group for g in v.groups)]
import bmesh
bm = bmesh.new(); bm.from_mesh(human.data); bm.verts.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.verts[i] for i in helpers], context='VERTS')
# Always under shorts: the anatomy is relaxed into a smooth surface so garments built from the body do not
# trace it (it produced a leotard-like V at the jersey hem and a bulge in the shorts).
gen = bm.verts.layers.float['fe_genitals']
region = [v for v in bm.verts if v[gen] > .02]
for _ in range(PARAMS['genitalRelaxIterations']):
    new = {v: v.co.lerp(sum((e.other_vert(v).co for e in v.link_edges), Vector()) / len(v.link_edges), .5) for v in region}
    for v, p in new.items():
        v.co = p
bm.to_mesh(human.data); bm.free()
for m in list(human.modifiers):
    if m.type == 'MASK':
        human.modifiers.remove(m)

P = np.array([v.co[:] for v in human.data.vertices])
stature = float(P[:, 2].max() - P[:, 2].min())
scale = C.STATURE_M / stature
for obj in (ge, human, eyes):
    obj.select_set(True)
bpy.context.view_layer.objects.active = ge
ge.scale = (scale, scale, scale)
bpy.ops.object.select_all(action='DESELECT')
for obj in (ge, human, eyes):
    obj.select_set(True)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
for obj in (human, eyes):
    for v in obj.data.vertices:
        v.co.z += SOLE_M
    obj.data.update()

G = {b.name: (b.head_local + Vector((0, 0, SOLE_M)), b.tail_local + Vector((0, 0, SOLE_M)), b) for b in ge.data.bones}

def joint(name):
    return G[C.ge_name(name)][0].copy()

heads, tails, rolls = {}, {}, {}
names = C.runtime_bones()
parent_of = dict(names)
child_of = {}
for n, p in names:
    child_of.setdefault(p, []).append(n)
for n, _ in names:
    if n.split('.')[0].endswith('support'):
        continue
    src = C.ge_name(n)
    heads[n], tails[n] = G[src][0].copy(), G[src][1].copy()
# Vertical trunk bones keep roll zero so authored spine flexion stays about world X.
heads['root'], tails['root'] = Vector((0, 0, 0)), Vector((0, 0, .186))
hip_mid = (joint('thigh.L') + joint('thigh.R')) / 2
heads['pelvis'] = Vector((0, hip_mid.y, hip_mid.z))
heads['spine'] = Vector((0, *G['spine_01'][0].yz))
heads['chest'] = Vector((0, *G['spine_03'][0].yz))
heads['neck'] = Vector((0, *G['neck_01'][0].yz))
heads['head'] = Vector((0, *G['head'][0].yz))
tails['pelvis'], tails['spine'], tails['chest'], tails['neck'] = heads['spine'], heads['chest'], heads['neck'], heads['head']
tails['head'] = Vector((0, heads['head'].y, heads['head'].z + .20))

# Contract lengths: keep the knee slightly lower than MPFB, then slide hip and ankle along their segments.
leg_report = {}
for s in C.SIDES:
    hip, knee, ankle = heads[f'thigh.{s}'], heads[f'shin.{s}'], heads[f'foot.{s}']
    knee = knee + Vector((0, 0, PARAMS['kneeDropM']))
    hip = knee + (hip - knee).normalized() * C.THIGH_M
    ankle = knee + (ankle - knee).normalized() * C.SHIN_M
    leg_report[s] = {'hipMoveMm': (hip - heads[f'thigh.{s}']).length * 1000,
                     'kneeMoveMm': abs(PARAMS['kneeDropM']) * 1000,
                     'ankleMoveMm': (ankle - heads[f'foot.{s}']).length * 1000,
                     'hipZ': hip.z, 'kneeZ': knee.z, 'ankleZ': ankle.z}
    heads[f'thigh.{s}'], tails[f'thigh.{s}'] = hip, knee
    heads[f'shin.{s}'], tails[f'shin.{s}'] = knee, ankle
    heads[f'foot.{s}'] = ankle
for s in C.SIDES:
    for support, source in C.SUPPORTS:
        heads[f'{support}.{s}'], tails[f'{support}.{s}'] = heads[f'{source}.{s}'], tails[f'{source}.{s}']
# Children start where their parent ends so that the exported hierarchy has no hidden offsets in limbs.
for s in C.SIDES:
    tails[f'clavicle.{s}'] = heads[f'upper_arm.{s}']
    tails[f'upper_arm.{s}'] = heads[f'forearm.{s}']
    tails[f'forearm.{s}'] = heads[f'hand.{s}']
    tails[f'foot.{s}'] = heads[f'toe.{s}']
    for support, source in C.SUPPORTS:
        tails[f'{support}.{s}'] = tails[f'{source}.{s}']

arm_data = bpy.data.armatures.new('FINAL_Rig')
rig = bpy.data.objects.new('FINAL_Rig', arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.ops.object.select_all(action='DESELECT')
bpy.context.view_layer.objects.active = rig; rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones
ge_roll = {}
for n, p in names:
    b = eb.new(n)
    b.head, b.tail = heads[n], tails[n]
    if p:
        b.parent = eb[p]
trunk = {'root', 'pelvis', 'spine', 'chest', 'neck', 'head'}
for n, _ in names:
    b = eb[n]
    if n in trunk:
        b.roll = 0
    else:
        source = n
        for support, src in C.SUPPORTS:
            if n.startswith(support + '.'):
                source = src + '.' + n.split('.')[1]
        b.align_roll(G[C.ge_name(source)][2].matrix_local.to_3x3().col[2])
    b.use_deform = n != 'root'
    if p := parent_of[n]:
        b.use_connect = (b.head - eb[p].tail).length < 1e-6 and not n.split('.')[0].endswith('support')
bpy.ops.object.mode_set(mode='OBJECT')
for pb in rig.pose.bones:
    pb.rotation_mode = 'QUATERNION'

# Weights: MPFB's painted game_engine groups renamed onto runtime bones; spine_02 straddles spine and chest.
def remap(obj):
    W = [dict() for _ in obj.data.vertices]
    ge_groups = {g.index: g.name for g in obj.vertex_groups}
    rename = {C.ge_name(n): n for n, _ in names if not n.split('.')[0].endswith('support') and n != 'root'}
    for v in obj.data.vertices:
        for g in v.groups:
            name = ge_groups[g.group]
            if name in rename:
                W[v.index][rename[name]] = W[v.index].get(rename[name], 0) + g.weight
            elif name == 'spine_02':
                for t in ('spine', 'chest'):
                    W[v.index][t] = W[v.index].get(t, 0) + g.weight * .5
            elif name == 'Root':
                W[v.index]['pelvis'] = W[v.index].get('pelvis', 0) + g.weight
    # Half-rotation helpers carry the shared crease, where linear blending of parent and child collapses volume.
    for w in W:
        for s in C.SIDES:
            for support, source in C.SUPPORTS:
                child = f'{source}.{s}'; par = parent_of[child]
                shared = min(w.get(par, 0), w.get(child, 0))
                if shared > 1e-4:
                    w[par] -= shared; w[child] -= shared
                    w[f'{support}.{s}'] = w.get(f'{support}.{s}', 0) + 2 * shared
    stats = {'unweighted': 0}
    for g in list(obj.vertex_groups):
        obj.vertex_groups.remove(g)
    groups = {n: obj.vertex_groups.new(name=n) for n, _ in names}
    for i, w in enumerate(W):
        top = sorted(((n, x) for n, x in w.items() if x > 1e-5), key=lambda t: -t[1])[:4]
        total = sum(x for _, x in top)
        if total <= 0:
            stats['unweighted'] += 1
            top, total = [('head', 1.0)], 1.0
        for n, x in top:
            groups[n].add([i], x / total, 'REPLACE')
    return stats

report = {'scale': scale, 'mpfbStatureM': stature, 'legs': leg_report, 'params': PARAMS,
          'landmarks': {k: list(v * scale + Vector((0, 0, SOLE_M))) for k, v in raw_landmarks.items()}}
for obj in (human, eyes):
    report[obj.name + 'Weights'] = remap(obj)
    for m in list(obj.modifiers):
        if m.type == 'ARMATURE':
            obj.modifiers.remove(m)
    mod = obj.modifiers.new('Runtime skin', 'ARMATURE'); mod.object = rig
    obj.parent = rig
human.name, human.data.name = 'FE Skin', 'FE Skin Mesh'
eyes.name, eyes.data.name = 'FE Eyes', 'FE Eyes Mesh'
bpy.data.objects.remove(ge, do_unlink=True)

# MakeHuman stands with feet ~40 cm apart; the runtime contract binds feet under the hips. Rotating each thigh
# (and counter-rolling the foot so the sole stays flat) preserves the contract bone lengths.
from mathutils import Matrix
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True); bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='POSE')
stance = {}
for s in C.SIDES:
    hip = rig.data.bones[f'thigh.{s}'].head_local.copy()
    ankle = rig.data.bones[f'foot.{s}'].head_local.copy()
    target_dx = (abs(hip.x) - PARAMS['ankleInsetFromHipM']) * (1 if hip.x > 0 else -1) - hip.x
    h = hip.z - ankle.z
    theta = math.atan2(ankle.x - hip.x, h) - math.atan2(target_dx, h)
    spin = lambda pivot, angle: Matrix.Translation(pivot) @ Matrix.Rotation(angle, 4, 'Y') @ Matrix.Translation(-pivot)
    for name in (f'thigh.{s}', f'hip_support.{s}'):
        pb = rig.pose.bones[name]; pb.matrix = spin(hip, theta) @ pb.matrix
        bpy.context.view_layer.update()
    foot = rig.pose.bones[f'foot.{s}']
    new_ankle = foot.matrix.translation.copy()
    foot.matrix = spin(new_ankle, -theta) @ foot.matrix
    bpy.context.view_layer.update()
    stance[s] = {'thighRollDeg': math.degrees(theta), 'ankleX': foot.matrix.translation.x}
bpy.ops.object.mode_set(mode='OBJECT')
for obj in (human, eyes):
    bpy.context.view_layer.objects.active = obj
    with bpy.context.temp_override(object=obj, active_object=obj):
        bpy.ops.object.modifier_apply(modifier='Runtime skin')
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='POSE')
bpy.ops.pose.select_all(action='SELECT')
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode='OBJECT')
for obj in (human, eyes):
    mod = obj.modifiers.new('Runtime skin', 'ARMATURE'); mod.object = rig
# Rest roll of the supports must equal their sources again after the pose is baked into the rest.
bpy.ops.object.mode_set(mode='EDIT')
for s in C.SIDES:
    for support, source in C.SUPPORTS:
        a, b = rig.data.edit_bones[f'{support}.{s}'], rig.data.edit_bones[f'{source}.{s}']
        a.head, a.tail, a.roll = b.head.copy(), b.tail.copy(), b.roll
bpy.ops.object.mode_set(mode='OBJECT')
# Swinging a leg inward about the hip lifts the foot (h·(1−cos θ)); the bare sole goes back onto the boot sole.
drop = SOLE_M - min(v.co.z for v in human.data.vertices)
for obj in (human, eyes):
    for v in obj.data.vertices:
        v.co.z += drop
    obj.data.update()
bpy.ops.object.mode_set(mode='EDIT')
# Absolute targets: moving a connected child's head also moves its parent's tail, so increments would double.
targets = {b.name: (b.head + Vector((0, 0, drop)), b.tail + Vector((0, 0, drop))) for b in rig.data.edit_bones if b.name != 'root'}
for name, (head, tail) in targets.items():
    b = rig.data.edit_bones[name]; b.head, b.tail = head, tail
bpy.ops.object.mode_set(mode='OBJECT')
for pb in rig.pose.bones:
    pb.rotation_mode = 'QUATERNION'
stance['bodyDropMm'] = drop * 1000
report['stance'] = stance
report['statureWithSoleM'] = max(v.co.z for v in human.data.vertices)

report['bones'] = len(rig.data.bones)
report['lengths'] = {n: rig.data.bones[n].length for n in ('thigh.L', 'shin.L', 'thigh.R', 'shin.R')}
report['statureWithSoleM'] = max(v.co.z for v in human.data.vertices)
report['skinVertices'] = len(human.data.vertices)
report['skinTriangles'] = sum(len(p.vertices) - 2 for p in human.data.polygons)
assert report['bones'] == 60
for n, t in (('thigh', C.THIGH_M), ('shin', C.SHIN_M)):
    for s in C.SIDES:
        assert abs(rig.data.bones[f'{n}.{s}'].length - t) < 1e-5
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(C.OUT / 'body.blend'))
(C.OUT / 'body-report.json').write_text(json.dumps(report, indent=2, default=str))
print('BODY_BUILT', json.dumps({k: report[k] for k in ('scale', 'lengths', 'statureWithSoleM', 'skinVertices', 'skinTriangles', 'legs')}, default=str), flush=True)
