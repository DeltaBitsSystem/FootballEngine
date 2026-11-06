"""Build a preserved revision using our existing cage tooling and explicit corrections.

The archived author is an input, not edited in place. Every source transformation
is cardinality-checked so a changed upstream author cannot silently produce a
different asset. Output ownership/provenance is recorded with the input hash.
"""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUTPUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Revision-Sol'
SOURCE = HERE / 'final_body_author_v6.py'
text = SOURCE.read_text()
changes = []


def replace(old, new, count=1):
    global text
    actual = text.count(old)
    if actual != count:
        raise RuntimeError(f'Author contract changed: expected {count}, found {actual}: {old[:90]}')
    text = text.replace(old, new)
    changes.append({'before': old, 'after': new, 'occurrences': count})


replace("OUT=BASE/'Final-Body/Revision-F'", "OUT=BASE/'Final-Body/Revision-Sol'")
replace('self.patch_count=0', 'self.patch_count=0;self.digit_progress={}')
replace("rotation_quaternion,.75)", "rotation_quaternion,.5)")
# sstep is declared (x, lower, upper), unlike the older smooth helper.
replace('def hip_mask(z):return sstep(.750,.815,z)*(1-sstep(.960,1.045,z))',
        'def hip_mask(z):return sstep(z,.750,.815)*(1-sstep(z,.960,1.045))')
# A superelliptic frontal skull was creating a mask-like plane across the face.
replace('**3.2)**(1/3.2)', '**2.2)**(1/2.2)', count=4)
replace('(0.000,-.088,1.664,.014,.035,.013,0,-.0105,0)',
        '(0.000,-.088,1.664,.014,.045,.016,0,-.0230,0)')
replace('(0.000,-.082,1.680,.010,.035,.022,0,-.0055,0)',
        '(0.000,-.082,1.680,.011,.045,.023,0,-.0090,0)')
replace('(0.016,-.082,1.652,.008,.030,.008,0,-.0028,0)',
        '(0.016,-.082,1.652,.008,.040,.008,0,-.0060,0)')
replace('(0.032,-.080,1.705,.031,.045,.013,0,-.0050,0)',
        '(0.032,-.080,1.705,.031,.045,.013,0,-.0025,0)')
for stroke in [
    '(0.000,-.084,1.633,.030,.045,.010,0,-.0042,0),',
    '(0.000,-.084,1.618,.032,.045,.011,0,-.0048,0),',
    '(0.000,-.086,1.625,.032,.035,.0022,0,.0022,0),',
]:
    replace(stroke, '')
replace('facial_y(s*.033,1.688)+.0105', 'facial_y(s*.033,1.688)+.0140')
replace("        faces.append((a,bstart+(j+1)%seg,bstart+j))\n", '')
replace('seg=32;rings=8;verts=[];faces=[]', 'seg=64;rings=16;verts=[];faces=[]')
replace('verts.append((base+s*h,yy,zz))', 'verts.append((base+s*(h+.006*r*cos(t)),yy,zz))')
replace('h+=.0052*exp(-((r-.92)/.10)**2)', 'h+=.0065*exp(-((r-.83)/.12)**2)')
replace('h+=.0030*exp(-((r-.62)/.11)**2)', 'h+=.0035*exp(-((r-.49)/.12)**2)')
replace('exp(-(((t+pi/2)/.55)**2))', 'exp(-((sin(t)+1)/.25)**2)')
replace('sstep(-.052,-.064,y)', 'sstep(-y,.052,.064)')
replace('thumb_hole=C.hole(palm_grid,3,4,range(-2,3))', 'thumb_hole=C.hole(palm_grid,1,3,range(-3,3))')
replace('thumb_pts=[hc,hc+hand_u*.006+hand_d*.010-hand_v*.026,hc+hand_u*.010+hand_d*.026-hand_v*.040,hc+hand_u*.010+hand_d*.044-hand_v*.050,hc+hand_u*.008+hand_d*.058-hand_v*.052]',
        'thumb_pts=[hc,hc+hand_u*.014+hand_d*.010-hand_v*.012,hc+hand_u*.025+hand_d*.025-hand_v*.022,hc+hand_u*.030+hand_d*.044-hand_v*.026,hc+hand_u*.027+hand_d*.060-hand_v*.028]')
replace('patch=C.new_patch();last=thumb_hole\n    for k in range(len(thumb_pts)-1):', '''patch=C.new_patch()
    initial_direction=(thumb_pts[1]-thumb_pts[0]).normalized()
    initial_u=(hand_v-initial_direction*hand_v.dot(initial_direction)).normalized()
    initial_v=initial_direction.cross(initial_u).normalized()
    last,thumb_angles=C.angular(thumb_hole,initial_u,initial_v)
    thumb_angles=[2*pi*j/len(last) for j in range(len(last))]
    for k in range(len(thumb_pts)-1):''')
replace('td=(b-a).normalized();tu=hand_v;tv=td.cross(tu).normalized()',
        'td=(b-a).normalized();tu=(hand_v-td*hand_v.dot(td)).normalized();tv=td.cross(tu).normalized()')
replace("len(last),{f'hand.{sf}':1},'thumb.'+sf)",
        "len(last),{f'hand.{sf}':1},'thumb.'+sf,angles=thumb_angles)")
replace("loop=C.ring(p,tu,tv,r,r*.88,len(last),{f'hand.{sf}':1},'thumb.'+sf,angles=thumb_angles)",
        "loop=C.ring(p,tu,tv,r,r*.88,len(last),{f'hand.{sf}':1},'thumb.'+sf,angles=thumb_angles)\n            for vi in loop:C.digit_progress[vi]=(k+f)/(len(thumb_pts)-1)")
replace("C.cap(last,tip,{f'hand.{sf}':1},'thumb.'+sf,patch)",
        "C.cap(last,tip,{f'hand.{sf}':1},'thumb.'+sf,patch)\n    C.digit_progress[len(C.v)-1]=1.0")
replace('base.normalize();curves=[];p=center;theta=0.0', '''base.normalize()
        last,finger_angles=C.angular(opening,hand_u,base.cross(hand_u).normalized())
        finger_angles=[2*pi*j/len(last) for j in range(len(last))]
        curves=[];p=center;theta=0.0''')
replace("len(opening),{f'hand.{sf}':1},'finger_'+name+'.'+sf)",
        "len(opening),{f'hand.{sf}':1},'finger_'+name+'.'+sf,angles=finger_angles)")
replace("loop=C.ring(p,hand_u,n,radius*scale,radius*.84*scale,len(opening),{f'hand.{sf}':1},'finger_'+name+'.'+sf,angles=finger_angles)",
        "loop=C.ring(p,hand_u,n,radius*scale,radius*.84*scale,len(opening),{f'hand.{sf}':1},'finger_'+name+'.'+sf,angles=finger_angles)\n            for vi in loop:C.digit_progress[vi]=t")
replace("C.cap(last,tipc+tipdir*.0025,{f'hand.{sf}':1},'finger_'+name+'.'+sf,patch)",
        "C.cap(last,tipc+tipdir*.0025,{f'hand.{sf}':1},'finger_'+name+'.'+sf,patch)\n        C.digit_progress[len(C.v)-1]=1.0")
replace('C.region=[C.region[i] for i in used]',
        'C.region=[C.region[i] for i in used]\nC.digit_progress={remap[i]:t for i,t in C.digit_progress.items() if i in remap}')
replace('(r*.46,r*.62,.0017)', '(r*.46,r*.62,.0004)')
replace('for i in range(len(xs)-1):\n        for r in range(3):',
        'for i in range(len(xs)-1):\n        for r in range(2):')
replace("None,[3,6,9,12],{f'toe.{sf}':1}", "None,([3,6,9,12] if s>0 else [4,7,10,13]),{f'toe.{sf}':1}")
replace('if s<0:toe_specs=list(reversed(toe_specs))', 'if s>0:toe_specs=list(reversed(toe_specs))')
# The lip surface needs an actual inset opening and vermilion loops, not bars
# painted or displaced across an unbroken face plane.
replace('faces=[f for f in C.f if f is not None]', '''mouth_row0=near(head_z,1.619)
mouth_row1=near(head_z,1.631)
mouth_count=mouth_row1-mouth_row0
mouth_widths=[max(2,round(2+5*sqrt(max(0,1-((i-(mouth_count-1)/2)/max(1,(mouth_count-1)/2))**2)))) for i in range(mouth_count)]
mouth_opening=C.hole_shape(head_grid,mouth_row0,mouth_row1,mouth_widths,front_column(0,profile(head_profile,1.625)[0]))
mouth_opening,mouth_angles=C.angular(mouth_opening,(1,0,0),(0,0,1))
mouth_patch=C.new_patch()
last=mouth_opening
for j,theta in enumerate(mouth_angles):
    x=.031*cos(theta)
    line=1.625-.0018*(abs(x)/.031)**2
    z=line+(.0060 if sin(theta)>0 else .0060)*sin(theta)
    C.v[last[j]]=(x,facial_y(x,z),z)
for width,upper,lower,projection,region in [
    (.029,.0037,.0044,-.0005,'lip_border'),
    (.0275,.0020,.0024,-.0018,'lip_vermilion'),
    (.027,.00008,.00008,-.0001,'lip_contact'),
    (.0265,.00008,.00008,.0050,'mouth_cavity'),
]:
    loop=[]
    for theta in mouth_angles:
        x=width*cos(theta)
        line=1.625-.0018*(abs(x)/.031)**2
        cupid=(.0006 if upper>.001 else 0)*exp(-((abs(x)-.008)/.005)**2)*max(0,sin(theta))
        z=line+(upper if sin(theta)>0 else lower)*sin(theta)+cupid
        y=facial_y(x,z)+projection*abs(sin(theta))**.5
        loop.append(C.vertex((x,y,z),{'head':1},region))
    C.connect(last,loop,mouth_patch)
    last=loop
C.cap(last,(0,facial_y(0,1.625)+.008,1.625),{'head':1},'mouth_cavity',mouth_patch)
C.seam_loop(mouth_opening)
faces=[f for f in C.f if f is not None]''')
replace('    tint.data[i].color=(r,g,b,1.0)', '''    if C.region[i]=='lip_vermilion' or C.region[i]=='lip_border':
        r,g,b=.90,.65,.66
    if C.region[i]=='mouth_cavity':
        r,g,b=.10,.045,.045
    tint.data[i].color=(r,g,b,1.0)''')
replace('bm.to_mesh(mesh);bm.free()\nfor p in mesh.polygons:p.use_smooth=True', '''# Relax the skin-to-lip socket, not the vermilion or the contact seam.
mouth_transition=[v for v in bm.verts if C.region[v.index] in ('head','lip_border') and abs(v.co.x)<.041 and v.co.y<-.035 and 1.610<v.co.z<1.642]
for _ in range(8):
    bmesh.ops.smooth_vert(bm,verts=mouth_transition,factor=.35,use_axis_x=True,use_axis_y=True,use_axis_z=True)
hand_domains=('palm.','thumb.','finger_','forefoot.','toe','foot.')
web=set()
for edge in bm.edges:
    a,b=edge.verts
    ra,rb=C.region[a.index],C.region[b.index]
    if ra!=rb and ra.startswith(hand_domains) and rb.startswith(hand_domains):
        web.update((a,b))
for _ in range(3):
    web.update(other for v in list(web) for edge in v.link_edges for other in edge.verts if C.region[other.index].startswith(hand_domains))
for _ in range(8):
    bmesh.ops.smooth_vert(bm,verts=list(web),factor=.35,use_axis_x=True,use_axis_y=True,use_axis_z=True)
bm.to_mesh(mesh);bm.free()
for p in mesh.polygons:p.use_smooth=True''')
# The jaw must overhang the throat rather than tapering the entire lower skull.
start = text.index('head_profile=[')
end = text.index('\nhead_z=[]', start)
head = '''head_profile=[
(1.525,.045,.039,.047,.018),(1.540,.042,.043,.052,.017),
(1.555,.043,.052,.062,.013),(1.565,.045,.066,.070,.008),
(1.575,.048,.079,.075,.004),(1.585,.052,.086,.080,.002),
(1.595,.058,.087,.084,.002),(1.610,.064,.085,.091,.002),
(1.625,.068,.083,.096,.002),(1.645,.072,.084,.099,.002),
(1.665,.075,.083,.102,.002),(1.685,.075,.079,.103,.002),
(1.705,.074,.077,.102,.002),(1.730,.072,.074,.098,.002),
(1.750,.068,.069,.093,.002),(1.770,.063,.062,.084,.002),
(1.788,.054,.051,.069,.002),(1.798,.038,.035,.047,.002),
(1.802,.012,.011,.015,.002)]'''
changes.append({'stage': 'replace cranio-cervical control profile'})
text = text[:start] + head + text[end:]
replace('p.x*=1-.38*k', 'p.x*=1-.08*k')
replace('(1.49,.092,.059,.011)', '(1.49,.067,.053,.014)')
replace('(1.50,.078,.057,.011)', '(1.50,.055,.049,.016)')
replace('(1.51,.068,.055,.010)', '(1.51,.048,.046,.017)')
replace('(1.52,.061,.054,.009)', '(1.52,.045,.045,.018)')
replace('(1.525,.058,.053,.009)', '(1.525,.045,.045,.018)')

# Half-rotation hip bones preserve volume with ordinary glTF linear skinning.
replace("bpy.ops.object.mode_set(mode='OBJECT')\nfrom mathutils import Quaternion", '''for sf in ['L','R']:
    for label, child in [('hip_support','thigh'),('knee_support','shin'),('elbow_support','forearm')]:
        source = rig.data.edit_bones[child+'.'+sf]
        support = rig.data.edit_bones.new(label+'.'+sf)
        support.head = source.head
        support.tail = source.tail
        support.roll = source.roll
        support.parent = source.parent
bpy.ops.object.mode_set(mode='OBJECT')
from mathutils import Quaternion''')
replace("scene.frame_set(1)\n\ndef smooth", '''for frame in range(1,81):
    scene.frame_set(frame)
    for sf in ['L','R']:
        for label, child in [('hip_support','thigh'),('knee_support','shin'),('elbow_support','forearm')]:
            support = rig.pose.bones[label+'.'+sf]
            support.rotation_mode = 'QUATERNION'
            support.rotation_quaternion = Quaternion((1,0,0,0)).slerp(rig.pose.bones[child+'.'+sf].rotation_quaternion,.5)
            support.keyframe_insert('rotation_quaternion',frame=frame,group=support.name)
scene.frame_set(1)

def smooth''')
replace("groups={name:body.vertex_groups.new(name=name) for name in sorted({n for weights in C.w for n in weights})}", '''for i,w in enumerate(C.w):
    p = mesh.vertices[i].co
    for sf in ['L','R']:
        thigh = 'thigh.'+sf
        if .70 < p.z < 1.01 and w.get(thigh,0) > 0:
            if (p.x>=0) != (sf=='L'):
                w['pelvis']=w.get('pelvis',0)+w[thigh]
                w[thigh]=0
                continue
            total = w.get(thigh,0)+w.get('pelvis',0)
            fraction = (1-sstep(p.z,.70,.99))*(1-(1-sstep(abs(p.x),.004,.055))*sstep(p.z,.70,.84))
            w[thigh] = total*fraction
            w['pelvis'] = total*(1-fraction)
# Weight diffusion uses physical edge lengths, so tiny socket edges cannot
# sustain the same jump as a centimetre-long longitudinal edge.
coords = [v.co.copy() for v in mesh.vertices]
conductance = [[(j,1/max((coords[i]-coords[j]).length_squared,1e-8)) for j in ns] for i,ns in enumerate(adj)]
for i,w in enumerate(C.w):
    total=sum(w.values())
    C.w[i]={k:v/total for k,v in w.items() if v>1e-10}
for iteration in range(60):
    updates={}
    for i,ns in enumerate(conductance):
        p=coords[i]
        mask=bandv(abs(p.x),.10,.16,.29,.38)*bandv(p.z,1.15,1.23,1.40,1.48)
        if mask<=0 or not ns: continue
        denominator=sum(c for _,c in ns)
        acc={}
        for j,c in ns:
            for bone,value in C.w[j].items():
                acc[bone]=acc.get(bone,0)+value*c/denominator
        alpha=.65*mask
        updates[i]={bone:(1-alpha)*C.w[i].get(bone,0)+alpha*acc.get(bone,0) for bone in set(acc)|set(C.w[i])}
    for i,w in updates.items(): C.w[i]=w
for i,w in enumerate(C.w):
    p=coords[i]
    for sf in ['L','R']:
        pairs=[]
        if .70<p.z<1.01 and (p.x>=0)==(sf=='L'): pairs.append(('pelvis','thigh.'+sf,'hip_support.'+sf))
        if .35<p.z<.66: pairs.append(('thigh.'+sf,'shin.'+sf,'knee_support.'+sf))
        if .24<abs(p.x)<.51 and 1.06<p.z<1.34: pairs.append(('upper_arm.'+sf,'forearm.'+sf,'elbow_support.'+sf))
        for parent,child,label in pairs:
            a,b=w.get(parent,0),w.get(child,0)
            total=a+b
            if a<=1e-7 or b<=1e-7: continue
            f=b/total
            w[label]=2*min(f,1-f)*total
            w[child]=max(0,2*f-1)*total
            w[parent]=max(0,1-2*f)*total
groups={name:body.vertex_groups.new(name=name) for name in sorted({n for weights in C.w for n in weights})}''')

# Preserve source vertex positions as delivered after geometric relaxation.
replace('q=Vector(C.v[i]);ax=abs(q.x);z=q.z;', 'q=mesh.vertices[i].co;ax=abs(q.x);z=q.z;')
replace('for _ in range(8):\n    for i,d in umbrella', 'for _ in range(32):\n    for i,d in umbrella')
replace('(1.788,.054,.051,.069,.002),(1.798,.038,.035,.047,.002),\n(1.802,.012,.011,.015,.002)',
        '(1.788,.044,.040,.049,.002),(1.798,.022,.020,.026,.002),\n(1.802,.004,.004,.005,.002)')
text = text.split('# --- Preview renders on the saved source')[0]
text = text.replace("if os.environ.get('FE_SKIP_RENDER')=='1':raise SystemExit", '')
OUTPUT.mkdir(parents=True, exist_ok=True)
(OUTPUT / 'author-transformations.json').write_text(json.dumps({
    'source': str(SOURCE.relative_to(ROOT)),
    'sourceSha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    'changes': changes,
}, indent=2))
authored = {'__file__': str(SOURCE), '__name__': '__main__'}
exec(compile(text, str(SOURCE), 'exec'), authored)

import bpy
from mathutils import Vector, Quaternion
import math
scene=bpy.context.scene
body=bpy.data.objects['FINAL_Body_Connected']
rig=bpy.data.objects['FINAL_Rig']
# Give each authored digit its own phalanges, rather than exporting rigid fingers.
region_names=authored['region_names']
regions=body.data.attributes['Anatomical_region_id'].data
digit_data=[]
for sf,s in [('L',1),('R',-1)]:
    for digit in ['thumb','index','middle','ring','little']:
        region=('thumb.' if digit=='thumb' else 'finger_'+digit+'.')+sf
        indices=[i for i,a in enumerate(regions) if region_names[a.value]==region]
        points=[body.data.vertices[i].co.copy() for i in indices]
        # Bind by the authored curve parameter, not projection onto a straight
        # axis: opposition makes the thumb double back along that projection.
        projections=[authored['C'].digit_progress[i] for i in indices]
        low,high=0.0,1.0
        span=high-low
        centers=[]
        for f in [0,.34,.68,1]:
            nearest=sorted(range(len(points)),key=lambda j:abs((projections[j]-low)/span-f))[:max(4,len(points)//20)]
            centers.append(sum((points[j] for j in nearest),Vector())/len(nearest))
        digit_data.append((sf,s,digit,indices,projections,low,span,centers))
print('SOL_DIGIT_CENTERS',len(digit_data),flush=True)
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.object.mode_set(mode='EDIT')
for sf,s,digit,indices,projections,low,span,centers in digit_data:
    parent=rig.data.edit_bones['hand.'+sf]
    for j in range(3):
        bone=rig.data.edit_bones.new(f'{digit}_{j+1:02d}.{sf}')
        bone.head=centers[j]
        bone.tail=centers[j+1]
        bone.parent=parent
        bone.use_connect=j>0
        parent=bone
bpy.ops.object.mode_set(mode='OBJECT')
print('SOL_DIGIT_BONES',len(rig.data.bones),flush=True)
for sf,s,digit,indices,projections,low,span,centers in digit_data:
    print('SOL_DIGIT_WEIGHTS',sf,digit,flush=True)
    for existing in body.vertex_groups:
        existing.remove(indices)
    groups=[body.vertex_groups.new(name=f'{digit}_{j+1:02d}.{sf}') for j in range(3)]
    hand=body.vertex_groups['hand.'+sf]
    for index,projection in zip(indices,projections):
        t=(projection-low)/span
        if t<.24:
            f=authored['smooth'](0,.24,t)
            hand.add([index],1-f,'REPLACE')
            groups[0].add([index],f,'REPLACE')
        else:
            second=authored['smooth'](.22,.46,t)
            third=authored['smooth'](.56,.80,t)
            for group,value in zip(groups,[1-second,second*(1-third),third]):
                if value>0: group.add([index],value,'REPLACE')
    nail=bpy.data.objects.get(f'GEO_Nail_hand_{digit}.{sf}')
    if nail:
        nail.vertex_groups.clear()
        nail.vertex_groups.new(name=f'{digit}_03.{sf}').add(list(range(len(nail.data.vertices))),1,'REPLACE')
    for j in range(3):
        bone=rig.pose.bones[f'{digit}_{j+1:02d}.{sf}']
        bone.rotation_mode='QUATERNION'
        for frame in [1,80]:
            bone.rotation_quaternion=Quaternion((1,0,0,0))
            bone.keyframe_insert('rotation_quaternion',frame=frame,group=bone.name)
# Elevation cannot be performed by the humerus alone: share its world-space
# swing with the shoulder girdle while retaining the requested arm orientation.
arm_samples={}
for frame in range(1,81):
    scene.frame_set(frame)
    arm_samples[frame]={sf:rig.pose.bones['upper_arm.'+sf].rotation_quaternion.copy() for sf in ['L','R']}
for frame in range(1,81):
    scene.frame_set(frame)
    for sf,s in [('L',1),('R',-1)]:
        upper=rig.pose.bones['upper_arm.'+sf]
        clavicle=rig.pose.bones['clavicle.'+sf]
        original=arm_samples[frame][sf]
        axis,angle=original.to_axis_angle()
        world_axis=upper.bone.matrix_local.to_3x3() @ axis
        elevation=max(0,-s*world_axis.y*angle)
        shared=min(math.radians(30),max(0,elevation-math.radians(45))*.4)
        clavicle.rotation_mode='QUATERNION'
        local_axis=clavicle.bone.matrix_local.to_3x3().inverted() @ Vector((0,1,0))
        clavicle.rotation_quaternion=Quaternion(local_axis,-s*shared)
        relative=clavicle.bone.matrix_local.to_quaternion().inverted() @ upper.bone.matrix_local.to_quaternion()
        upper.rotation_quaternion=relative.inverted() @ clavicle.rotation_quaternion.inverted() @ relative @ original
        support=rig.pose.bones['axilla_support.'+sf]
        support.rotation_quaternion=Quaternion((1,0,0,0)).slerp(upper.rotation_quaternion,.5)
        for bone in [upper,clavicle,support]:
            bone.keyframe_insert('rotation_quaternion',frame=frame,group=bone.name)
# The additional grip is a validation pose, explicitly not a gameplay animation.
for frame,curl in [(100,0),(120,1),(140,0)]:
    scene.frame_set(frame)
    for bone in rig.pose.bones:
        bone.rotation_mode='QUATERNION'
        bone.rotation_quaternion=Quaternion((1,0,0,0))
        bone.location=(0,0,0)
    for sf,s,digit,indices,projections,low,span,centers in digit_data:
        for j,degrees in enumerate([25,32,18]):
            bone=rig.pose.bones[f'{digit}_{j+1:02d}.{sf}']
            if digit=='thumb':
                target=rig.data.bones['hand.'+sf].head_local+Vector((s*.69,0,-.724)).normalized()*.10+Vector((0,-.020,0))
                forward=(centers[1]-centers[0]).normalized()
                desired=(target-centers[0]).normalized()
                world_axis=forward.cross(desired).normalized()
                axis=bone.bone.matrix_local.to_3x3().inverted() @ world_axis
                bone.rotation_quaternion=Quaternion(axis,math.radians([38,15,8][j])*curl)
            else:
                axis=bone.bone.matrix_local.to_3x3().inverted() @ Vector((s*.724,0,.69)).normalized()
                bone.rotation_quaternion=Quaternion(axis,-s*math.radians(degrees)*curl)
    for bone in rig.pose.bones:
        bone.keyframe_insert('rotation_quaternion',frame=frame,group=bone.name)
        bone.keyframe_insert('location',frame=frame,group=bone.name)
rig.animation_data.action.name='PoseValidation_NotLocomotion'
print('SOL_DIGIT_ANIMATION_COMPLETE',flush=True)
additional_poses=[]
def rotate_world_axis(name,axis,degrees):
    bone=rig.pose.bones[name]
    local=bone.bone.matrix_local.to_3x3().inverted() @ Vector(axis)
    bone.rotation_quaternion=Quaternion(local,math.radians(degrees))
for frame,kind in [(160,'foot_planted_squat'),(180,'torso_twist'),(190,'right_hip_flex'),(200,'neutral')]:
    scene.frame_set(frame)
    for bone in rig.pose.bones:
        bone.rotation_quaternion=Quaternion((1,0,0,0))
        bone.location=(0,0,0)
    if kind=='foot_planted_squat':
        for sf in ['L','R']:
            thigh=rig.data.bones['thigh.'+sf]
            shin=rig.data.bones['shin.'+sf]
            angle=math.radians(50)
            beta=math.asin(min(.99,thigh.length/shin.length*math.sin(angle)))
            rotate_world_axis('thigh.'+sf,(1,0,0),-50)
            rotate_world_axis('shin.'+sf,(1,0,0),50+math.degrees(beta))
            rotate_world_axis('foot.'+sf,(1,0,0),-math.degrees(beta))
    elif kind=='torso_twist':
        rotate_world_axis('chest',(0,0,1),25)
    elif kind=='right_hip_flex':
        rotate_world_axis('thigh.R',(1,0,0),-70)
        rotate_world_axis('shin.R',(1,0,0),110)
    for sf in ['L','R']:
        for label,child in [('hip_support','thigh'),('knee_support','shin'),('elbow_support','forearm'),('axilla_support','upper_arm')]:
            rig.pose.bones[label+'.'+sf].rotation_quaternion=Quaternion((1,0,0,0)).slerp(rig.pose.bones[child+'.'+sf].rotation_quaternion,.5)
    bpy.context.view_layer.update()
    if kind=='foot_planted_squat':
        target=sum((rig.data.bones['foot.'+sf].head_local for sf in ['L','R']),Vector())*.5
        actual=sum((rig.pose.bones['foot.'+sf].head for sf in ['L','R']),Vector())*.5
        root=rig.pose.bones['root']
        root.location=root.bone.matrix_local.to_3x3().inverted() @ (target-actual)
        bpy.context.view_layer.update()
        additional_poses.append({'frame':frame,'name':kind,'ankleContactErrorsMm':{
            sf:(rig.pose.bones['foot.'+sf].head-rig.data.bones['foot.'+sf].head_local).length*1000 for sf in ['L','R']}})
    else:
        additional_poses.append({'frame':frame,'name':kind})
    for bone in rig.pose.bones:
        bone.keyframe_insert('rotation_quaternion',frame=frame,group=bone.name)
        bone.keyframe_insert('location',frame=frame,group=bone.name)
scene.frame_end=200
# Smooth the coarse hair shell as geometry, rather than disguising its facets.
for obj in scene.objects:
    if obj.name.startswith('GEO_Brow.') or obj.name=='GEO_Hair':
        bpy.ops.object.select_all(action='DESELECT')
        obj.hide_set(False)
        obj.select_set(True)
        bpy.context.view_layer.objects.active=obj
        sub=obj.modifiers.new('Authored shell continuity','SUBSURF')
        sub.levels=1
        bpy.ops.object.modifier_move_up(modifier=sub.name)
        bpy.ops.object.modifier_apply(modifier=sub.name)
meta=json.loads((OUTPUT/'final-body-authoring.json').read_text())
meta['rigContract']={'bones':len(rig.data.bones), 'names':[b.name for b in rig.data.bones],
    'supportRule':'Hip, knee, elbow and axilla supports follow half the local child quaternion. Baked on all 80 frames of the pose-test action. Runtime motion generation must apply the same rule; these are not arbitrary independent sporting motions.'}
meta['weights']='Physical-edge conductance relaxation at axilla, broad medial hip transition, half-rotation joint supports; top four normalized.'
meta['handArticulation']='Three phalanges per finger and thumb, independent vertex weights; validation grip at frame 120, neutral hands at 100 and 140.'
meta['shoulderRhythm']='Arm elevation above 45 degrees shares 40% of its additional swing with the clavicle, capped at 30 degrees. Upper-arm local rotation compensates to retain its requested world orientation. All channels baked in the validation action.'
meta['additionalPoseChecks']=additional_poses
(OUTPUT/'final-body-authoring.json').write_text(json.dumps(meta,indent=2))
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT/'final-body-source.blend'))
