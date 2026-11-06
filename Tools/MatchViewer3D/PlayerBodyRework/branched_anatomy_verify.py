"""Reopen refinement and render control cage / original comparison independently."""
import bpy,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Reconstruction-Study/Branched-Cage/Anatomy-Refined'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'anatomy-refined.blend'))
scene=bpy.context.scene;ref=bpy.data.objects['Anatomy_refined_control_cage'];coarse=bpy.data.objects['B_new_original_skin_cage'];context=bpy.data.objects['Separate_head_arms_legs_context']
e=ref.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh();coords=[v.co.copy() for v in m.vertices]
report={'refinedVisible':not ref.hide_render,'coarseHidden':coarse.hide_render,'contextVisible':not context.hide_render,'controlVertices':len(ref.data.vertices),'evaluatedVertices':len(coords),'vertexGroups':{g.name:sum(any(x.group==g.index for x in v.groups) for v in ref.data.vertices) for g in ref.vertex_groups},'boundsMin':[min(p[i] for p in coords) for i in range(3)],'boundsMax':[max(p[i] for p in coords) for i in range(3)]}
e.to_mesh_clear();(OUT/'reopen-check.json').write_text(json.dumps(report,indent=2))
scene.render.filepath=str(OUT/'evidence/reopened-context.png');bpy.ops.render.render(write_still=True)
for obj in scene.objects:
    if obj.name.startswith('B_connected_'):obj.hide_render=True
context.hide_render=True
shots=[('front',(0,-4,1.1),(0,0,1.1),1.05),('back',(0,4,1.1),(0,0,1.1),1.05),('three-quarter',(3,-5,1.35),(0,0,1.1),1.05)]
for name,pos,target,scale in shots:
    cam=scene.camera;cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/('torso-'+name+'.png'));bpy.ops.render.render(write_still=True)
coarse.hide_render=False;ref.hide_render=True
scene.render.filepath=str(OUT/'evidence/coarse-three-quarter.png');bpy.ops.render.render(write_still=True)
coarse.hide_render=True;ref.hide_render=False
wire=ref.copy();wire.data=ref.data.copy();scene.collection.objects.link(wire)
for mod in list(wire.modifiers):wire.modifiers.remove(mod)
mat=bpy.data.materials.new('Refined_wire');mat.use_nodes=True
node=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');node.inputs['Base Color'].default_value=(.01,.01,.01,1)
wire.data.materials.clear();wire.data.materials.append(mat)
mod=wire.modifiers.new('Control edges','WIREFRAME');mod.thickness=.0008
scene.render.filepath=str(OUT/'evidence/control-wire.png');bpy.ops.render.render(write_still=True)
print('ANATOMY_REOPEN',json.dumps(report),flush=True)
