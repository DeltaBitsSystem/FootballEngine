"""Edit the existing thorax control surfaces toward the drawn anatomical guide."""
import bpy,json,math,os
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework'
OUT=BASE/'Reconstruction-Study/Branched-Cage/Anatomy-Refined/Thorax'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'thorax-final.blend'))
scene=bpy.context.scene;body=bpy.data.objects['Anatomy_refined_control_cage'];context=bpy.data.objects['Separate_head_arms_legs_context']
before=body.copy();before.data=body.data.copy();scene.collection.objects.link(before);before.name='Guided_before';before.hide_render=True;before.hide_set(True)
for obj in scene.objects:
    if obj.type=='MESH' and obj not in [body,context] and obj.name!='Studio_floor_NOT_exported':obj.hide_render=True;obj.hide_set(True)
    if obj in [body,context]:obj.hide_render=False;obj.hide_set(False)
mesh=body.data;mesh.update()
before_positions=[v.co.copy() for v in mesh.vertices]
def smooth(x,a,b):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def window(x,a,b,c,d):return smooth(x,a,b)*(1-smooth(x,c,d))
patches={name:[] for name in ['sternum','pectoral','clavicle','deltoid','scapula','trapezius']}
stage=int(os.environ.get('FE_GUIDED_STAGE','1'))
for v in mesh.vertices:
    p=v.co;x,y,z=p;ax=abs(x);sign=1 if x>=0 else -1
    # Match each face region to a distinct oriented plane rather than applying
    # a common translation. Blend only across its supporting perimeter.
    anterior=1-smooth(y,-.050,-.010);posterior=smooth(y,.015,.065)
    w=anterior*(1-smooth(ax,.018,.052))*window(z,1.245,1.27,1.35,1.39)
    if w:
        plane=-.107+.16*(z-1.31);p.y=(1-.8*w)*p.y+.8*w*plane;patches['sternum'].append(v.index)
    w=anterior*window(ax,.018,.05,.12,.175)*window(z,1.21,1.255,1.35,1.395)
    if w:
        # The lower/outer edge slopes toward the humeral insertion, while
        # the medial end meets the sternum rather than forming a ball.
        plane=-.127+.27*(ax-.07)+.25*(z-1.30)
        p.y=(1-.9*w)*p.y+.9*w*plane;patches['pectoral'].append(v.index)
    w=anterior*window(ax,.025,.055,.145,.19)*window(z,1.345,1.372,1.407,1.445)
    if w:
        plane=-.082+.17*(ax-.07);p.y=(1-.75*w)*p.y+.75*w*plane;p.z+=.008*w;patches['clavicle'].append(v.index)
    w=window(ax,.145,.18,.25,.30)*window(z,1.27,1.31,1.40,1.44)
    if w:
        p.x+=sign*.014*w;p.z+=.006*w;p.y+=(.008 if y>0 else -.008)*w;patches['deltoid'].append(v.index)
    w=posterior*window(ax,.025,.055,.13,.18)*window(z,1.24,1.285,1.365,1.415)
    if w:
        plane=.112-.20*(ax-.08)-.13*(z-1.32);p.y=(1-.85*w)*p.y+.85*w*plane;patches['scapula'].append(v.index)
    w=posterior*(1-smooth(ax,.055,.14))*window(z,1.34,1.39,1.435,1.49)
    if w:p.y+=.008*w;p.z-=.006*w;patches['trapezius'].append(v.index)
    if stage==2:
        # Refine two preview defects: reduce the outer acromial ridge and
        # open the central sternum-to-neck transition.
        w=window(ax,.18,.20,.25,.28)*window(z,1.355,1.37,1.405,1.43)
        p.z-=.006*w
        w=anterior*(1-smooth(ax,.025,.065))*window(z,1.35,1.38,1.42,1.455)
        p.y+=.006*w
mesh.update()
for name,ids in patches.items():
    if not ids:raise RuntimeError('No affected vertices in '+name)
    group=body.vertex_groups.get('guided_'+name) or body.vertex_groups.new(name='guided_'+name)
    group.add(ids,1,'REPLACE')
scene.render.threads_mode='FIXED';scene.render.threads=6;scene.cycles.samples=24;scene.cycles.use_denoising=False
scene.camera.location=(3,-5,1.5);scene.camera.rotation_euler=(Vector((0,0,.90))-scene.camera.location).to_track_quat('-Z','Y').to_euler();scene.camera.data.ortho_scale=2.02
body.hide_render=False;context.hide_render=False;before.hide_render=True
path=OUT/('thorax-guided-final.blend' if stage==2 else 'thorax-guided-preview.blend')
bpy.ops.wm.save_as_mainfile(filepath=str(path))
# Calculate after ALL control edits, including stage 2, and compare evaluated
# normals at matching vertices (same topology and modifier stack).
deps=bpy.context.evaluated_depsgraph_get();bm=body.evaluated_get(deps).to_mesh();am=before.evaluated_get(deps).to_mesh()
if len(am.vertices)!=len(bm.vertices):raise RuntimeError('Evaluated topology mismatch')
shifts=[(b.co-a.co).length for a,b in zip(am.vertices,bm.vertices)]
angles=[a.normal.angle(b.normal) for a,b in zip(am.vertices,bm.vertices) if a.normal.length and b.normal.length]
report={'stage':stage,'controlVertices':len(mesh.vertices),'controlChanged':sum((v.co-before_positions[v.index]).length>1e-6 for v in mesh.vertices),'patchCounts':{k:len(v) for k,v in patches.items()},'evaluatedVertices':len(bm.vertices),'evaluatedMaxDisplacementMm':max(shifts)*1000,'evaluatedMaxNormalChangeDegrees':math.degrees(max(angles)),'evaluatedNormalsOver3Degrees':sum(a>math.radians(3) for a in angles),'visibleMeshes':[o.name for o in scene.objects if o.type=='MESH' and not o.hide_render]}
body.evaluated_get(deps).to_mesh_clear();before.evaluated_get(deps).to_mesh_clear()
(OUT/('guided-final-metrics.json' if stage==2 else 'guided-preview-metrics.json')).write_text(json.dumps(report,indent=2))
shots=[('front',(0,-4,1.28),(0,0,1.28),.64),('profile',(4,0,1.28),(0,0,1.28),.64),('back',(0,4,1.28),(0,0,1.28),.64),('three-quarter',(3,-5,1.4),(0,0,1.28),.64)]
context.hide_render=True
for name,pos,target,scale in shots:
    cam=scene.camera;cam.location=pos;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
    scene.render.filepath=str(OUT/'evidence'/('guided-'+str(stage)+'-'+name+'.png'));bpy.ops.render.render(write_still=True)
context.hide_render=False;cam=scene.camera;cam.location=(3,-5,1.5);cam.rotation_euler=(Vector((0,0,.9))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=2.02
scene.render.filepath=str(OUT/'evidence'/('guided-'+str(stage)+'-context.png'));bpy.ops.render.render(write_still=True)
print('GUIDED_EDITS',json.dumps(report),flush=True)
