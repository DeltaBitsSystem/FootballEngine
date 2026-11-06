"""Original parametric athlete, authored in an isolated Blender process only.

Run: blender --background --factory-startup --python Tools/MatchViewer3D/PlayerBodyRework/author_body.py
No imported meshes, images, libraries, motion data or generators are used.
"""
import bpy
import bmesh
import math
import json
import os
from pathlib import Path
from mathutils import Vector, Quaternion
from math import sin, cos, pi, exp

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT.mkdir(parents=True, exist_ok=True)
EVIDENCE = OUT / 'evidence'
EVIDENCE.mkdir(exist_ok=True)
scene = bpy.context.scene
# This file is a new factory-startup asset, never the user's interactive scene.
for obj in list(scene.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1

def active(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o

def enum(cls, prop):
    return [i.identifier for i in cls.bl_rna.properties[prop].enum_items]

def material(name, color, roughness=.55):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = (*color, 1)
    b.inputs['Roughness'].default_value = roughness
    return m

skin = material('Skin_original_untextured', (.42, .235, .145), .56)
white = material('Sclera', (.57, .55, .49), .38)
iris = material('Iris_brown', (.045, .026, .012), .42)
pupil = material('Pupil', (.006, .004, .003), .35)
cloth = material('Separate_compression_briefs', (.025, .045, .065), .85)

parts = []
def mesh(name, verts, faces, mat=skin, add=True):
    m = bpy.data.meshes.new(name)
    m.from_pydata(verts, [], faces)
    m.update()
    bm=bmesh.new();bm.from_mesh(m)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(m);bm.free()
    o = bpy.data.objects.new(name, m)
    scene.collection.objects.link(o)
    o.data.materials.append(mat)
    for p in m.polygons:
        p.use_smooth = True
    if add:
        parts.append(o)
    return o

def sample(rows, subdivisions=4):
    """Catmull-Rom sections retain intentional bony landmarks without tube steps."""
    result = []
    for i in range(len(rows)-1):
        a,b,c,d = [rows[max(0,min(len(rows)-1,j))] for j in (i-1,i,i+1,i+2)]
        for k in range(subdivisions):
            t=k/subdivisions
            result.append([.5*((2*b[n])+(-a[n]+c[n])*t+(2*a[n]-5*b[n]+4*c[n]-d[n])*t*t+(-a[n]+3*b[n]-3*c[n]+d[n])*t*t*t) for n in range(len(b))])
    result.append(rows[-1])
    return result

def loft(name, rows, axis1=(1,0,0), axis2=(0,1,0), sides=48, sculpt=None, subdivisions=4):
    rows = sample(rows, subdivisions)
    u,v = Vector(axis1),Vector(axis2)
    verts=[]
    for x,y,z,a,b in rows:
        center=Vector((x,y,z))
        for j in range(sides):
            t=2*pi*j/sides
            p=center+u*a*cos(t)+v*b*sin(t)
            if sculpt:
                p=sculpt(p,t)
            verts.append(tuple(p))
    faces=[]
    for k in range(len(rows)-1):
        for j in range(sides):
            a=k*sides+j; b=k*sides+(j+1)%sides
            faces.append((a,b,b+sides,a+sides))
    faces.extend([tuple(reversed(range(sides))),tuple((len(rows)-1)*sides+j for j in range(sides))])
    return mesh(name,verts,faces)

def G(x,c,s):
    return exp(-((x-c)/s)**2)

def torso_sculpt(p,t):
    x,y,z=p
    front=max(0,-sin(t))**5
    back=max(0,sin(t))**4
    # Paired pectoral planes, sternum valley, costal margin and modest abdominal pads.
    relief=.012*G(abs(x),.09,.065)*G(z,1.32,.075)
    relief-=.006*G(x,0,.019)*G(z,1.29,.12)
    relief+=.005*G(abs(x),.047,.030)*(G(z,1.18,.038)+G(z,1.105,.035))
    relief-=.005*G(z,1.255,.014)*G(abs(x),.085,.06)
    relief+=.007*G(z,1.405-.12*abs(x),.012)*G(abs(x),.085,.075)
    relief-=.004*G(x,0,.009)*G(z,1.08,.012)
    p.y-=front*relief
    p.y+=back*(.008*G(abs(x),.085,.045)*G(z,1.32,.095)-.006*G(x,0,.015)*G(z,1.25,.20))
    return p

loft('SOURCE_Ribcage_abdomen_pelvis',[(0,.015,.80,.075,.065),(0,.015,.86,.133,.103),(0,.012,.94,.148,.105),(0,0,1.015,.133,.094),(0,0,1.10,.124,.090),(0,0,1.19,.146,.103),(0,0,1.29,.176,.111),(0,0,1.365,.183,.100),(0,0,1.40,.161,.078),(0,.01,1.455,.064,.056),(0,.012,1.49,.035,.035)],sculpt=torso_sculpt)
loft('SOURCE_Neck',[(0,.012,1.41,.063,.052),(0,.014,1.46,.051,.049),(0,.010,1.515,.046,.047),(0,.008,1.56,.052,.050)],sides=48)

def face(p,t):
    x,y,z=p
    f=max(0,-sin(t))**4
    relief=.014*G(x,0,.013)*G(z,1.65,.040)+.018*G(x,0,.022)*G(z,1.628,.015)
    relief+=.009*G(abs(x),.018,.009)*G(z,1.623,.009)
    relief+=.008*G(abs(x),.044,.022)*G(z,1.648,.025)
    relief+=.007*G(abs(x),.032,.024)*G(z,1.696,.010)
    relief-=.013*G(abs(x),.033,.018)*G(z,1.679,.010)
    # A low raised orbital rim surrounds the recessed socket, rather than stuck-on lids.
    for sign in [-1,1]:
        r=math.sqrt(((x-sign*.033)/.021)**2+((z-1.679)/.010)**2)
        relief+=.004*exp(-((r-1)/.23)**2)
    relief+=.007*G(x,0,.027)*G(z,1.592,.006)
    relief+=.006*G(x,0,.025)*G(z,1.582,.006)
    relief-=.0045*G(x,0,.027)*G(z,1.588,.0025)
    relief+=.008*G(x,0,.033)*G(z,1.557,.018)
    p.y-=f*relief
    return p

head=loft('SOURCE_Integrated_face_cranium',[(0,-.006,1.525,.025,.034),(0,-.010,1.548,.049,.054),(0,-.001,1.575,.065,.065),(0,.005,1.62,.075,.077),(0,.009,1.67,.078,.081),(0,.011,1.715,.078,.081),(0,.014,1.757,.064,.070),(0,.014,1.787,.035,.043),(0,.014,1.80,.002,.003)],sides=128,sculpt=face,subdivisions=10)

def ellipsoid(name,center,radii,mat=skin,add=True,segments=32,rings=20):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments,ring_count=rings,location=center)
    o=bpy.context.object
    o.name=name
    o.scale=radii
    active(o)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(mat)
    for p in o.data.polygons:p.use_smooth=True
    if add:parts.append(o)
    return o

for s,suffix in [(-1,'R'),(1,'L')]:
    # A flattened pinna is fused into the temporal surface; its concha is sculpted after union.
    ear=ellipsoid('SOURCE_Pinna.'+suffix,(s*.078,.009,1.65),(.019,.015,.034))
    for v in ear.data.vertices:
        if s*v.co.x>0:
            v.co.x-=s*.008*G(v.co.y,0,.009)*G(v.co.z,0,.020)
    shoulder=Vector((s*.177,0,1.377));elbow=Vector((s*.390,0,1.20));wrist=Vector((s*.558,0,1.042))
    d=(wrist-shoulder).normalized();u=Vector((0,1,0));v=d.cross(u).normalized()
    armrows=[]
    for t,a,b in [(-.20,.038,.038),(0,.056,.059),(.12,.067,.067),(.27,.059,.057),(.42,.050,.048),(.49,.038,.039),(.54,.036,.036),(.59,.041,.040),(.69,.044,.042),(.83,.035,.032),(1,.022,.023)]:
        p=shoulder.lerp(wrist,t)
        armrows.append((*p,a,b))
    loft('SOURCE_Deltoid_arm_forearm.'+suffix,armrows,u,v,sides=48)
    # Palms follow the arm plane; four fingers fan slightly, the thumb opposes proximally.
    hand_d=Vector((s*.69,0,-.72)).normalized();hand_u=Vector((s*.72,0,.69)).normalized()
    palmend=wrist+hand_d*.082
    loft('SOURCE_Palm.'+suffix,[(*wrist,.023,.020),(*(wrist+hand_d*.03),.032,.018),(*(wrist+hand_d*.068),.034,.014),(*palmend,.029,.012)],hand_u,(0,1,0),sides=32)
    for i,(offset,length,radius) in enumerate([(-.024,.060,.0080),(-.008,.081,.0090),(.009,.085,.0092),(.025,.071,.0082)]):
        start=palmend+hand_u*offset-hand_d*.009
        end=start+hand_d*length+hand_u*(offset*.20)
        rows=[]
        for t,r in [(0,1.1),(.16,1),(.36,.95),(.44,.90),(.58,.84),(.72,.83),(.90,.70),(1,.26)]:
            p=start.lerp(end,t)+Vector((0,-.008*sin(pi*t),0))
            rows.append((*p,radius*r,radius*r*.87))
        loft('SOURCE_Finger%d.%s'%(i,suffix),rows,hand_u,(0,1,0),sides=20,subdivisions=2)
    start=wrist+hand_d*.026+hand_u*.022
    end=start+hand_d*.035+hand_u*.047+Vector((0,-.020,0))
    loft('SOURCE_Opposed_thumb.'+suffix,[(*start,.016,.015),(*start.lerp(end,.4),.013,.012),(*start.lerp(end,.73),.010,.009),(*end,.004,.004)],hand_u,(0,1,0),sides=24)
    def leg_sculpt(p,t,s=s):
        x,y,z=p
        p.y-=max(0,-sin(t))**5*(.007*G(z,.57,.035)+.008*G(z,.72,.10))
        p.y+=max(0,sin(t))**5*(.010*G(z,.34,.080)+.012*G(z,.87,.08))
        return p
    loft('SOURCE_Thigh_knee_calf_ankle.'+suffix,[(s*.083,.012,.99,.035,.038),(s*.093,.015,.935,.081,.086),(s*.098,.012,.88,.092,.094),(s*.101,0,.78,.084,.087),(s*.104,-.007,.65,.069,.071),(s*.101,-.014,.55,.047,.051),(s*.100,-.014,.493,.041,.044),(s*.101,-.006,.46,.044,.043),(s*.104,.011,.38,.052,.054),(s*.104,.016,.30,.049,.057),(s*.101,.010,.22,.037,.043),(s*.100,.003,.13,.025,.031),(s*.100,0,.075,.028,.029)],sculpt=leg_sculpt,sides=48)
    # Calcaneus, midfoot arch and metatarsal fan are volumes of the bare foot, not a boot proxy.
    def foot_sculpt(p,t,s=s):
        if p.z<.04 and (p.x-s*.10)*s<0:
            p.z+=.015*G(p.y,-.055,.043)
        return p
    loft('SOURCE_Heel_arch_forefoot.'+suffix,[(s*.10,.058,.053,.020,.023),(s*.10,.032,.049,.032,.040),(s*.10,-.015,.050,.033,.044),(s*.10,-.065,.040,.036,.030),(s*.10,-.13,.030,.048,.022),(s*.10,-.175,.027,.043,.019),(s*.10,-.195,.026,.026,.015)],(1,0,0),(0,0,1),sculpt=foot_sculpt,sides=48)
    for i,(off,length,radius) in enumerate([(-.031,.052,.013),(-.009,.055,.010),(.010,.048,.009),(.027,.039,.008),(.041,.028,.007)]):
        x=s*(.10+off)
        loft('SOURCE_Toe%d.%s'%(i,suffix),[(x,-.164,.026,radius,radius),(x,-.19,.025,radius*.98,radius*.85),(x,-.181-length,.024,radius*.5,radius*.55)],(1,0,0),(0,0,1),sides=20,subdivisions=3)

# Voxel union removes all internal intersection shells; the editable analytic source is retained.
source_collection=bpy.data.collections.new('00_Original_parametric_sources')
scene.collection.children.link(source_collection)
for o in parts:
    for c in list(o.users_collection):c.objects.unlink(o)
    source_collection.objects.link(o)
    o.hide_render=True
    o.hide_set(True)
copies=[]
for o in parts:
    n=o.copy();n.data=o.data.copy();scene.collection.objects.link(n)
    n.hide_render=False;n.hide_set(False);copies.append(n)
bpy.ops.object.select_all(action='DESELECT')
for o in copies:o.select_set(True)
bpy.context.view_layer.objects.active=copies[0]
bpy.ops.object.join()
body=bpy.context.object;body.name='Athlete_ContinuousSkin_LOD0'
body.data.remesh_voxel_size=.0025
bpy.ops.object.voxel_remesh()
smooth=body.modifiers.new('Relax_union_without_hidden_intersections','SMOOTH');smooth.factor=.6;smooth.iterations=3
bpy.ops.object.modifier_apply(modifier=smooth.name)
body.data.materials.clear();body.data.materials.append(skin)
for p in body.data.polygons:p.use_smooth=True
print('UNION',len(body.data.vertices),len(body.data.polygons),flush=True)
sculpt_source=body.copy();sculpt_source.data=body.data.copy();source_collection.objects.link(sculpt_source)
sculpt_source.name='SOURCE_Continuous_high_resolution_skin';sculpt_source.hide_render=True;sculpt_source.hide_set(True)
active(body)
dec=body.modifiers.new('Preparation_for_quad_remesh','DECIMATE');dec.ratio=min(1,50000/max(1,len(body.data.polygons)*2))
bpy.ops.object.modifier_apply(modifier=dec.name)
bm=bmesh.new();bm.from_mesh(body.data)
seen=set();small=[]
for v in bm.verts:
    if v in seen:continue
    component=[];todo=[v];seen.add(v)
    while todo:
        u=todo.pop();component.append(u)
        for e in u.link_edges:
            w=e.other_vert(u)
            if w not in seen:seen.add(w);todo.append(w)
    if len(component)<20:small.extend(component)
if small:bmesh.ops.delete(bm,geom=small,context='VERTS')
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(body.data);bm.free()
body.data.validate(verbose=True)
print('QUAD_START',flush=True)
bpy.ops.object.quadriflow_remesh(target_faces=12000,use_mesh_symmetry=True,use_preserve_sharp=False,use_preserve_boundary=False,seed=17)
print('QUAD_DONE',len(body.data.vertices),len(body.data.polygons),flush=True)
for p in body.data.polygons:p.use_smooth=True

# Preserve the canonical names and A-pose, but author a true 1.80 m bind skeleton.
S=1.8/1.936
defs=[('root',(0,0,0),(0,0,.20),None),('pelvis',(0,0,.97),(0,0,1.11),'root'),('spine',(0,0,1.11),(0,0,1.33),'pelvis'),('chest',(0,0,1.33),(0,0,1.52),'spine'),('neck',(0,0,1.52),(0,0,1.65),'chest'),('head',(0,0,1.65),(0,0,1.87),'neck')]
for s,suffix in [(-1,'R'),(1,'L')]:
    defs.extend([(f'clavicle.{suffix}',(0,0,1.48),(s*.19,0,1.48),'chest'),(f'upper_arm.{suffix}',(s*.19,0,1.48),(s*.42,0,1.29),f'clavicle.{suffix}'),(f'forearm.{suffix}',(s*.42,0,1.29),(s*.60,0,1.12),f'upper_arm.{suffix}'),(f'hand.{suffix}',(s*.60,0,1.12),(s*.67,0,1.06),f'forearm.{suffix}'),(f'thigh.{suffix}',(s*.10,0,.97),(s*.10,-.015,.53),'pelvis'),(f'shin.{suffix}',(s*.10,-.015,.53),(s*.10,0,.095),f'thigh.{suffix}'),(f'foot.{suffix}',(s*.10,0,.095),(s*.10,-.16,.065),f'shin.{suffix}'),(f'toe.{suffix}',(s*.10,-.16,.065),(s*.10,-.23,.045),f'foot.{suffix}')])
data=bpy.data.armatures.new('CanonicalNames_New180cmBind')
rig=bpy.data.objects.new('AthleteRig_Staging',data);scene.collection.objects.link(rig);active(rig)
bpy.ops.object.mode_set(mode='EDIT')
for name,h,t,parent in defs:
    b=data.edit_bones.new(name);b.head=Vector(h)*S;b.tail=Vector(t)*S
    if parent:b.parent=data.edit_bones[parent]
bpy.ops.object.mode_set(mode='OBJECT')
rig.show_in_front=True

def smoothstep(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)))
    return t*t*(3-2*t)

def weights(p):
    x,y,z=p;ax=abs(x);sf='L' if x>=0 else 'R'
    if z>1.505:return {'head':1}
    if z>1.43 and ax<.09:
        t=smoothstep(1.45,1.535,z);return {'neck':1-t,'head':t}
    # Side gating keeps the wrist from attracting the anatomically nearby pelvis.
    arm=smoothstep(.14,.21,ax) if z>1.29 else smoothstep(.19,.29,ax)
    if arm>.001 and z>.91:
        shoulder=Vector((.19*S,0,1.48*S));wrist=Vector((.60*S,0,1.12*S))
        q=Vector((ax,y,z));t=(q-shoulder).dot(wrist-shoulder)/(wrist-shoulder).length_squared
        elbow=smoothstep(.43,.61,t);hand=smoothstep(.94,1.07,t)
        w={f'upper_arm.{sf}':(1-elbow)*(1-hand)*arm,f'forearm.{sf}':elbow*(1-hand)*arm,f'hand.{sf}':hand*arm,'chest':1-arm}
        return w
    if z<.90:
        pel=smoothstep(.79,.94,z)
        shin=1-smoothstep(.445,.545,z)
        foot=1-smoothstep(.075,.15,z)
        toe=1-smoothstep(-.185,-.14,y)
        return {'pelvis':pel,f'thigh.{sf}':(1-pel)*(1-shin),f'shin.{sf}':(1-pel)*shin*(1-foot),f'foot.{sf}':(1-pel)*shin*foot*(1-toe),f'toe.{sf}':(1-pel)*shin*foot*toe}
    sp=smoothstep(.94,1.14,z);ch=smoothstep(1.16,1.32,z)
    return {'pelvis':1-sp,'spine':sp*(1-ch),'chest':sp*ch}

def skin_object(o,rigid=None):
    groups={b.name:o.vertex_groups.new(name=b.name) for b in data.bones}
    for v in o.data.vertices:
        w={rigid:1} if rigid else weights(v.co)
        w=sorted([(n,a) for n,a in w.items() if a>1e-6],key=lambda p:-p[1])[:4]
        total=sum(a for _,a in w)
        for n,a in w:groups[n].add([v.index],a/total,'REPLACE')
    mod=o.modifiers.new('CanonicalNames_Skinning','ARMATURE');mod.object=rig;mod.use_deform_preserve_volume=False
    o.parent=rig

active(body)
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(70),island_margin=.012)
bpy.ops.object.mode_set(mode='OBJECT')
skin_object(body)
eyes=[]
for s,suffix in [(-1,'R'),(1,'L')]:
    for name,center,radii,mat in [('Eye', (s*.033,-.0685,1.679),(.016,.010,.0068),white),('Iris',(s*.033,-.0781,1.679),(.005,.0015,.005),iris),('Pupil',(s*.033,-.0793,1.679),(.0023,.001,.0024),pupil)]:
        o=ellipsoid(name+'.'+suffix,center,radii,mat,False,24,12)
        bpy.ops.object.transform_apply(location=True,rotation=False,scale=False)
        skin_object(o,'head');eyes.append(o)

# Separate modesty garment; skin exists underneath and remains inspectable in the source.
brief=loft('Separate_briefs',[(0,.012,1.005,.140,.103),(0,.012,.965,.153,.113),(0,.012,.91,.151,.117),(0,.01,.86,.106,.097)],sides=64,subdivisions=3)
brief.data.materials.clear();brief.data.materials.append(cloth)
skin_object(brief)

def stats(o):
    bm=bmesh.new();bm.from_mesh(o.data)
    edges=sum(not e.is_manifold for e in bm.edges)
    deg=sum(f.calc_area()<1e-12 for f in bm.faces)
    seen=set();components=0
    for v in bm.verts:
        if v in seen:continue
        components+=1;seen.add(v)
        todo=[v]
        while todo:
            u=todo.pop()
            for e in u.link_edges:
                w=e.other_vert(u)
                if w not in seen:seen.add(w);todo.append(w)
    bm.free()
    o.data.calc_loop_triangles()
    return {'vertices':len(o.data.vertices),'quads':sum(len(p.vertices)==4 for p in o.data.polygons),'triangles':len(o.data.loop_triangles),'nonManifoldEdges':edges,'degenerateFaces':deg,'connectedComponents':components}

report={'body':stats(body),'heightMeters':max(v.co.z for v in body.data.vertices)-min(v.co.z for v in body.data.vertices),'source':'Original analytic contours and Gaussian anatomical relief, voxel union, Blender QuadriFlow; no external mesh/texture/clip data','skeleton':[{'name':b.name,'head':list(b.head_local),'tail':list(b.tail_local),'parent':b.parent.name if b.parent else None} for b in data.bones],'bindScaleVsCurrentAuthoring':S,'maximumWeights':max(len(v.groups) for v in body.data.vertices),'unweightedVertices':sum(not v.groups for v in body.data.vertices),'maxWeightSumError':max(abs(sum(g.weight for g in v.groups)-1) for v in body.data.vertices),'limitations':['New bind transforms: not a drop-in replacement of current GLB or motion manifest.','Fingers have complete geometry but move rigidly with hand bones; no individual finger animation.','Automatic quad flow is not hand-retopologized; inspect shoulder and crotch extremes before production integration.','Skin is constant PBR base color, not the runtime atlas; no copied texture pixels.','Validation action contains authored test poses only, not a production locomotion library.']}
(OUT/'validation.json').write_text(json.dumps(report,indent=2))

# Named frame markers make the deformation evidence repeatable in the editable file.
def set_world_rotation(name,axis,degrees):
    b=rig.pose.bones[name];b.rotation_mode='QUATERNION'
    local=data.bones[name].matrix_local.to_3x3().inverted()@Vector(axis)
    b.rotation_quaternion=Quaternion(local,math.radians(degrees))

poses=[('Rest',1,{}),('Arms_raised',20,{'upper_arm.L':((0,1,0),-110),'upper_arm.R':((0,1,0),110)}),('Elbow_110',40,{'forearm.L':((0,0,1),-110),'forearm.R':((0,0,1),110)}),('Hip70_Knee110',60,{'thigh.L':((1,0,0),-70),'shin.L':((1,0,0),110)}),('Running_stride',80,{'thigh.L':((1,0,0),-48),'shin.L':((1,0,0),73),'thigh.R':((1,0,0),33),'shin.R':((1,0,0),45),'upper_arm.L':((1,0,0),32),'forearm.L':((0,0,1),-65),'upper_arm.R':((1,0,0),-35),'forearm.R':((0,0,1),65)})]
for name,frame,rotations in poses:
    for b in rig.pose.bones:
        b.rotation_mode='QUATERNION';b.rotation_quaternion=(1,0,0,0)
    for bn,(axis,angle) in rotations.items():set_world_rotation(bn,axis,angle)
    for b in rig.pose.bones:b.keyframe_insert('rotation_quaternion',frame=frame,group=b.name)
    scene.timeline_markers.new(name,frame=frame)
rig.animation_data.action.name='Original_Deformation_Validation_NOT_runtime_motion'
scene.frame_end=80;scene.frame_set(1)

# Explicit selection prevents reference/source geometry and studio objects entering the GLB.
active(rig)
for o in [body,brief]+eyes:o.select_set(True)
formats=enum(bpy.types.ImageFormatSettings,'file_format')
export_rna=bpy.ops.export_scene.gltf.get_rna_type()
export_formats=[i.identifier for i in export_rna.properties['export_format'].enum_items]
print('EXPORT_ENUM_RNA',export_formats,flush=True)
bpy.ops.export_scene.gltf(filepath=str(OUT/'athlete-body-staging.glb'),export_format='GLB',use_selection=True,export_animations=False,export_skins=True,export_yup=True,export_apply=False)

# Original studio illumination; CPU rendering avoids competing for the parent's GPU.
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=24
scene.cycles.use_denoising=True;scene.cycles.max_bounces=4
scene.render.threads_mode='FIXED';scene.render.threads=6
scene.render.resolution_x=640;scene.render.resolution_y=800;scene.render.resolution_percentage=100
assert 'PNG' in formats
scene.render.image_settings.file_format='PNG'
world=bpy.data.worlds.new('Original_neutral_studio');world.use_nodes=True;scene.world=world
bg=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs['Color'].default_value=(.12,.14,.17,1);bg.inputs['Strength'].default_value=.45
def aim(o,p):o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
for name,pos,power,size in [('Key',(-2,-3,3.3),350,2),('Fill',(2,-2,2),160,2.5),('Rim',(1,2,2.8),250,1.5)]:
    d=bpy.data.lights.new(name,'AREA');o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=pos;d.energy=power;d.size=size;aim(o,(0,0,1))
bpy.ops.mesh.primitive_plane_add(size=200)
floor=bpy.context.object;floor.name='Studio_floor_NOT_exported';floor.data.materials.append(material('Studio_floor',(.065,.079,.095),.9));floor.location.z=-.005
d=bpy.data.cameras.new('Orthographic_inspection');cam=bpy.data.objects.new('Orthographic_inspection',d);scene.collection.objects.link(cam);scene.camera=cam
d.type='ORTHO';d.ortho_scale=2.02

def render(name,pos,target,scale,frame=1):
    scene.frame_set(frame);cam.location=pos;aim(cam,target);d.ortho_scale=scale
    scene.render.filepath=str(EVIDENCE/(name+'.png'))
    assert scene.camera is not None
    bpy.ops.render.render(write_still=True)

bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-body-editable.blend'))
if os.environ.get('FE_BODY_SKIP_RENDER')!='1':
    for name,pos in [('front',(0,-5,.92)),('profile',(5,0,.92)),('back',(0,5,.92)),('three-quarter',(3,-5,1.5))]:render(name,pos,(0,0,.90),2.02)
    render('head-front',(0,-3,1.67),(0,0,1.66),.37)
    render('head-profile',(3,-.8,1.67),(0,0,1.66),.37)
    render('hand-closeup',(1,-3,1.10),(.635,0,.97),.30)
    render('feet-closeup',(.7,-1.6,.55),(0,-.07,.105),.48)
    render('arms-raised',(2,-5,1.3),(0,0,1.02),2.25,20)
    render('elbows-flexed',(2,-5,1.3),(0,0,.98),2.12,40)
    render('hip-knee-flexed',(4,-4,1.2),(0,0,.90),2.05,60)
    render('running-stride',(4,-4,1.2),(0,0,.90),2.05,80)
scene.frame_set(1)
print('DELIVERY_COMPLETE',json.dumps(report['body']),flush=True)
