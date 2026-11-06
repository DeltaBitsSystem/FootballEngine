"""Reshape the existing lower lateral head into mandibular/neck planes."""
from pathlib import Path
import hashlib
import json
import bpy

ROOT=Path(__file__).resolve().parents[5]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
OUT=BASE/'Mandible-R2'
SOURCE=BASE/'final-body-source.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene=bpy.context.scene
scene.frame_set(1)
obj=bpy.data.objects['FINAL_Body_Connected']
mesh=obj.data
regions=mesh.attributes['Anatomical_region_id'].data
names=json.loads((BASE/'final-body-authoring.json').read_text())['regionNames']
original=[v.co.copy() for v in mesh.vertices]

def smooth(t):
    t=max(0.0,min(1.0,t))
    return t*t*(3-2*t)

changed=[]
for v in mesh.vertices:
    if names[regions[v.index].value]!='head':continue
    x,y,z=v.co
    # The lower edge rises from the chin toward the angle anterior to the ear.
    # Recede the broad lateral neck below that edge, not the back of the skull.
    edge=1.586+.36*max(0,min(.10,y+.070))
    below=smooth((edge-z+.003)/.032)
    low=smooth((z-1.525)/.045)
    anterior=smooth((y+.075)/.028)
    posterior=1-smooth((y-.010)/.045)
    lateral=smooth((abs(x)-.012)/.023)
    amount=.0075*below*low*anterior*posterior*lateral
    v.co.x -= (1 if x>0 else -1)*amount
    # A low-amplitude, long transition changes the nape cross-sections without
    # creating the tight concavity produced by the previous posterior mask.
    rear=smooth((y-.030)/.055)
    height=smooth((z-1.535)/.065)*(1-smooth((z-1.625)/.065))
    v.co.y -= .0035*rear*height
    if (v.co-original[v.index]).length>1e-8:changed.append(v.index)
mesh.update()
assert changed and max(mesh.vertices[i].co.z for i in changed)<1.69
assert all((mesh.vertices[i].co-original[i]).length<1e-8 for i in range(len(original)) if names[regions[i].value]!='head')
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'candidate.blend'))
record={'source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        'candidateSha256':hashlib.sha256((OUT/'candidate.blend').read_bytes()).hexdigest(),
        'changedVertices':len(changed),'maxDisplacementMm':max((mesh.vertices[i].co-original[i]).length for i in changed)*1000,
        'method':'Broad lower lateral plane under a rising mandibular boundary and long low-amplitude nape transition.',
        'references':['https://medicine.uams.edu/neuroscience/education/medical-school-courses/human-structure-module/anatomy-tables/topographical-anatomy-tables/topographical-anatomy-of-the-head-and-neck/','https://pmc.ncbi.nlm.nih.gov/articles/PMC6624334/']}
(OUT/'author.json').write_text(json.dumps(record,indent=2))
print('AUTHOR',json.dumps(record),flush=True)
