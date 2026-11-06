"""Original clay study, independently saved; reshape auricular and oral forms."""
import bpy
import bmesh
import json
import math
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'PhaseA-Connected-09';OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Connected-08/athlete-form-editable.blend'))
scene=bpy.context.scene;body=bpy.data.objects['B_connected_0'];mesh=body.data
names=json.loads((BASE/'Iteration2/cage-construction.json').read_text())['regionNames']
regions=[names[v.value] for v in mesh.attributes['Anatomical_region_id'].data]
initial=[v.co.copy() for v in mesh.vertices]
def G(x,c,s):return math.exp(-((x-c)/s)**2)
for v in mesh.vertices:
    p=v.co;label=regions[v.index]
    if label.startswith('ear.'):
        s=1 if label.endswith('.L') else -1
        # Work on existing ear shell: recessed concha and a curved antihelix,
        # with a fuller lobe. Displacements remain in the outward-normal axis.
        y,z=p.y,p.z
        concha=-.0035*G(y,.010,.006)*G(z,1.673,.012)
        anti=.0025*G(y,.011+.15*(z-1.675),.0035)*G(z,1.682,.015)
        lobe=.002*G(y,.008,.008)*G(z,1.651,.007)
        p.x+=s*(concha+anti+lobe)
    elif label=='head' and p.y<-.04 and 1.59<p.z<1.64 and abs(p.x)<.038:
        # Separate the maxillary lip pads from the philtrum and chin; use
        # broad low-amplitude volumes, not a sharp crease or added geometry.
        x,z=p.x,p.z
        upper=.0022*(G(x,-.008,.009)+G(x,.008,.009))*G(z,1.612,.0035)
        lower=.0028*G(x,0,.019)*G(z,1.604,.004)
        groove=-.0015*G(x,0,.024)*G(z,1.608,.0018)
        philtrum=-.0011*G(x,0,.004)*G(z,1.624,.006)
        p.y-=upper+lower+groove+philtrum
mesh.update()
bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table()
ear=[v for v in bm.verts if regions[v.index].startswith('ear.')]
for _ in range(3):bmesh.ops.smooth_vert(bm,verts=ear,factor=.10,use_axis_x=True,use_axis_y=True,use_axis_z=True)
bm.to_mesh(mesh);bm.free()
movement=[(v.co-p).length for v,p in zip(mesh.vertices,initial)]
report={'source':'PhaseA-Connected-08/athlete-form-editable.blend','changedVertices':sum(x>1e-7 for x in movement),'maxMovementMm':max(movement)*1000,'technique':'Original shell displacement on existing vertices, followed by Blender native ear smoothing; no imported geometry or pixels.','vertexCount':len(mesh.vertices)}
(OUT/'operations.json').write_text(json.dumps(report,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-form-editable.blend'))
shots=[('head-front',(0,-3,1.67),(0,0,1.66),.37),('head-profile',(3,0,1.67),(0,0,1.66),.37),('head-three-quarter',(3,-5,1.7),(0,0,1.665),.37),('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02),('hand',(1,-3,1.10),(.635,0,.97),.32),('feet',(.7,-1.6,.55),(0,-.07,.105),.48)]
for name,pos,target,scale in shots:
    cam=scene.camera;cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(name+'.png'));bpy.ops.render.render(write_still=True)
print('ANATOMY09',json.dumps(report),flush=True)
