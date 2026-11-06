"""Independent process verifies saved cage and renders the saved visible variant."""
import bpy,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Reconstruction-Study'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'torso-rebuild-working.blend'))
scene=bpy.context.scene;o=bpy.data.objects['B_new_torso_control_cage']
deps=bpy.context.evaluated_depsgraph_get();evaluated=o.evaluated_get(deps);mesh=evaluated.to_mesh()
points=[evaluated.matrix_world@v.co for v in mesh.vertices]
report={'visible':not o.hide_render,'controlVertices':len(o.data.vertices),'controlFaces':len(o.data.polygons),'evaluatedVertices':len(points),'evaluatedBoundsMeters':{'min':[min(p[i] for p in points) for i in range(3)],'max':[max(p[i] for p in points) for i in range(3)]},'subdivisionRenderLevels':next(m.render_levels for m in o.modifiers if m.type=='SUBSURF'),'source':'torso-rebuild-working.blend'}
evaluated.to_mesh_clear();(OUT/'reopen-geometry.json').write_text(json.dumps(report,indent=2))
cam=scene.camera;cam.location=(3,-5,1.5);cam.rotation_euler=(Vector((0,0,.9))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=2.02
scene.render.filepath=str(OUT/'evidence/reopened-three-quarter.png');bpy.ops.render.render(write_still=True)
print('REOPEN',json.dumps(report),flush=True)
