"""Read actual binary GLB accessors and manifest without changing runtime code."""
from pathlib import Path
import hashlib,json,struct
import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[5]
OUT=ROOT/'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
path=OUT/'player.glb';raw=path.read_bytes();size=struct.unpack_from('<I',raw,12)[0]
doc=json.loads(raw[20:20+size]);bin_header=20+size
binary_offset=bin_header+8
manifest=json.loads((OUT/'player-motion.json').read_text())
assert manifest['schemaVersion']==2 and manifest['rigSchemaVersion']==1
assert hashlib.sha256(raw).hexdigest()==manifest['file']['sha256']
assert len(doc['skins'])==1 and len(doc['skins'][0]['joints'])==manifest['joints']==60
assert manifest['atlas']=={'columns':4,'homeRows':2,'materials':['jersey','shorts','socks','skin','hair','boots','trim','eyes']}
for entry in manifest['textures']:
    p=OUT/entry['path'];assert hashlib.sha256(p.read_bytes()).hexdigest()==entry['sha256'] and Image.open(p).size==(4096,4096)
for chain in ('albedo','normalRoughness'):
    assert len(manifest['mips'][chain])==8
    for level,entry in enumerate(manifest['mips'][chain],1):
        p=OUT/entry['path'];size=4096>>level
        assert entry['width']==size and entry['height']==size and Image.open(p).size==(size,size)
        assert hashlib.sha256(p.read_bytes()).hexdigest()==entry['sha256']
dtypes={5120:np.int8,5121:np.uint8,5122:np.int16,5123:np.uint16,5125:np.uint32,5126:np.float32}
sizes={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}
def accessor(index):
    a=doc['accessors'][index];view=doc['bufferViews'][a['bufferView']]
    typ=np.dtype(dtypes[a['componentType']]);n=sizes[a['type']]
    offset=binary_offset+view.get('byteOffset',0)+a.get('byteOffset',0)
    stride=view.get('byteStride',typ.itemsize*n)
    return np.ndarray((a['count'],n),dtype=typ,buffer=raw,offset=offset,strides=(stride,typ.itemsize)).copy()
nodes={x.get('name') for x in doc['nodes']}
required={'pelvis','chest','head'}|{f'{part}.{side}' for side in ('L','R') for part in ('thigh','shin','foot','toe','upper_arm','forearm','hand')}
assert required<=nodes
uvlo=1.;uvhi=0.;weight_error=0.;max_joint=0;verts=0
for node in doc['nodes']:
    if 'mesh' not in node:continue
    assert node.get('skin')==0
    for prim in doc['meshes'][node['mesh']]['primitives']:
        assert prim.get('mode',4)==4
        a=prim['attributes'];uv=accessor(a['TEXCOORD_0']);w=accessor(a['WEIGHTS_0']);j=accessor(a['JOINTS_0'])
        pos=accessor(a['POSITION']);norm=accessor(a['NORMAL'])
        assert np.isfinite(pos).all() and np.isfinite(norm).all() and np.isfinite(uv).all() and np.isfinite(w).all()
        uvlo=min(uvlo,float(uv[:,1].min()));uvhi=max(uvhi,float(uv[:,1].max()))
        assert uv[:,0].min()>=0 and uv[:,0].max()<=1 and w.min()>=0
        weight_error=max(weight_error,float(np.max(np.abs(w.sum(axis=1)-1))))
        max_joint=max(max_joint,int(j.max()));verts+=len(pos)
assert uvlo>=0 and uvhi<=.5 and weight_error<.0001 and max_joint<60
animations={a['name']:a for a in doc['animations']}
by_name={n.get('name'):n for n in doc['nodes']}
limb_lengths={}
for side in ('L','R'):
    thigh=float(np.linalg.norm(by_name['shin.'+side].get('translation',[0,0,0])))
    shin=float(np.linalg.norm(by_name['foot.'+side].get('translation',[0,0,0])))
    assert abs(thigh-.4402556)<1e-6 and abs(shin-.4352585)<1e-6
    limb_lengths[side]={'thighM':thigh,'shinM':shin}
scale_error=0.;rotation_error=0.
for node in doc['nodes']:
    if 'matrix' in node:continue
    scale_error=max(scale_error,float(np.max(np.abs(np.array(node.get('scale',[1,1,1]))-1))))
    rotation_error=max(rotation_error,abs(float(np.linalg.norm(node.get('rotation',[0,0,0,1])))-1))
for animation in doc['animations']:
    for channel in animation['channels']:
        kind=channel['target']['path']
        values=accessor(animation['samplers'][channel['sampler']]['output'])
        if kind=='scale':scale_error=max(scale_error,float(np.max(np.abs(values-1))))
        if kind=='rotation':rotation_error=max(rotation_error,float(np.max(np.abs(np.linalg.norm(values,axis=1)-1))))
assert scale_error<.001 and rotation_error<.001
def local(node):
    if 'matrix' in node:return np.array(node['matrix']).reshape((4,4),order='F')
    x,y,z,w=node.get('rotation',[0,0,0,1]);sx,sy,sz=node.get('scale',[1,1,1])
    mat=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w),0],
                  [2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w),0],
                  [2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y),0],[0,0,0,1]],dtype=float)
    mat[:3,:3]=mat[:3,:3]@np.diag([sx,sy,sz]);mat[:3,3]=node.get('translation',[0,0,0]);return mat
parents={c:i for i,n in enumerate(doc['nodes']) for c in n.get('children',[])}
def world(i):
    matrix=local(doc['nodes'][i])
    while i in parents:
        i=parents[i];matrix=local(doc['nodes'][i])@matrix
    return matrix
skin=doc['skins'][0];ibm=accessor(skin['inverseBindMatrices']);inverse_error=0.
for i,node in enumerate(skin['joints']):
    inverse_error=max(inverse_error,float(np.max(np.abs(world(node)@ibm[i].reshape((4,4),order='F')-np.eye(4)))))
assert inverse_error<1e-5, inverse_error
durations={}
for clip in manifest['clips']:
    a=animations[clip['id']]
    max_time=max(float(accessor(s['input']).max()) for s in a['samplers'])
    durations[clip['id']]={'glbSeconds':max_time,'manifestSeconds':clip['durationSeconds']}
    assert abs(max_time-clip['durationSeconds'])<.02,(clip['id'],max_time,clip['durationSeconds'])
boots=next(n for n in doc['nodes'] if n.get('name')=='FE Boots')
bp=accessor(doc['meshes'][boots['mesh']]['primitives'][0]['attributes']['POSITION'])
report={'skinCount':1,'jointCount':60,'meshNodes':sum('mesh' in n for n in doc['nodes']),'verticesIncludingSeamSplits':verts,
        'glbSha256':hashlib.sha256(raw).hexdigest(),'limbBindTranslationLengthsM':limb_lengths,
        'maxInverseBindIdentityError':inverse_error,'maxNodeAndAnimatedScaleError':scale_error,'maxNodeAndAnimatedQuaternionNormError':rotation_error,
        'gltfUvYRange':[uvlo,uvhi],'maxWeightSumError':weight_error,'maxJointIndex':max_joint,
        'mipsPerTexture':8,'clipDurations':durations,'bootLocalZRangeM':[float(bp[:,2].min()),float(bp[:,2].max())],
        'note':'Positive Z contains the front foot; proportions checked on both sides at 1.8 m.'}
(OUT/'contract-verification.json').write_text(json.dumps(report,indent=2))
print('CONTRACT',json.dumps(report,indent=2),flush=True)
