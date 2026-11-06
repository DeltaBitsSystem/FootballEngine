"""Check the packaged body is precisely the consolidated chosen surface."""
from pathlib import Path
import json,hashlib
import bpy,numpy as np

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
def get(path):
    bpy.ops.wm.open_mainfile(filepath=str(path))
    o=bpy.data.objects['FINAL_Body_Connected'];r=bpy.data.objects['FINAL_Rig']
    bones={b.name for b in r.data.bones}
    return np.array([v.co[:] for v in o.data.vertices]),[[(o.vertex_groups[g.group].name,g.weight) for g in v.groups if o.vertex_groups[g.group].name in bones] for v in o.data.vertices],len(bones)
a,w,n=get(OUT/'body-consolidated.blend')
b,x,m=get(OUT/'player-runtime-source.blend')
assert np.array_equal(a,b) and w==x and n==m==60
report={'selectedSurfaceExactlyPreserved':True,'boneWeightsExactlyPreserved':True,'bones':m,
        'sourceSha256':hashlib.sha256((OUT/'player-runtime-source.blend').read_bytes()).hexdigest(),
        'reference':'consolidation.json proves the selected lattice evaluation was applied exactly.'}
(OUT/'selected-surface-verification.json').write_text(json.dumps(report,indent=2))
print('SELECTED_SURFACE_VERIFIED',json.dumps(report),flush=True)
