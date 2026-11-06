"""Independently verify evaluated geometry and rendering after reopen."""
import bpy,json,math
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Reconstruction-Study/Branched-Cage/Anatomy-Refined/Thorax'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'thorax-guided-final.blend'))
scene=bpy.context.scene;body=bpy.data.objects['Anatomy_refined_control_cage'];before=bpy.data.objects['Guided_before'];deps=bpy.context.evaluated_depsgraph_get()
a=before.evaluated_get(deps);b=body.evaluated_get(deps);am=a.to_mesh();bm=b.to_mesh()
if len(am.vertices)!=len(bm.vertices):raise RuntimeError('Mismatch of evaluated vertex topology')
dist=[(x.co-y.co).length for x,y in zip(am.vertices,bm.vertices)]
angles=[x.normal.angle(y.normal) for x,y in zip(am.vertices,bm.vertices) if x.normal.length and y.normal.length]
report={'visibleMeshes':[o.name for o in scene.objects if o.type=='MESH' and not o.hide_render],'evaluatedVertices':len(bm.vertices),'evaluatedMaxDisplacementMm':max(dist)*1000,'evaluatedMaxNormalChangeDegrees':math.degrees(max(angles)),'evaluatedNormalsOver3Degrees':sum(x>math.radians(3) for x in angles)}
a.to_mesh_clear();b.to_mesh_clear()
record=json.loads((OUT/'guided-final-metrics.json').read_text())
for k in ['evaluatedVertices','evaluatedMaxDisplacementMm','evaluatedMaxNormalChangeDegrees','evaluatedNormalsOver3Degrees']:
    if abs(record[k]-report[k])>1e-5:raise RuntimeError('Reopen mismatch in '+k)
(OUT/'guided-reopen-metrics.json').write_text(json.dumps(report,indent=2))
scene.render.filepath=str(OUT/'evidence/guided-reopened-context.png');bpy.ops.render.render(write_still=True)
