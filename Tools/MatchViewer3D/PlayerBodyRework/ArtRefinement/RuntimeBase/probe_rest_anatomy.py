"""Measure prospective semantic joints against the unchanged closed body."""
from pathlib import Path
import json
import bpy
from mathutils.bvhtree import BVHTree

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'body-consolidated.blend'))
body=bpy.data.objects['FINAL_Body_Connected']; rig=bpy.data.objects['FINAL_Rig']
bvh=BVHTree.FromPolygons([v.co for v in body.data.vertices],[tuple(p.vertices) for p in body.data.polygons])
results={}
for side in ('L','R'):
    thigh=rig.data.bones['thigh.'+side]; shin=rig.data.bones['shin.'+side]
    knee=shin.head_local.copy()
    hip=knee+(thigh.head_local-knee).normalized()*0.4402556
    ankle=knee+(shin.tail_local-knee).normalized()*0.4352585
    joints={}
    for label,p in [('hip',hip),('knee',knee),('ankle',ankle)]:
        near,normal,face,distance=bvh.find_nearest(p)
        joints[label]={'xyz':list(p),'nearestSurfaceMm':distance*1000,'insideByNearestNormal':(p-near).dot(normal)<0}
    results[side]=joints
(OUT/'proposed-rest-anatomy.json').write_text(json.dumps(results,indent=2))
print('PROPOSED_JOINTS',json.dumps(results),flush=True)
