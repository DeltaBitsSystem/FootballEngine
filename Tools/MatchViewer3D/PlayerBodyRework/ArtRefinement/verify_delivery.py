"""Cross-check the saved source, embedded PBR pixels and actual GLB UVs."""
import hashlib
import json
from pathlib import Path

import bpy
from mathutils.kdtree import KDTree

ROOT=Path(__file__).resolve().parents[4]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement'
path=OUT/'final-body-source.blend'
bpy.ops.wm.open_mainfile(filepath=str(path))
scene=bpy.context.scene
scene.frame_set(1)
body=bpy.data.objects['FINAL_Body_Connected']
rig=bpy.data.objects['FINAL_Rig']
assert not body.hide_get() and not body.hide_render and len(rig.data.bones)==60
mat=body.data.materials[0]
bsdf=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
base=bsdf.inputs['Base Color'].links[0].from_node
rough=bsdf.inputs['Roughness'].links[0].from_node
normal=bsdf.inputs['Normal'].links[0].from_node.inputs['Color'].links[0].from_node
images={}
for label,node in [('albedo',base),('normal',normal),('roughness',rough)]:
    image=node.image
    file=OUT/'textures'/('final-body-'+label+'-2048.png')
    disk=hashlib.sha256(file.read_bytes()).hexdigest()
    packed=hashlib.sha256(bytes(image.packed_file.data)).hexdigest()
    assert disk==packed and tuple(image.size)==(2048,2048)
    images[label]={'file':str(file),'sha256':disk,'packedMatchesDisk':True,'colorspace':image.colorspace_settings.name}

sources={}
for level in range(3):
    obj=bpy.data.objects[f'Export_Body_LOD{level}']
    verts=[obj.matrix_world@v.co for v in obj.data.vertices]
    tree=KDTree(len(verts))
    for i,p in enumerate(verts):tree.insert(p,i)
    tree.balance()
    uv=[[] for _ in verts]
    for loop in obj.data.loops:
        uv[loop.vertex_index].append(tuple(obj.data.uv_layers.active.data[loop.index].uv))
    sources[level]=(tree,uv)

results=[]
for level in range(3):
    fresh=bpy.data.scenes.new('verify_import_'+str(level))
    bpy.context.window.scene=fresh
    glb=OUT/f'final-body-lod{level}.glb'
    bpy.ops.import_scene.gltf(filepath=str(glb))
    imported=next(o for o in fresh.objects if o.type=='MESH' and o.name.startswith('Export_Body'))
    irig=next(o for o in fresh.objects if o.type=='ARMATURE')
    assert len(irig.data.bones)==60 and irig.animation_data and irig.animation_data.action
    assert imported.data.uv_layers.active
    tree,uv=sources[level]
    mapping=[tree.find(imported.matrix_world@v.co) for v in imported.data.vertices]
    maxpos=max(d for _,_,d in mapping)
    maxuv=0.
    for loop in imported.data.loops:
        pt=imported.data.uv_layers.active.data[loop.index].uv
        options=uv[mapping[loop.vertex_index][1]]
        delta=min(max(abs(pt.x-a),abs(pt.y-b)) for a,b in options)
        maxuv=max(maxuv,delta)
    shader=next(n for n in imported.data.materials[0].node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    inputs={key:bool(shader.inputs[key].links) for key in ('Base Color','Roughness','Normal')}
    assert maxpos<.00015 and maxuv<.00002 and all(inputs.values())
    results.append({'file':str(glb),'sha256':hashlib.sha256(glb.read_bytes()).hexdigest(),
                    'reimportedJointCount':len(irig.data.bones),'action':irig.animation_data.action.name,
                    'restPositionMaxMm':maxpos*1000,'uvMaxAbsoluteError':maxuv,'pbrInputsConnected':inputs})
report={'source':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
        'bodyVisible':True,'boneCount':60,'images':images,'glbReimports':results,
        'note':'Posed roundtrip errors and LOD-to-source errors are in sol-export-roundtrip.json.'}
(OUT/'delivery-verification.json').write_text(json.dumps(report,indent=2))
print('DELIVERY_VERIFIED',json.dumps(report,indent=2),flush=True)
