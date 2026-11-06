"""Fit project-authored kit geometry to the consolidated body for inspection."""
from pathlib import Path
import importlib.util,sys
import bpy

ROOT=Path(__file__).resolve().parents[5]
BASE=ROOT/'FootballEngine.Client/Assets/MatchViewer'
OUT=BASE/'PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
sys.dont_write_bytecode=True
def load(name,path):
    spec=importlib.util.spec_from_file_location(name,path);module=importlib.util.module_from_spec(spec)
    sys.modules[name]=module;spec.loader.exec_module(module);return module
player=load('player_authoring',BASE/'player_authoring.py')
equipment=load('athlete_equipment_readonly',BASE/'athlete_equipment.py')
bpy.ops.wm.open_mainfile(filepath=str(OUT/'body-with-motion.blend'))
scene=bpy.context.scene;rig=bpy.data.objects['FINAL_Rig']
scene.frame_set(0)
materials={}
for key,rgb in player.MATERIALS.items():
    mat=bpy.data.materials.new('RuntimeKit '+key);mat.use_nodes=True
    shader=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    shader.inputs['Base Color'].default_value=(*rgb,1);shader.inputs['Roughness'].default_value=.72
    materials[key]=mat
for layer in equipment.wardrobe(scene,rig):
    mesh=bpy.data.meshes.new(layer['name']+' Mesh')
    mesh.from_pydata(layer['points'],[],layer['faces']);mesh.update()
    obj=bpy.data.objects.new(layer['name'],mesh);scene.collection.objects.link(obj)
    for name in player.MATERIALS:mesh.materials.append(materials[name])
    uv=mesh.uv_layers.new(name='Runtime Atlas UV')
    names=list(player.MATERIALS)
    for poly,uvs,name in zip(mesh.polygons,layer['uvs'],layer['materials']):
        poly.use_smooth=True;poly.material_index=names.index(name);slot=names.index(name)
        for loop,(u,v) in zip(poly.loop_indices,uvs):
            u=max(.002,min(.998,u));v=max(.002,min(.998,v))
            uv.data[loop].uv=((slot%4+u)/4,1-(slot//4+1-v)/4)
    groups={b.name:obj.vertex_groups.new(name=b.name) for b in rig.data.bones}
    for index,ws in enumerate(layer['weights']):
        positive={k:v for k,v in ws.items() if v>1e-8};total=sum(positive.values())
        assert total>0 and len(positive)<=4
        for name,w in positive.items():groups[name].add([index],w/total,'REPLACE')
    arm=obj.modifiers.new('Runtime kit skin','ARMATURE');arm.object=rig
    obj.parent=rig;obj['fe_category']=layer['category'];obj['fe_coverage']=layer['coverage']
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'body-with-kit.blend'))
print('KIT_SAVED',flush=True)
