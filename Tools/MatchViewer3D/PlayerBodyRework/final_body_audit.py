"""Topology, weight, UV and asset audit for the final body. Read-only."""
import bpy
import bmesh
import json
from pathlib import Path

ROOT = Path('/home/pablo/Projects/FootballEngine')
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Revision-E'
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'final-body-source.blend'))
scene = bpy.context.scene
scene.frame_set(1)
body = bpy.data.objects['FINAL_Body_Connected']
me = body.data
bm = bmesh.new()
bm.from_mesh(me)
boundary = sum(1 for e in bm.edges if e.is_boundary)
nonmanifold = sum(1 for e in bm.edges if not e.is_manifold)
loose = sum(1 for v in bm.verts if not v.link_faces)
seen = set()
components = 0
for v in bm.verts:
    if v.index in seen:
        continue
    components += 1
    stack = [v]
    seen.add(v.index)
    while stack:
        x = stack.pop()
        for e in x.link_edges:
            o = e.other_vert(x)
            if o.index not in seen:
                seen.add(o.index)
                stack.append(o)
bm.free()
groups = {g.index: g.name for g in body.vertex_groups}
unweighted = 0
max_inf = 0
over4 = 0
sums = []
for v in me.vertices:
    ws = [(groups[g.group], g.weight) for g in v.groups if g.weight > 1e-6]
    if not ws:
        unweighted += 1
    max_inf = max(max_inf, len(ws))
    if len(ws) > 4:
        over4 += 1
    sums.append(sum(w for _, w in ws))
xs = [v.co.x for v in me.vertices]
ys = [v.co.y for v in me.vertices]
zs = [v.co.z for v in me.vertices]
rig = bpy.data.objects['FINAL_Rig']
action = rig.animation_data.action if rig.animation_data else None
mesh_objects = sorted(o.name for o in scene.objects if o.type == 'MESH' and o.name.startswith('GEO_'))
report = {
    'blend': str((OUT / 'final-body-source.blend').relative_to(ROOT)),
    'bodyObject': body.name,
    'vertices': len(me.vertices),
    'edges': len(me.edges),
    'faces': len(me.polygons),
    'triangles': sum(len(p.vertices) - 2 for p in me.polygons),
    'boundaryEdges': boundary,
    'nonManifoldEdges': nonmanifold,
    'looseVertices': loose,
    'connectedComponents': components,
    'unweightedVertices': unweighted,
    'maxInfluences': max_inf,
    'verticesOver4Influences': over4,
    'minWeightSum': min(sums),
    'maxWeightSum': max(sums),
    'heightMeters': max(zs) - min(zs),
    'boundsMeters': {'x': [min(xs), max(xs)], 'y': [min(ys), max(ys)], 'z': [min(zs), max(zs)]},
    'uvLayers': [l.name for l in me.uv_layers],
    'colorAttributes': [a.name for a in me.color_attributes],
    'materials': [m.name for m in me.materials],
    'imageTextures': [(i.name, list(i.size), bool(i.packed_file)) for i in bpy.data.images if i.source == 'FILE'],
    'armature': rig.name,
    'bones': len(rig.data.bones),
    'action': action.name if action else None,
    'actionFrameRange': list(action.frame_range) if action else None,
    'accessoryMeshes': mesh_objects,
    'sceneFrame': scene.frame_current,
}
(OUT / 'topology-audit.json').write_text(json.dumps(report, indent=2))
print('AUDIT', json.dumps(report), flush=True)
