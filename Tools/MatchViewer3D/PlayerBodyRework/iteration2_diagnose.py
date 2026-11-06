"""Snapshot our previous iteration and localize deformation failures in Blender."""
import bpy
import json
import shutil
import hashlib
from pathlib import Path

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
SNAP=BASE/'Iteration1'
OUT=BASE/'Iteration2'
OUT.mkdir(exist_ok=True)
if not SNAP.exists():
    SNAP.mkdir()
    for p in BASE.iterdir():
        if p.is_file() and p.suffix in {'.blend','.glb','.png','.json','.md'}:shutil.copy2(p,SNAP/p.name)
    shutil.copytree(BASE/'evidence',SNAP/'evidence')
    shutil.copytree(Path(__file__).parent,SNAP/'authoring',ignore=shutil.ignore_patterns('__pycache__'))
bpy.ops.wm.open_mainfile(filepath=str(SNAP/'athlete-body-editable.blend'))
o=bpy.data.objects['Athlete_ContinuousSkin_LOD0'];scene=bpy.context.scene
report={'sourceSha256':hashlib.sha256((SNAP/'athlete-body-editable.blend').read_bytes()).hexdigest(),'poses':[]}
for frame in [1,20,40,60,80]:
    scene.frame_set(frame)
    eo=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=eo.to_mesh()
    records=[]
    for e in o.data.edges:
        a,b=e.vertices;rest=(o.data.vertices[a].co-o.data.vertices[b].co).length
        length=(m.vertices[a].co-m.vertices[b].co).length
        records.append((length/rest,e.index,a,b,rest,length))
    records.sort()
    critical=[]
    for ratio,i,a,b,rest,length in records[:10]+records[-10:]:
        vertices=[]
        for j in [a,b]:
            v=o.data.vertices[j]
            vertices.append({'index':j,'rest':list(v.co),'posed':list(m.vertices[j].co),'weights':{o.vertex_groups[g.group].name:g.weight for g in v.groups}})
        critical.append({'edge':i,'ratio':ratio,'restMillimeters':rest*1000,'posedMillimeters':length*1000,'vertices':vertices})
    report['poses'].append({'frame':frame,'min':records[0][0],'max':records[-1][0],'criticalEdges':critical})
    eo.to_mesh_clear()
(OUT/'baseline-edge-diagnosis.json').write_text(json.dumps(report,indent=2))
for pose in report['poses']:
    print('FRAME',pose['frame'],'MIN',pose['min'],'MAX',pose['max'])
    for edge in [pose['criticalEdges'][0],pose['criticalEdges'][-1]]:
        print(json.dumps(edge))
