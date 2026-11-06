"""Check a real native sculpt stroke in a separate isolated Blender UI."""
import bpy
import json
from pathlib import Path
from mathutils import Vector
from bpy_extras.view3d_utils import location_3d_to_region_2d

OUT=Path('/tmp/opencode/player-body-brush-probe.json')
state={}
def stroke_probe():
    try:
        area=state['area'];region=next(r for r in area.regions if r.type=='WINDOW')
        with bpy.context.temp_override(area=area,region=region):
            brush=bpy.context.tool_settings.sculpt.brush;brush.size=100;brush.strength=.75
            p=Vector((0,-1,0));q=p+Vector((.25,0,0))
            a=location_3d_to_region_2d(region,area.spaces.active.region_3d,p)
            b=location_3d_to_region_2d(region,area.spaces.active.region_3d,q)
            points=[]
            for i in range(12):
                t=i/11;mouse=a.lerp(b,t);location=p.lerp(q,t)
                points.append({'name':'NativeGrabProbe','location':tuple(location),'mouse':tuple(mouse),'mouse_event':tuple(mouse),'pressure':1,'size':100,'time':i*.05,'is_start':i==0,'x_tilt':0,'y_tilt':0})
            before=[v.co.copy() for v in state['object'].data.vertices]
            result=bpy.ops.sculpt.brush_stroke(stroke=points,override_location=True)
            after=[v.co.copy() for v in state['object'].data.vertices]
            OUT.write_text(json.dumps({'brush':brush.name,'result':list(result),'strokePixelStart':list(a),'strokePixelEnd':list(b),'maxVertexMovement':max((a-b).length for a,b in zip(before,after)),'changedVertices':sum((a-b).length>1e-6 for a,b in zip(before,after))},indent=2))
    except Exception:
        import traceback;OUT.write_text(traceback.format_exc())
    bpy.context.preferences.filepaths.temporary_directory='/tmp/opencode/player-body-isolated-ui/'
    bpy.ops.wm.quit_blender()
    return None
def setup():
    try:
        area=next(a for a in bpy.context.screen.areas if a.type=='VIEW_3D');region=next(r for r in area.regions if r.type=='WINDOW')
        with bpy.context.temp_override(area=area,region=region):
            bpy.ops.object.select_all(action='DESELECT');bpy.ops.mesh.primitive_uv_sphere_add(segments=64,ring_count=32)
            obj=bpy.context.object;obj.name='Our_sculpt_probe_only'
            bpy.ops.object.mode_set(mode='SCULPT');obj.data.use_mirror_x=False
            modes=[i.identifier for i in bpy.ops.brush.asset_activate.get_rna_type().properties['asset_library_type'].enum_items]
            assert 'ESSENTIALS' in modes
            bpy.ops.brush.asset_activate(asset_library_type='ESSENTIALS',relative_asset_identifier='brushes/essentials_brushes-mesh_sculpt.blend/Brush/Grab')
            bpy.context.tool_settings.sculpt.use_symmetry_x=False
            bpy.ops.view3d.view_axis(type='FRONT',align_active=False);bpy.ops.view3d.view_selected(use_all_regions=False)
            state.update(area=area,object=obj);area.tag_redraw()
            bpy.app.timers.register(stroke_probe,first_interval=2)
    except Exception:
        import traceback;OUT.write_text(traceback.format_exc());bpy.ops.wm.quit_blender()
    return None
bpy.app.timers.register(setup,first_interval=2)
