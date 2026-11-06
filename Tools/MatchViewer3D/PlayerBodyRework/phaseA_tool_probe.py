"""Probe genuine Blender sculpt context in our separate no-focus UI process.

Never connects to or resets the user's Blender. Does not change/save any asset.
"""
import bpy
import json
from pathlib import Path

OUT=Path('/tmp/opencode/player-body-sculpt-probe.json')
def probe():
    try:
        area=next(a for a in bpy.context.screen.areas if a.type=='VIEW_3D')
        region=next(r for r in area.regions if r.type=='WINDOW')
        with bpy.context.temp_override(area=area,region=region):
            bpy.ops.object.select_all(action='DESELECT')
            bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=16)
            obj=bpy.context.object;obj.name='Our_tool_probe_only'
            bpy.ops.object.mode_set(mode='SCULPT')
            rna=bpy.ops.sculpt.brush_stroke.get_rna_type()
            activate=bpy.ops.brush.asset_activate.get_rna_type()
            report={'area':[area.width,area.height],'region':[region.width,region.height],'strokeProperties':list(rna.properties.keys()),'activateProperties':list(activate.properties.keys()),'assetLibraries':[x.name for x in bpy.context.preferences.filepaths.asset_libraries]}
            brushfile='/usr/share/blender/5.2/datafiles/assets/brushes/essentials_brushes-mesh_sculpt.blend'
            with bpy.data.libraries.load(brushfile) as (source,target):report['essentialBrushes']=list(source.brushes)
            OUT.write_text(json.dumps(report,indent=2))
    except Exception:
        import traceback
        OUT.write_text(traceback.format_exc())
    bpy.ops.wm.quit_blender()
    return None
bpy.app.timers.register(probe,first_interval=2)
