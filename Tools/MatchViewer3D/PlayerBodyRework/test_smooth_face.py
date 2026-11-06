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
OUT=BASE/'Final-Body/TEST-SmoothFace';OUT.mkdir(exist_ok=True)
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
# The staging rig has the glenohumeral pivot at 1.376, which leaves a long
# neck above the shoulder line. Raise the pivot and split the arm chain at the
# same straight-line elbow height as the authored surface.
for sf in ['L','R']:
    bones=rig.data.edit_bones
    bones['clavicle.'+sf].head.z=1.392;bones['clavicle.'+sf].tail.z=1.392
    bones['upper_arm.'+sf].head.z=1.392
    bones['upper_arm.'+sf].tail.z=1.199
    bones['forearm.'+sf].head.z=1.199
    bones['forearm.'+sf].tail.z=1.0413
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
    def hole_shape(self,grid,r0,r1,widths,center):
        """Oval socket: the removed strip narrows at both ends, so the rim is a
        rounded loop rather than a rectangle with four stress corners."""
        removed=[]
        for r,w in zip(range(r0,r1),widths):
            row=grid[r];n=len(row)
            for j in range(center-w,center+w):removed.append(row[j%n])
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
def loft(rows,n,w_fn,region,shape_fn,u,v):
    """Loft rings with per-row superellipse shaping and an explicit quad grid."""
    u,v=Vector(u),Vector(v);patch=C.new_patch();loops=[];grid=[]
    for c,a,b in rows:
        c=Vector(c)
        loop=[]
        for j in range(n):
            t=2*pi*j/n
            p=Vector(c)+u*(a*cos(t))+v*(b*sin(t))
            p=shape_fn(p,t,c,a,b)
            loop.append(C.vertex(p,w_fn(p,t),region))
        if loops:
            ids=[C.face((loops[-1][j],loops[-1][(j+1)%n],loop[(j+1)%n],loop[j]),patch) for j in range(n)]
            grid.append(ids);C.seams.add(tuple(sorted((loops[-1][0],loop[0]))))
        loops.append(loop)
    return loops,grid,patch
foot_tips=[];finger_tips=[]

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
torso_profile=[(0.90,.170,.114,.010),(0.92,.171,.115,.010),(0.94,.173,.117,.009),(0.96,.172,.116,.008),(0.98,.169,.114,.007),(1.00,.165,.112,.006),(1.02,.160,.110,.005),(1.04,.155,.108,.004),(1.06,.148,.105,.002),(1.08,.142,.103,0),(1.10,.140,.103,0),(1.12,.142,.104,0),(1.16,.150,.108,0),(1.20,.160,.113,0),(1.24,.172,.119,0),(1.28,.182,.123,0),(1.32,.190,.125,0),(1.35,.196,.122,.001),(1.38,.202,.110,.003),(1.40,.200,.096,.005),(1.42,.184,.082,.008),(1.44,.170,.072,.010),(1.46,.145,.066,.011),(1.48,.100,.060,.011),(1.505,.078,.056,.010),(1.525,.066,.056,.009)]
# One-centimetre vertical control is required for chest, waist and pelvic
# planes; the previous 1.5 cm rows turned every anatomical edge into a step.
zs=sorted(set([0.90+i*.01 for i in range(60)]+[1.505]))
def sstep(x,a,b):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def bandv(x,a,b,c,d):return sstep(x,a,b)*(1-sstep(x,c,d))
def torso_shape(p,t):
    x,y,z=p;ax=abs(x);front=max(0,-sin(t));back=max(0,sin(t))
    # Pectoral mass: broad sternal-to-axillary plate with a soft inferior
    # border. Snapping it to a plane made the lower edge a shelf in revision B.
    pec=bandv(ax,.012,.045,.140,.180)*bandv(z,1.220,1.255,1.350,1.388)*front
    p.y-=.0085*pec*(.72+.28*sstep(z,1.25,1.35))
    p.y+=.0038*G(z,1.228,.012)*bandv(ax,.018,.045,.130,.170)*front
    p.y+=.0022*bandv(ax,.015,.045,.135,.170)*(1-sstep(abs(z-1.232),.008,.022))*front
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
    # Rectus abdominis is a rounded plate divided by one linea alba and three
    # tendinous intersections; a flat plane erased the abdominal planes.
    rect=bandv(z,.985,1.015,1.210,1.245)*front*(1-sstep(ax,.085,.115))
    p.y-=.0038*rect
    p.y+=.0022*(1-sstep(ax,.004,.012))*bandv(z,1.015,1.045,1.20,1.235)*front
    for zz in (1.055,1.125,1.195):
        p.y+=.0014*(1-sstep(ax,.004,.012))*G(z,zz,.006)*front
    mz=1.192+.20*ax
    p.y+=.0025*(1-sstep(abs(z-mz),.004,.016))*bandv(ax,.012,.035,.130,.160)*front
    # --- Pelvis: iliac crest, ASIS, inguinal crease, pubic mound, gluteal mass.
    # These are planes of the bony pelvis, not a generic barrel; the hip-to-thigh
    # silhouette depends on them being present before the legs branch.
    crest=G(z,1.058,.013)*sstep(ax,.070,.105)
    p.x+=(1 if x>0 else -1)*.0032*crest
    p.y+=.0018*crest*back
    p.y-=.0030*G(ax,.116,.016)*G(z,1.036,.013)*front
    p.y-=.0022*G(ax,.028,.016)*G(z,1.078,.010)*back
    # Iliac crest runs up and medially to the lumbar spine.
    p.y+=.0020*G(z,1.098,.010)*bandv(ax,.028,.055,.085,.115)*back
    # Inguinal crease from ASIS to the pubic tubercle. Its diagonal is what
    # separates abdomen from thigh in the front view.
    p1x,p1z,p2x,p2z=.124,1.034,.030,.888
    dx,dz=p2x-p1x,p2z-p1z;ll=dx*dx+dz*dz
    tt=max(0,min(1,((ax-p1x)*dx+(z-p1z)*dz)/ll))
    cx,cz=p1x+dx*tt,p1z+dz*tt
    crease=exp(-(((ax-cx)**2+(z-cz)**2)/.00030))*front*bandv(ax,.006,.020,.148,.168)
    p.y+=.0020*crease
    # Lower abdominal plane rolls forward above the crease, pubic mound below it.
    p.y-=.0022*bandv(z,.945,.965,1.030,1.055)*front*(1-sstep(ax,.100,.140))
    p.y-=.0038*G(ax,0,.042)*G(z,.892,.024)*front
    # The lower abdomen recedes into the groin; a vertical pelvic wall would
    # put the pubic plane 2 cm in front of the thigh and read as a shelf.
    p.y+=.010*front*bandv(z,.870,.890,.950,.980)
    # Gluteus maximus volume and medial cleft. The infragluteal fold is the
    # dark line under the buttock, so the mass has to overhang the thigh.
    gl=bandv(z,.852,.880,.958,1.000)
    p.y+=.019*gl*back*(.78+.22*sstep(ax,.020,.085))
    p.y-=.010*gl*(1-sstep(ax,.004,.020))*back
    p.y-=.0022*G(z,.892,.011)*back*sstep(ax,.015,.050)
    # Navel: rim and depth are separate so it reads in raking light.
    nd=G(ax,0,.0055)*G(z,1.076,.0065)
    p.y+=.0030*nd*front
    p.y-=.0009*G(ax,0,.013)*G(z,1.076,.014)*front
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
    shoulder=Vector((s*.1766529,0,1.392));wrist=Vector((s*.5578512,0,1.041322))
    d=(wrist-shoulder).normalized();u=Vector((0,-1,0));v=d.cross(u).normalized()
    hole=C.hole(torso_grid,near(zs,1.30),near(zs,1.405),range(-7,8) if s==1 else range(41,56))
    hole,angles=C.angular(hole,u,v);n=len(hole);patch=C.new_patch();previous=hole
    attachment_weights={round(theta,9):dict(C.w[index]) for index,theta in zip(hole,angles)}
    # The socket is the armhole boundary offset a few millimetres along the
    # local torso normal. Starting the arm from a small circular ring 7 cm
    # outside the torso produced the acromion spike in revision B.
    socket_pts=[]
    for index in hole:
        p=Vector(C.v[index])
        nrm=Vector((p.x,p.y-.005,0))
        if nrm.length<1e-6:nrm=Vector((s,0,0))
        nrm.normalize()
        socket_pts.append(p+nrm*.0055+d*.004)
    socket=[C.vertex(p,attachment_weights[round(theta,9)],'arm.'+sf) for p,theta in zip(socket_pts,angles)]
    C.connect(previous,socket,patch);previous=socket
    attachment_weights={round(theta,9):dict(C.w[index]) for index,theta in zip(socket,angles)}
    # The first rings carry the deltoid cap: widest at the acromion, then
    # tapering into the humerus instead of reading as a sleeve.
    arm_rows=[(.08,.065,.067),(.14,.066,.068),(.20,.063,.064),(.25,.060,.060),(.32,.056,.056),(.39,.052,.052),(.44,.048,.048),(.48,.045,.045),(.52,.043,.044),(.56,.043,.044),(.60,.045,.045),(.66,.047,.046),(.74,.044,.042),(.84,.037,.035),(.92,.030,.029),(1,.024,.024)]
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
    # Palm: wrist taper, thenar/hypothenar thickness and a knuckle line. The
    # perimeter is a rounded rectangle, not a flat paddle.
    palm_rows=[(.012,.022,.017),(.026,.028,.017),(.042,.034,.016),(.056,.038,.015),(.070,.040,.014),(.082,.039,.013)]
    for distance,width,depth in palm_rows:
        c=wrist+hand_d*distance;loop=[]
        for j in range(32):
            t=2*pi*j/32
            x=width*math.copysign(abs(cos(t))**.82,cos(t))
            y=depth*math.copysign(abs(sin(t))**.72,sin(t))
            # Thenar eminence on the radial-palmar side, hypothenar on the
            # ulnar-palmar side, both tapering before the knuckles.
            thenar=1-smooth(.020,.062,distance)
            y-=.0040*thenar*max(0,cos(t))*max(0,-sin(t))
            y-=.0022*thenar*max(0,-cos(t))*max(0,-sin(t))
            if distance>.070:y+=.0022*max(0,sin(t))*(.55+.45*cos(2*t))
            loop.append(C.vertex(c+hand_u*x+hand_v*y,{f'hand.{sf}':1},'palm.'+sf))
        if len(palm_loops)>1:
            ids=[C.face((palm_loops[-1][j],palm_loops[-1][(j+1)%32],loop[(j+1)%32],loop[j]),hand_patch) for j in range(32)]
            palm_grid.append(ids);C.seams.add(tuple(sorted((palm_loops[-1][0],loop[0]))))
        else:C.connect(previous,loop,hand_patch)
        palm_loops.append(loop);previous=loop
    # Thumb: CMC, MP and IP joints form a real opposition curve, so the first
    # digit is not a horizontal peg.
    thumb_hole=C.hole(palm_grid,0,3,range(-3,3));thumb_hole,thumb_angles=C.angular(thumb_hole,hand_v,hand_d)
    hc=sum((Vector(C.v[i]) for i in thumb_hole),Vector())/len(thumb_hole)
    thumb_pts=[hc,hc+hand_u*.016+hand_d*.004-hand_v*.010,hc+hand_u*.026+hand_d*.026-hand_v*.026,hc+hand_u*.028+hand_d*.050-hand_v*.040,hc+hand_u*.024+hand_d*.066-hand_v*.048]
    thumb_radii=[.0150,.0142,.0124,.0102,.0066]
    patch=C.new_patch();last=thumb_hole
    for k in range(len(thumb_pts)-1):
        a=thumb_pts[k];b=thumb_pts[k+1];td=(b-a).normalized();tu=hand_v;tv=td.cross(tu).normalized()
        for f in ([0,.5] if k<len(thumb_pts)-2 else [0,.5,1]):
            p=a.lerp(b,f)+(hand_v*-.0012*sin(pi*f))
            r=thumb_radii[k]*(1-f)+thumb_radii[k+1]*f
            loop=C.ring(p,tu,tv,r,r*.88,len(last),{f'hand.{sf}':1},'thumb.'+sf)
            C.connect(last,loop,patch);last=loop
    tip=thumb_pts[-1]+(thumb_pts[-1]-thumb_pts[-2]).normalized()*.006
    C.cap(last,tip,{f'hand.{sf}':1},'thumb.'+sf,patch)
    finger_tips.append((sf,tip,(thumb_pts[-1]-thumb_pts[-2]).normalized(),hand_v,.0075,'thumb','hand'))
    openings=branch_distal(palm_loops[-1],hand_u,hand_v,hand_d,None,[4,8,12],{f'hand.{sf}':1},'palm.'+sf)
    # Positive transverse side is the radial/index side beside the opposed thumb.
    for name,opening,length,radius,splay in zip(['index','middle','ring','little'],openings,[.078,.086,.080,.064],[.0095,.0098,.0092,.0080],[.010,.003,-.004,-.011]):
        center=sum((Vector(C.v[i]) for i in opening),Vector())/len(opening)
        patch=C.new_patch();last=opening
        # A relaxed finger is a chain of three phalanges: the arc bends toward
        # the palm after the metacarpophalangeal knuckle. A straight tube reads
        # as rubber at any distance.
        base=hand_d+hand_u*splay+hand_v*.02
        base.normalize();curves=[];p=center;theta=0.0
        steps=24;ds=length/steps
        for k in range(steps+1):
            t=k/steps
            theta=math.radians(42)*smooth(.26,1.0,t)**1.25
            tangent=(base*cos(theta)+hand_v*(-sin(theta))).normalized()
            curves.append((p,theta,tangent))
            p=p+tangent*ds
        tipc=curves[-1][0];tipdir=curves[-1][2]
        for k,(p,theta,tangent) in enumerate(curves):
            t=k/steps
            if k<2:continue
            joint=.0
            for jc in (.33,.66,.93):
                joint+=.055*G(t,jc,.022)
            scale=.96+.06*sin(pi*min(1,t/.9))*(1-smooth(.82,1,t))+joint
            if t>.94:scale*=.72-(t-.94)*6.0
            n=tangent.cross(hand_u).normalized()
            loop=C.ring(p,hand_u,n,radius*scale,radius*.84*scale,len(opening),{f'hand.{sf}':1},'finger_'+name+'.'+sf)
            C.connect(last,loop,patch);last=loop
        C.cap(last,tipc+tipdir*.004,{f'hand.{sf}':1},'finger_'+name+'.'+sf,patch)
        finger_tips.append((sf,tipc+tipdir*.004,tipdir,hand_u.cross(tipdir).normalized(),radius*.85,name,'hand'))

# The pelvis branches into two complete leg openings through one shared
# perineal chain. Each opening owns half of the hip ring (49 of 96 vertices)
# plus the chain; the previous build sewed each thigh to a 120-degree sector
# and left the rear quarter of the pelvis open.
bottom=torso[0]
crotch_chain=[bottom[24]]
for y,z in [(.116,.884),(.096,.874),(.070,.860),(.042,.846),(.014,.836),(-.014,.833),(-.042,.838),(-.068,.850),(-.086,.866),(-.096,.880)]:
    crotch_chain.append(C.vertex((0,y,z),{'pelvis':1},'perineum'))
crotch_chain.append(bottom[72])
crotch_inner=crotch_chain[1:-1]
leg_axis_x=.095
for s,sf in [(1,'L'),(-1,'R')]:
    opening=[bottom[j%96] for j in range(72,96)]+[bottom[j] for j in range(0,25)] if s==1 else [bottom[j] for j in range(24,73)]
    inner=crotch_inner if s==1 else list(reversed(crotch_inner))
    opening=opening+inner
    # Sort the hip/perineum boundary by its polar angle around the femoral
    # axis, then reuse those exact angles for every leg ring. Equal spacing
    # would compress the medial thigh at the groin.
    axis=Vector((s*leg_axis_x,.010,0))
    opening=sorted(opening,key=lambda i:math.atan2((Vector(C.v[i])-axis).y,(Vector(C.v[i])-axis).x))
    ang=[]
    for i in opening:
        q=Vector(C.v[i])-axis;ang.append(math.atan2(q.y,q.x))
    n=len(opening);last=opening;patch=C.new_patch()
    leg_profile=[(.11,.027,.032,0),(.14,.030,.035,.001),(.18,.037,.042,.004),(.22,.043,.047,.008),(.26,.048,.053,.010),(.30,.053,.058,.012),(.34,.058,.063,.014),(.38,.058,.062,.013),(.42,.056,.059,.008),(.45,.053,.056,.004),(.49,.052,.055,-.003),(.54,.053,.058,-.008),(.60,.061,.071,-.010),(.66,.069,.078,-.008),(.72,.076,.084,-.004),(.76,.080,.087,0),(.79,.083,.090,.004),(.815,.085,.092,.007),(.835,.087,.094,.009),(.855,.088,.095,.010),(.875,.089,.097,.011),(.90,.089,.098,.012)]
    zs_leg=[.875,.855,.835,.815,.79,.76,.72,.68,.64,.60,.575,.555,.535,.515,.495,.475,.455,.435,.41,.38,.34,.30,.26,.22,.18,.13,.115]
    def leg_weights(z):
        hip=1-smooth(.785,.935,z);shin=1-smooth(.375,.615,z)
        # The ankle joint is at z=.088, so only the last two centimetres of
        # the shin may follow the foot. The previous 7 cm blend made the lower
        # shin bend like rubber when the foot flexed.
        foot=1-smooth(.078,.132,z)
        return {'pelvis':1-hip,f'thigh.{sf}':hip*(1-shin),f'shin.{sf}':hip*shin*(1-foot),f'foot.{sf}':hip*shin*foot}
    def leg_shape(p,t):
        z=p.z;ax=abs(p.x);front=max(0,-sin(t));back=max(0,sin(t))
        outer=max(0,s*cos(t));medial=max(0,-s*cos(t))
        # The buttock overhangs the hamstrings; the fold is a crease, and
        # the tensor/IT band flattens the lateral upper thigh.
        p.y+=.020*back*G(z,.872,.032)*(.55+.45*outer)
        p.y+=.010*back*G(z,.890,.040)
        p.y-=.010*front*G(z,.875,.035)
        p.y-=.006*back*G(z,.800,.014)*(1-.45*outer)
        p.x+=s*.0035*outer*G(z,.842,.050)
        p.x-=s*.0030*medial*G(z,.850,.045)
        # Quadriceps plane, rectus ridge and vastus lateralis silhouette.
        q=bandv(z,.515,.575,.860,.885)*front
        if q:
            p.y-=.0055*q
            p.x+=s*.0045*q*outer
        p.y-=.0034*front*G(cos(t),0,.34)*bandv(z,.640,.700,.860,.885)
        # Patella sits proud of the condyles; the popliteal hollow is behind.
        pat=(1-sstep((Vector((ax-s*.095,z-.492))).length,.008,.026))*front
        p.y-=.0040*pat
        pop=(1-sstep(ax,.012,.035))*bandv(z,.445,.470,.525,.550)*back
        p.y-=.0035*pop
        # Gastrocnemius heads, tibial ridge, Achilles tendon.
        p.y+=.0080*G(z,.335,.060)*back
        p.x+=s*.0025*G(z,.320,.055)*back
        p.y-=.0020*(1-sstep(abs(p.x-s*.091),.008,.024))*bandv(z,.150,.185,.360,.405)*front
        p.y-=.0028*(1-sstep(abs(p.x-s*.095),.008,.022))*bandv(z,.110,.135,.220,.255)*back
        return p
    # The hip opening and the thigh cross-section are different shapes. Instead
    # of meeting them with one flat ring (which makes a shelf), the opening is
    # blended into the upper thigh over three rings, so the inguinal fold and
    # the greater trochanter roll into the leg.
    target_z=.855;a,b,ty=profile(leg_profile,target_z);tcx=s*.092
    def target_point(k):
        p=Vector((tcx+a*cos(ang[k]),ty+b*sin(ang[k]),target_z))
        return leg_shape(p,ang[k])
    for blend in [.34,.67,1.0]:
        loop=[]
        for k,index in enumerate(opening):
            q=Vector(C.v[index]);p=q.lerp(target_point(k),blend)
            loop.append(C.vertex(p,leg_weights(p.z),'leg.'+sf))
        C.connect(last,loop,patch);last=loop
    for z in zs_leg[2:]:
        a,b,y=profile(leg_profile,z)
        weights=leg_weights(z)
        cx=s*(.092-.004*(1-smooth(.49,.82,z)))
        loop=C.ring((cx,y,z),(1,0,0),(0,1,0),a,b,n,weights,'leg.'+sf,angles=ang,sculpt=leg_shape)
        C.connect(last,loop,patch);last=loop
    ankle=last;C.seam_loop(ankle)
    # --- Ankle and foot as one authored surface -------------------------------
    # The old foot was a horizontal tube with a small rectangular hole bridged
    # to a much larger leg ring; that mismatch produced the pinch and the
    # X-crease. Here the leg tapers through two explicit ankle rings to the
    # 24-vertex rim of a 4.6 x 3.5 cm instep opening, and the foot itself is
    # lofted with a flat sole, an arch, a heel and a metatarsal fan.
    def se(v,e):return math.copysign(abs(v)**e,v)
    def foot_weight(p,t):
        toe=1-smooth(-.162,-.130,p.y)
        return {f'foot.{sf}':1-toe,f'toe.{sf}':toe}
    # The instep is the top of the foot, not a flat plate with a collar on
    # top: each sagittal section rises from the heel to ankle height and then
    # falls toward the metatarsals. The ankle emerges where the surface is
    # already at ankle height, so no box-like sleeve is needed.
    top_key=[(-.162,.0300),(-.145,.0320),(-.125,.0350),(-.105,.0390),(-.085,.0440),(-.065,.0520),(-.050,.0580),(-.035,.0660),(-.025,.0740),(-.015,.0820),(-.005,.0860),(.005,.0870),(.015,.0850),(.025,.0780),(.035,.0680),(.045,.0520),(.055,.0380),(.065,.0300)]
    a_key=[(-.162,.0410),(-.145,.0450),(-.125,.0470),(-.105,.0470),(-.085,.0460),(-.065,.0440),(-.050,.0410),(-.035,.0380),(-.025,.0360),(-.015,.0340),(-.005,.0330),(.005,.0320),(.015,.0320),(.025,.0330),(.035,.0330),(.045,.0310),(.055,.0280),(.065,.0240)]
    fy=sorted({round(.065-i*.00582,6) for i in range(40)})
    def foot_shape(p,t,c,a,b):
        x,y,z=p
        # Superellipse section: the low exponent flattens the sole and squares
        # the forefoot without a hard clamp that would duplicate vertices.
        x=c.x+a*se(cos(t),.72)
        z=c.z+b*se(sin(t),.55)
        medial=max(0,-s*(x-c.x)/max(a,1e-5))
        sole=max(0,-sin(t))
        # The medial longitudinal arch lifts the inner border between the heel
        # and the ball while the lateral border stays on the ground.
        z+=.011*G(y,-.052,.038)*medial*sole
        if z<.0045:z=.0045
        return Vector((x,y,z))
    foot_rows=[]
    for y in reversed(fy):
        top,=profile(top_key,y);a,=profile(a_key,y)
        top=max(.014,top);sole=.005
        foot_rows.append(((s*.093,y,(top+sole)/2),a,(top-sole)/2))
    # A tiny terminal ring keeps the heel cap from being a flat disc.
    # Round the heel with a short sequence of shrinking sections instead of a
    # tiny ring that jumps straight to the full heel width.
    heel_extra=[]
    for y,a,k in [(.074,.008,.30),(.072,.016,.55),(.070,.022,.82)]:
        top=.005+2*(.0125*k+.001)
        heel_extra.append(((s*.093,y,(top+.005)/2),a,(top-.005)/2))
    foot_rows=heel_extra+foot_rows
    loops,grid,foot_patch=loft(foot_rows,32,foot_weight,'foot.'+sf,foot_shape,(1,0,0),(0,0,1))
    C.cap(loops[0],(s*.093,.073,.012),{f'foot.{sf}':1},'heel.'+sf,foot_patch)
    hole_row=next(i for i in range(len(grid)) if foot_rows[i][0][1]<=.031 and foot_rows[i+1][0][1]<.031)
    # The ankle footprint, not a narrow slot: the opening must be as wide as
    # the shin, otherwise the leg funnels into a cuff above the instep.
    instep_hole=C.hole_shape(grid,hole_row,hole_row+9,[3,4,4,4,4,4,4,4,3],8)
    instep_hole,instep_angles=C.angular(instep_hole,(1,0,0),(0,1,0))
    # Build the ankle wall directly from the instep rim: a lifted, slightly
    # narrowed copy of the same loop. No intermediate ring can bulge or twist.
    cc=Vector((s*.093,.003))
    # Align the shin ring to the instep rim so the 54->30 transition cannot
    # spiral around the ankle. A single cone avoids the flat collar that made
    # the old ankle read as a boot cuff.
    def ankle_angle(i):
        q=Vector(C.v[i]);return math.atan2(q.y-cc.y,q.x-cc.x)
    aligned=sorted(last,key=ankle_angle)
    k=min(range(len(aligned)),key=lambda j:abs(ankle_angle(aligned[j])-ankle_angle(instep_hole[0])))
    aligned=aligned[k:]+aligned[:k]
    C.connect(aligned,instep_hole,patch)
    print('FOOT_RIM',sf,len(instep_hole),flush=True)
    # Five toes branch from the metatarsal rim. The medial toe is the largest
    # and the length schedule is mirrored with the foot, not copied blindly.
    openings=branch_distal(loops[-1],(1,0,0),(0,0,1),(0,-.001,0),None,[3,6,9,12],{f'toe.{sf}':1},'forefoot.'+sf)
    toe_specs=[(.040,.0076),(.045,.0082),(.048,.0086),(.052,.0094),(.055,.0108)]
    if s<0:toe_specs=list(reversed(toe_specs))
    for toe,(opening,(length,radius)) in enumerate(zip(openings,toe_specs)):
        c=sum((Vector(C.v[i]) for i in opening),Vector())/len(opening)
        splay=(c.x-s*.093)*.55
        d=Vector((splay,-1,-.16)).normalized();patch=C.new_patch();last=opening
        tip=c+d*length
        foot_tips.append((sf,tip,d,(0,0,1),radius,'toe%d'%toe,'toe'))
        # Straight taper, no mid bulge: a toe is a column of phalanges with a
        # broad base and a slightly flattened dorsal nail bed.
        for t,scale in [(.08,1.00),(.20,.99),(.34,.97),(.48,.94),(.62,.90),(.74,.85),(.84,.78),(.92,.68),(.97,.52),(1.0,.34)]:
            p=c+d*(length*t)+Vector((0,0,.0012*sin(pi*t)))
            loop=C.ring(p,(1,0,0),(0,0,1),radius*scale,radius*.88*scale,len(opening),foot_weight,'toe%d.%s'%(toe,sf))
            C.connect(last,loop,patch);last=loop
        C.cap(last,tip,{f'toe.{sf}':1},'toe%d.%s'%(toe,sf),patch)

# Craniofacial planes: a tapered mandible and a projected maxilla carry the
# masculine structure; the skull is not an egg with features painted on.
head_profile=[(1.525,.060,.050,.058,.006),(1.535,.060,.052,.060,.005),(1.545,.061,.055,.062,.004),(1.555,.062,.058,.064,.003),(1.565,.063,.061,.066,.002),(1.575,.065,.065,.068,.001),(1.585,.068,.069,.070,0),(1.595,.071,.072,.072,0),(1.605,.074,.074,.074,0),(1.615,.076,.075,.076,0),(1.625,.078,.076,.078,0),(1.635,.079,.077,.080,0),(1.645,.080,.078,.082,0),(1.655,.080,.079,.084,0),(1.665,.080,.080,.086,0),(1.675,.080,.080,.088,0),(1.685,.079,.079,.090,0),(1.695,.078,.079,.092,0),(1.705,.077,.078,.094,0),(1.715,.076,.078,.096,0),(1.725,.075,.077,.098,0),(1.735,.074,.076,.099,0),(1.745,.072,.074,.098,0),(1.755,.070,.072,.096,0),(1.765,.067,.069,.093,0),(1.775,.062,.064,.088,0),(1.785,.055,.057,.080,0),(1.793,.045,.048,.070,0),(1.799,.030,.032,.052,0),(1.8002,.018,.018,.032,0)]
head_z=sorted(set([1.525+i*.004 for i in range(69)]+[1.552+i*.0012 for i in range(141)]+[1.799,1.800]))
head_candidates=sorted({round(z,6) for z in head_z if z<=1.800})
head_z=[]
for z in head_candidates:
    if not head_z or z-head_z[-1]>=.0008:head_z.append(z)
def face_shape(p,t):
    x,y,z=p
    front=max(0,-sin(t))**1.35
    if front==0:return p
    ax=abs(x)
    def gx(cx,sx):return exp(-((x-cx)/sx)**2)
    def gz(cz,sz):return exp(-((z-cz)/sz)**2)
    def bz(a,b,c,d):return sstep(z,a,b)*(1-sstep(z,c,d))
    def ridgex(cx,w):
        d=abs(x-cx)/w
        return max(0.0,1-d*d)**2
    relief=0.0
    return p
    # Brow shelf over recessed orbits. Amplitudes stay near anatomical
    # thickness: a 1.5 cm shelf reads as a mask, not a skull.
    relief+=.0060*gx(.034,.029)*gz(1.712,.0090)
    relief+=.0020*gx(0,.017)*gz(1.714,.0090)
    relief-=.0100*gx(.034,.025)*gz(1.690,.0125)
    relief-=.0025*gx(0,.048)*gz(1.691,.0150)
    # Nasal root, straight dorsum, tip pad, alar wings and nostril crease.
    relief-=.0020*gx(0,.010)*gz(1.701,.0080)
    relief+=.0100*ridgex(0,.0105)*bz(1.664,1.676,1.700,1.712)
    relief+=.0180*gx(0,.0130)*gz(1.659,.0095)
    relief+=.0040*gx(0,.0075)*gz(1.650,.0065)
    relief+=.0065*gx(.0185,.0065)*gz(1.6505,.0070)
    relief-=.0025*gx(.0122,.0040)*gz(1.6450,.0040)
    relief+=.0025*gx(0,.0040)*gz(1.6425,.0040)
    # Nasolabial fold from the ala to the mouth corner.
    nx1,nz1,nx2,nz2=.020,1.648,.033,1.606
    ndx,ndz=nx2-nx1,nz2-nz1;nl=ndx*ndx+ndz*ndz
    nt=max(0,min(1,((ax-nx1)*ndx+(z-nz1)*ndz)/nl))
    ncx,ncz=nx1+ndx*nt,nz1+ndz*nt
    relief-=.0015*exp(-(((ax-ncx)**2+(z-ncz)**2)/.000030))
    # Zygomatic shelf, lateral arch and the hollow beneath it.
    relief+=.0070*gx(.058,.020)*gz(1.668,.0120)
    relief+=.0025*gx(.073,.011)*gz(1.659,.0110)
    relief-=.0035*gx(.056,.018)*gz(1.639,.0150)
    # Mandibular border from gonion to chin, with a buccal hollow above.
    jx,jz=.074,1.623;cx2,cz2=.032,1.580
    dx,dz=cx2-jx,cz2-jz;ll=dx*dx+dz*dz
    tt=max(0,min(1,((ax-jx)*dx+(z-jz)*dz)/ll))
    lx,lz=jx+dx*tt,jz+dz*tt
    relief+=.0045*exp(-(((ax-lx)**2+(z-lz)**2)/.000035))*bandv(ax,.020,.032,.078,.084)
    relief-=.0025*gx(.057,.018)*gz(1.636,.016)
    # Square chin mass, mental crease and two mental lobes.
    relief+=.0050*(1-sstep(ax,.022,.038))*bz(1.566,1.578,1.596,1.612)
    relief-=.0015*gx(0,.020)*gz(1.594,.0040)
    relief+=.0012*gx(.012,.009)*gz(1.578,.006)
    # Philtrum, Cupid's bow, upper lip, mouth slit, lower lip and groove.
    relief-=.0010*gx(0,.0045)*bz(1.616,1.624,1.632,1.640)
    relief+=.0008*gx(.0055,.0035)*bz(1.616,1.622,1.630,1.636)
    relief+=.0022*(1-sstep(ax,.022,.032))*gz(1.619,.0065)
    relief+=.0008*gx(0,.0050)*gz(1.621,.0045)
    relief+=.0008*gx(.0080,.0048)*gz(1.620,.0048)
    relief-=.0024*gx(0,.028)*gz(1.6075,.0028)
    relief-=.0012*gx(.031,.007)*gz(1.606,.0065)
    relief+=.0030*gx(0,.017)*gz(1.600,.0055)
    relief-=.0015*gx(0,.019)*gz(1.5905,.0040)
    # Temple hollow and forehead plane.
    relief-=.0020*gx(.070,.015)*gz(1.724,.019)
    n=Vector((x,y,0))
    if n.length<1e-6:return p
    # Heightfields applied along view Y make plates; projecting each form
    # along the local radial normal gives the nose and lips real side walls.
    p+=n.normalized()*front*relief
    return p
head_rows=[]
for z in head_z:
    a,front,back,y=profile(head_profile,z);head_rows.append(((0,y,z),a,(front+back)/2))
def head_shape(p,t):
    x,y,z=p
    front=max(0,-sin(t));back=max(0,sin(t))
    if z<1.632:
        # The jaw is a V in plan, not the full skull ellipse carried down to
        # the chin; the mandible also overhangs the throat.
        k=1-smooth(1.576,1.632,z)
        p.x*=1-.30*k
        p.y+=front*k*.006
        p.y-=back*k*.004
    return p
head,head_grid,head_patch=C.cylinder(head_rows,128,lambda i,p,t:{'head':smooth(1.52,1.565,p.z),'neck':1-smooth(1.52,1.565,p.z)},'head',head_shape)
for k,z in enumerate(head_z):
    a,front,back,cy=profile(head_profile,z)
    for j,index in enumerate(head[k]):
        t=2*pi*j/128;x=a*cos(t)
        if sin(t)<0:
            y=cy-front*max(0,1-(abs(x)/max(a,1e-6))**3.2)**(1/3.2)
        else:y=cy+back*sin(t)
        if sin(t)<0:y-=.004*G(z,1.660,.070)*max(0,-sin(t))**2
        C.v[index]=tuple(face_shape(Vector((x,y,z)),t))
C.connect(torso[-1],head[0],C.new_patch());C.seam_loop(head[0])
C.cap(head[-1],(0,.015,1.8003),{'head':1},'head',head_patch)
eyes=[]
def facial_y(x,z):
    radius,front,back,cy=profile(head_profile,z)
    ratio=max(-.999,min(.999,x/radius));theta=2*pi-math.acos(ratio)
    y=cy-front*max(0,1-abs(ratio)**3.2)**(1/3.2)-.004*G(z,1.660,.070)*sin(theta)**2
    return face_shape(Vector((x,y,z)),theta).y
def front_column(target,a):
    def key(j):
        t=2*pi*j/128
        return abs(a*cos(t)-target) if sin(t)<0 else 99
    return min(range(64,128),key=key)
eye_a=profile(head_profile,1.688)[0]
for s,sf in [(1,'L'),(-1,'R')]:
    # The opening is a compact orbital cut, not a slit: the lid funnel starts
    # on the skin and ends at an almond aperture. The eyeball sits 7 mm behind
    # that aperture, so the brow and lids, not the sphere, define the eye.
    col=front_column(s*.033,eye_a)
    opening=C.hole(head_grid,near(head_z,1.683),near(head_z,1.695),range(col-3,col+3))
    opening,angles=C.angular(opening,(1,0,0),(0,0,1));patch=C.new_patch();last=opening
    center=Vector((s*.033,facial_y(s*.033,1.688)+.0150,1.6875))
    for layer in range(3):
        loop=[]
        width,hu,hl=[(.0148,.0082,.0064),(.0128,.0052,.0044),(.0110,.0042,.0036)][layer]
        for t in angles:
            dx=width*cos(t);dz=(hu if sin(t)>0 else hl)*sin(t)+.0007*cos(t)*s
            surface=facial_y(center.x+dx,center.z+dz)
            if layer==0:y=surface-.0002
            else:
                spherical=center.y-sqrt(max(.000001,.0122**2-dx*dx-dz*dz))-.0004
                blend=1.0 if layer==2 else .70*abs(sin(t))**.5
                y=surface*(1-blend)+spherical*blend
            loop.append(C.vertex((center.x+dx,y,center.z+dz),{'head':1},('orbital_rim.' if layer==0 else 'eyelid.')+sf))
        C.connect(last,loop,patch);last=loop
    inner=[]
    for index in last:
        p=Vector(C.v[index]);p.y+=.0012;inner.append(C.vertex(p,{'head':1},'eyelid_inner.'+sf))
    C.connect(last,inner,patch);C.seam_loop(opening)
    last=inner
    for depth,radius in [(.002,.0130),(.010,.0088)]:
        cavity=[C.vertex((center.x+radius*cos(t),center.y+depth,center.z+radius*sin(t)),{'head':1},'orbital_cavity.'+sf) for t in angles]
        C.connect(last,cavity,patch);last=cavity
    C.cap(last,center+Vector((0,.016,0)),{'head':1},'orbital_cavity.'+sf,patch)
    eyes.append((sf,center))
    # --- Ear: a radial shell with a real helix, scapha, antihelix, concha,
    # tragus and lobe. The old four flat rings read as a disc glued to the skull.
    col=2 if s==1 else 62
    opening=C.hole(head_grid,near(head_z,1.646),near(head_z,1.696),range(col-4,col+4))
    opening,angles=C.angular(opening,(0,1,0),(0,0,1));last=opening;patch=C.new_patch()
    ear_rings=[(1.00,.0152,.0292,.0808,.0000),(1.00,.0164,.0304,.0862,.0054),(.82,.0136,.0250,.0822,-.0010),(.66,.0108,.0196,.0798,-.0014),(.48,.0072,.0138,.0770,-.0010),(.32,.0044,.0088,.0758,-.0006)]
    for k,(rho,a,b,xx,ridge) in enumerate(ear_rings):
        loop=[]
        for t in angles:
            lower=max(0,-sin(t));front=max(0,cos(t))
            lobe=smooth(.25,.95,lower)
            # The helix is a rolled rim that lifts away from the skull at the
            # top and dies into the lobe; the antihelix and concha are inner
            # folds, so the ear has a lit rim, a groove and a dark bowl.
            crest=ridge*sstep(-.15,1.0,sin(t))*(1-.85*lobe)
            tragus=.0030*front*(1-smooth(.34,.58,rho))*sstep(.28,.85,rho)
            x=s*(xx+crest+tragus-.0016*lobe*(1-rho))
            y=.009+rho*a*cos(t)+.006*rho*b*sin(t)/.032
            loop.append(C.vertex((x,y,1.672+rho*b*sin(t)),{'head':1},'ear.'+sf))
        C.connect(last,loop,patch);last=loop
    C.cap(last,(s*.0752,.008,1.668),{'head':1},'ear.'+sf,patch)

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
interfaces=[v for v in bm.verts if (1.275<v.co.z<1.445 and .125<abs(v.co.x)<.305) or (1.495<v.co.z<1.55) or (.118<v.co.z<.132)]
for _ in range(12):bmesh.ops.smooth_vert(bm,verts=interfaces,factor=.34,use_axis_x=True,use_axis_y=True,use_axis_z=True)
orbital=[v for v in bm.verts if C.region[v.index].startswith('orbital_rim.') or (C.region[v.index]=='head' and .015<abs(v.co.x)<.055 and 1.668<v.co.z<1.700 and v.co.y<-.035)]
for _ in range(5):bmesh.ops.smooth_vert(bm,verts=orbital,factor=.18,use_axis_x=True,use_axis_y=True,use_axis_z=True)
# The hip opening, perineal chain and thigh rings meet in a tight curvature
# region; a local relax turns those authored loops into one inguinal surface
# instead of leaving the shelf where the pelvis loft ends.
hip=[v for v in bm.verts if .79<v.co.z<.96 and abs(v.co.x)<.26]
for _ in range(16):bmesh.ops.smooth_vert(bm,verts=hip,factor=.48,use_axis_x=True,use_axis_y=True,use_axis_z=True)
# The relax deliberately removes the sharp construction creases; these fields
# put the masses back as continuous anatomy rather than as patch edges.
for v in bm.verts:
    p=v.co;z=p.z;ax=abs(p.x);s=1 if p.x>=0 else -1
    if p.y>0 and .78<z<.98 and ax<.24:
        g=bandv(z,.81,.84,.94,.97)
        p.y+=.016*g*(.62+.38*sstep(ax,.03,.13))
        p.y-=.009*g*(1-sstep(ax,.004,.022))
        p.y-=.004*G(z,.792,.012)*(1-sstep(ax,.02,.07))
    if p.y<0 and 1.20<z<1.42 and ax<.20:
        pec=bandv(z,1.22,1.26,1.35,1.39)*(1-sstep(abs(ax-.072),.035,.085))
        p.y-=.008*pec
        p.y+=.0035*G(ax,0,.012)*bandv(z,1.24,1.28,1.38,1.41)
    if .16<z<.86 and ax<.22:
        if p.y<0:
            p.y-=.006*G(ax,.093,.030)*bandv(z,.56,.62,.80,.86)
            p.y-=.004*bandv(ax,.12,.14,.17,.19)*bandv(z,.60,.66,.80,.85)
            p.y-=.0035*bandv(ax,.02,.05,.08,.10)*bandv(z,.48,.53,.58,.62)
        else:
            p.y+=.005*bandv(z,.58,.64,.78,.84)
            p.y+=.004*G(z,.33,.060)
            p.y-=.003*G(z,.798,.012)
        p.x+=s*.003*bandv(ax,.13,.15,.18,.19)*bandv(z,.60,.66,.80,.85)
    if z>1.00 and z<1.34 and ax>.22 and ax<.30:
        if p.y<0:p.y-=.003*G(z,1.23,.065)
        else:p.y+=.003*G(z,1.25,.065)
        p.x+=s*.002*G(z,1.32,.05)
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
white=material('Sclera_v2',(.56,.52,.47),.34);iris=material('Iris_v2',(.095,.052,.022),.40);pupil=material('Pupil_v2',(.003,.0025,.002),.25)
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
def spherical_cap(name,center,radius,cap_radius,mat,bone):
    """A curved iris/pupil patch on the eyeball, not a floating flat disc."""
    seg=32;rings=5;verts=[(center.x,center.y-radius,center.z)];faces=[]
    maxang=math.asin(min(.999,cap_radius/radius))
    for r in range(1,rings+1):
        ang=maxang*r/rings;rr=radius*math.sin(ang);yy=center.y-radius*math.cos(ang)
        for j in range(seg):
            t=2*pi*j/seg
            verts.append((center.x+rr*math.cos(t),yy,center.z+rr*math.sin(t)))
    for j in range(seg):faces.append((0,1+j,1+(j+1)%seg))
    for r in range(rings-1):
        base=1+r*seg
        for j in range(seg):
            a=base+j;b=base+(j+1)%seg;c=base+seg+(j+1)%seg;d=base+seg+j
            faces.append((a,b,c,d))
    me=bpy.data.meshes.new(name);me.from_pydata(verts,[],faces);me.update()
    for p in me.polygons:p.use_smooth=True
    o=bpy.data.objects.new(name,me);scene.collection.objects.link(o);o.data.materials.append(mat)
    g=o.vertex_groups.new(name=bone);g.add(list(range(len(me.vertices))),1,'REPLACE')
    m=o.modifiers.new('Rigid_cap_skinning','ARMATURE');m.object=rig;o.parent=rig;accessories.append(o)
    return o
for sf,c in eyes:
    rigid_sphere('GEO_Eyeball_v2.'+sf,c,(.0122,.0122,.0122),white)
    spherical_cap('GEO_Iris_v2.'+sf,c,.01230,.0072,iris,'head')
    spherical_cap('GEO_Pupil_v2.'+sf,c,.01238,.0034,pupil,'head')
# Eyebrows are conforming three-row ribbons placed by ray casting the finished
# face, so both sides follow the same brow ridge and neither can be buried.
brow_mat=material('Brow_v2',(.016,.009,.005),.66)
for s,sf in [(1,'L'),(-1,'R')]:
    xs=[.009,.014,.019,.024,.029,.034,.039,.044,.049,.054,.059,.064,.069];verts=[];faces=[]
    for i,x in enumerate(xs):
        u=i/(len(xs)-1)
        zc=1.7085+.0082*sin(pi*min(1,u*1.05))**.70-.0035*u
        hh=.0009+.0016*sin(pi*min(1,u*1.05))**.45*(1-.25*u)
        for r in range(3):
            dz=(r-1)*hh
            hit,loc,norm,face=body.ray_cast(Vector((s*x,-.30,zc+dz)),Vector((0,1,0)))
            y=loc.y-.0006 if hit else facial_y(s*x,zc+dz)-.0006
            verts.append((s*x,y,zc+dz))
    for i in range(len(xs)-1):
        for r in range(3):
            a=3*i+r;b=3*(i+1)+r;c=3*(i+1)+r+1;d=3*i+r+1
            faces.append((a,b,c,d) if s==1 else (a,d,c,b))
    bm=bpy.data.meshes.new('GEO_Brow.'+sf);bm.from_pydata(verts,[],faces);bm.update()
    for poly in bm.polygons:poly.use_smooth=True
    obj=bpy.data.objects.new('GEO_Brow.'+sf,bm);scene.collection.objects.link(obj)
    obj.data.materials.append(brow_mat)
    g=obj.vertex_groups.new(name='head');g.add(list(range(len(obj.data.vertices))),1,'REPLACE')
    m=obj.modifiers.new('Rigid_brow_skinning','ARMATURE');m.object=rig;obj.parent=rig;accessories.append(obj)

# Keratin plates are separate conforming objects, like the eyes: the skin is
# one continuous surface and nails are a different material on top of it.
nail_mat=material('Nail_v2',(.58,.43,.37),.30)
def oriented_ellipsoid(name,center,axes,radii,mat,bone):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20,ring_count=10,location=(0,0,0))
    o=bpy.context.object;o.name=name
    a1,a2,a3=[Vector(a).normalized() for a in axes]
    for v in o.data.vertices:
        v.co=Vector(center)+a1*v.co.x*radii[0]+a2*v.co.y*radii[1]+a3*v.co.z*radii[2]
    o.data.update()
    for p in o.data.polygons:p.use_smooth=True
    o.data.materials.append(mat)
    g=o.vertex_groups.new(name=bone);g.add(list(range(len(o.data.vertices))),1,'REPLACE')
    m=o.modifiers.new('Rigid_nail_skinning','ARMATURE');m.object=rig;o.parent=rig;accessories.append(o)
    return o
for sf,tip,axis,dorsal,r,name,kind in finger_tips+foot_tips:
    across=Vector(axis).cross(Vector(dorsal)).normalized()
    c=Vector(tip)-Vector(axis)*.008+Vector(dorsal)*(r*.70)
    oriented_ellipsoid('GEO_Nail_%s_%s.%s'%(kind,name,sf),c,(across,axis,dorsal),(r*.52,r*1.05,.0012),nail_mat,('hand.' if kind=='hand' else 'toe.')+sf)


# The compression shell experiment is intentionally absent from the final
# deliverable: it hid the crotch instead of proving the pelvic surface, and a
# garment cannot be used as evidence for a continuous body.

regions=mesh.attributes.new('Anatomical_region_id','INT','POINT');region_names=sorted(set(C.region))
for i,r in enumerate(C.region):regions.data[i].value=region_names.index(r)
(OUT/'final-body-authoring.json').write_text(json.dumps({'regionNames':region_names,'patchCount':C.patch_count,'technique':'Explicit connected anatomical contour loops; bridged shoulder openings, shared crotch, shared interdigital webs, orbital eyelid loops and ear shells. No remesh or decimation in source cage.','weights':'Regional support and broad smooth joint transitions; no automatic heat weights or top-four support switching on the ribs.','rigContract':'24 staging bones: the existing 22 bind bones plus axilla_support.L/R. Support bones share the humerus rest matrix and clavicle parent, and rotate by the shortest-arc quaternion three-quarter swing of the local humerus rotation. Their test motion is baked at all 80 frames. Original 22 local pose channels are unchanged; no runtime contract overwritten.','uv':'Anatomical patch seams plus one longitudinal cut per tube, native Blender angle-based unwrap.'},indent=2))
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'final-body-source.blend'))
print('CAGE_CREATED',len(mesh.vertices),len(mesh.polygons),'patches',C.patch_count,flush=True)
import os
if os.environ.get('FE_SKIP_RENDER')=='1':raise SystemExit

# --- Preview renders on the saved source, using a neutral clay override -----
def preview_mat(name,value,rough=.62):
    m=bpy.data.materials.new(name);m.use_nodes=True
    bsdf=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value=(value,value,value,1);bsdf.inputs['Roughness'].default_value=rough
    return m
body.data.materials.clear();body.data.materials.append(preview_mat('Preview_clay',.46))
visible={o.name for o in accessories}
for obj in scene.objects:
    if obj.type=='MESH' and obj is not body and obj.name not in visible:obj.hide_render=True;obj.hide_set(True)
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
