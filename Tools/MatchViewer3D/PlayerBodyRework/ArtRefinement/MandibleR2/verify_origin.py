"""Prove the exported editable mesh is the chosen candidate, not an old LOD."""
from pathlib import Path
import hashlib,json
import bpy
import numpy as np

ROOT=Path(__file__).resolve().parents[5]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
OUT=BASE/'Mandible-R2'
def read(path):
    bpy.ops.wm.open_mainfile(filepath=str(path))
    bpy.context.scene.frame_set(1)
    o=bpy.data.objects['FINAL_Body_Connected'];m=o.data
    return {'coords':np.array([v.co[:] for v in m.vertices]),
            'regions':[a.value for a in m.attributes['Anatomical_region_id'].data],
            'uv':np.array([a.uv[:] for a in m.uv_layers.active.data]),
            'weights':[[(o.vertex_groups[g.group].name,g.weight) for g in v.groups] for v in m.vertices],
            'bones':[b.name for b in bpy.data.objects['FINAL_Rig'].data.bones]}
before=read(BASE/'final-body-source.blend')
candidate=read(OUT/'candidate.blend')
final=read(OUT/'final-body-source.blend')
assert np.array_equal(candidate['coords'],final['coords'])
assert np.array_equal(before['uv'],final['uv'])
assert before['weights']==final['weights'] and before['bones']==final['bones'] and len(final['bones'])==60
delta=np.linalg.norm(final['coords']-before['coords'],axis=1)
names=json.loads((BASE/'final-body-authoring.json').read_text())['regionNames']
affected={names[final['regions'][i]] for i in np.flatnonzero(delta>1e-8)}
assert affected=={'head'}
report={'sourceSha256':hashlib.sha256((BASE/'final-body-source.blend').read_bytes()).hexdigest(),
        'finalSha256':hashlib.sha256((OUT/'final-body-source.blend').read_bytes()).hexdigest(),
        'candidateMatchesSavedBody':True,'unchangedWeightsUVBones':True,'affectedRegions':sorted(affected),
        'changedVertices':int(np.count_nonzero(delta>1e-8)),'maximumDisplacementMm':float(np.max(delta)*1000)}
(OUT/'origin-verification.json').write_text(json.dumps(report,indent=2))
print('ORIGIN_VERIFIED',json.dumps(report),flush=True)
