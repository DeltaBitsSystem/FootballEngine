"""Local native lattice form study on the connected, weighted source body."""
from pathlib import Path
import json,math,hashlib
import bpy

ROOT=Path(__file__).resolve().parents[5]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
OUT=BASE/'Landmark-Cage'
SOURCE=BASE/'Contour-Study/contour-form.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene=bpy.context.scene;scene.frame_set(1)
body=bpy.data.objects['FINAL_Body_Connected']
names=json.loads((BASE/'final-body-authoring.json').read_text())['regionNames']
region=body.data.attributes['Anatomical_region_id'].data
def smooth(x):
    x=max(0,min(1,x));return x*x*(3-2*x)
mask=body.vertex_groups.new(name='Cage_form_review_only')
for v in body.data.vertices:
    if names[region[v.index].value]!='head':continue
    z=v.co.z
    w=smooth((z-1.515)/.020)*(1-smooth((z-1.665)/.035))
    if w>.00001:mask.add([v.index],w,'REPLACE')
data=bpy.data.lattices.new('Cervicomandibular control cage')
data.points_u=5;data.points_v=5;data.points_w=9
for attr in ('interpolation_type_u','interpolation_type_v','interpolation_type_w'):
    prop=data.bl_rna.properties[attr]
    choice=next(i.identifier for i in prop.enum_items if 'BSPLINE' in i.identifier)
    setattr(data,attr,choice)
cage=bpy.data.objects.new('Cervicomandibular form cage',data)
scene.collection.objects.link(cage)
cage.location=(0,-.01,1.61)
cage.scale=(.055,.055,.025)
# Index order is U fastest. Displacements are physical metres, then expressed
# in lattice space. A few control rows carry whole volumes, not surface marks.
edits=[]
for w in range(9):
    for v in range(5):
        for u in range(5):
            p=data.points[u+5*v+25*w]
            dx=dy=dz=0.
            # Recede the throat behind the chin, with no edit to the nape.
            if w==1 and v==1:dy=.018
            if w==2 and v==1:dy=.026
            if w==3 and v==1:dy=-.010
            # Broaden the lateral lower ramus near the ear, not the chin.
            if w==4 and v==2 and u in (1,3):dx=.020*(1 if u==3 else -1);dz=-.005
            if w==5 and v==2 and u in (1,3):dx=.010*(1 if u==3 else -1)
            if dx or dy or dz:
                p.co_deform.x+=dx/cage.scale.x;p.co_deform.y+=dy/cage.scale.y;p.co_deform.z+=dz/cage.scale.z
                edits.append({'u':u,'v':v,'w':w,'deltaM':[dx,dy,dz]})
mod=body.modifiers.new('Cervicomandibular cage before skin','LATTICE')
mod.object=cage;mod.vertex_group=mask.name
bpy.context.view_layer.objects.active=body
while body.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
cage.hide_render=True
scene.frame_set(1)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'cage-form.blend'))
meta={'source':str(SOURCE),'sourceSha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
      'cageLocationM':list(cage.location),'cageScaleM':list(cage.scale),'controlGrid':[5,5,9],
      'controlEdits':edits,'landmarksApproxM':{'menton':[0,-.083,1.584],
        'gonion_left':[.057,-.006,1.612],'ear_bounds_y':[-.011,.023],
        'ear_bounds_z':[1.642,1.702],'mastoid_left':[.065,.032,1.634],
        'neck_base_center':[0,.018,1.525]},
      'landmarkCaveat':'Approximate artistic targets inferred from this stylized mesh, not anatomical measurements or universal coordinates.',
      'status':'Unapplied lattice form experiment; weights, UV, bakes and LODs are not validated for it.'}
(OUT/'cage-parameters.json').write_text(json.dumps(meta,indent=2))
print('CAGE_SAVED',json.dumps(meta),flush=True)
