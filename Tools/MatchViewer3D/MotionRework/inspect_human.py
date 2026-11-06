"""Read the editable runtime character without touching the interactive scene."""
import bpy, json
from pathlib import Path

root = Path(__file__).resolve().parents[3]
source = root / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base/player-runtime-source.blend'
bpy.ops.wm.open_mainfile(filepath=str(source))
rig = bpy.data.objects['FINAL_Rig']
result = {'objects': [], 'bones': {}}
for obj in bpy.context.scene.objects:
    if obj.type == 'MESH' and obj.parent == rig:
        result['objects'].append({'name': obj.name, 'vertices': len(obj.data.vertices), 'bounds': [[min(v.co[i] for v in obj.data.vertices), max(v.co[i] for v in obj.data.vertices)] for i in range(3)], 'modifiers': [(m.name, m.type) for m in obj.modifiers]})
for bone in rig.data.bones:
    result['bones'][bone.name] = [list(bone.head_local), list(bone.tail_local)]
Path('/tmp/opencode/astra-human-source.json').write_text(json.dumps(result, indent=2))
