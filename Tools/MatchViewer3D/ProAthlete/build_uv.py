"""Step 3a: lay every surface into the runtime 4x4 atlas tiles and export a rasterisable surface sample.

blender -b --python-exit-code 1 --python Tools/MatchViewer3D/ProAthlete/build_uv.py
Reads Pro-Athlete/kit.blend, writes Pro-Athlete/uv.blend and surface-sample.npz (consumed by paint_atlas.py).
"""
import importlib.util, json, math, sys
from pathlib import Path
import bpy, bmesh
import numpy as np

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('pa_common', Path(__file__).with_name('common.py'))
C = importlib.util.module_from_spec(spec); spec.loader.exec_module(C)

bpy.ops.wm.open_mainfile(filepath=str(C.OUT / 'kit.blend'))
rig = bpy.data.objects['FINAL_Rig']
OBJECTS = ['FE Skin', 'FE Eyes', 'FE Hair', 'FE Jersey', 'FE Shorts', 'FE Socks', 'FE Boots']
objs = [bpy.data.objects[n] for n in OBJECTS]
TILE = {name: i for i, name in enumerate(C.ATLAS_MATERIALS)}
# Island density multipliers: faces are what close cameras read; hidden torso skin is already deleted.
SKIN_DENSITY = {'head': 3.0, 'hand': 1.5}


def tile_of(obj, poly):
    return TILE[obj.data.materials[poly.material_index].name.replace('FE ', '')]


for o in objs:
    me = o.data
    atlas = me.uv_layers.new(name='Atlas')
    if o.name == 'FE Skin' and 'UVMap' in me.uv_layers:
        src = me.uv_layers['UVMap'].data
        for i, d in enumerate(atlas.data):
            d.uv = src[i].uv
    me.uv_layers.active = atlas
    for uv in [u for u in me.uv_layers if u.name != 'Atlas']:
        me.uv_layers.remove(uv)

scene = bpy.context.scene
scene.tool_settings.use_uv_select_sync = True


def uv_islands(bm, uv, faces):
    faces = set(faces)
    key = lambda l: (round(l[uv].uv.x, 6), round(l[uv].uv.y, 6))
    seen, islands = set(), []
    for f in faces:
        if f in seen:
            continue
        stack, island = [f], []
        seen.add(f)
        while stack:
            x = stack.pop(); island.append(x)
            for l in x.loops:
                for e_link in l.edge.link_loops:
                    g = e_link.face
                    if g in seen or g not in faces:
                        continue
                    # Shared edge is continuous in UV when both endpoints coincide.
                    a, b = key(l), key(l.link_loop_next)
                    c, d = key(e_link), key(e_link.link_loop_next)
                    if {a, b} == {c, d}:
                        seen.add(g); stack.append(g)
        islands.append(island)
    return islands


report = {}
for tile_name, tile in TILE.items():
    members = [o for o in objs if any(tile_of(o, p) == tile for p in o.data.polygons)]
    if not members:
        continue
    bpy.ops.object.mode_set(mode='OBJECT') if bpy.context.object and bpy.context.object.mode != 'OBJECT' else None
    bpy.ops.object.select_all(action='DESELECT')
    for o in members:
        o.select_set(True)
    bpy.context.view_layer.objects.active = members[0]
    bpy.ops.object.mode_set(mode='EDIT')
    for o in members:
        bm = bmesh.from_edit_mesh(o.data)
        for f in bm.faces:
            f.select = tile_of(o, o.data.polygons[f.index]) == tile
        bmesh.update_edit_mesh(o.data)
    if tile_name == 'skin':
        bpy.ops.uv.average_islands_scale()
        o = bpy.data.objects['FE Skin']
        bm = bmesh.from_edit_mesh(o.data); uv = bm.loops.layers.uv.active; dl = bm.verts.layers.deform.active
        gid = {g.name: g.index for g in o.vertex_groups}
        head_ids = {gid[n] for n in ('head', 'neck')}
        hand_ids = {gid[n] for n in gid if n.startswith(('hand.', 'thumb', 'index', 'middle', 'ring', 'little'))}
        islands = uv_islands(bm, uv, [f for f in bm.faces if f.select])
        for island in islands:
            verts = {v for f in island for v in f.verts}
            head = np.mean([sum(w for g, w in v[dl].items() if g in head_ids) for v in verts])
            hand = np.mean([sum(w for g, w in v[dl].items() if g in hand_ids) for v in verts])
            factor = 1 + (SKIN_DENSITY['head'] - 1) * head + (SKIN_DENSITY['hand'] - 1) * hand
            loops = [l for f in island for l in f.loops]
            centre = sum((l[uv].uv for l in loops), loops[0][uv].uv.copy() * 0) / len(loops)
            for l in loops:
                l[uv].uv = centre + (l[uv].uv - centre) * factor
        bmesh.update_edit_mesh(o.data)
        report['skinIslands'] = len(islands)
    else:
        bpy.ops.uv.smart_project(angle_limit=math.radians(62), island_margin=.003, area_weight=0.0, scale_to_bounds=False)
        bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(rotate=True, margin=.006)
    col, row = tile % 4, tile // 4
    for o in members:
        bm = bmesh.from_edit_mesh(o.data); uv = bm.loops.layers.uv.active
        for f in bm.faces:
            if f.select:
                for l in f.loops:
                    u, v = l[uv].uv
                    # Blender V is up; glTF flips it, so home row r occupies Blender V in [1-(r+1)/4, 1-r/4].
                    l[uv].uv = ((col + min(max(u, 0), 1)) / 4, 1 - (row + 1 - min(max(v, 0), 1)) / 4)
        bmesh.update_edit_mesh(o.data)
    bpy.ops.object.mode_set(mode='OBJECT')

# Surface sample: triangles in glTF UV space with rest positions, normals and authored attributes.
landmarks = json.loads((C.OUT / 'body-report.json').read_text())['landmarks']
sample = {}
for o in objs:
    me = o.data
    me.calc_loop_triangles()
    uv = me.uv_layers['Atlas'].data
    tris = me.loop_triangles
    n = len(tris)
    loops = np.array([t.loops[:] for t in tris])
    verts = np.array([t.vertices[:] for t in tris])
    uvs = np.array([d.uv[:] for d in uv])[loops]
    uvs[..., 1] = 1 - uvs[..., 1]
    co = np.array([v.co[:] for v in me.vertices])
    nrm = np.array([v.normal[:] for v in me.vertices])
    tile = np.array([tile_of(o, me.polygons[t.polygon_index]) for t in tris])
    key = o.name.replace('FE ', '').lower()
    sample[f'{key}_uv'] = uvs.astype(np.float32)
    sample[f'{key}_pos'] = co[verts].astype(np.float32)
    sample[f'{key}_nrm'] = nrm[verts].astype(np.float32)
    sample[f'{key}_tile'] = tile.astype(np.int8)
    for a in me.attributes:
        if a.name.startswith('fe_') and a.domain == 'POINT' and a.data_type == 'FLOAT':
            vals = np.empty(len(me.vertices), np.float32); a.data.foreach_get('value', vals)
            sample[f'{key}_{a.name}'] = vals[verts]
    report[o.name] = {'triangles': n}
    assert uvs.min() >= 0 and uvs[..., 0].max() <= 1 and uvs[..., 1].max() <= .5, o.name
sample['landmarks'] = np.array(json.dumps(landmarks))
np.savez_compressed(C.OUT / 'surface-sample.npz', **sample)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(C.OUT / 'uv.blend'))
print('UV_BUILT', json.dumps(report), flush=True)
