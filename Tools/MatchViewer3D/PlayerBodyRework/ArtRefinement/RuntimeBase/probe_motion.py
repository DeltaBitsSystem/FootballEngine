"""Check the existing procedural motion's absolute targets on the selected rig."""
from pathlib import Path
import importlib.util,json,sys
import bpy
sys.dont_write_bytecode=True

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
script=ROOT/'FootballEngine.Client/Assets/MatchViewer/player_authoring.py'
spec=importlib.util.spec_from_file_location('read_only_player_authoring',script)
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'body-consolidated.blend'))
rig=bpy.data.objects['FINAL_Rig'];scene=bpy.context.scene
if rig.animation_data:rig.animation_data.action=None
rows=[]
for name,duration,speed,kind in module.CLIPS:
    for phase in (0,.25,.5,.75):
        try:
            module.pose_clip(rig,name,kind,phase,speed,duration)
            rows.append({'id':name,'phase':phase,'result':'ok'})
        except Exception as e:
            rows.append({'id':name,'phase':phase,'result':'failed','error':f'{type(e).__name__}: {e}'})
(OUT/'motion-target-probe.json').write_text(json.dumps(rows,indent=2))
fail=[x for x in rows if x['result']=='failed']
print('MOTION_TARGET_PROBE',json.dumps({'samples':len(rows),'failures':len(fail),'examples':fail[:8]}),flush=True)
