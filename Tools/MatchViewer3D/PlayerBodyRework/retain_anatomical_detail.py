"""Preserve finger and facial silhouette that global QuadriFlow simplified too far.

Run after refine_surface.py, before skin_and_pose.py. The exploratory all-quad
surface remains editable but is not the final close-camera runtime mesh.
"""
import bpy
import bmesh
import json
import math
from pathlib import Path

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'athlete-body-editable.blend'))
scene=bpy.context.scene;scene.frame_set(1)
old=bpy.data.objects['Athlete_ContinuousSkin_LOD0']
skin=old.data.materials[0]
source=bpy.data.objects['SOURCE_Continuous_high_resolution_skin']
o=source.copy();o.data=source.data.copy();scene.collection.objects.link(o)
o.hide_render=False;o.hide_set(False)
old.name='SOURCE_QuadriFlow_exploration_fingers_oversimplified'
old.hide_render=True;old.hide_set(True)
o.name='Athlete_ContinuousSkin_LOD0'
bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
dec=o.modifiers.new('CloseCamera_OriginalDetail_50000tris','DECIMATE')
dec.ratio=50000/(len(o.data.polygons)*2)
bpy.ops.object.modifier_apply(modifier=dec.name)
bm=bmesh.new();bm.from_mesh(o.data)
seen=set();small=[]
for v in bm.verts:
    if v in seen:continue
    todo=[v];seen.add(v);component=[]
    while todo:
        u=todo.pop();component.append(u)
        for e in u.link_edges:
            w=e.other_vert(u)
            if w not in seen:seen.add(w);todo.append(w)
    if len(component)<20:small.extend(component)
if small:bmesh.ops.delete(bm,geom=small,context='VERTS')
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
o.data.validate()
o.data.materials.clear();o.data.materials.append(skin)
for p in o.data.polygons:p.use_smooth=True;p.material_index=0
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(70),island_margin=.012)
bpy.ops.object.mode_set(mode='OBJECT')

# Subtle original lip/brow colour follows the integrated face, not stuck-on facial geometry.
def mat(name,color,rough=.65):
    m=bpy.data.materials.new(name);m.use_nodes=True
    b=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value=(*color,1);b.inputs['Roughness'].default_value=rough
    return m
o.data.materials.append(mat('Original_lip_tone',(.22,.085,.060)))
o.data.materials.append(mat('Original_brow_tone',(.055,.030,.018),.85))
for p in o.data.polygons:
    c=p.center
    if c.y<-.060 and abs(c.x)<.026 and 1.579<c.z<1.595:p.material_index=1
    elif c.y<-.052 and .015<abs(c.x)<.057 and 1.699<c.z<1.704:p.material_index=2

report=json.loads((OUT/'validation.json').read_text())
o.data.calc_loop_triangles()
bm=bmesh.new();bm.from_mesh(o.data)
report['body']={'vertices':len(o.data.vertices),'quads':sum(len(p.vertices)==4 for p in o.data.polygons),'triangles':len(o.data.loop_triangles),'nonManifoldEdges':sum(not e.is_manifold for e in bm.edges),'degenerateFaces':sum(f.calc_area()<1e-12 for f in bm.faces),'connectedComponents':1}
bm.free()
report['topologyDecision']='Global QuadriFlow completed, but visual inspection found it reduced the fingers to mittens and softened the face. Final close-camera skin therefore preserves the original voxel-union anatomy with 50k-triangle collapse topology. The all-quad experiment remains hidden and editable; it is not the export.'
report['limitations']=[s for s in report['limitations'] if 'Automatic quad flow' not in s]
report['limitations'].append('Final close-camera topology is a decimated continuous surface, not artist-retopologized deformation loops. Automated pose tests do not replace production animation review.')
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-body-editable.blend'))
print('DETAIL_RETAINED',report['body'])
