"""Inspect the real ring topology and test a local section-cage reconstruction."""
import json,math,sys,hashlib
from pathlib import Path
import bpy,bmesh

ROOT=Path(__file__).resolve().parents[5]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
OUT=BASE/'Contour-Study'
SOURCE=BASE/'final-body-source.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene=bpy.context.scene;scene.frame_set(1)
obj=bpy.data.objects['FINAL_Body_Connected'];mesh=obj.data
names=json.loads((BASE/'final-body-authoring.json').read_text())['regionNames']
ids=mesh.attributes['Anatomical_region_id'].data
bm=bmesh.new();bm.from_mesh(mesh);bm.verts.ensure_lookup_table()
verts=[v for v in bm.verts if names[ids[v.index].value]=='head']
bands=[]
for z in [1.525,1.54,1.55,1.56,1.575,1.59,1.605,1.62,1.635,1.65,1.665,1.68]:
    p=[v for v in verts if abs(v.co.z-z)<.002]
    if p:
        bands.append({'z':z,'count':len(p),'front':min(v.co.y for v in p),'rear':max(v.co.y for v in p),'width':2*max(abs(v.co.x) for v in p)})
degree={str(n):sum(1 for v in verts if len(v.link_edges)==n) for n in range(3,9)}
print('HEAD_LOOPS',json.dumps({'bands':bands,'vertexDegree':degree,'headVertices':len(verts)}),flush=True)
if '--inspect' in sys.argv:
    bm.free();raise SystemExit

# Cross-section points prescribe the rear/forward envelope; these are whole
# slices, not independent bumps.  Outside this patch the old surface is fixed.
rear=[(1.520,.066),(1.540,.066),(1.555,.066),(1.575,.067),(1.590,.068),(1.605,.072),(1.620,.078),(1.635,.087),(1.650,.096),(1.665,.103),(1.680,.105)]
front=[(1.535,-.018),(1.550,-.034),(1.560,-.045),(1.575,-.061),(1.590,-.088),(1.605,-.088),(1.620,-.083)]
side_width=[(1.525,1.0),(1.550,1.0),(1.575,.96),(1.590,.95),(1.605,.97),(1.625,1.0),(1.650,1.0)]
def interp(points,z):
    if z<=points[0][0]:return points[0][1]
    if z>=points[-1][0]:return points[-1][1]
    for i,((a,x),(b,y)) in enumerate(zip(points,points[1:])):
        if a<=z<=b:
            slope=(y-x)/(b-a)
            left=slope if i==0 else (y-points[i-1][1])/(b-points[i-1][0])
            right=slope if i+2==len(points) else (points[i+2][1]-x)/(points[i+2][0]-a)
            t=(z-a)/(b-a)
            return (2*t**3-3*t*t+1)*x+(t**3-2*t*t+t)*(b-a)*left+(-2*t**3+3*t*t)*y+(t**3-t*t)*(b-a)*right
def mix(a,b,t):return a+(b-a)*max(0,min(1,t))
before=[v.co.copy() for v in bm.verts]
rear_bins={};front_bins={}
for v in verts:
    p=before[v.index]
    if abs(p.x)<.001 and 1.525<=p.z<=1.682:
        key=round(p.z,5)
        if p.y>.03:rear_bins.setdefault(key,[]).append(p.y)
        if p.y<-.01:front_bins.setdefault(key,[]).append(p.y)
rear_source=sorted((z,max(values)) for z,values in rear_bins.items())
front_source=sorted((z,min(values)) for z,values in front_bins.items())
assert len(rear_source)>20 and len(front_source)>20
for v in verts:
    x,y,z=v.co
    if not 1.525<z<1.68:continue
    # Sampling the existing posterior contour keeps the loft tied to actual mesh
    # cross-sections, including their nonuniform spacing.
    if y>.018:
        current=interp(rear_source,z)
        target=interp(rear,z)
        radial=max(0,min(1,(y-.018)/max(.001,current-.018)))
        radial=radial*radial*(3-2*radial)
        edge=min(1,(z-1.525)/.015,(1.68-z)/.02)
        v.co.y=mix(y,y+(target-current)*radial,edge)
    # Only the anterior underside is rebuilt; the chin and lips remain fixed.
    if 1.535<z<1.605 and y<-.025:
        current=interp(front_source,z)
        target=interp(front,z)
        radial=max(0,min(1,(-y-.025)/max(.001,-current-.025)))
        radial=radial*radial*(3-2*radial)
        edge=min(1,(z-1.535)/.015,(1.605-z)/.015)
        v.co.y=mix(v.co.y,v.co.y+(target-current)*radial,edge)
    if 1.55<z<1.625 and y<.04:
        lateral=max(0,min(1,(.04-y)/.05))
        v.co.x=x*mix(1,interp(side_width,z),lateral)
bm.to_mesh(mesh);bm.free();mesh.update()
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'contour-form.blend'))
changed=[v.index for v in mesh.vertices if (v.co-before[v.index]).length>1e-8]
report={'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(), 'bandsBefore':bands,'vertexDegree':degree,
        'changedVertices':len(changed),'maxDisplacementMm':max((mesh.vertices[i].co-before[i]).length for i in changed)*1000,
        'note':'Experimental section-cage form; inherited skin and maps not validated or updated.'}
(OUT/'contour-study.json').write_text(json.dumps(report,indent=2))
