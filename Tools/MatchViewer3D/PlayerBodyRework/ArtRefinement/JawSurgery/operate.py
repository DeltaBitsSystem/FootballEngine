"""Replace a connected annular strip with jaw-following control loops."""
from pathlib import Path
import math,json,hashlib
import bpy,bmesh
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[5]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
OUT=BASE/'Jaw-Surgery'
SOURCE=BASE/'Contour-Study/contour-form.blend'
BASELINE=BASE/'final-body-source.blend'
bpy.ops.wm.open_mainfile(filepath=str(BASELINE))
baseline=[v.co.copy() for v in bpy.data.objects['FINAL_Body_Connected'].data.vertices]
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene=bpy.context.scene;scene.frame_set(1)
obj=bpy.data.objects['FINAL_Body_Connected'];mesh=obj.data
bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table();bm.verts.index_update()
ring_z=[1.543+i*.003 for i in range(23)]
rings=[]
for z in ring_z:
    row=[v for v in bm.verts if abs(v.co.z-z)<.00004]
    if len(row)!=128:raise RuntimeError(f'Expected 128 connected controls at {z}, found {len(row)}')
    if not rings:
        row.sort(key=lambda v:math.atan2(v.co.y-.012,v.co.x))
    else:
        pool=set(row)
        ordered=[]
        for previous in rings[-1]:
            adjacent=[e.other_vert(previous) for e in previous.link_edges if e.other_vert(previous) in pool]
            if len(adjacent)!=1:raise RuntimeError(f'Expected one longitudinal neighbour at {z}, got {len(adjacent)}')
            ordered.append(adjacent[0])
        if len(set(ordered))!=128:raise RuntimeError(f'Non-bijective longitudinal loop at {z}')
        row=ordered
    rings.append(row)
for row in rings:
    assert all(bm.edges.get((row[i],row[(i+1)%128])) for i in range(128))
for a,b in zip(rings,rings[1:]):
    assert all(bm.edges.get((a[i],b[i])) for i in range(128)), 'ring angular correspondence failed'
coords=[[v.co.copy() for v in row] for row in rings]
original=[[baseline[v.index] for v in row] for row in rings]
start,end=rings[0],rings[-1]
deleted=[v for row in rings[1:-1] for v in row]
bmesh.ops.delete(bm,geom=deleted,context='VERTS')
newrings=[start]
num=27
for i in range(1,num):
    t=i/num; u=t*(len(coords)-1);k=min(len(coords)-2,int(u));f=u-k
    row=[]
    for j in range(128):
        p=coords[k][j].lerp(coords[k+1][j],f)
        base=original[k][j].lerp(original[k+1][j],f)
        # Blend the anterior underside back toward the pre-contour surface;
        # the posterior contour study remains, removing its under-chin wedge.
        anterior=max(0,min(1,(-p.y-.005)/.06))
        p.y=p.y*(1-anterior)+base.y*anterior
        angle=math.atan2(p.y-.012,p.x)
        side=abs(math.cos(angle))**2
        taper=math.sin(math.pi*t)**2
        # Inclined control flow: loops lift laterally toward the ramus but
        # meet both original circular borders continuously.
        p.z+=.003*side*taper
        p.x+=(1 if p.x>0 else -1)*.001*side*taper
        row.append(bm.verts.new(p))
    newrings.append(row)
newrings.append(end)
for row,nextrow in zip(newrings,newrings[1:]):
    for j in range(128):
        bm.faces.new((row[j],row[(j+1)%128],nextrow[(j+1)%128],nextrow[j]))
bm.normal_update()
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
nonmanifold=sum(not e.is_manifold for e in bm.edges)
assert nonmanifold==0, f'Unclosed strip: {nonmanifold} edges'
bm.to_mesh(mesh);bm.free();mesh.update()
for poly in mesh.polygons:poly.use_smooth=True
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'jaw-form.blend'))
result={'source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        'removedVertices':len(deleted),'newVertices':(num-1)*128,'newFaces':num*128,'nonManifoldEdges':nonmanifold,
        'note':'Form-only surgery: new loops have not yet received UVs, anatomical region IDs or skin weights.'}
(OUT/'surgery.json').write_text(json.dumps(result,indent=2))
print('SURGERY',json.dumps(result),flush=True)
