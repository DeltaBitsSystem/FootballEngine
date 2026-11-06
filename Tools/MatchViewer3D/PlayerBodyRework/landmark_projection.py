"""Project explicitly approximate model landmarks onto its own orthographic render."""
import bpy,json
from pathlib import Path
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'Reconstruction-Study';OUT.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(BASE/'PhaseA-Sculpt-11/athlete-sculpt-study.blend'))
scene=bpy.context.scene;cam=scene.camera
landmarks={'coronilla':(0,0,1.8002),'menton':(0,-.0683,1.573),'base cervical':(0,-.04,1.49),'esternon superior':(0,-.085,1.405),'acromion L':(.2395,-.0058,1.385),'acromion R':(-.2395,-.0058,1.385),'ombligo aprox':(0,-.0923,1.095),'pelvis ASIS L aprox':(.13,-.07,.98),'pelvis ASIS R aprox':(-.13,-.07,.98),'pubis aprox':(0,-.093,.90),'rodilla':(.10,-.0652,.495),'tobillo':(.1246,.0024,.122),'suelo':(0,0,.004875)}
views={'frontal':((0,-5,.92),(0,0,.90)),'perfil':((5,0,.92),(0,0,.90))}
data={'note':'Ejes y marcadores aproximados de la superficie de nuestro modelo, no landmarks óseos detectados. Coordenadas en metros Z-up.','points':landmarks,'views':{}}
for name,(pos,target) in views.items():
    cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=2.02
    bpy.context.view_layer.update()
    points={}
    for label,p in landmarks.items():
        xy=world_to_camera_view(scene,cam,Vector(p));points[label]=[xy.x*640,(1-xy.y)*800]
    data['views'][name]=points
(OUT/'landmark-projection.json').write_text(json.dumps(data,indent=2))
