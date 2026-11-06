"""Measure the final evaluated skin, retaining absolute lengths and worst cases."""
import json
from pathlib import Path
import bpy
import bmesh
import numpy as np

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Revision-Sol'
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'final-body-source.blend'))
scene = bpy.context.scene
scene.frame_set(1)
body = bpy.data.objects['FINAL_Body_Connected']
mesh = body.data
mesh.calc_loop_triangles()
rest = np.array([v.co[:] for v in mesh.vertices], dtype=np.float64)
edges = np.array([e.vertices[:] for e in mesh.edges])
triangles = np.array([t.vertices[:] for t in mesh.loop_triangles])
lengths = np.linalg.norm(rest[edges[:, 1]] - rest[edges[:, 0]], axis=1)


def areas(points):
    return np.linalg.norm(np.cross(points[triangles[:, 1]]-points[triangles[:, 0]],
                                   points[triangles[:, 2]]-points[triangles[:, 0]]), axis=1) * .5


rest_areas = areas(rest)
if np.min(lengths) <= 1e-10 or np.min(rest_areas) <= 1e-14:
    raise RuntimeError('Degenerate rest edge or triangle; ratios would be undefined.')
bm = bmesh.new()
bm.from_mesh(mesh)
bm.verts.ensure_lookup_table()
seen = set()
components = 0
for vertex in bm.verts:
    if vertex.index in seen:
        continue
    components += 1
    stack = [vertex]
    seen.add(vertex.index)
    while stack:
        v = stack.pop()
        for edge in v.link_edges:
            other = edge.other_vert(v)
            if other.index not in seen:
                seen.add(other.index)
                stack.append(other)
topology = {'vertices': len(mesh.vertices), 'triangles': len(triangles),
            'components': components, 'boundaryEdges': sum(e.is_boundary for e in bm.edges),
            'nonManifoldEdges': sum(not e.is_manifold for e in bm.edges),
            'signedRestVolumeM3': bm.calc_volume(signed=True)}
bm.free()
groups = {g.index: g.name for g in body.vertex_groups}
weights = [[(groups[g.group], g.weight) for g in v.groups if g.weight > 1e-8] for v in mesh.vertices]
topology.update({'maxInfluences': max(map(len, weights)),
                 'unweighted': sum(not w for w in weights),
                 'maxWeightSumError': max(abs(sum(w for _, w in ws)-1) for ws in weights),
                 'heightM': float(np.ptp(rest[:, 2]))})
accessory_checks=[]
rig=bpy.data.objects['FINAL_Rig']
for obj in scene.objects:
    if obj.type!='MESH' or not obj.name.startswith('GEO_') or obj.parent!=rig:
        continue
    copy=obj.data.copy()
    changed=copy.validate(verbose=False,clean_customdata=False)
    bpy.data.meshes.remove(copy)
    ws=[[g.weight for g in v.groups if g.weight>1e-8] for v in obj.data.vertices]
    check={'name':obj.name,'invalidGeometryRequiringRepair':changed,
           'unweighted':sum(not x for x in ws),'maxInfluences':max(map(len,ws)),
           'maxWeightSumError':max(abs(sum(x)-1) for x in ws)}
    accessory_checks.append(check)
    if changed or check['unweighted'] or check['maxWeightSumError']>1e-5:
        raise RuntimeError(f'Invalid accessory source: {check}')
report = {'topology': topology, 'accessories':accessory_checks, 'poses': [], 'method': 'All edges and triangles, no percentile exclusions. Includes intermediate animation frames.'}
region_names=json.loads((OUT/'final-body-authoring.json').read_text())['regionNames']
region_ids=mesh.attributes['Anatomical_region_id'].data
sole_indices={}
for sf in ['L','R']:
    foot=[i for i,a in enumerate(region_ids) if region_names[a.value].endswith('.'+sf) and region_names[a.value].startswith(('foot.','heel.','forefoot.','toe'))]
    lowest=float(np.min(rest[foot,2]))
    sole_indices[sf]=[i for i in foot if rest[i,2]<lowest+.002]
for frame in range(1,201):
    scene.frame_set(frame)
    obj = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    evaluated = obj.to_mesh()
    points = np.array([v.co[:] for v in evaluated.vertices], dtype=np.float64)
    obj.to_mesh_clear()
    if not np.all(np.isfinite(points)):
        raise RuntimeError(f'Nonfinite pose {frame}')
    posed_lengths = np.linalg.norm(points[edges[:, 1]]-points[edges[:, 0]], axis=1)
    ratios = posed_lengths / lengths
    area_ratios = areas(points) / rest_areas
    extrema = {}
    for name, index in [('stretch', int(np.argmax(ratios))), ('compression', int(np.argmin(ratios)))]:
        ids = edges[index].tolist()
        extrema[name] = {'edge': index, 'vertexIds': ids,
                         'ratio': float(ratios[index]), 'restLengthMm': float(lengths[index]*1000),
                         'posedLengthMm': float(posed_lengths[index]*1000),
                         'restPositionsM': rest[ids].tolist(),
                         'weights': [weights[i] for i in ids]}
    report['poses'].append({'frame': frame, 'extrema': extrema,
                            'p01': float(np.quantile(ratios, .01)), 'p99': float(np.quantile(ratios, .99)),
                            'minTriangleAreaRatio': float(np.min(area_ratios)),
                            'maxTriangleAreaRatio': float(np.max(area_ratios))})
    if frame==160:
        report['footPlantedSquat']={sf:{
            'soleMinZRestM':float(np.min(rest[indices,2])),
            'soleMinZPosedM':float(np.min(points[indices,2])),
            'maxSoleVertexDisplacementMm':float(np.max(np.linalg.norm(points[indices]-rest[indices],axis=1)))*1000,
        } for sf,indices in sole_indices.items()}
scene.frame_set(1)
(OUT / 'sol-validation.json').write_text(json.dumps(report, indent=2))
print(json.dumps({'topology': topology, 'poses': [{
    'frame': p['frame'], 'stretch': p['extrema']['stretch']['ratio'],
    'compression': p['extrema']['compression']['ratio']} for p in report['poses']]}, indent=2))
