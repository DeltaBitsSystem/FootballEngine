"""Unfiltered edge-strain diagnostics and same-camera Blender evidence for iteration 2."""
import bpy
import bmesh
import json
import math
import os
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Iteration2'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'athlete-iteration2.blend'))
scene=bpy.context.scene;body=bpy.data.objects['GEO_AthleteBody_Iteration2']
def topology(o):
    bm=bmesh.new();bm.from_mesh(o.data);seen=set();components=0
    for v in bm.verts:
        if v in seen:continue
        components+=1;todo=[v];seen.add(v)
        while todo:
            a=todo.pop()
            for edge in a.link_edges:
                b=edge.other_vert(a)
                if b not in seen:todo.append(b);seen.add(b)
    o.data.calc_loop_triangles()
    result={'vertices':len(o.data.vertices),'faces':len(o.data.polygons),'quads':sum(len(p.vertices)==4 for p in o.data.polygons),'triangles':len(o.data.loop_triangles),'nonManifoldEdges':sum(not e.is_manifold for e in bm.edges),'boundaryEdges':sum(e.is_boundary for e in bm.edges),'degenerateFaces':sum(f.calc_area()<1e-12 for f in bm.faces),'components':components}
    bm.free();return result
report={'topology':topology(body),'heightMeters':max(v.co.z for v in body.data.vertices)-min(v.co.z for v in body.data.vertices),'maximumInfluences':max(len(v.groups) for v in body.data.vertices),'unweighted':sum(not v.groups for v in body.data.vertices),'maximumWeightSumError':max(abs(1-sum(g.weight for g in v.groups)) for v in body.data.vertices),'poses':[],'comparisonPolicy':'All source edges, including every short edge, same rest/local bone pose tests as iteration 1. No edge-length threshold, excluded region or relaxed degeneracy threshold.'}
def vertex_info(index,m):
    v=body.data.vertices[index]
    return {'index':index,'rest':list(v.co),'posed':list(m.vertices[index].co),'weights':{body.vertex_groups[g.group].name:g.weight for g in v.groups}}
for frame in [1,20,40,60,80]:
    scene.frame_set(frame);eo=body.evaluated_get(bpy.context.evaluated_depsgraph_get());m=eo.to_mesh();m.calc_loop_triangles()
    entries=[]
    for edge in body.data.edges:
        a,b=edge.vertices;rest=(body.data.vertices[a].co-body.data.vertices[b].co).length
        length=(m.vertices[a].co-m.vertices[b].co).length
        if rest<=0:raise RuntimeError('Zero-length source edge')
        entries.append((length/rest,edge.index,a,b,rest,length))
    entries.sort();critical=[]
    for ratio,index,a,b,rest,length in entries[:10]+entries[-10:]:
        critical.append({'edge':index,'ratio':ratio,'restMillimeters':rest*1000,'posedMillimeters':length*1000,'vertices':[vertex_info(a,m),vertex_info(b,m)]})
    area=[]
    for tri in m.loop_triangles:
        a,b,c=[m.vertices[i].co for i in tri.vertices];area.append((b-a).cross(c-a).length/2)
    row={'frame':frame,'min':entries[0][0],'max':entries[-1][0],'p01':entries[len(entries)//100][0],'p99':entries[len(entries)*99//100][0],'maxAbsoluteLengthChangeMillimeters':max(abs(x[5]-x[4])*1000 for x in entries),'degenerateTriangles':sum(a<1e-12 for a in area),'finite':all(math.isfinite(c) for v in m.vertices for c in v.co),'criticalEdges':critical}
    report['poses'].append(row);print('POSE',frame,'MIN',row['min'],'MAX',row['max'],'P01/P99',row['p01'],row['p99'],flush=True)
    eo.to_mesh_clear()
(OUT/'deformation-report.json').write_text(json.dumps(report,indent=2))
print('TOPOLOGY',json.dumps(report['topology']),flush=True)
if os.environ.get('FE_V2_SKIP_RENDER')=='1':raise SystemExit
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.render.threads_mode='FIXED';scene.render.threads=6
scene.render.resolution_x=640;scene.render.resolution_y=800;scene.render.resolution_percentage=100
cam=scene.camera
shots=[('front',(0,-5,.92),(0,0,.90),2.02,1),('profile',(5,0,.92),(0,0,.90),2.02,1),('back',(0,5,.92),(0,0,.90),2.02,1),('three-quarter',(3,-5,1.5),(0,0,.90),2.02,1),('head-front',(0,-3,1.67),(0,0,1.66),.37,1),('head-profile',(3,-.8,1.67),(0,0,1.66),.37,1),('hand-closeup',(1,-3,1.10),(.635,0,.97),.30,1),('feet-closeup',(.7,-1.6,.55),(0,-.07,.105),.48,1),('arms-raised',(3,-5,1.3),(0,0,1.02),2.25,20),('elbows-flexed',(3,-5,1.3),(0,0,.98),2.12,40),('hip-knee-flexed',(4,-4,1.2),(0,0,.90),2.05,60),('running-stride',(4,-4,1.2),(0,0,.90),2.05,80)]
for name,pos,target,scale,frame in shots:
    scene.frame_set(frame);cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(name+'.png'));bpy.ops.render.render(write_still=True)
