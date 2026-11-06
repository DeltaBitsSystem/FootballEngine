"""Measure actual influence of the cage at anatomical target neighbourhoods."""
from pathlib import Path
import json,math
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Landmark-Cage'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'cage-form.blend'))
s=bpy.context.scene;s.frame_set(1)
obj=bpy.data.objects['FINAL_Body_Connected'];orig=obj.data
ev=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
assert len(mesh.vertices)==len(orig.vertices)
points={'menton':(0,-.083,1.584),'gonion_L':(.057,-.006,1.612),'mastoides_L':(.065,.032,1.634),
        'garganta':(0,-.040,1.555),'nuca':(0,.065,1.555),'base_cuello':(0,-.02,1.525)}
result={}
for name,p in points.items():
    target=Vector(p);v=min(orig.vertices,key=lambda v:(v.co-target).length)
    final=mesh.vertices[v.index].co
    result[name]={'requested':list(target),'sourceVertex':list(v.co),'nearestDistanceMm':(v.co-target).length*1000,
                  'evaluatedVertex':list(final),'deltaMm':[(final[i]-v.co[i])*1000 for i in range(3)]}
ev.to_mesh_clear()
(OUT/'landmark-measurements.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result,indent=2),flush=True)
