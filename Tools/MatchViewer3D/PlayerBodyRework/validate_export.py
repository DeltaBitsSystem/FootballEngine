"""Validate the isolated authored asset, generate a lower LOD and round-trip both GLBs.

Run AFTER author_body.py with Blender --background --factory-startup --python.
This script never opens or writes a live match-viewer asset.
"""
import bpy
import bmesh
import json
import math
import struct
import hashlib
from pathlib import Path

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'athlete-body-editable.blend'))
scene=bpy.context.scene
body=bpy.data.objects['Athlete_ContinuousSkin_LOD0']
rig=bpy.data.objects['AthleteRig_Staging']
report=json.loads((OUT/'validation.json').read_text())
def geometry_stats(obj):
    bm=bmesh.new();bm.from_mesh(obj.data)
    seen=set();components=0
    for v in bm.verts:
        if v in seen:continue
        components+=1;todo=[v];seen.add(v)
        while todo:
            u=todo.pop()
            for e in u.link_edges:
                w=e.other_vert(u)
                if w not in seen:seen.add(w);todo.append(w)
    obj.data.calc_loop_triangles()
    result={'vertices':len(obj.data.vertices),'quads':sum(len(p.vertices)==4 for p in obj.data.polygons),'triangles':len(obj.data.loop_triangles),'nonManifoldEdges':sum(not e.is_manifold for e in bm.edges),'degenerateFaces':sum(f.calc_area()<1e-12 for f in bm.faces),'connectedComponents':components}
    bm.free()
    return result
report['body']=geometry_stats(body)
report['heightMeters']=max(v.co.z for v in body.data.vertices)-min(v.co.z for v in body.data.vertices)
report['maximumWeights']=max(len(v.groups) for v in body.data.vertices)
report['unweightedVertices']=sum(not v.groups for v in body.data.vertices)
report['maxWeightSumError']=max(abs(sum(g.weight for g in v.groups)-1) for v in body.data.vertices)
pose_results=[]
for frame in [1,20,40,60,80]:
    scene.frame_set(frame)
    evaluated=body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    m=evaluated.to_mesh();m.calc_loop_triangles()
    lengths=[];area_ratios=[];degenerate=0
    for edge in m.edges:
        a,b=edge.vertices
        rest=(body.data.vertices[a].co-body.data.vertices[b].co).length
        lengths.append((m.vertices[a].co-m.vertices[b].co).length/max(rest,1e-12))
    for tri in m.loop_triangles:
        a,b,c=[m.vertices[i].co for i in tri.vertices]
        ar=(b-a).cross(c-a).length*.5
        if ar<1e-12:degenerate+=1
        x,y,z=[body.data.vertices[i].co for i in tri.vertices]
        rest=(y-x).cross(z-x).length*.5
        area_ratios.append(ar/max(rest,1e-12))
    ratios=sorted(lengths)
    pose_results.append({'frame':frame,'finiteCoordinates':all(math.isfinite(c) for v in m.vertices for c in v.co),'degenerateTriangles':degenerate,'minimumEdgeLengthRatio':min(ratios),'maximumEdgeLengthRatio':max(ratios),'p01EdgeLengthRatio':ratios[len(ratios)//100],'p99EdgeLengthRatio':ratios[len(ratios)*99//100],'minimumAreaRatio':min(area_ratios),'note':'Measures collapse/stretch, not absence of self-intersections or artistic quality.'})
    evaluated.to_mesh_clear()
report['deformationMetrics']=pose_results
scene.frame_set(1)

# Inspect UVs with original generated checker pixels without modifying the saved material.
skin=body.data.materials[0]
tex=next(n for n in skin.node_tree.nodes if n.type=='TEX_IMAGE')
original=tex.image
bsdf=next(n for n in skin.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
original_color_sources=[link.from_socket for link in bsdf.inputs['Base Color'].links]
skin.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
checker=bpy.data.images.new('Original_UV_diagnostic_checker',512,512)
pixels=[]
for y in range(512):
    for x in range(512):
        c=.65 if ((x//32+y//32)%2) else .16
        pixels.extend((c,c,c,1))
checker.pixels.foreach_set(pixels);tex.image=checker
scene.camera.location=(3,-5,1.5)
from mathutils import Vector
scene.camera.rotation_euler=(Vector((0,0,.90))-scene.camera.location).to_track_quat('-Z','Y').to_euler()
scene.camera.data.ortho_scale=2.02
scene.render.filepath=str(OUT/'evidence/uv-checker.png')
bpy.ops.render.render(write_still=True)
tex.image=original
for socket in original_color_sources:skin.node_tree.links.new(socket,bsdf.inputs['Base Color'])

# Keep LOD0 untouched in the editable scene. The staging LOD1 is a separate export.
lod=body.copy();lod.data=body.data.copy();scene.collection.objects.link(lod)
lod.name='Athlete_ContinuousSkin_LOD1'
bpy.ops.object.select_all(action='DESELECT');lod.select_set(True);bpy.context.view_layer.objects.active=lod
arm=next(m for m in lod.modifiers if m.type=='ARMATURE');lod.modifiers.remove(arm)
dec=lod.modifiers.new('LOD1_Collapse','DECIMATE');dec.ratio=min(.38,8000/max(1,len(body.data.loop_triangles)))
bpy.ops.object.modifier_apply(modifier=dec.name)
arm=lod.modifiers.new('CanonicalNames_Skinning','ARMATURE');arm.object=rig
lod.data.calc_loop_triangles()
report['lod1']={'vertices':len(lod.data.vertices),'triangles':len(lod.data.loop_triangles),'maximumWeightsBeforePruning':max(len(v.groups) for v in lod.data.vertices)}
report['lod1']['geometry']=geometry_stats(lod)
# Collapse interpolation can create extra influences: restore the four-influence runtime cap.
for v in lod.data.vertices:
    ranked=sorted([(g.group,g.weight) for g in v.groups if g.weight>1e-7],key=lambda p:-p[1])[:4]
    for g in lod.vertex_groups:g.remove([v.index])
    total=sum(w for _,w in ranked)
    for i,w in ranked:lod.vertex_groups[i].add([v.index],w/total,'REPLACE')
report['lod1']['maximumWeights']=max(len(v.groups) for v in lod.data.vertices)
report['lod1']['unweightedVertices']=sum(not v.groups for v in lod.data.vertices)
report['lod1']['maxWeightSumError']=max(abs(sum(g.weight for g in v.groups)-1) for v in lod.data.vertices)
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);lod.select_set(True)
for o in scene.objects:
    if o.type=='MESH' and (o.name.startswith(('Eye.','Iris.','Pupil.')) or o.name=='Separate_briefs'):o.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(OUT/'athlete-body-staging-lod1.glb'),export_format='GLB',use_selection=True,export_animations=False,export_skins=True,export_yup=True,export_apply=False)

def glb_info(path):
    raw=path.read_bytes();magic,version,total=struct.unpack_from('<III',raw)
    assert magic==0x46546C67 and version==2 and total==len(raw)
    length,kind=struct.unpack_from('<II',raw,12)
    assert kind==0x4e4f534a
    doc=json.loads(raw[20:20+length])
    bin_header=20+length
    bin_length,bin_kind=struct.unpack_from('<II',raw,bin_header)
    assert bin_kind==0x004e4942
    binary=memoryview(raw)[bin_header+8:bin_header+8+bin_length]
    primitives=[p for m in doc['meshes'] for p in m['primitives']]
    assert all(p.get('mode',4)==4 for p in primitives)
    assert all('JOINTS_0' in p['attributes'] and 'WEIGHTS_0' in p['attributes'] for p in primitives)
    max_weight_error=0;unweighted=0
    for p in primitives:
        a=doc['accessors'][p['attributes']['WEIGHTS_0']]
        assert a['componentType']==5126 and a['type']=='VEC4'
        view=doc['bufferViews'][a['bufferView']]
        offset=view.get('byteOffset',0)+a.get('byteOffset',0);stride=view.get('byteStride',16)
        for i in range(a['count']):
            w=struct.unpack_from('<4f',binary,offset+i*stride)
            assert all(math.isfinite(x) and x>=0 for x in w)
            max_weight_error=max(max_weight_error,abs(sum(w)-1));unweighted+=sum(w)==0
    return {'file':path.name,'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest(),'meshPrimitives':len(primitives),'joints':len(doc['skins'][0]['joints']),'indexedTriangles':sum(doc['accessors'][p['indices']]['count']//3 for p in primitives),'hasUVs':all('TEXCOORD_0' in p['attributes'] for p in primitives),'animations':len(doc.get('animations',[])),'exportedMaxWeightSumError':max_weight_error,'exportedUnweightedVertices':unweighted}

report['exports']=[glb_info(OUT/n) for n in ['athlete-body-staging.glb','athlete-body-staging-lod1.glb']]
# Import into a NEW scene, not the editable source: loader acceptance and scale are checked separately.
report['roundTrip']=[]
studio=[o for o in scene.objects if o.type in {'CAMERA','LIGHT'} or o.name=='Studio_floor_NOT_exported']
for n in ['athlete-body-staging.glb','athlete-body-staging-lod1.glb']:
    inspection=bpy.data.scenes.new('Isolated_GLTF_roundtrip_'+n)
    inspection.world=scene.world
    for item in studio:
        clone=item.copy();inspection.collection.objects.link(clone)
        if clone.type=='CAMERA':inspection.camera=clone
    inspection.render.engine=scene.render.engine;inspection.cycles.device='CPU';inspection.cycles.samples=32;inspection.cycles.use_denoising=True
    inspection.render.threads_mode='FIXED';inspection.render.threads=6
    inspection.render.resolution_x=640;inspection.render.resolution_y=800;inspection.render.resolution_percentage=100
    bpy.context.window.scene=inspection
    before=set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(OUT/n))
    imported=[o for o in bpy.data.objects if o not in before]
    meshes=[o for o in imported if o.type=='MESH' and any(m.type=='ARMATURE' for m in o.modifiers)]
    imported_body=max(meshes,key=lambda o:len(o.data.vertices))
    bpy.context.view_layer.update()
    points=[imported_body.matrix_world@v.co for v in imported_body.data.vertices]
    height=max(v.z for v in points)-min(v.z for v in points)
    report['roundTrip'].append({'file':n,'skinnedMeshObjects':len(meshes),'armatures':sum(o.type=='ARMATURE' for o in imported),'finiteVertices':all(math.isfinite(c) for o in meshes for v in o.data.vertices for c in v.co),'bodyHeightMeters':height,'sourceHeightDifferenceMeters':abs(height-report['heightMeters'])})
    inspection.render.filepath=str(OUT/'evidence'/('roundtrip-lod1.png' if 'lod1' in n else 'roundtrip-lod0.png'))
    bpy.ops.render.render(write_still=True)
    for o in imported:bpy.data.objects.remove(o,do_unlink=True)
    bpy.context.window.scene=scene
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
