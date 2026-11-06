"""Final-body anatomy source: connected control loops, no voxel/heat final cage.

Blender --background --factory-startup --python .../final_body_author.py
The rig and pose-test action are read from our archived Iteration1 staging
source; the surface, weights, UVs, eyes and brief are authored here.
"""
import bpy
import bmesh
import math
import json
from pathlib import Path
from mathutils import Vector
from math import pi,sin,cos,exp,sqrt

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'Final-Body';OUT.mkdir(exist_ok=True)
(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'Iteration1/athlete-body-editable.blend'))
scene=bpy.context.scene;scene.frame_set(1)
for o in list(scene.objects):
    if o.type in {'MESH','ARMATURE'} and o.name!='Studio_floor_NOT_exported':o.hide_render=True;o.hide_set(True)
oldrig=bpy.data.objects['AthleteRig_Staging']
rig=oldrig.copy();rig.data=oldrig.data.copy();scene.collection.objects.link(rig)
rig.name='FINAL_Rig';rig.hide_render=False;rig.hide_set(False)
rig.animation_data_create();rig.animation_data.action=oldrig.animation_data.action.copy()
rig.animation_data.action.name='Iteration2_identical_local_pose_tests'
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
for sf in ['L','R']:
    source=rig.data.edit_bones['upper_arm.'+sf]
    support=rig.data.edit_bones.new('axilla_support.'+sf)
    support.head=source.head;support.tail=source.tail;support.roll=source.roll
    support.parent=rig.data.edit_bones['clavicle.'+sf]
bpy.ops.object.mode_set(mode='OBJECT')
from mathutils import Quaternion
for frame in range(1,81):
    scene.frame_set(frame)
    for sf in ['L','R']:
        support=rig.pose.bones['axilla_support.'+sf];support.rotation_mode='QUATERNION'
        support.rotation_quaternion=Quaternion((1,0,0,0)).slerp(rig.pose.bones['upper_arm.'+sf].rotation_quaternion,.75)
        support.keyframe_insert('rotation_quaternion',frame=frame,group=support.name)
scene.frame_set(1)

def smooth(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def G(x,c,s):return exp(-((x-c)/s)**2)
def profile(rows,z):
    # Cubic Hermite interpolation gives authored silhouettes without stepped sections.
    i=next((i for i in range(len(rows)-1) if rows[i][0]<=z<=rows[i+1][0]),len(rows)-2)
    i=max(0,i);a,b=rows[i],rows[i+1];h=b[0]-a[0];t=(z-a[0])/h
    out=[]
    for k in range(1,len(a)):
        before=rows[max(0,i-1)];after=rows[min(len(rows)-1,i+2)]
        ma=(b[k]-before[k])/(b[0]-before[0]);mb=(after[k]-a[k])/(after[0]-a[0])
        out.append((2*t**3-3*t*t+1)*a[k]+(t**3-2*t*t+t)*h*ma+(-2*t**3+3*t*t)*b[k]+(t**3-t*t)*h*mb)
    return out

class Cage:
    def __init__(self):self.v=[];self.f=[];self.w=[];self.region=[];self.patch=[];self.seams=set();self.patch_count=0
    def vertex(self,p,w,region):
        self.v.append(tuple(p));self.w.append({n:a for n,a in w.items() if a>1e-8});self.region.append(region);return len(self.v)-1
    def face(self,indices,patch):self.f.append(tuple(indices));self.patch.append(patch);return len(self.f)-1
    def new_patch(self):self.patch_count+=1;return self.patch_count
    def seam_loop(self,loop):
        for a,b in zip(loop,loop[1:]+loop[:1]):self.seams.add(tuple(sorted((a,b))))
    def connect(self,a,b,patch):
        # Uniform angular ordering on different-count cuffs gives a deterministic zipper.
        i=j=0;n=len(a);m=len(b)
        while i<n or j<m:
            na=(i+1)/n if i<n else 2;nb=(j+1)/m if j<m else 2
            if abs(na-nb)<1e-7:
                self.face((a[i%n],a[(i+1)%n],b[(j+1)%m],b[j%m]),patch);i+=1;j+=1
            elif na<nb:self.face((a[i%n],a[(i+1)%n],b[j%m]),patch);i+=1
            else:self.face((a[i%n],b[(j+1)%m],b[j%m]),patch);j+=1
        self.seams.add(tuple(sorted((a[0],b[0]))))
    def ring(self,c,u,v,a,b,n,w,region,angles=None,sculpt=None):
        angles=angles or [2*pi*j/n for j in range(n)];c,u,v=Vector(c),Vector(u),Vector(v)
        points=[]
        for t in angles:
            p=c+u*a*cos(t)+v*b*sin(t)
            if sculpt:p=sculpt(p,t)
            points.append(self.vertex(p,w(p,t) if callable(w) else w,region))
        return points
    def cylinder(self,rows,n,w,region,sculpt=None,u=(1,0,0),v=(0,1,0)):
        patch=self.new_patch();loops=[];grid=[]
        for i,(c,a,b) in enumerate(rows):
            loop=self.ring(c,u,v,a,b,n,lambda p,t:w(i,p,t),region,sculpt=sculpt);loops.append(loop)
            if len(loops)>1:
                faces=[]
                for j in range(n):faces.append(self.face((loops[-2][j],loops[-2][(j+1)%n],loop[(j+1)%n],loop[j]),patch))
                grid.append(faces);self.seams.add(tuple(sorted((loops[-2][0],loop[0]))))
        return loops,grid,patch
    def hole(self,grid,r0,r1,columns):
        removed=[grid[r][j%len(grid[r])] for r in range(r0,r1) for j in columns]
        counts={}
        for index in removed:
            f=self.f[index]
            if f is None:raise RuntimeError('Overlapping authoring patches')
            for a,b in zip(f,f[1:]+f[:1]):
                e=tuple(sorted((a,b)));counts[e]=counts.get(e,0)+1
            self.f[index]=None
        edges=[e for e,c in counts.items() if c==1];adj={}
        for a,b in edges:adj.setdefault(a,[]).append(b);adj.setdefault(b,[]).append(a)
        first=edges[0][0];loop=[first];previous=None;current=first
        while True:
            nxt=next(x for x in adj[current] if x!=previous)
            if nxt==first:break
            loop.append(nxt);previous,current=current,nxt
        if len(loop)!=len(edges):raise RuntimeError('Non-simple patch boundary')
        self.seam_loop(loop);return loop
    def angular(self,loop,u,v):
        c=sum((Vector(self.v[i]) for i in loop),Vector())/len(loop);u,v=Vector(u),Vector(v)
        ordered=sorted(loop,key=lambda i:math.atan2((Vector(self.v[i])-c).dot(v),(Vector(self.v[i])-c).dot(u))%(2*pi))
        actual={tuple(sorted(e)) for e in zip(loop,loop[1:]+loop[:1])}
        if any(tuple(sorted(e)) not in actual for e in zip(ordered,ordered[1:]+ordered[:1])):
            # A non-convex projected loop retains its real adjacency, never an invalid sort.
            start=max(range(len(loop)),key=lambda j:(Vector(self.v[loop[j]])-c).dot(u));ordered=loop[start:]+loop[:start]
            if (Vector(self.v[ordered[1]])-c).dot(v)<0:ordered=[ordered[0]]+list(reversed(ordered[1:]))
        angles=[math.atan2((Vector(self.v[i])-c).dot(v),(Vector(self.v[i])-c).dot(u))%(2*pi) for i in ordered]
        return ordered,angles
    def cap(self,loop,center,w,region,patch):
        c=self.vertex(center,w,region)
        for a,b in zip(loop,loop[1:]+loop[:1]):self.face((a,b,c),patch)

C=Cage()
def torso_weights(p,t=0):
    x,y,z=p;sf='L' if x>=0 else 'R'
    spine=smooth(.96,1.16,z);chest=smooth(1.14,1.27,z);neck=smooth(1.405,1.49,z)
    clav=smooth(.075,.175,abs(x))*smooth(1.18,1.38,z)*(1-neck)
    w={'pelvis':(1-spine)*(1-clav),'spine':spine*(1-chest)*(1-clav),'chest':spine*chest*(1-neck)*(1-clav),'neck':neck*(1-clav),f'clavicle.{sf}':clav}
    # Soft pectoral/axillary support has continuous compact support. Unlike heat
    # pruning, it cannot switch a remote arm influence on across one short edge.
    humerus=.75*smooth(.07,.20,abs(x))*smooth(1.13,1.38,z)*(1-neck)
    w={bone:weight*(1-humerus) for bone,weight in w.items()}
    w[f'axilla_support.{sf}']=humerus
    return w
torso_profile=[(0.90,.160,.110,.006),(0.96,.156,.111,.003),(1.00,.152,.108,.001),(1.04,.146,.105,0),(1.08,.140,.101,0),(1.12,.142,.103,0),(1.16,.150,.107,0),(1.20,.158,.111,0),(1.24,.168,.117,0),(1.28,.176,.121,0),(1.32,.183,.123,0),(1.35,.188,.120,.001),(1.38,.194,.108,.003),(1.40,.192,.094,.005),(1.42,.168,.078,.008),(1.44,.128,.064,.010),(1.46,.096,.056,.011),(1.48,.062,.050,.011),(1.505,.048,.047,.010),(1.525,.046,.045,.009)]
# One-centimetre vertical control is required for chest, waist and pelvic
# planes; the previous 1.5 cm rows turned every anatomical edge into a step.
zs=sorted(set([0.90+i*.01 for i in range(60)]+[1.505]))
def sstep(x,a,b):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def bandv(x,a,b,c,d):return sstep(x,a,b)*(1-sstep(x,c,d))
def torso_shape(p,t):
    x,y,z=p;ax=abs(x);front=max(0,-sin(t));back=max(0,sin(t))
    # Pectoral plates land on one sloped plane per side: medial fibres reach
    # the sternum, lateral fibres insert under the deltoid, lower edge turns
    # into the costal wall. This is a plane change, not an inflated chest.
    pec=bandv(ax,.012,.045,.145,.180)*bandv(z,1.225,1.250,1.352,1.385)*front
    if pec:
        plane=-.121+.24*(ax-.070)+.30*(z-1.305)
        p.y=p.y*(1-.78*pec)+plane*.78*pec
    p.y+=.0035*bandv(ax,.015,.045,.135,.170)*(1-sstep(abs(z-1.232),.008,.022))*front
    st=(1-sstep(ax,.004,.018))*bandv(z,1.245,1.275,1.395,1.425)*front
    p.y+=.0035*st
    cz=1.437-.13*ax
    cl=bandv(ax,.002,.020,.165,.198)*(1-sstep(abs(z-cz),.005,.018))*front
    p.y-=.0028*cl
    ho=bandv(ax,.045,.070,.125,.155)*bandv(z,cz+.008,cz+.020,cz+.038,cz+.050)*front
    p.y+=.0026*ho
    tr=bandv(ax,.020,.050,.130,.175)*bandv(z,1.415,1.440,1.505,1.525)*back
    p.y+=.0040*tr
    # Deltoid cap belongs to the shoulder girdle; the arm emerges below it.
    delt=bandv(ax,.135,.165,.200,.220)*bandv(z,1.300,1.330,1.400,1.430)
    p.x+=(1 if x>0 else -1)*.0065*delt
    p.y+=(.0035 if y>0 else -.0035)*delt
    rect=(1-sstep(ax,.045,.075))*bandv(z,1.015,1.040,1.190,1.225)*front
    if rect:
        plane=-.105-.008*sstep(z,1.03,1.15)
        p.y=p.y*(1-.55*rect)+plane*.55*rect
    p.y+=.0022*(1-sstep(ax,.004,.015))*bandv(z,1.015,1.045,1.20,1.235)*front
    mz=1.192+.20*ax
    p.y+=.0025*(1-sstep(abs(z-mz),.004,.016))*bandv(ax,.012,.035,.130,.160)*front
    iz=1.002-.22*max(0,ax-.035)
    p.y-=.0026*(1-sstep(abs(z-iz),.004,.018))*bandv(ax,.025,.055,.125,.158)
    p.y-=.0022*(1-sstep((Vector((ax,z))-Vector((.138,.962))).length,.012,.034))
    gz=.958-.5*max(0,.135-ax)
    p.y+=.0020*(1-sstep(abs(z-gz),.004,.016))*bandv(ax,.030,.055,.128,.152)*front
    # Posterior planes: a flat scapular plate with its spine, a spine valley,
    # erector ridges and the lat flare that carries the back into the waist.
    scap=bandv(ax,.030,.055,.135,.175)*bandv(z,1.235,1.270,1.365,1.400)*back
    if scap:
        plane=.108-.18*(ax-.08)-.10*(z-1.31)
        p.y=p.y*(1-.65*scap)+plane*.65*scap
    p.y+=.0026*bandv(ax,.040,.060,.155,.185)*(1-sstep(abs(z-(1.335+.15*ax)),.004,.018))*back
    p.y-=.0030*(1-sstep(ax,.004,.016))*bandv(z,1.045,1.080,1.395,1.430)*back
    p.y+=.0020*(1-sstep(abs(ax-.030),.010,.026))*bandv(z,1.020,1.050,1.380,1.420)*back
    p.y+=.0025*bandv(ax,.100,.125,.165,.195)*bandv(z,1.120,1.160,1.300,1.350)*back
    return p
rows=[]
for z in zs:
    a,b,y=profile(torso_profile,z);rows.append(((0,y,z),a,b))
torso,torso_grid,_=C.cylinder(rows,96,lambda i,p,t:torso_weights(p),'torso',torso_shape)
def near(values,z):return min(range(len(values)),key=lambda i:abs(values[i]-z))

finger_defs=[]
def branch_distal(loop,u,v,d,centers,cuts,w,region):
    """Shared interdigital webs split one closed palm/forefoot into digit openings."""
    n=len(loop);half=n//2;bridges={}
    for k in cuts:
        back,front=loop[k],loop[n-k]
        p,q=Vector(C.v[back]),Vector(C.v[front])
        bridges[k]=[back]+[C.vertex(p.lerp(q,t)+Vector(d)*.002,w,region) for t in [.333,.667]]+[front]
    boundaries=[0]+cuts+[half];loops=[]
    for i,(a,b) in enumerate(zip(boundaries,boundaries[1:])):
        if a==0:
            ids=list(loop[n-b:])+[loop[0]]+list(loop[1:b+1])+bridges[b][1:-1]
        elif b==half:
            ids=list(loop[a:half+1])+list(loop[half+1:n-a+1])+list(reversed(bridges[a][1:-1]))
        else:
            ids=list(loop[a:b+1])+bridges[b][1:-1]+list(loop[n-b:n-a+1])+list(reversed(bridges[a][1:-1]))
        loops.append(C.angular(ids,u,v)[0]);C.seam_loop(ids)
    return loops

for s,sf in [(1,'L'),(-1,'R')]:
    shoulder=Vector((s*.1766529,0,1.376033));wrist=Vector((s*.5578512,0,1.041322))
    d=(wrist-shoulder).normalized();u=Vector((0,-1,0));v=d.cross(u).normalized()
    hole=C.hole(torso_grid,near(zs,1.285),near(zs,1.39),range(-7,8) if s==1 else range(41,56))
    hole,angles=C.angular(hole,u,v);n=len(hole);patch=C.new_patch();previous=hole
    attachment_weights={round(theta,9):dict(C.w[index]) for index,theta in zip(hole,angles)}
    # The first rings carry the deltoid cap: widest at the acromion, then
    # tapering into the humerus instead of reading as a sleeve.
    arm_rows=[(.08,.060,.062),(.14,.060,.062),(.20,.057,.058),(.25,.055,.055),(.32,.051,.051),(.39,.047,.047),(.44,.043,.043),(.48,.040,.040),(.52,.038,.039),(.56,.038,.039),(.60,.040,.040),(.66,.042,.041),(.74,.039,.037),(.84,.032,.030),(.92,.026,.025),(1,.021,.021)]
    ts=sorted(set([t for t,_,_ in arm_rows]+[.11,.17,.23,.28,.36,.41,.46,.50,.54,.58,.63,.70,.78,.88,.96]))
    def arm_weight(t):
        root=smooth(-.08,.24,t);elbow=smooth(.26,.82,t);hand=smooth(.94,1.075,t)
        return {f'clavicle.{sf}':1-root,f'upper_arm.{sf}':root*(1-elbow),f'forearm.{sf}':root*elbow*(1-hand),f'hand.{sf}':root*elbow*hand}
    for t in ts:
        a,b=profile(arm_rows,t);c=shoulder.lerp(wrist,t)
        def arm_shape(p,theta,t=t,c=c):
            r=(p-c).normalized();outer=max(0,-sin(theta));front=max(0,cos(theta));back=max(0,-cos(theta))
            # Deltoid wraps over the humeral head; biceps and triceps are
            # directional masses, not a uniform radial inflation.
            p+=r*(.0020*G(t,.10,.12)*(.35+.65*outer))
            p+=u*(-.0042*front*G(t,.34,.10)+.0034*back*G(t,.32,.12))
            p+=u*(.0024*back*G(t,.50,.035))
            lateral=max(0,r.dot(Vector((s,0,0))))
            p+=r*(.0034*G(t,.61,.08)*(.25+.75*lateral))
            return p
        free=arm_weight(t);inherit=1-smooth(.08,.26,t)
        def cuff_weights(p,theta):
            attached=attachment_weights[round(theta,9)]
            return {bone:inherit*attached.get(bone,0)+(1-inherit)*free.get(bone,0) for bone in set(attached)|set(free)}
        loop=C.ring(c,u,v,a,b,n,cuff_weights,'arm.'+sf,angles,arm_shape)
        C.connect(previous,loop,patch);previous=loop
    C.seam_loop(previous)
    hand_d=Vector((s*.69,0,-.724)).normalized();hand_u=Vector((s*.724,0,.69)).normalized();hand_v=Vector((0,1,0))
    previous,_=C.angular(previous,hand_u,hand_v)
    hand_patch=C.new_patch();palm_loops=[previous];palm_grid=[]
    # Match the cuff before transitioning to a 32-vertex palmar perimeter.
    for distance,width,depth in [(.014,.024,.018),(.032,.031,.017),(.050,.034,.015),(.068,.034,.013),(.082,.033,.011)]:
        c=wrist+hand_d*distance
        loop=C.ring(c,hand_u,hand_v,width,depth,32,{f'hand.{sf}':1},'palm.'+sf)
        if distance==.082:
            for j,index in enumerate(loop):
                if j<=16:q=Vector((width*(1-j/8),depth*sin(pi*j/16)**.3,0))
                else:q=Vector((width*(-1+(j-16)/8),-depth*sin(pi*(j-16)/16)**.3,0))
                C.v[index]=tuple(c+hand_u*q.x+hand_v*q.y)
        if len(palm_loops)>1:
            ids=[]
            for j in range(32):ids.append(C.face((palm_loops[-1][j],palm_loops[-1][(j+1)%32],loop[(j+1)%32],loop[j]),hand_patch))
            palm_grid.append(ids);C.seams.add(tuple(sorted((palm_loops[-1][0],loop[0]))))
        else:C.connect(previous,loop,hand_patch)
        palm_loops.append(loop);previous=loop
    thumb_hole=C.hole(palm_grid,0,2,range(-2,2));thumb_hole,thumb_angles=C.angular(thumb_hole,hand_v,hand_d)
    thumb_start=wrist+hand_d*.035+hand_u*.026
    thumb_end=thumb_start+hand_d*.030+hand_u*.052+Vector((0,-.018,0))
    td=(thumb_end-thumb_start).normalized();tu=Vector((0,1,0));tv=tu.cross(td).normalized();patch=C.new_patch();last=thumb_hole
    for t,a,b in [(0,.013,.014),(.22,.013,.013),(.45,.012,.012),(.58,.011,.011),(.76,.010,.010),(.90,.0085,.0085),(.97,.0065,.0065),(.99,.0035,.0035)]:
        p=thumb_start.lerp(thumb_end,t)
        loop=C.ring(p,tu,tv,a,b,len(last),{f'hand.{sf}':1},'thumb.'+sf)
        C.connect(last,loop,patch);last=loop
    C.cap(last,thumb_end,{f'hand.{sf}':1},'thumb.'+sf,patch)
    openings=branch_distal(palm_loops[-1],hand_u,hand_v,hand_d,None,[4,8,12],{f'hand.{sf}':1},'palm.'+sf)
    # Positive transverse side is the radial/index side beside the opposed thumb.
    for name,opening,length,radius in zip(['index','middle','ring','little'],openings,[.074,.083,.077,.060],[.0082,.0085,.0080,.0070]):
        center=sum((Vector(C.v[i]) for i in opening),Vector())/len(opening)
        patch=C.new_patch();last=opening
        end=center+hand_d*length+hand_u*{'index':.007,'middle':.002,'ring':-.003,'little':-.008}[name]
        for t,scale in [(.10,1),(.22,.98),(.34,1.07),(.41,1.05),(.49,.93),(.62,.92),(.70,1.00),(.77,.96),(.87,.86),(.94,.77),(.985,.43)]:
            p=center.lerp(end,t)+hand_v*(-.003*sin(pi*t))
            # Rounded phalangeal pads and knuckles replace conical fingers.
            loop=C.ring(p,hand_u,hand_v,radius*scale,radius*.86*scale,len(opening),{f'hand.{sf}':1},'finger_'+name+'.'+sf)
            C.connect(last,loop,patch);last=loop
        C.cap(last,end,{f'hand.{sf}':1},'finger_'+name+'.'+sf,patch)

# The pelvis branches through shared crotch edges, not intersecting capped legs.
bottom=torso[0]
crotch=[C.vertex((0,y,.817),{'pelvis':1},'pelvis') for y in [.05,0,-.05]]
left=[bottom[j%96] for j in range(-16,17)]+crotch
right=[bottom[j%96] for j in range(16,49)]+list(reversed(crotch))
for s,sf,opening in [(1,'L',left),(-1,'R',right)]:
    opening,_=C.angular(opening,(1,0,0),(0,1,0));n=len(opening);last=opening;patch=C.new_patch()
    leg_profile=[(.10,.027,.032,0),(.16,.032,.037,.003),(.22,.043,.047,.008),(.28,.051,.056,.012),(.34,.058,.063,.014),(.40,.058,.061,.012),(.45,.053,.056,.004),(.49,.052,.055,-.006),(.54,.053,.058,-.012),(.60,.061,.071,-.012),(.66,.069,.078,-.010),(.72,.077,.085,-.007),(.78,.084,.090,-.002),(.82,.089,.093,.002),(.845,.092,.095,.006)]
    zs_leg=[.845,.82,.79,.76,.72,.68,.64,.60,.575,.555,.535,.515,.495,.475,.455,.435,.41,.38,.34,.30,.26,.22,.18,.14,.11]
    for i,z in enumerate(zs_leg):
        a,b,y=profile(leg_profile,z)
        hip=1-smooth(.68,.95,z);shin=1-smooth(.375,.615,z)
        foot=1-smooth(.075,.155,z)
        weights={'pelvis':1-hip,f'thigh.{sf}':hip*(1-shin),f'shin.{sf}':hip*shin*(1-foot),f'foot.{sf}':hip*shin*foot}
        def leg_shape(p,t):
            z=p.z;ax=abs(p.x);front=max(0,-sin(t));back=max(0,sin(t))
            # Gluteal volume belongs to the leg top as much as to the pelvis;
            # the fold and cleft are real depressions, not a flat seat.
            gl=sstep(z,.755,.815)*(1-sstep(z,.895,.965))
            if back>0:
                p.y+=.033*gl
                p.z-=.004*gl
                p.y-=.011*gl*(1-sstep(ax,.004,.022))
                p.y-=.0045*(1-sstep(abs(z-.790),.004,.015))*sstep(ax,.018,.045)
            # Quadriceps plane and vastus lateralis silhouette.
            q=bandv(z,.515,.575,.750,.815)*front
            if q:
                p.y-=.0055*q
                p.x+=s*.0045*q*max(0.0,(p.x-s*.10)/.075)
            # Patella sits proud of the condyles; the popliteal hollow is behind.
            pat=(1-sstep((Vector((ax-s*.10,z-.492))).length,.008,.026))*front
            p.y-=.0040*pat
            pop=(1-sstep(ax,.012,.035))*bandv(z,.445,.470,.525,.550)*back
            p.y-=.0035*pop
            # Gastrocnemius heads, tibial ridge, Achilles tendon.
            p.y+=.0080*G(z,.335,.060)*back
            p.x+=s*.0025*G(z,.320,.055)*back
            p.y-=.0020*(1-sstep(abs(p.x-s*.096),.008,.024))*bandv(z,.150,.185,.360,.405)*front
            p.y-=.0028*(1-sstep(abs(p.x-s*.100),.008,.022))*bandv(z,.110,.135,.220,.255)*back
            return p
        loop=C.ring((s*.10,y,z),(1,0,0),(0,1,0),a,b,n,weights,'leg.'+sf,sculpt=leg_shape)
        C.connect(last,loop,patch);last=loop
    ankle=last;C.seam_loop(ankle)
    foot_rows=[];ys=[.058,.046,.032,.018,.004,-.010,-.024,-.040,-.065,-.090,-.12,-.145,-.168]
    fp=[(-.168,.044,.018,.026),(-.145,.047,.020,.027),(-.12,.046,.023,.030),(-.090,.039,.027,.034),(-.065,.034,.032,.038),(-.040,.032,.037,.042),(-.024,.032,.041,.046),(.004,.033,.044,.049),(.032,.031,.039,.046),(.058,.018,.028,.039)]
    for y in ys:
        a,b,z=profile(fp,y);foot_rows.append(((s*.10,y,z),a,b))
    def foot_weights(i,p,t):
        toe=1-smooth(-.19,-.135,p.y);return {f'foot.{sf}':1-toe,f'toe.{sf}':toe}
    def foot_shape(p,t):
        if sin(t)<0:
            # Medial longitudinal arch rises between the heel and metatarsal heads.
            medial=max(0,-s*(p.x-s*.10)/.035)
            p.z+=.009*G(p.y,-.060,.040)*medial
        p.z=max(.004,p.z)
        return p
    loops,grid,patch=C.cylinder(foot_rows,40,foot_weights,'foot.'+sf,foot_shape,(1,0,0),(0,0,1))
    C.cap(loops[0],(s*.10,.069,.039),{f'foot.{sf}':1},'heel.'+sf,patch)
    ankle_hole=C.hole(grid,1,7,range(7,13));ankle_hole,_=C.angular(ankle_hole,(1,0,0),(0,1,0))
    C.connect(ankle,ankle_hole,C.new_patch())
    # Foot terminal ring uses evenly spaced metatarsal sectors for five rounded toes.
    for j,index in enumerate(loops[-1]):
        c=Vector((s*.10,-.168,.026));width=.044;depth=.018
        if j<=20:a=width*(1-j/10);b=depth*sin(pi*j/20)**.3
        else:a=width*(-1+(j-20)/10);b=-depth*sin(pi*(j-20)/20)**.3
        C.v[index]=tuple(c+Vector((a,0,b)))
    openings=branch_distal(loops[-1],(1,0,0),(0,0,1),(0,-1,0),None,[4,8,12,16],{f'toe.{sf}':1},'forefoot.'+sf)
    # The medial toe is largest; mirror the length schedule, not the anatomy itself.
    lengths=[.027,.035,.043,.049,.047] if s==1 else [.047,.049,.043,.035,.027]
    for toe,opening,length in zip(range(5),openings,lengths):
        c=sum((Vector(C.v[i]) for i in opening),Vector())/len(opening);patch=C.new_patch();last=opening
        radius=.0100 if (toe==4 if s==1 else toe==0) else .0075
        for t,scale in [(.17,1),(.38,1.03),(.57,.95),(.76,.90),(.91,.73),(.985,.40)]:
            loop=C.ring(c+Vector(((2-toe)*.0015*t,-length*t,0)),(1,0,0),(0,0,1),radius*scale,radius*.80*scale,len(opening),{f'toe.{sf}':1},'toe%d.%s'%(toe,sf))
            C.connect(last,loop,patch);last=loop
        C.cap(last,c+Vector(((2-toe)*.0015,-length,0)),{f'toe.{sf}':1},'toe%d.%s'%(toe,sf),patch)

# Reconstructed craniofacial planes; frontal face is not a spherical cross-section.
head_profile=[(1.525,.046,.043,.045,.008),(1.548,.040,.056,.043,.004),(1.563,.044,.066,.048,.002),(1.583,.059,.070,.060,.004),(1.603,.068,.072,.071,.008),(1.637,.077,.074,.081,.010),(1.672,.080,.074,.086,.011),(1.700,.076,.073,.086,.012),(1.735,.074,.069,.082,.013),(1.765,.064,.060,.071,.014),(1.786,.043,.039,.049,.015),(1.797,.020,.018,.023,.015),(1.800,.0015,.0015,.002,.015)]
head_z=sorted(set([1.525+i*.004 for i in range(69)]+[1.59+i*.0018 for i in range(14)]+[1.667+i*.002 for i in range(17)]+[1.797,1.800]))
head_candidates=sorted({round(z,6) for z in head_z if z<=1.800})
head_z=[]
for z in head_candidates:
    if not head_z or z-head_z[-1]>=.0008:head_z.append(z)
def face_shape(p,t):
    x,y,z=p
    front=max(0,-sin(t))**2
    if front==0:return p
    ax=abs(x)
    # Facial planes are authored as ridges and recesses over the cranial
    # ellipsoid: brow shelf, orbital recess, nose bridge/tip, cheekbone,
    # lips, chin and jaw. No surface texture is used to fake them.
    relief=.0038*G(ax,.028,.030)*G(z,1.708,.010)
    relief+=.0030*G(x,0,.012)*G(z,1.705,.018)
    relief-=.0040*G(ax,.032,.018)*G(z,1.694,.010)
    relief+=.0075*G(x,0,.011)*G(z,1.672,.040)
    relief+=.0130*G(x,0,.013)*G(z,1.650,.014)
    relief+=.0080*G(ax,.012,.010)*G(z,1.642,.008)
    relief-=.0030*G(ax,.017,.006)*G(z,1.639,.004)
    relief+=.0040*G(ax,.048,.016)*G(z,1.663,.016)
    relief-=.0025*G(ax,.042,.014)*G(z,1.630,.016)
    relief+=.0030*G(x,0,.020)*G(z,1.626,.006)
    relief+=.0035*G(x,0,.018)*G(z,1.609,.007)
    relief-=.0025*G(x,0,.022)*G(z,1.618,.0018)
    relief-=.0012*G(x,0,.004)*G(z,1.634,.006)
    relief+=.0040*G(x,0,.018)*G(z,1.597,.010)
    relief+=.0025*G(ax,.055,.014)*G(z,1.605,.020)
    p.y-=front*relief;return p
head_rows=[]
for z in head_z:
    a,front,back,y=profile(head_profile,z);head_rows.append(((0,y,z),a,(front+back)/2))
head,head_grid,head_patch=C.cylinder(head_rows,128,lambda i,p,t:{'head':smooth(1.52,1.565,p.z),'neck':1-smooth(1.52,1.565,p.z)},'head')
for k,z in enumerate(head_z):
    a,front,back,cy=profile(head_profile,z)
    for j,index in enumerate(head[k]):
        t=2*pi*j/128;x=a*cos(t)
        if sin(t)<0:
            y=cy-front*max(0,1-(abs(x)/max(a,1e-6))**3.2)**(1/3.2)
        else:y=cy+back*sin(t)
        if sin(t)<0:y-=.009*G(z,1.665,.075)*max(0,-sin(t))**2
        C.v[index]=tuple(face_shape(Vector((x,y,z)),t))
C.connect(torso[-1],head[0],C.new_patch());C.seam_loop(head[0])
C.cap(head[-1],(0,.015,1.8002),{'head':1},'head',head_patch)
eyes=[]
def facial_y(x,z):
    radius,front,back,cy=profile(head_profile,z)
    ratio=max(-.999,min(.999,x/radius));theta=2*pi-math.acos(ratio)
    y=cy-front*max(0,1-abs(ratio)**3.2)**(1/3.2)-.009*G(z,1.665,.075)*sin(theta)**2
    return face_shape(Vector((x,y,z)),theta).y
for s,sf in [(1,'L'),(-1,'R')]:
    # Rectangular grid removal is followed by concentric anatomical orbital/lid loops.
    col=near([cos(2*pi*j/128) for j in range(128)],s*.032/.075)
    col=min(range(65,128),key=lambda j:abs(.075*cos(2*pi*j/128)-s*.032))
    opening=C.hole(head_grid,near(head_z,1.665),near(head_z,1.701),range(col-6,col+6))
    opening,angles=C.angular(opening,(1,0,0),(0,0,1));patch=C.new_patch();last=opening
    center=Vector((s*.032,-.0525,1.682))
    for layer in range(3):
        loop=[]
        for t in angles:
            upper=sin(t)>0
            width=[.019,.0148,.0130][layer]
            height=([.0115,.0070,.0048][layer] if upper else [.009,.0050,.0033][layer])
            dx=width*cos(t);dz=height*sin(t)+.001*cos(t)*s
            surface=facial_y(center.x+dx,center.z+dz)
            if layer==0:y=surface-.0006
            else:
                spherical=center.y-sqrt(max(.000001,.0132**2-dx*dx-dz*dz))-(.0010 if layer==1 else .00035)
                blend=abs(sin(t))**.55
                y=(surface-.0005)*(1-blend)+spherical*blend
            loop.append(C.vertex((center.x+dx,y,center.z+dz),{'head':1},('orbital_rim.' if layer==0 else 'eyelid.')+sf))
        C.connect(last,loop,patch);last=loop
    # The eyelid margin has real thickness, ending behind the visible corneal surface.
    inner=[]
    for index in last:
        p=Vector(C.v[index]);p.y+=.0012;inner.append(C.vertex(p,{'head':1},'eyelid_inner.'+sf))
    C.connect(last,inner,patch);C.seam_loop(opening)
    last=inner
    for depth,radius in [(.002,.0140),(.011,.0095)]:
        cavity=[C.vertex((center.x+radius*cos(t),center.y+depth,center.z+radius*sin(t)),{'head':1},'orbital_cavity.'+sf) for t in angles]
        C.connect(last,cavity,patch);last=cavity
    C.cap(last,center+Vector((0,.016,0)),{'head':1},'orbital_cavity.'+sf,patch)
    eyes.append((sf,center))
    # Ear shell, raised helix, antihelix and recessed concha, attached through one head opening.
    col=0 if s==1 else 64
    opening=C.hole(head_grid,near(head_z,1.650),near(head_z,1.702),range(col-2,col+2))
    opening,angles=C.angular(opening,(0,1,0),(0,0,1));last=opening;patch=C.new_patch()
    angles=[2*pi*j/len(opening) for j in range(len(opening))]
    for layer in range(4):
        loop=[]
        for t in angles:
            a=[.013,.014,.010,.006][layer];b=[.031,.031,.022,.014][layer]
            x=s*(.073+[.010,.016,.010,.007][layer]*sqrt(max(.06,1-.85*sin(t)**2))+.002*cos(t))
            y=.011+a*cos(t);z=1.672+b*sin(t)
            if sin(t)<-.5:x-=s*.002
            loop.append(C.vertex((x,y,z),{'head':1},'ear.'+sf))
        C.connect(last,loop,patch);last=loop
    C.cap(last,(s*.080,.011,1.672),{'head':1},'ear.'+sf,patch)

faces=[f for f in C.f if f is not None]
used=sorted({i for f in faces for i in f});remap={old:new for new,old in enumerate(used)}
faces=[tuple(remap[i] for i in f) for f in faces]
C.v=[C.v[i] for i in used];C.w=[C.w[i] for i in used];C.region=[C.region[i] for i in used]
C.seams={tuple(sorted((remap[a],remap[b]))) for a,b in C.seams if a in remap and b in remap}
mesh=bpy.data.meshes.new('Original_ControlLoop_Anatomy_v2');mesh.from_pydata(C.v,[],faces);mesh.update()
body=bpy.data.objects.new('FINAL_Body_Connected',mesh);scene.collection.objects.link(body)
bm=bmesh.new();bm.from_mesh(mesh)
loose=[v for v in bm.verts if not v.link_faces]
if loose:
    # Keep indices stable for diagnostics; construction should never leave unattached vertices.
    raise RuntimeError('Unused cage vertices: '+str(len(loose)))
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
bm=bmesh.new();bm.from_mesh(mesh)
# Relax only construction interfaces; retain the deliberately placed bend loops.
interfaces=[v for v in bm.verts if (1.29<v.co.z<1.43 and .13<abs(v.co.x)<.245) or (1.495<v.co.z<1.55) or (.075<v.co.z<.14)]
for _ in range(6):bmesh.ops.smooth_vert(bm,verts=interfaces,factor=.30,use_axis_x=True,use_axis_y=True,use_axis_z=True)
orbital=[v for v in bm.verts if C.region[v.index].startswith('orbital_rim.') or (C.region[v.index]=='head' and .011<abs(v.co.x)<.059 and 1.662<v.co.z<1.707 and v.co.y<-.035)]
for _ in range(8):bmesh.ops.smooth_vert(bm,verts=orbital,factor=.20,use_axis_x=True,use_axis_y=True,use_axis_z=True)
bm.to_mesh(mesh);bm.free()
for p in mesh.polygons:p.use_smooth=True
groups={name:body.vertex_groups.new(name=name) for name in sorted({n for weights in C.w for n in weights})}
for i,weights in enumerate(C.w):
    ranked=sorted(weights.items(),key=lambda p:-p[1])[:4];total=sum(a for _,a in ranked)
    for name,value in ranked:groups[name].add([i],value/total,'REPLACE')
mod=body.modifiers.new('Controlled_linear_skinning','ARMATURE');mod.object=rig;mod.use_deform_preserve_volume=False;body.parent=rig

# Seams follow authored anatomic patches and one longitudinal cut per tube.
face_patches=[p for f,p in zip(C.f,C.patch) if f is not None]
edge_patches={}
for face,patch in zip(mesh.polygons,face_patches):
    for edge in face.edge_keys:edge_patches.setdefault(tuple(sorted(edge)),set()).add(patch)
for edge in mesh.edges:edge.use_seam=tuple(sorted(edge.vertices)) in C.seams or len(edge_patches.get(tuple(sorted(edge.vertices)),set()))>1
bpy.ops.object.select_all(action='DESELECT');body.select_set(True);bpy.context.view_layer.objects.active=body
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
methods=[i.identifier for i in bpy.ops.uv.unwrap.get_rna_type().properties['method'].enum_items]
method='ANGLE_BASED' if 'ANGLE_BASED' in methods else methods[0]
bpy.ops.uv.unwrap(method=method,margin=.020)
bpy.ops.object.mode_set(mode='OBJECT')

def material(name,color,rough=.56):
    m=bpy.data.materials.new(name);m.use_nodes=True
    node=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    node.inputs['Base Color'].default_value=(*color,1);node.inputs['Roughness'].default_value=rough
    return m
skin=material('Original_skin_v2',(.34,.172,.105),.58);body.data.materials.append(skin)
white=material('Sclera_v2',(.63,.61,.55),.32);iris=material('Iris_v2',(.065,.035,.014),.40);pupil=material('Pupil_v2',(.004,.003,.002),.25)
accessories=[]
def rigid_sphere(name,center,radii,mat,bone='head'):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=16,location=center)
    o=bpy.context.object;o.name=name;o.scale=radii
    bpy.ops.object.transform_apply(location=True,rotation=False,scale=True)
    o.data.materials.append(mat)
    for p in o.data.polygons:p.use_smooth=True
    g=o.vertex_groups.new(name=bone);g.add(list(range(len(o.data.vertices))),1,'REPLACE')
    m=o.modifiers.new('Rigid_eye_skinning','ARMATURE');m.object=rig;o.parent=rig;accessories.append(o)
    return o
for sf,c in eyes:
    rigid_sphere('GEO_Eyeball_v2.'+sf,c,(.0132,.0132,.0132),white)
    rigid_sphere('GEO_Iris_v2.'+sf,c+Vector((0,-.0131,0)),(.0051,.0005,.0051),iris)
    rigid_sphere('GEO_Pupil_v2.'+sf,c+Vector((0,-.01365,0)),(.0021,.0003,.0021),pupil)

# Separate compression shell is not used to hide joint or surface discontinuities.
garment_mesh=mesh.copy();bm=bmesh.new();bm.from_mesh(garment_mesh)
bmesh.ops.delete(bm,geom=[f for f in bm.faces if abs(f.calc_center_median().x)>.24],context='FACES')
for z,inner,outer in [(.825,True,False),(1.00,False,True)]:
    bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=1e-6,plane_co=(0,0,z),plane_no=(0,0,1),clear_inner=inner,clear_outer=outer)
loose=[v for v in bm.verts if not v.link_faces]
if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
bm.normal_update()
for v in bm.verts:v.co+=v.normal*.004
bm.to_mesh(garment_mesh);bm.free()
brief=bpy.data.objects.new('FINAL_Brief',garment_mesh);scene.collection.objects.link(brief)
brief.data.materials.clear();brief.data.materials.append(material('Compression_v2',(.025,.045,.065),.83))
for p in brief.data.polygons:p.material_index=0
for g in body.vertex_groups:brief.vertex_groups.new(name=g.name)
m=brief.modifiers.new('Matching_control_weights','ARMATURE');m.object=rig;brief.parent=rig
accessories.append(brief)

regions=mesh.attributes.new('Anatomical_region_id','INT','POINT');region_names=sorted(set(C.region))
for i,r in enumerate(C.region):regions.data[i].value=region_names.index(r)
(OUT/'final-body-authoring.json').write_text(json.dumps({'regionNames':region_names,'patchCount':C.patch_count,'technique':'Explicit connected anatomical contour loops; bridged shoulder openings, shared crotch, shared interdigital webs, orbital eyelid loops and ear shells. No remesh or decimation in source cage.','weights':'Regional support and broad smooth joint transitions; no automatic heat weights or top-four support switching on the ribs.','rigContract':'24 staging bones: the existing 22 bind bones plus axilla_support.L/R. Support bones share the humerus rest matrix and clavicle parent, and rotate by the shortest-arc quaternion three-quarter swing of the local humerus rotation. Their test motion is baked at all 80 frames. Original 22 local pose channels are unchanged; no runtime contract overwritten.','uv':'Anatomical patch seams plus one longitudinal cut per tube, native Blender angle-based unwrap.'},indent=2))
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'final-body-source.blend'))
print('CAGE_CREATED',len(mesh.vertices),len(mesh.polygons),'patches',C.patch_count,flush=True)

# --- Preview renders on the saved source, using a neutral clay override -----
def preview_mat(name,value,rough=.62):
    m=bpy.data.materials.new(name);m.use_nodes=True
    bsdf=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value=(value,value,value,1);bsdf.inputs['Roughness'].default_value=rough
    return m
body.data.materials.clear();body.data.materials.append(preview_mat('Preview_clay',.46))
for obj in scene.objects:
    if obj.type=='MESH' and obj not in {body}:obj.hide_render=True;obj.hide_set(True)
brief.hide_render=True;brief.hide_set(True)
world=scene.world or bpy.data.worlds.new('World');scene.world=world;world.use_nodes=True
bg=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND')
bg.inputs['Color'].default_value=(.035,.038,.045,1);bg.inputs['Strength'].default_value=.35
for obj in list(scene.objects):
    if obj.type=='LIGHT':bpy.data.objects.remove(obj,do_unlink=True)
for name,loc,energy,size in [('Preview_key',(2.6,-2.2,2.4),420,2.4),('Preview_fill',(-2.8,-3.2,1.2),90,3.0),('Preview_rim',(1.2,3.4,2.2),160,2.0)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.size=size
    light=bpy.data.objects.new(name,data);scene.collection.objects.link(light);light.location=loc
    light.rotation_euler=(Vector((0,0,1.15))-light.location).to_track_quat('-Z','Y').to_euler()
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=24
scene.cycles.use_denoising=True;scene.cycles.denoiser='OPENIMAGEDENOISE'
scene.render.threads_mode='FIXED';scene.render.threads=6
scene.render.resolution_x=640;scene.render.resolution_y=800
cam=scene.camera
def shot(name,pos,target,scale):
    cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/('preview-'+name+'.png'));bpy.ops.render.render(write_still=True)
shot('front',(0,-5,.92),(0,0,.92),2.02)
shot('profile',(5,0,.92),(0,0,.92),2.02)
shot('back',(0,5,.92),(0,0,.92),2.02)
shot('three-quarter',(3.2,-4.6,1.45),(0,0,.92),2.02)
shot('head-front',(0,-2,1.68),(0,0,1.68),.30)
shot('head-three-quarter',(1.4,-2,1.78),(0,0,1.68),.30)
shot('hand',(.62,-2,1.04),(.62,0,1.04),.26)
shot('foot',(.10,-2,.10),(.10,-.04,.10),.34)
print('PREVIEW_DONE',len(mesh.vertices),len(mesh.polygons),flush=True)
