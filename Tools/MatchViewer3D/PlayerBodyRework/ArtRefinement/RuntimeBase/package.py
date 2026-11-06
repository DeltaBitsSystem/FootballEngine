"""Package the selected body, experimental kit and project-authored clips."""
from pathlib import Path
import hashlib,json,struct
import bpy

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'final-body-source.blend'))
scene=bpy.context.scene;rig=bpy.data.objects['FINAL_Rig'];scene.frame_set(0)
meshes=[o for o in scene.objects if o.type=='MESH' and o.parent==rig and (o.name=='FINAL_Body_Connected' or o.name.startswith(('GEO_','FE ')))]
assert len(rig.data.bones)==60 and meshes
albedo=bpy.data.images.load(str(OUT/'player-albedo.png'),check_existing=False);albedo.pack()
detail=bpy.data.images.load(str(OUT/'player-normal-roughness.png'),check_existing=False);detail.colorspace_settings.name='Non-Color';detail.pack()
mat=bpy.data.materials.new('Runtime atlas PBR');mat.use_nodes=True
nodes=mat.node_tree.nodes;links=mat.node_tree.links
bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
bsdf.inputs['Base Color'].default_value=(1,1,1,1)
base=nodes.new('ShaderNodeTexImage');base.image=albedo
nr=nodes.new('ShaderNodeTexImage');nr.image=detail
normal=nodes.new('ShaderNodeNormalMap');links.new(base.outputs['Color'],bsdf.inputs['Base Color']);links.new(nr.outputs['Color'],normal.inputs['Color']);links.new(normal.outputs['Normal'],bsdf.inputs['Normal']);links.new(nr.outputs['Alpha'],bsdf.inputs['Roughness'])
for obj in meshes:
    if obj.name.startswith('FE '):
        assert obj.data.uv_layers.active
    else:
        if obj.name.startswith(('GEO_Hair','GEO_Brow')):slot=4
        elif obj.name.startswith(('GEO_Eyeball','GEO_Iris','GEO_Pupil')):slot=7
        else:slot=3
        if not obj.data.uv_layers.active:obj.data.uv_layers.new(name='Runtime Atlas UV')
        uv=obj.data.uv_layers.active
        if obj.name.startswith('GEO_Iris'):u0=.5
        elif obj.name.startswith('GEO_Pupil'):u0=.84
        elif obj.name.startswith('GEO_Eyeball'):u0=.16
        else:u0=.5
        for loop in uv.data:
            if obj.name=='FINAL_Body_Connected':
                u=max(.0001,min(.9999,loop.uv.x));v=max(.0001,min(.9999,loop.uv.y))
            else:u=v=u0
            loop.uv=((slot%4+u)/4,1-(slot//4+1-v)/4)
    obj.data.materials.clear();obj.data.materials.append(mat)
    for p in obj.data.polygons:p.material_index=0
    for uv in obj.data.uv_layers.active.data:
        assert 0<=uv.uv.x<=1 and .5<=uv.uv.y<=1, f'UV outside Blender home half: {obj.name}'
    for v in obj.data.vertices:
        weights=[g.weight for g in v.groups if g.weight>1e-8 and obj.vertex_groups[g.group].name in rig.data.bones]
        assert 0<len(weights)<=4 and abs(sum(weights)-1)<.0001, f'bad skin {obj.name}/{v.index}'
    obj.hide_set(False);obj.hide_render=False
for selected in bpy.context.selected_objects:selected.select_set(False)
rig.hide_set(False);rig.select_set(True)
for obj in meshes:obj.select_set(True)
bpy.context.view_layer.objects.active=rig
path=OUT/'player.glb'
bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,use_active_scene=True,
                          export_animations=True,export_skins=True,export_animation_mode='ACTIONS',
                          export_force_sampling=True,export_frame_range=False,export_yup=True,
                          export_apply=False,export_influence_nb=4,export_all_influences=False)
raw=path.read_bytes();length=struct.unpack_from('<I',raw,12)[0];doc=json.loads(raw[20:20+length])
clips=json.loads((OUT/'procedural-clips.json').read_text())
assert len(doc.get('skins',[]))==1
present={x.get('name') for x in doc.get('animations',[])}
missing={c['id'] for c in clips}-present
assert not missing, f'GLB missing animations: {missing}; found {present}'
for mesh in doc['meshes']:
    for p in mesh['primitives']:assert {'POSITION','NORMAL','TEXCOORD_0','JOINTS_0','WEIGHTS_0'}<=set(p['attributes'])
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
manifest={'schemaVersion':1,'rigSchemaVersion':1,'coordinates':{'up':'+Y','forward':'+Z','units':'metres'},
          'file':{'path':path.name,'sha256':digest(path)},'joints':len(doc['skins'][0]['joints']),
          'textures':[{'path':n,'sha256':digest(OUT/n)} for n in ('player-albedo.png','player-normal-roughness.png')],
          'clips':clips,'author':'FootballEngine original body and procedural motion; conforming draft kit',
          'qualityStatus':'experimental package: gameplay, clipping, axis and reimport acceptance pending'}
(OUT/'player-motion.json').write_text(json.dumps(manifest,indent=2))
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'player-runtime-source.blend'))
print('PACKAGE_CREATED',len(meshes),len(present),manifest['joints'],flush=True)
