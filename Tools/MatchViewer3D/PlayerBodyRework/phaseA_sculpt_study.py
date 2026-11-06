"""Art-directed clay study using native Lattice, proportional editing and sculpt brushes.

Run in a NEW no-focus Blender UI process, never in the user's scene. This is not
a human generator: it edits our existing cage, with recorded anatomical decisions.
No remeshing, automatic weighting, UV bake, LOD or GLB export is performed here.
"""
import bpy
import math
import json
import shutil
from pathlib import Path
from mathutils import Vector
from bpy_extras.view3d_utils import location_3d_to_region_2d

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'PhaseA-Sculpt'/'Trial02';OUT.mkdir(parents=True,exist_ok=True)
(OUT/'evidence').mkdir(exist_ok=True)
state={'audit':[],'job':0}

def grey(name,value,rough=.70):
    m=bpy.data.materials.new(name);m.use_nodes=True
    node=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    node.inputs['Base Color'].default_value=(value,value,value,1)
    node.inputs['Roughness'].default_value=rough
    node.inputs['Specular IOR Level'].default_value=.25
    return m

def copy_frozen(o,name,mat):
    n=o.copy();n.data=o.data.copy();bpy.context.scene.collection.objects.link(n)
    n.name=name;n.parent=None;n.matrix_world=o.matrix_world.copy()
    n.modifiers.clear();n.hide_render=False;n.hide_set(False)
    n.data.materials.clear();n.data.materials.append(mat)
    for p in n.data.polygons:p.material_index=0
    return n

def native_lattice(name,center,scale,resolution,edit,objects,group=None):
    data=bpy.data.lattices.new(name);data.points_u,data.points_v,data.points_w=resolution
    for prop in ['interpolation_type_u','interpolation_type_v','interpolation_type_w']:
        available=[i.identifier for i in data.bl_rna.properties[prop].enum_items]
        assert 'KEY_BSPLINE' in available;setattr(data,prop,'KEY_BSPLINE')
    obj=bpy.data.objects.new(name,data);bpy.context.scene.collection.objects.link(obj)
    obj.location=center;obj.scale=scale;obj.hide_render=True
    nu,nv,nw=resolution
    for w in range(nw):
        for v in range(nv):
            for u in range(nu):
                p=data.points[u+nu*(v+nv*w)]
                delta=Vector(edit(u,v,w))
                p.co_deform+=Vector((delta.x/scale[0],delta.y/scale[1],delta.z/scale[2]))
    for target in objects:
        bpy.context.view_layer.update()
        target.hide_viewport=False;target.hide_set(False);target.hide_select=False
        bpy.ops.object.select_all(action='DESELECT');target.select_set(True);bpy.context.view_layer.objects.active=target
        mod=target.modifiers.new(name,'LATTICE');mod.object=obj
        if group and target.vertex_groups.get(group):mod.vertex_group=group
        bpy.context.view_layer.update()
        print('LATTICE_TARGET',target.name,bpy.context.mode,target.visible_get(),target.hide_get(),bpy.ops.object.modifier_apply.poll(),flush=True)
        with bpy.context.temp_override(object=target,active_object=target,selected_objects=[target],selected_editable_objects=[target]):
            bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.hide_set(True)
    state['audit'].append({'tool':'Native Lattice / B-Spline','name':name,'resolution':resolution,'center':center,'scale':scale,'controlDisplacementsMeters':[{'u':u,'v':v,'w':w,'delta':list(edit(u,v,w))} for w in range(nw) for v in range(nv) for u in range(nu) if Vector(edit(u,v,w)).length>0]})

def set_mask(prefixes):
    mesh=state['body'].data
    attr=mesh.attributes.get('.sculpt_mask') or mesh.attributes.new(name='.sculpt_mask',type='FLOAT',domain='POINT')
    for i,d in enumerate(attr.data):d.value=0 if prefixes is None or any(state['regions'][i].startswith(p) for p in prefixes) else 1
    mesh.update()

def activate_brush(name):
    bpy.ops.brush.asset_activate(asset_library_type='ESSENTIALS',relative_asset_identifier='brushes/essentials_brushes-mesh_sculpt.blend/Brush/'+name)
    brush=bpy.context.tool_settings.sculpt.brush
    brush.use_pressure_size=False;brush.use_pressure_strength=False
    sizes=[i.identifier for i in brush.bl_rna.properties['use_locked_size'].enum_items]
    assert 'SCENE' in sizes;brush.use_locked_size='SCENE'
    return brush

def sculpt(description,tool,p,q,radius,strength,mask=None,repeats=1):
    body=state['body'];area=state['area'];region=next(r for r in area.regions if r.type=='WINDOW');rv=area.spaces.active.region_3d
    set_mask(mask)
    brush=activate_brush(tool);brush.strength=strength
    p,q=Vector(p),Vector(q);delta=q-p;hint=p.copy()
    if state['view']=='RIGHT' and abs(p.x)<.003:p.x=.004
    candidates=[v for v in body.data.vertices if mask is None or any(state['regions'][v.index].startswith(prefix) for prefix in mask)]
    p=min(candidates,key=lambda v:(v.co-p).length_squared).co.copy();q=p+delta
    a=location_3d_to_region_2d(region,rv,p);b=location_3d_to_region_2d(region,rv,q)
    axis=Vector((1,0,0)) if state['view']!='RIGHT' else Vector((0,1,0))
    edge=location_3d_to_region_2d(region,rv,p+axis*radius)
    size=max(2,round((edge-a).length));brush.size=size;brush.unprojected_size=radius
    if not (-size<a.x<region.width+size and -size<a.y<region.height+size):raise RuntimeError('Sculpt target outside its own prepared viewport: '+description)
    before=[v.co.copy() for v in body.data.vertices]
    for repeat in range(repeats):
        stroke=[]
        steps=12 if tool in ['Grab','Elastic Grab'] else max(2,min(12,math.ceil(delta.length/max(.001,radius*.25))+1))
        for i in range(steps):
            t=i/(steps-1);mouse=a.lerp(b,t);location=p.lerp(q,t)
            stroke.append({'name':description,'location':tuple(location),'mouse':tuple(mouse),'mouse_event':tuple(mouse),'pressure':1,'size':size,'time':i*.035,'is_start':i==0,'x_tilt':0,'y_tilt':0})
        result=bpy.ops.sculpt.brush_stroke(stroke=stroke,override_location=True)
        if result!={'FINISHED'}:raise RuntimeError('Native sculpt stroke failed: '+description)
    after=[v.co.copy() for v in body.data.vertices]
    movements=[(a-b).length for a,b in zip(before,after)]
    state['audit'].append({'tool':'Native Sculpt / '+tool,'decision':description,'view':state['view'],'requestedLandmarkMeters':list(hint),'resolvedSurfaceStartMeters':list(p),'endMeters':list(q),'radiusMeters':radius,'sizeMode':brush.use_locked_size,'strength':strength,'maskPrefixes':mask,'repeats':repeats,'maxMovementMeters':max(movements),'changedVertices':sum(d>1e-7 for d in movements)})

JOBS=[
 ('RIGHT',(0,0,1.66),.46,[
  ('Retract the nasal wedge; keep the maxilla in front of the cheek plane','Grab',(0,-.103,1.654),(0,-.097,1.654),.023,1,['head']),
  ('Give the mouth barrel real anterior volume, rather than a slit on a flat face','Grab',(0,-.073,1.619),(0,-.079,1.619),.029,1,['head']),
  ('Reduce the projecting mentum and lift its lower edge','Grab',(0,-.078,1.576),(0,-.074,1.578),.026,.9,['head']),
  ('Separate the submental plane from the front of the neck','Grab',(0,-.045,1.546),(0,-.039,1.546),.029,1,None),
  ('Blend the old horizontal mouth ripples before rebuilding the lip masses','Smooth',(.015,-.075,1.618),(.016,-.075,1.618),.021,.20,['head'],2),
  ('Flatten the broad bridge into a nasal bone plane, not a cone','Flatten/Contrast',(0,-.082,1.679),(0,-.083,1.690),.015,.15,['head']),
 ]),
 ('FRONT',(0,0,1.66),.46,[
  ('Place the malar prominence below and outside the orbit','Grab',(.047,-.065,1.666),(.049,-.065,1.668),.027,.85,['head']),
  ('Build the zygomatic to maxillary transition','Clay Strips',(.052,-.065,1.661),(.034,-.071,1.669),.018,.09,['head']),
  ('Build a restrained superior orbital ridge','Clay Strips',(.016,-.068,1.707),(.044,-.066,1.707),.011,.075,['head']),
  ('Blend the glabella into the frontal bone','Smooth',(0,-.070,1.707),(.001,-.070,1.707),.017,.12,['head'],2),
  ('Soften the overly wide mandibular angle without a heroic jaw','Grab',(.058,-.012,1.608),(.055,-.012,1.610),.032,.85,['head']),
  ('Support the lower lip pad at anatomical scale','Clay',(.006,-.078,1.614),(.018,-.076,1.614),.007,.075,['head']),
  ('Bring the earlobe down toward the level of the nasal base','Grab',(.082,.011,1.656),(.082,.011,1.646),.019,.85,['ear.']),
 ]),
 ('FRONT',(0,0,1.22),1.10,[
  ('Remove the angular acromial roof in the existing cage','Smooth',(.218,-.025,1.447),(.22,-.025,1.445),.052,.17,None,2),
  ('Define the clavicular arch without a cylindrical bone','Clay Strips',(.040,-.071,1.456),(.146,-.040,1.452),.011,.075,None),
  ('Give the pectoral plane moderate athletic fullness','Clay Strips',(.066,-.113,1.341),(.129,-.091,1.353),.035,.09,None),
  ('Join anterior deltoid and upper arm volume','Clay',(.213,-.044,1.411),(.243,-.046,1.368),.029,.075,None),
  ('Separate modest biceps volume from the elbow taper','Clay Strips',(.278,-.048,1.310),(.321,-.044,1.264),.025,.065,None),
  ('Support the brachioradialis without adding veins or excessive bulk','Inflate/Deflate',(.442,-.035,1.145),(.444,-.035,1.144),.027,.06,None),
  ('Widen the lower neck to support an adult head','Grab',(.043,-.012,1.505),(.047,-.012,1.505),.027,.85,None),
 ]),
 ('FRONT',(.63,0,.99),.31,[
  ('Integrate the first metacarpal saddle into the palm','Smooth',(.603,-.010,1.035),(.605,-.010,1.034),.020,.14,['thumb.','palm.'],2),
  ('Build a restrained thenar pad, not a separate ball','Clay',(.591,-.018,1.016),(.608,-.017,1.000),.019,.07,['palm.']),
  ('Blend palm and wrist with a narrow transition','Smooth',(.559,-.019,1.041),(.561,-.019,1.039),.024,.13,None,2),
 ]),
 ('FRONT',(.10,0,.48),.85,[
  ('Shape the patellar shield as a small anatomical plane','Clay',(.100,-.058,.499),(.102,-.057,.485),.024,.075,None),
  ('Round the transition from quadriceps into the patellar tendon','Smooth',(.100,-.055,.553),(.100,-.055,.545),.034,.10,None),
  ('Build the lateral malleolus rather than a featureless ankle tube','Grab',(.128,0,.105),(.131,0,.105),.017,.8,None),
  ('Build the medial malleolus lower and slightly smaller','Grab',(.076,-.005,.091),(.074,-.005,.091),.015,.8,None),
 ]),
 ('BACK',(0,0,1.23),1.1,[
  ('Join neck and shoulder through the trapezius, not a flat tent','Clay Strips',(.040,.052,1.490),(.145,.056,1.447),.025,.07,None),
  ('Establish the scapular plane under light athletic soft tissue','Scrape/Fill',(.065,.104,1.382),(.107,.095,1.342),.039,.08,None),
  ('Carry moderate triceps volume into the olecranon taper','Clay Strips',(.277,.049,1.311),(.323,.043,1.263),.025,.065,None),
 ]),
 ('BACK',(.10,0,.37),.70,[
  ('Give the gastrocnemius an athletic but not exaggerated belly','Clay Strips',(.100,.066,.372),(.101,.067,.322),.041,.075,None),
  ('Blend the calf into the Achilles taper','Smooth',(.100,.038,.193),(.100,.037,.181),.032,.10,None),
 ]),
 ('RIGHT',(.10,-.06,.13),.42,[
  ('Reduce the oversized posterior heel bulb','Grab',(.100,.058,.044),(.100,.052,.041),.031,.85,['foot.','heel.']),
  ('Blend the anterior ankle cuff into the instep','Smooth',(.100,-.019,.093),(.100,-.023,.085),.038,.13,None,2),
  ('Lift the medial arch while leaving heel and metatarsal contacts','Grab',(.070,-.060,.019),(.070,-.060,.024),.025,.8,['foot.']),
  ('Blend the toe roots without erasing five toes','Smooth',(.100,-.171,.031),(.101,-.175,.030),.033,.12,['toe','forefoot','foot.'],2),
 ]),
]

def prepare_view():
    if state['job']>=len(JOBS):finish();return None
    try:
        view,center,distance,_=JOBS[state['job']]
        area=state['area'];region=next(r for r in area.regions if r.type=='WINDOW')
        with bpy.context.temp_override(area=area,region=region):
            bpy.ops.view3d.view_axis(type=view,align_active=False)
            rv=area.spaces.active.region_3d;rv.view_location=center;rv.view_distance=distance;rv.view_perspective='ORTHO'
            state['view']=view;area.tag_redraw()
        bpy.app.timers.register(run_job,first_interval=.5)
    except Exception:fail()
    return None

def run_job():
    try:
        area=state['area'];region=next(r for r in area.regions if r.type=='WINDOW')
        with bpy.context.temp_override(area=area,region=region,object=state['body'],active_object=state['body'],selected_objects=[state['body']],selected_editable_objects=[state['body']]):
            for stroke in JOBS[state['job']][3]:sculpt(*stroke)
        state['job']+=1;bpy.app.timers.register(prepare_view,first_interval=.1)
    except Exception:fail()
    return None

def fail():
    import traceback
    (OUT/'native-sculpt-error.txt').write_text(traceback.format_exc())
    (OUT/'native-sculpt-audit-partial.json').write_text(json.dumps(state['audit'],indent=2))
    bpy.ops.wm.quit_blender()

def finish():
    try:
        with bpy.context.temp_override(object=state['body'],active_object=state['body'],selected_objects=[state['body']],selected_editable_objects=[state['body']]):
            bpy.ops.object.mode_set(mode='OBJECT')
        set_mask(None)
        scene=bpy.context.scene;scene.frame_set(1)
        (OUT/'native-sculpt-audit.json').write_text(json.dumps(state['audit'],indent=2))
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-form-study.blend'))
        cam=scene.camera
        shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02),('head-front',(0,-3,1.67),(0,0,1.66),.37),('head-profile',(3,0,1.67),(0,0,1.66),.37),('head-three-quarter',(3,-5,1.7),(0,0,1.665),.37),('hand-closeup',(1,-3,1.10),(.635,0,.97),.30),('feet-closeup',(.7,-1.6,.55),(0,-.07,.105),.48)]
        for side,objects in [('A',state['A']),('B',state['B'])]:
            for obj in state['A']+state['B']:obj.hide_render=obj not in objects
            for name,pos,target,scale in shots:
                cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
                scene.render.filepath=str(OUT/'evidence'/(side+'-'+name+'.png'));bpy.ops.render.render(write_still=True)
        for obj in state['A']:obj.hide_render=True;obj.hide_set(True)
        for obj in state['B']:obj.hide_render=False
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-form-study.blend'))
        (OUT/'phaseA-run-status.json').write_text(json.dumps({'status':'RENDERED_FOR_CRITIQUE_NOT_APPROVED','nativeBrushStrokes':sum(x['tool'].startswith('Native Sculpt') for x in state['audit']),'nativeLattices':sum(x['tool'].startswith('Native Lattice') for x in state['audit']),'bodyVertices':len(state['body'].data.vertices),'bodyPolygons':len(state['body'].data.polygons),'renders':18,'exportsCreated':0},indent=2))
    except Exception:fail();return
    bpy.ops.wm.quit_blender()

def setup():
    try:
        scene=bpy.context.scene;scene.frame_set(1)
        bpy.context.preferences.filepaths.temporary_directory='/tmp/opencode/player-body-isolated-ui/'
        bpy.context.preferences.view.smooth_view=0
        main=bpy.data.objects['GEO_AthleteBody_Iteration2'];rig=bpy.data.objects['GEO_AthleteRig_Iteration2']
        original=[main]+[o for o in scene.objects if o.type=='MESH' and o!=main and not o.hide_render and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)]
        for o in scene.objects:
            if o.type in {'MESH','ARMATURE'} and o.name!='Studio_floor_NOT_exported':o.hide_render=True;o.hide_set(True)
        clay=grey('PhaseA_neutral_clay',.18);garment=grey('PhaseA_garment_clay',.09);sclera=grey('PhaseA_sclera',.38);iris=grey('PhaseA_iris',.10);pupil=grey('PhaseA_pupil',.025)
        A=[];B=[]
        for index,o in enumerate(original):
            mat=clay
            if 'Separate_compression' in o.name:mat=garment
            elif 'Eyeball' in o.name:mat=sclera
            elif 'Iris' in o.name:mat=iris
            elif 'Pupil' in o.name:mat=pupil
            A.append(copy_frozen(o,'A_previous_'+str(index),mat));B.append(copy_frozen(o,'B_sculpt_'+str(index),mat))
        for o in A:o.hide_render=True;o.hide_set(True)
        body=B[0];state.update(A=A,B=B,body=body)
        info=json.loads((BASE/'Iteration2/cage-construction.json').read_text());region_names=info['regionNames']
        state['regions']=[region_names[d.value] for d in body.data.attributes['Anatomical_region_id'].data]
        group=body.vertex_groups.new(name='Head_volumetric_edit_only')
        for vertex in body.data.vertices:
            t=max(0,min(1,(vertex.co.z-1.49)/.045));weight=t*t*(3-2*t)
            if weight>0:group.add([vertex.index],weight,'REPLACE')
        def head_edit(u,v,w):
            dz=[0,.004,.016,.017,.009,.002,-.002,0,0][w]
            dx=0;dy=0
            if w in [5,6] and u in [0,4]:dx=(-1 if u==0 else 1)*.003
            if v<=1:
                if w==1:dy=.002
                elif w==2:dy=-.006
                elif w==3:dy=.010 if v==0 and u==2 else -.007
                elif w==4:dy=-.003 if u in [1,3] else 0
                elif w in [6,7]:dy=.008 if v==0 else .003
            return dx,dy,dz
        eyes=[o for o in B[1:] if 'compression' not in original[B.index(o)].name]
        native_lattice('Anatomical_head_volume_edit',(0,-.005,1.6575),(.19,.25,.315),(5,5,9),head_edit,[body]+eyes,group.name)
        def trunk_edit(u,v,w):
            sign=-1 if u<2 else (1 if u>2 else 0);dx=0;dy=0;dz=0
            if w==3:dx=sign*(.014 if u in [0,4] else .010)
            elif w in [2,4,5,6]:dx=sign*.003
            elif w==0:dx=-sign*.004
            if w in [3,4,5,6] and v!=1:dy=(-1 if v==0 else 1)*(.006 if w in [5,6] else .003)
            dz=[0,0,0,0,0,.003,.020,.080,.020][w]
            return dx,dy,dz
        clothes=[o for o in B[1:] if 'compression' in original[B.index(o)].name]
        torso_group=body.vertex_groups.new(name='Thorax_shoulders_not_head')
        for target in [body]+clothes:
            target_group=torso_group if target==body else target.vertex_groups.new(name=torso_group.name)
            for vertex in target.data.vertices:
                t=max(0,min(1,(vertex.co.z-1.43)/.10));lower=max(0,min(1,(vertex.co.z-.82)/.13))
                weight=(1-t*t*(3-2*t))*lower*lower*(3-2*lower)
                if weight>0:target_group.add([vertex.index],weight,'REPLACE')
        native_lattice('Athletic_thorax_waist_volume_edit',(0,0,1.18),(.56,.30,.64),(5,3,9),trunk_edit,[body]+clothes,torso_group.name)

        # Native connected proportional scaling enlarges adult hands without a new mesh.
        bpy.ops.object.select_all(action='DESELECT');body.select_set(True);bpy.context.view_layer.objects.active=body
        scene.tool_settings.mesh_select_mode=(True,False,False)
        for side,sign in [('L',1),('R',-1)]:
            for polygon in body.data.polygons:polygon.select=False
            for edge in body.data.edges:edge.select=False
            for vertex in body.data.vertices:vertex.select=state['regions'][vertex.index].endswith('.'+side) and state['regions'][vertex.index].startswith(('palm.','thumb.','finger_'))
            body.data.update()
            scene.cursor.location=(sign*.5578512,0,1.041322)
            scene.tool_settings.transform_pivot_point='CURSOR'
            area=next(a for a in bpy.context.screen.areas if a.type=='VIEW_3D');region=next(r for r in area.regions if r.type=='WINDOW')
            with bpy.context.temp_override(area=area,region=region,object=body,active_object=body,selected_objects=[body],selected_editable_objects=[body]):
                bpy.ops.object.mode_set(mode='EDIT')
                with bpy.context.temp_override(edit_object=body):
                    bpy.ops.transform.resize(value=(1.08,1.08,1.08),use_proportional_edit=True,use_proportional_connected=True,proportional_size=.035,proportional_edit_falloff='SMOOTH')
                bpy.ops.object.mode_set(mode='OBJECT')
            for polygon in body.data.polygons:polygon.select=False
            for edge in body.data.edges:edge.select=False
            for vertex in body.data.vertices:vertex.select=state['regions'][vertex.index]=='thumb.'+side
            scene.cursor.location=(sign*.606,-.010,1.035)
            with bpy.context.temp_override(area=area,region=region,object=body,active_object=body,selected_objects=[body],selected_editable_objects=[body]):
                bpy.ops.object.mode_set(mode='EDIT')
                with bpy.context.temp_override(edit_object=body):
                    bpy.ops.transform.rotate(value=sign*math.radians(20),orient_axis='Y',orient_type='GLOBAL',use_proportional_edit=True,use_proportional_connected=True,proportional_size=.030,proportional_edit_falloff='SMOOTH')
                bpy.ops.object.mode_set(mode='OBJECT')
        state['audit'].append({'tool':'Native connected proportional Resize','decision':'Adult hand/palm proportions, 1.08 scale around each wrist; 35 mm connected falloff','factor':1.08})
        scene.tool_settings.transform_pivot_point='MEDIAN_POINT'
        with bpy.context.temp_override(object=body,active_object=body,selected_objects=[body],selected_editable_objects=[body]):
            bpy.ops.object.mode_set(mode='SCULPT')
        scene.tool_settings.sculpt.use_symmetry_x=True
        body.data.use_mirror_x=True
        unified=getattr(scene.tool_settings.sculpt,'unified_paint_settings',None)
        if unified is not None:
            unified.use_unified_size=False;unified.use_unified_strength=False
        area=next(a for a in bpy.context.screen.areas if a.type=='VIEW_3D');state['area']=area
        area.spaces.active.shading.type='SOLID'
        scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=32;scene.cycles.use_denoising=True
        scene.render.threads_mode='FIXED';scene.render.threads=6
        scene.render.resolution_x=640;scene.render.resolution_y=800;scene.render.resolution_percentage=100
        for o in scene.objects:
            if o.type=='LIGHT':
                o.data.color=(1,1,1)
                if o.name=='Key':o.data.energy=350;o.data.size=2.2
                elif o.name=='Fill':o.data.energy=100;o.data.size=2.5
                else:o.data.energy=100
        if scene.world and scene.world.use_nodes:
            bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs['Color'].default_value=(.12,.12,.12,1);bg.inputs['Strength'].default_value=.10
        bpy.app.timers.register(prepare_view,first_interval=.5)
    except Exception:fail()
    return None

def load_source():
    source=BASE/'Iteration2/athlete-iteration2.blend';snapshot=OUT/'previous-iteration2.blend'
    if not snapshot.exists():shutil.copy2(source,snapshot)
    bpy.ops.wm.open_mainfile(filepath=str(snapshot))
    # File loading replaces UI context. Let the new screen become current before editing.
    bpy.app.timers.register(setup,first_interval=.5)
    return None
bpy.app.timers.register(load_source,first_interval=2)
