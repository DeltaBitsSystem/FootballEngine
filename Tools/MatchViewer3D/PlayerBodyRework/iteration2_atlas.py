"""Original image atlas on deliberate anatomical UV islands, no vertex-colour fallback."""
import bpy
import math
import json
from pathlib import Path
from mathutils import Vector
import numpy as np

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Iteration2'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'athlete-iteration2.blend'))
scene=bpy.context.scene;scene.frame_set(1)
body=bpy.data.objects['GEO_AthleteBody_Iteration2'];mesh=body.data
def G(x,c,s):return math.exp(-((x-c)/s)**2)
def blend(a,b,t):return tuple(x*(1-t)+y*t for x,y in zip(a,b))
names=json.loads((OUT/'cage-construction.json').read_text())['regionNames']
regions=[names[x.value] for x in mesh.attributes['Anatomical_region_id'].data]
def pigment(p,region):
    x,y,z=p;color=(.34,.172,.105)
    if y<-.030 and z>1.54 and not region.startswith('orbital_cavity'):
        lips=.65*G(x,0,.024)*G(z,1.602,.006)
        color=blend(color,(.265,.095,.073),lips)
        arch=1.704-.12*(abs(x)-.032)
        brow=.88*G(abs(x),.033,.020)*G(z,arch,.0024)
        color=blend(color,(.075,.035,.018),brow)
        cheek=.10*G(abs(x),.047,.020)*G(z,1.647,.020)
        color=blend(color,(.36,.145,.10),cheek)
    if region.startswith('ear'):color=blend(color,(.36,.145,.10),.13)
    # Low-amplitude pigment is original and continuous in object space, not baked light.
    v=1+.006*math.sin(x*57)*math.sin(y*63)*math.sin(z*81)
    return (*[c*v for c in color],1)

attr=mesh.color_attributes.new(name='Original_pigment_bake_source_v2',type='FLOAT_COLOR',domain='CORNER')
for loop in mesh.loops:attr.data[loop.index].color=pigment(mesh.vertices[loop.vertex_index].co,regions[loop.vertex_index])
mat=body.data.materials[0];nodes=mat.node_tree.nodes;links=mat.node_tree.links
bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED');output=next(n for n in nodes if n.type=='OUTPUT_MATERIAL')
color=nodes.new('ShaderNodeVertexColor');color.layer_name=attr.name
emission=nodes.new('ShaderNodeEmission');links.new(color.outputs['Color'],emission.inputs['Color']);links.new(emission.outputs[0],output.inputs['Surface'])
size=2048;image=bpy.data.images.new('Original_anatomical_albedo_v2',size,size,alpha=False)
seed=np.tile(np.array([.34,.172,.105,1],dtype=np.float32),size*size);image.pixels.foreach_set(seed)
target=nodes.new('ShaderNodeTexImage');target.image=image;nodes.active=target
bpy.ops.object.select_all(action='DESELECT');body.select_set(True);bpy.context.view_layer.objects.active=body
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=1;scene.render.bake.use_clear=False
bpy.ops.object.bake(type='EMIT',margin=12)
image.filepath_raw=str(OUT/'athlete-albedo-2048.png');image.file_format='PNG';image.save();image.pack()
links.new(bsdf.outputs[0],output.inputs['Surface']);links.new(target.outputs['Color'],bsdf.inputs['Base Color'])
nodes.remove(emission);nodes.remove(color);mesh.color_attributes.remove(attr)
scene.cycles.samples=32

# Raster audit: strict triangle interiors avoid double-counting a shared UV edge.
mesh.calc_loop_triangles();uv=mesh.uv_layers.active.data
occupancy=np.zeros((size,size),np.uint16);degenerate=0;centroid_misses=[];areas=[]
rgba=np.empty(size*size*4,dtype=np.float32);image.pixels.foreach_get(rgba);rgba=rgba.reshape((size,size,4))
for tri in mesh.loop_triangles:
    pts=np.array([tuple(uv[i].uv) for i in tri.loops],dtype=float)*size
    a,b,c=pts;den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
    areas.append(abs(den)*.5)
    if abs(den)<1e-9:degenerate+=1;continue
    lo=np.maximum(0,np.floor(pts.min(axis=0)).astype(int));hi=np.minimum(size-1,np.ceil(pts.max(axis=0)).astype(int))
    xx,yy=np.meshgrid(np.arange(lo[0],hi[0]+1)+.5,np.arange(lo[1],hi[1]+1)+.5)
    w0=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
    w1=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den
    inside=(w0>1e-5)&(w1>1e-5)&(1-w0-w1>1e-5)
    occupancy[lo[1]:hi[1]+1,lo[0]:hi[0]+1]+=inside
    center=pts.mean(axis=0).astype(int);center=np.clip(center,0,size-1)
    if rgba[center[1],center[0],0]<.035:centroid_misses.append(tri.polygon_index)
report={'resolution':[size,size],'uvLayer':mesh.uv_layers.active.name,'method':'Anatomical seam islands, native angle-based unwrap, original emission-only pigment bake; no lighting, stock pixels or vertex-colour fallback.','marginPixels':12,'degenerateUVTriangles':degenerate,'overlappingInteriorPixels':int((occupancy>1).sum()),'coveredInteriorPixels':int((occupancy>0).sum()),'bodyUVTriangles':len(mesh.loop_triangles),'triangleCentroidBlackMisses':len(centroid_misses),'centroidMissFaceIds':sorted(set(centroid_misses)),'minimumUVTriangleAreaPixels':float(min(areas)),'sourceColourAttributeRemoved':not bool(mesh.color_attributes)}
(OUT/'uv-atlas-audit.json').write_text(json.dumps(report,indent=2));print('UV_AUDIT',json.dumps(report),flush=True)

# Keep checker evidence independent from the colour render, then restore exact PBR image.
checker=bpy.data.images.new('Original_UV_checker_v2',1024,1024)
yy,xx=np.indices((1024,1024));values=np.where((xx//32+yy//32)%2,.62,.15).astype(np.float32)
px=np.ones((1024,1024,4),np.float32);px[:,:,:3]=values[:,:,None];checker.pixels.foreach_set(px.reshape(-1));target.image=checker
cam=scene.camera
for name,pos,point,scale in [('uv-checker',(3,-5,1.5),(0,0,.90),2.02),('uv-face',(0,-3,1.67),(0,0,1.66),.37),('uv-hand',(1,-3,1.10),(.635,0,.97),.30)]:
    cam.location=pos;cam.rotation_euler=(Vector(point)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(name+'.png'));bpy.ops.render.render(write_still=True)
target.image=image
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-iteration2.blend'))
