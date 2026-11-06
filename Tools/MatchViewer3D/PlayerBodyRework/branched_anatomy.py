"""Editable anatomical plane study on the independently authored Skin cage."""
import bpy,bmesh,json,os,math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'Reconstruction-Study/Branched-Cage/Anatomy-Refined';OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT.parent/'branched-cage-editable.blend'))
scene=bpy.context.scene;coarse=bpy.data.objects['B_new_original_skin_cage'];graph=bpy.data.objects['B_source_branch_graph']
refined=coarse.copy();refined.data=coarse.data.copy();scene.collection.objects.link(refined);refined.name='Anatomy_refined_control_cage'
coarse.hide_render=True;coarse.hide_set(True);graph.hide_render=True;graph.hide_set(True)
bpy.ops.object.select_all(action='DESELECT');refined.select_set(True);bpy.context.view_layer.objects.active=refined
mod=next(m for m in refined.modifiers if m.type=='SUBSURF');mod.levels=1;mod.render_levels=1
bpy.ops.object.modifier_apply(modifier=mod.name)
mesh=refined.data
base=[v.co.copy() for v in mesh.vertices]
def tent(v,a,b,c,d):return max(0,min(1,(v-a)/(b-a),(d-v)/(d-c)))
def region(x,y,z,xc,xw,yc,yw,zc,zw):
    xweight=max(0,min(1,(xw-abs(abs(x)-xc))/(xw*.7)))
    return xweight * tent(y,yc-yw,yc-yw*.3,yc+yw*.3,yc+yw) * tent(z,zc-zw,zc-zw*.3,zc+zw*.3,zc+zw)
specs=[
 ('sternum',(0,.07,-.10,.07,1.32,.08),(0,-.009,0)),
 ('clavicle',(.09,.085,-.08,.08,1.395,.065),(0,-.012,.006)),
 ('acromion_deltoid',(.22,.10,0,.10,1.36,.09),(0,-.006,.009)),
 ('costal_margin',(.08,.08,-.105,.09,1.205,.09),(0,-.010,-.004)),
 ('iliac_crest_ASIS',(.13,.08,-.065,.10,.97,.105),(0,-.010,.008)),
 ('sacrum',(0,.08,.09,.09,.975,.10),(0,.008,0)),
 ('gluteal_transition',(.10,.08,.105,.10,.875,.11),(0,.020,0)),
 ('scapular_plane',(.10,.09,.105,.10,1.32,.10),(0,.012,.002)),
 ('lower_neck',(.045,.04,0,.09,1.465,.08),(0,0,-.006))]
groups={name:refined.vertex_groups.new(name=name) for name,*_ in specs}
offsets=[Vector() for v in mesh.vertices];membership={}
for name,params,delta in specs:
    ids=[]
    for v in mesh.vertices:
        weight=region(*v.co,*params)
        if weight>0:
            groups[name].add([v.index],weight,'REPLACE');offsets[v.index]+=Vector(delta)*weight;ids.append(v.index)
    membership[name]=len(ids)
    if not ids:raise RuntimeError('Empty editable landmark group '+name)
for v in mesh.vertices:v.co+=offsets[v.index]
sub=refined.modifiers.new('Presentation subdivision','SUBSURF');sub.levels=1;sub.render_levels=1

# Independent partial-body context retains original head, lower arms and legs.
reference=bpy.data.objects['A_previous_body_reference'];context=reference.copy();context.data=reference.data.copy();scene.collection.objects.link(context);context.name='Separate_head_arms_legs_context';context.hide_render=False;context.hide_set(False)
names=json.loads((BASE/'Iteration2/cage-construction.json').read_text())['regionNames']
regions=[names[x.value] for x in context.data.attributes['Anatomical_region_id'].data]
bm=bmesh.new();bm.from_mesh(context.data);bm.verts.ensure_lookup_table()
def keep(v):
    r=regions[v.index];p=v.co
    return r.startswith(('head','ear.','eyelid','orbital')) or (r.startswith('leg.') and p.z<.70) or (r.startswith('arm.') and abs(p.x)>.325) or r.startswith(('palm.','thumb.','finger_','foot.','heel.','toe','forefoot'))
faces=[f for f in bm.faces if not all(keep(v) for v in f.verts)]
bmesh.ops.delete(bm,geom=faces,context='FACES')
unused=[v for v in bm.verts if not v.link_faces]
if unused:bmesh.ops.delete(bm,geom=unused,context='VERTS')
bm.to_mesh(context.data);bm.free()
for o in scene.objects:
    if o.name.startswith('B_connected_') and o.name!='B_connected_1':o.hide_render=False;o.hide_set(False)
reference.hide_render=True;reference.hide_set(True)
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=16;scene.cycles.use_denoising=True
scene.render.threads_mode='FIXED';scene.render.threads=6;scene.render.resolution_x=640;scene.render.resolution_y=800
scene.frame_set(1)
refine=os.environ.get('FE_REFINE')=='1'
if refine:
    # After context preview: narrow excessive upper silhouette and support
    # pelvis rear to make it read as a tilted volume rather than a vertical tube.
    for v in mesh.vertices:
        p=v.co
        if p.z>1.49:p.z+=.024
        if p.z<.76:p.z-=.035
        if abs(p.x)>.29 and 1.18<p.z<1.43:p.x+=.025 if p.x>0 else -.025
        if 1.32<p.z<1.44 and abs(p.x)>.16:p.x*=.96
        if .82<p.z<.95 and p.y>.04:p.y+=.012
        if 1.13<p.z<1.25 and p.y<-.04:p.y+=.009
        if 1.26<p.z<1.36 and p.y<-.04 and abs(p.x)<.14:p.y-=.010
        if 1.27<p.z<1.38 and p.y>.04 and .05<abs(p.x)<.16:p.y+=.008
    mesh.update()
for o in [refined,context]:o.hide_render=False;o.hide_set(False)
scene.camera.location=(3,-5,1.5);scene.camera.rotation_euler=(Vector((0,0,.90))-scene.camera.location).to_track_quat('-Z','Y').to_euler();scene.camera.data.ortho_scale=2.02
outpath=OUT/('anatomy-refined.blend' if refine else 'anatomy-preview.blend')
bpy.ops.wm.save_as_mainfile(filepath=str(outpath))
(OUT/('final-edit-audit.json' if refine else 'preview-edit-audit.json')).write_text(json.dumps({'source':'branched-cage-editable.blend','controlVertices':len(mesh.vertices),'controlFaces':len(mesh.polygons),'landmarkGroups':membership,'context':'Separate old head and distal limbs; cut faces are open boundaries, not welded or rigged.','subdivisionAppliedForControl':1,'presentationSubdivision':1},indent=2))
shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02)]
for name,pos,target,scale in shots:
    c=scene.camera;c.location=pos;c.rotation_euler=(Vector(target)-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/(('final-' if refine else 'preview-')+name+'.png'));bpy.ops.render.render(write_still=True)
print('ANATOMY_CONTROL',len(mesh.vertices),membership,flush=True)
