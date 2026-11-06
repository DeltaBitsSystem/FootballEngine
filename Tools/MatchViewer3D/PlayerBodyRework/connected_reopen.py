"""Reopen saved connected study, render stored view and cage wire evidence."""
import bpy,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Reconstruction-Study/Connected-Torso'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'connected-body.blend'))
scene=bpy.context.scene;body=bpy.data.objects['B_connected_body'];deps=bpy.context.evaluated_depsgraph_get();evaluated=body.evaluated_get(deps);m=evaluated.to_mesh()
points=[evaluated.matrix_world@v.co for v in m.vertices]
report={'visible':not body.hide_render,'latticePresent':any(x.type=='LATTICE' and x.show_render for x in body.modifiers),'evaluatedVertices':len(points),'boundsMin':[min(p[i] for p in points) for i in range(3)],'boundsMax':[max(p[i] for p in points) for i in range(3)]}
evaluated.to_mesh_clear();(OUT/'reopened.json').write_text(json.dumps(report,indent=2))
scene.render.filepath=str(OUT/'evidence/reopened-three-quarter.png');bpy.ops.render.render(write_still=True)
# Wire overlay uses the original full cage, not triangulation or invented loops.
wire=body.copy();wire.data=body.data.copy();scene.collection.objects.link(wire);wire.name='Temporary_cage_wire_preview'
mat=bpy.data.materials.new('Cage_wire_preview');mat.diffuse_color=(.015,.015,.015,1);mat.use_nodes=True
node=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');node.inputs['Base Color'].default_value=(.015,.015,.015,1)
wire.data.materials.clear();wire.data.materials.append(mat)
mod=wire.modifiers.new('Wire overlay','WIREFRAME');mod.thickness=.00035;mod.use_replace=True
cam=scene.camera;cam.location=(3,-5,1.4);cam.rotation_euler=(Vector((0,0,1.17))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=.85
scene.render.filepath=str(OUT/'evidence/wire-torso-pelvis.png');bpy.ops.render.render(write_still=True)
print('REOPEN',json.dumps(report),flush=True)
