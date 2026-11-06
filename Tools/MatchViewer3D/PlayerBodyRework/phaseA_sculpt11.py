"""Isolated interactive Blender sculpt experiment on a copy of the clay study."""
import bpy
import json
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from bpy_extras.view3d_utils import location_3d_to_region_2d

ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'PhaseA-Sculpt-11';OUT.mkdir(exist_ok=True);(OUT/'evidence').mkdir(exist_ok=True)
state={'audit':[],'index':0}
jobs=[
 ('FRONT',(0,0,1.66),.43,[('malar','Grab',(.045,1.656),(.048,1.659),.020,.65),('orbital brow','Clay Strips',(.016,1.700),(.045,1.700),.009,.06)]),
 ('RIGHT',(0,0,1.66),.43,[('submental plane','Grab',(-.045,1.548),(-.039,1.548),.024,.65),('oral support','Grab',(-.067,1.616),(-.071,1.616),.018,.6)]),
 ('FRONT',(0,0,1.22),1.05,[('clavicle','Clay Strips',(.045,1.397),(.14,1.392),.012,.04),('deltoid','Clay',(.215,1.36),(.24,1.33),.026,.05)]),
 ('BACK',(0,0,1.24),1.05,[('scapular transition','Clay Strips',(.065,1.35),(.12,1.31),.027,.04)]),
]
def finish():
    bpy.ops.object.mode_set(mode='OBJECT')
    (OUT/'brush-audit.json').write_text(json.dumps(state['audit'],indent=2))
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'athlete-sculpt-study.blend'))
    scene=bpy.context.scene
    shots=[('front',(0,-5,.92),(0,0,.90),2.02),('profile',(5,0,.92),(0,0,.90),2.02),('back',(0,5,.92),(0,0,.90),2.02),('three-quarter',(3,-5,1.5),(0,0,.90),2.02),('head-front',(0,-3,1.67),(0,0,1.66),.37),('head-profile',(3,0,1.67),(0,0,1.66),.37),('head-three-quarter',(3,-5,1.7),(0,0,1.665),.37)]
    for name,pos,target,scale in shots:
        cam=scene.camera;cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
        scene.render.filepath=str(OUT/'evidence'/(name+'.png'));bpy.ops.render.render(write_still=True)
    bpy.ops.wm.quit_blender()
def step():
    try:
        if state['index']>=len(jobs):finish();return None
        view,center,distance,strokes=jobs[state['index']];area=state['area'];region=next(r for r in area.regions if r.type=='WINDOW')
        with bpy.context.temp_override(area=area,region=region,object=state['body'],active_object=state['body']):
            bpy.ops.view3d.view_axis(type=view,align_active=False)
            rv=area.spaces.active.region_3d;rv.view_location=center;rv.view_distance=distance;rv.view_perspective='ORTHO';area.tag_redraw()
        bpy.app.timers.register(lambda:stroke(view,strokes),first_interval=1)
    except Exception:fail()
    return None
def stroke(view,strokes):
    try:
        area=state['area'];region=next(r for r in area.regions if r.type=='WINDOW');rv=area.spaces.active.region_3d;body=state['body']
        with bpy.context.temp_override(area=area,region=region,object=body,active_object=body):
            for label,kind,a,b,radius,strength in strokes:
                tree=BVHTree.FromObject(body,bpy.context.evaluated_depsgraph_get())
                def surface(q):
                    horizontal,z=q
                    if view=='RIGHT':origin=Vector((1,horizontal,z));direction=Vector((-1,0,0))
                    elif view=='BACK':origin=Vector((horizontal,1,z));direction=Vector((0,-1,0))
                    else:origin=Vector((horizontal,-1,z));direction=Vector((0,1,0))
                    p,normal,_,_=tree.ray_cast(origin,direction)
                    if p is None and view=='RIGHT':
                        for offset in [.003,.006,.009,.012,.015,.018,.021]:
                            adjusted=origin+Vector((0,offset,0))
                            p,normal,_,_=tree.ray_cast(adjusted,direction)
                            if p is not None:break
                    if p is None:raise RuntimeError('Ray missed '+label)
                    return p
                p,q=surface(a),surface(b)
                m=location_3d_to_region_2d(region,rv,p);n=location_3d_to_region_2d(region,rv,q)
                axis=Vector((0,radius,0)) if view=='RIGHT' else Vector((radius,0,0))
                size=round((location_3d_to_region_2d(region,rv,p+axis)-m).length)
                bpy.ops.brush.asset_activate(asset_library_type='ESSENTIALS',relative_asset_identifier='brushes/essentials_brushes-mesh_sculpt.blend/Brush/'+kind)
                brush=bpy.context.tool_settings.sculpt.brush;brush.size=size;brush.strength=strength;brush.use_pressure_size=False;brush.use_pressure_strength=False
                before=[v.co.copy() for v in body.data.vertices]
                points=[]
                for i in range(12):
                    t=i/11;mouse=m.lerp(n,t);point=p.lerp(q,t)
                    points.append({'name':label,'location':tuple(point),'mouse':tuple(mouse),'mouse_event':tuple(mouse),'pressure':1,'size':size,'time':i*.05,'is_start':i==0,'x_tilt':0,'y_tilt':0})
                bpy.ops.sculpt.brush_stroke(stroke=points,override_location=True)
                d=[(v.co-p).length for v,p in zip(body.data.vertices,before)]
                state['audit'].append({'stroke':label,'view':view,'radiusMm':radius*1000,'maxMovementMm':max(d)*1000,'changedVertices':sum(x>1e-7 for x in d)})
                if max(d)<1e-7 or max(d)>.012:raise RuntimeError('Unacceptable sculpt displacement '+label+' '+str(max(d)))
        state['index']+=1;bpy.app.timers.register(step,first_interval=.5)
    except Exception:fail()
    return None
def fail():
    import traceback
    (OUT/'error.txt').write_text(traceback.format_exc());(OUT/'brush-audit-partial.json').write_text(json.dumps(state['audit'],indent=2));bpy.ops.wm.quit_blender()
def setup():
    try:
        body=bpy.data.objects['B_connected_0'];body.hide_set(False)
        bpy.ops.object.select_all(action='DESELECT');body.select_set(True);bpy.context.view_layer.objects.active=body
        bpy.ops.object.mode_set(mode='SCULPT');bpy.context.scene.tool_settings.sculpt.use_symmetry_x=True
        state['body']=body;state['area']=next(a for a in bpy.context.screen.areas if a.type=='VIEW_3D')
        bpy.app.timers.register(step,first_interval=1)
    except Exception:fail()
    return None
def load():
    bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Connected-10/athlete-form-editable.blend'))
    bpy.app.timers.register(setup,first_interval=1)
    return None
bpy.app.timers.register(load,first_interval=1)
