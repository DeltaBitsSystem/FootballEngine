"""Two isolated, region-masked large-form studies on the existing original cage."""
import bpy, json, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'Mass-Study-15';OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Sculpt-11/athlete-sculpt-study.blend'))
scene=bpy.context.scene;body=bpy.data.objects['B_connected_0'];mesh=body.data
names=json.loads((BASE/'Iteration2/cage-construction.json').read_text())['regionNames']
regions=[names[v.value] for v in mesh.attributes['Anatomical_region_id'].data]
base=[v.co.copy() for v in mesh.vertices]
def G(x,c,w):return math.exp(-((x-c)/w)**2)
def smooth(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def displaced(p,region,intensity):
    x,y,z=p;s=1 if x>=0 else -1;ax=abs(x);out=p.copy()
    if region=='torso':
        # Ribcage grows in depth as a coherent volume; the anterior and
        # posterior halves are adjusted independently, not pectoral balloons.
        rib=G(z,1.29,.135);front=smooth(0,.09,-y);back=smooth(0,.09,y)
        out.y+=intensity*(-.032*front+.029*back)*rib
        out.x+=intensity*s*.016*smooth(.035,.16,ax)*G(z,1.30,.14)
        # Broad neck transitions into the shoulder girdle rather than only
        # widening one sampled band. Anchor the head junction unchanged.
        neck=G(z,1.46,.052)*(1-smooth(1.51,1.535,z))
        out.x+=intensity*s*.021*smooth(.01,.055,ax)*neck
        # Clavicular sweep is broad, integrated into anterior surface.
        collar_z=1.415-.13*ax
        out.y-=intensity*.006*G(z,collar_z,.014)*G(ax,.085,.09)*front
        # Pelvic bowl and lumbar-to-sacral transition.
        out.y+=intensity*.024*back*G(z,.94,.105)
        out.y-=intensity*.008*front*G(z,.99,.11)
    elif region.startswith('arm.'):
        # Soft support ends before the elbow; no wrist/hand vertices move.
        shoulder=G(ax,.225,.065)*G(z,1.355,.09)
        out.y+=intensity*(.020*smooth(0,.05,y)-.015*smooth(0,.05,-y))*shoulder
        out.z+=intensity*.009*shoulder
    elif region.startswith('leg.'):
        # Upper posterior mass is separate from the tibial taper.
        back=smooth(0,.055,y);front=smooth(0,.055,-y)
        out.y+=intensity*.037*back*G(z,.815,.095)
        out.y+=intensity*.014*back*G(z,.59,.12)
        out.y-=intensity*.011*front*G(z,.495,.038)
        out.y+=intensity*.012*back*G(z,.36,.085)
        out.y-=intensity*.004*front*G(z,.28,.11)
    return out
body.shape_key_add(name='Basis')
audit={}
allowed=lambda r:r=='torso' or r.startswith(('arm.','leg.'))
for name,factor in [('A_conservative',.55),('B_reconstructed',1.0)]:
    key=body.shape_key_add(name=name);moved={};violations=[]
    offsets=[displaced(p,regions[i],factor)-p for i,p in enumerate(base)]
    adjacency=[[] for _ in base]
    for edge in mesh.edges:
        a,b=edge.vertices;adjacency[a].append(b);adjacency[b].append(a)
    # Only blend displacement vectors along the torso/limb seam; leave the
    # underlying shape and every protected region exactly untouched.
    seam={i for i,r in enumerate(regions) if allowed(r) and any(allowed(regions[j]) and regions[j]!=r for j in adjacency[i])}
    band=set(seam);front=set(seam)
    for _ in range(6):
        front={j for i in front for j in adjacency[i] if allowed(regions[j])}-band;band.update(front)
    for _ in range(20):
        changed={i:offsets[i].lerp(sum((offsets[j] for j in adjacency[i] if allowed(regions[j])),Vector())/sum(1 for j in adjacency[i] if allowed(regions[j])),.4) for i in band if any(allowed(regions[j]) for j in adjacency[i])}
        for i,v in changed.items():offsets[i]=v
    for i,p in enumerate(base):
        q=p+offsets[i];key.data[i].co=q;d=(q-p).length
        if d>1e-8:moved[regions[i]]=moved.get(regions[i],0)+1
        if d>1e-8 and not allowed(regions[i]):violations.append(i)
    audit[name]={'movedByRegion':moved,'outOfDomainVertices':violations,'interfaceBlendVertices':len(band),'maximumMovementMm':max((key.data[i].co-p).length for i,p in enumerate(base))*1000}
    if violations:raise RuntimeError('Protected region moved')
for o in scene.objects:
    if o.name.startswith('A_connected') or o.name=='B_connected_1':o.hide_render=True;o.hide_set(True)
    elif o.name.startswith('B_connected'):o.hide_render=False;o.hide_set(False)
scene.render.threads_mode='FIXED';scene.render.threads=6;scene.cycles.samples=24
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'mass-alternatives.blend'))
shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02),('torso-front',(0,-5,1.20),(0,0,1.20),.82),('torso-profile',(5,0,1.20),(0,0,1.20),.82)]
for name in ['A_conservative','B_reconstructed']:
    body.data.shape_keys.key_blocks['A_conservative'].value=1 if name.startswith('A') else 0
    body.data.shape_keys.key_blocks['B_reconstructed'].value=1 if name.startswith('B') else 0
    for shot,pos,target,scale in shots:
        cam=scene.camera;cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
        scene.render.filepath=str(OUT/'evidence'/(name[0]+'-'+shot+'.png'));bpy.ops.render.render(write_still=True)
(OUT/'mask-audit.json').write_text(json.dumps(audit,indent=2))
print('MASS_STUDIES_COMPLETE',json.dumps(audit),flush=True)
