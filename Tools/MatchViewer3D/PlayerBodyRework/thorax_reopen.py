"""Independent reopen and genuine control-cage wire review."""
import bpy,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Reconstruction-Study/Branched-Cage/Anatomy-Refined/Thorax'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'thorax-final.blend'))
scene=bpy.context.scene;o=bpy.data.objects['Anatomy_refined_control_cage']
visible=[x.name for x in scene.objects if x.type=='MESH' and not x.hide_render]
expected={'Anatomy_refined_control_cage','Separate_head_arms_legs_context','Studio_floor_NOT_exported'}
if set(visible)!=expected:raise RuntimeError('Unexpected visible meshes '+str(visible))
e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();points=[v.co.copy() for v in m.vertices]
report={'visibleMeshes':visible,'controlVertices':len(o.data.vertices),'controlFaces':len(o.data.polygons),'evaluatedVertices':len(points),'boundsMin':[min(p[i] for p in points) for i in range(3)],'boundsMax':[max(p[i] for p in points) for i in range(3)]};e.to_mesh_clear()
(OUT/'reopen-check.json').write_text(json.dumps(report,indent=2))
scene.render.filepath=str(OUT/'evidence/reopened-context.png');bpy.ops.render.render(write_still=True)
bpy.data.objects['Separate_head_arms_legs_context'].hide_render=True
wire=o.copy();wire.data=o.data.copy();scene.collection.objects.link(wire)
for mod in list(wire.modifiers):wire.modifiers.remove(mod)
mat=bpy.data.materials.new('Actual_wire_preview');mat.use_nodes=True
node=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');node.inputs['Base Color'].default_value=(.015,.015,.015,1)
wire.data.materials.clear();wire.data.materials.append(mat)
mod=wire.modifiers.new('Real control edges','WIREFRAME');mod.thickness=.00065
cam=scene.camera;cam.location=(3,-5,1.4);cam.rotation_euler=(Vector((0,0,1.28))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=.64
scene.render.filepath=str(OUT/'evidence/wire-scapular.png');bpy.ops.render.render(write_still=True)
