"""Reopen both sources and compare topology, weights, UVs and posed surfaces."""
import hashlib
import json
from pathlib import Path

import bmesh
import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[4]
OUT = ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
FRAMES = [1,20,40,60,80,120,160,180,190,200]

def collect(path):
    bpy.ops.wm.open_mainfile(filepath=str(path))
    scene = bpy.context.scene
    scene.frame_set(1)
    body = bpy.data.objects['FINAL_Body_Connected']
    rig = bpy.data.objects['FINAL_Rig']
    mesh = body.data
    coords = np.array([v.co[:] for v in mesh.vertices], dtype=np.float64)
    edges = np.array([e.vertices[:] for e in mesh.edges])
    uv = np.array([item.uv[:] for item in mesh.uv_layers.active.data], dtype=np.float32)
    weights = [[(body.vertex_groups[g.group].name, g.weight) for g in v.groups] for v in mesh.vertices]
    attr = [d.value for d in mesh.attributes['Anatomical_region_id'].data]
    bm = bmesh.new()
    bm.from_mesh(mesh)
    topology = {'vertices':len(mesh.vertices), 'polygons':len(mesh.polygons),
                'boundaryEdges':sum(e.is_boundary for e in bm.edges),
                'nonManifoldEdges':sum(not e.is_manifold for e in bm.edges)}
    bm.free()
    rest_lengths = np.linalg.norm(coords[edges[:,1]]-coords[edges[:,0]],axis=1)
    poses = {}
    for frame in FRAMES:
        scene.frame_set(frame)
        obj = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
        evaluated = obj.to_mesh()
        points = np.array([v.co[:] for v in evaluated.vertices], dtype=np.float64)
        obj.to_mesh_clear()
        ratio = np.linalg.norm(points[edges[:,1]]-points[edges[:,0]],axis=1)/rest_lengths
        assert np.all(np.isfinite(points)) and np.all(np.isfinite(ratio))
        poses[str(frame)] = {'points':points, 'maxEdgeRatio':float(np.max(ratio)), 'minEdgeRatio':float(np.min(ratio)), 'p99EdgeRatio':float(np.quantile(ratio,.99))}
    images = {im.name: {'size':list(im.size), 'packed': bool(im.packed_file)} for im in bpy.data.images if im.name.startswith('Original_body_')}
    return {'coords':coords,'edges':edges,'uv':uv,'weights':weights,'regions':attr,'topology':topology,'poses':poses,
            'bones':[b.name for b in rig.data.bones], 'action':rig.animation_data.action.name if rig.animation_data and rig.animation_data.action else None,
            'images':images,'visible':not body.hide_get() and not body.hide_render}

before = collect(OUT/'staging-source.blend')
after = collect(OUT/'face-refinement.blend')
assert before['topology'] == after['topology']
assert np.array_equal(before['edges'],after['edges'])
assert np.array_equal(before['uv'],after['uv'])
assert before['weights'] == after['weights'] and before['regions'] == after['regions']
assert before['bones'] == after['bones'] and len(after['bones']) == 60
assert before['action'] == after['action'] and after['action']
assert after['visible'] and len(after['images']) == 3 and all(i['packed'] and i['size']==[2048,2048] for i in after['images'].values())
delta = np.linalg.norm(after['coords']-before['coords'],axis=1)
names = json.loads((OUT.parent/'Revision-Sol/final-body-authoring.json').read_text())['regionNames']
affected = {names[after['regions'][i]] for i in np.where(delta>1e-8)[0]}
assert affected <= {'head','lip_border','lip_contact','lip_vermilion','mouth_cavity'} and np.count_nonzero(delta>1e-8)>0
poses = {}
for frame in FRAMES:
    key = str(frame)
    a, b = before['poses'][key], after['poses'][key]
    poses[key] = {'maxPosedChangeMm':float(np.max(np.linalg.norm(b['points']-a['points'],axis=1))*1000),
                  'baseline':{k:v for k,v in a.items() if k!='points'},
                  'refined':{k:v for k,v in b.items() if k!='points'}}
path = OUT/'face-refinement.blend'
report = {'reopenedFile':str(path), 'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
          'topology':after['topology'],'unchangedUvCoordinates':True,'unchangedWeights':True,
          'unchangedBoneNames':True,'action':after['action'],'bodyVisible':after['visible'],
          'packedPbrImages':after['images'],'changedRegions':sorted(affected),
          'changedVertices':int(np.count_nonzero(delta>1e-8)),'maxRestChangeMm':float(np.max(delta)*1000),
          'poses':poses, 'limitations':'Edge ratios are not collision or visual-quality checks. Runtime LOD meshes in this source are inherited, not updated.'}
(OUT/'face-reopen-verification.json').write_text(json.dumps(report,indent=2))
print('REOPEN_VERIFIED',json.dumps(report,indent=2),flush=True)
