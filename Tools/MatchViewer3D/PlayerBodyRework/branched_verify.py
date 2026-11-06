"""Independently reopen the saved Skin study and render its actual cage wire."""
import bpy,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Reconstruction-Study/Branched-Cage'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'branched-cage-editable.blend'))
scene=bpy.context.scene;cage=bpy.data.objects['B_new_original_skin_cage'];graph=bpy.data.objects['B_source_branch_graph']
e=cage.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh()
info={'cageVisible':not cage.hide_render,'graphHidden':graph.hide_render,'graphVertices':len(graph.data.vertices),'graphEdges':len(graph.data.edges),'cageVertices':len(cage.data.vertices),'evaluatedVertices':len(m.vertices),'boundsMin':[min(v.co[i] for v in m.vertices) for i in range(3)],'boundsMax':[max(v.co[i] for v in m.vertices) for i in range(3)]}
e.to_mesh_clear();(OUT/'reopen-check.json').write_text(json.dumps(info,indent=2))
scene.render.filepath=str(OUT/'evidence/reopened-three-quarter.png');bpy.ops.render.render(write_still=True)
wire=cage.copy();wire.data=cage.data.copy();scene.collection.objects.link(wire)
for mod in list(wire.modifiers):wire.modifiers.remove(mod)
material=bpy.data.materials.new('Control_wire');material.use_nodes=True
node=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED');node.inputs['Base Color'].default_value=(.01,.01,.01,1)
wire.data.materials.clear();wire.data.materials.append(material)
mod=wire.modifiers.new('Actual control edges','WIREFRAME');mod.thickness=.0015;mod.use_replace=True
scene.render.filepath=str(OUT/'evidence/control-wire.png');bpy.ops.render.render(write_still=True)
print('REOPEN_SKIN',json.dumps(info),flush=True)
