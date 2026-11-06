"""Inspect and refine lower head planes on the existing skinned surface."""
import json
import sys
import hashlib
from pathlib import Path
import bpy

ROOT = Path(__file__).resolve().parents[4]
OUT = ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
SOURCE = OUT/'face-refinement.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
bpy.context.scene.frame_set(1)
body = bpy.data.objects['FINAL_Body_Connected']
mesh = body.data
names = json.loads((OUT.parent/'Revision-Sol/final-body-authoring.json').read_text())['regionNames']
regions = mesh.attributes['Anatomical_region_id'].data
ear = bpy.data.objects['GEO_Ear_v2.L']
print('EAR bounds', [[round(min((ear.matrix_world@v.co)[i] for v in ear.data.vertices),5),round(max((ear.matrix_world@v.co)[i] for v in ear.data.vertices),5)] for i in range(3)], flush=True)
for z in [1.54,1.56,1.575,1.59,1.605,1.62,1.635,1.65,1.665,1.68]:
    verts = [v.co for v in mesh.vertices if names[regions[v.index].value]=='head' and abs(v.co.z-z)<.002]
    if verts:
        print('PROFILE', z, 'y',round(min(p.y for p in verts),4),round(max(p.y for p in verts),4),
              'width',round(max(abs(p.x) for p in verts),4),
              'side',[(round(t,3),round(max((abs(p.x) for p in verts if abs(p.y-t)<.005),default=0),4)) for t in [-.07,-.04,-.01,.02,.05,.08]],flush=True)

if '--inspect' not in sys.argv:
    def smooth(t):
        t=max(0.,min(1.,t))
        return t*t*(3-2*t)
    before = [v.co.copy() for v in mesh.vertices]
    changed=[]
    for v in mesh.vertices:
        if names[regions[v.index].value] != 'head':
            continue
        x,y,z = v.co
        vertical = smooth((z-1.545)/.035)*(1-smooth((z-1.620)/.060))
        back = smooth((y-.005)/.068)
        # Pull the low rear cross-sections into the neck, while the upper
        # occiput and the anterior jaw remain fixed.
        v.co.y -= .010*vertical*back
        # A wider transition on the lateral/posterior surface leaves a fuller
        # mandibular angle anterior to the ear instead of drawing a groove.
        lateral = smooth((abs(x)-.018)/.025)
        posterior_side = smooth((y+.025)/.055)
        side_z = smooth((z-1.553)/.038)*(1-smooth((z-1.615)/.045))
        v.co.x -= (1 if x>0 else -1)*.0035*lateral*posterior_side*side_z
        if (v.co-before[v.index]).length>1e-8:
            changed.append(v.index)
    mesh.update()
    assert changed and all(names[regions[i].value]=='head' for i in changed)
    assert all((v.co-before[v.index]).length<1e-8 for v in mesh.vertices if v.co.z>=1.68)
    bpy.context.preferences.filepaths.save_version=0
    result=OUT/'profile-refinement.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(result))
    report={'source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
            'result':str(result),'resultSha256':hashlib.sha256(result.read_bytes()).hexdigest(),
            'changedVertices':len(changed),'maxDisplacementMm':max((mesh.vertices[i].co-before[i]).length for i in changed)*1000,
            'occiputAtOrAbove1_68Unchanged':True,'region':'head','earObjectUnchanged':True}
    (OUT/'profile-edit.json').write_text(json.dumps(report,indent=2))
    print('PROFILE_EDIT',json.dumps(report),flush=True)
