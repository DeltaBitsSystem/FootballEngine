"""Pack our Blender-authored sky pair; no download, solar inference or rotation."""
import argparse
import hashlib
import json
import os
import tempfile
from pathlib import Path

import numpy as np
import OpenEXR
import Imath
from ibl_prefilter import brdf_lut, directions, prefilter

ROOT=Path(__file__).resolve().parents[2]
ASSETS=ROOT/'FootballEngine.Client/Assets/MatchViewer'
AUTHOR=ROOT/'Tools/MatchViewer3D/OriginalSky'
SOURCE=ASSETS/'OriginalSky'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def atomic_write(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent,prefix='.'+path.name,delete=False) as stream:
        temporary=Path(stream.name)
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    try:
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def json_bytes(value):
    return (json.dumps(value,indent=2,sort_keys=True)+'\n').encode('utf-8')


def verified_source(path,records):
    data=path.read_bytes()
    entry=next(item for item in records if item['path']==str(path.relative_to(ROOT)))
    if len(data)!=entry['bytes'] or sha(data)!=entry['sha256']:
        raise ValueError(f'Original source integrity failed: {path.name}; reauthor and review its provenance first.')
    return data


def source_image(name, records):
    path=SOURCE/name
    data=verified_source(path,records)
    with OpenEXR.File(str(path)) as file:
        channels=file.channels()
        if 'RGB' in channels:
            image=channels['RGB'].pixels.astype(np.float64)
        elif all(c in channels for c in 'RGB'):
            image=np.stack([channels[c].pixels for c in 'RGB'],axis=-1).astype(np.float64)
        else:
            raise ValueError(f'Missing linear RGB channels: {name}')
    if image.shape!=(1024,2048,3) or not np.isfinite(image).all() or image.min()<0 or image.max()>65504:
        raise ValueError(f'Unsupported original HDR source: {name}')
    return image,data


def rgba(image):
    return np.concatenate([image,np.ones((*image.shape[:-1],1))],axis=-1)


def exr_bytes(image,directory):
    handle,temporary=tempfile.mkstemp(suffix='.exr',dir=directory)
    os.close(handle)
    temporary=Path(temporary)
    try:
        header=OpenEXR.Header(image.shape[1],image.shape[0])
        header['channels']={c:Imath.Channel(Imath.PixelType(Imath.PixelType.FLOAT)) for c in 'RGB'}
        writer=OpenEXR.OutputFile(str(temporary),header)
        try:
            writer.writePixels({c:image[...,i].astype('<f4').tobytes() for i,c in enumerate('RGB')})
        finally:
            writer.close()
        return temporary.read_bytes()
    finally:
        temporary.unlink(missing_ok=True)


def build(output,samples):
    if samples<16:
        raise ValueError('At least 16 integration samples are required.')
    records=json.loads((AUTHOR/'hashes.json').read_text())
    settings=json.loads(verified_source(AUTHOR/'settings.json',records))
    author_script=verified_source(AUTHOR/'author_sky.py',records)
    author_blend=verified_source(AUTHOR/'daylight-sky.blend',records)
    visible,visible_bytes=source_image('sky-visible.exr',records)
    lighting,lighting_bytes=source_image('sky-lighting.exr',records)
    sun=np.array(settings['sunDirectionRuntime'],dtype=np.float64)
    if sun.shape!=(3,) or not np.isfinite(sun).all() or abs(np.linalg.norm(sun)-1)>1e-5 or sun[1]<=0:
        raise ValueError('Invalid authored sun direction.')
    rays=directions(2048,1024)
    peak=np.unravel_index(visible.sum(axis=-1).argmax(),visible.shape[:2])
    error=float(np.rad2deg(np.arccos(np.clip(np.dot(rays[peak],sun),-1,1))))
    if error>.25:
        raise ValueError(f'Authored sky solar alignment failed: {error:.3f} degrees.')
    # The authored atmosphere has no pitch ground hemisphere. Retain an explicit
    # first-party turf-bounce policy rather than convolving black ground as sky.
    y=rays[...,1:2]
    ground=np.array([.10,.15,.08])*(.65+.35*np.clip(-y,0,1))
    lighting=np.where(y>=0,lighting,ground)
    environment=prefilter(lighting,samples=samples)
    files=[]
    for name,image in [('environment.bin',rgba(environment)),('brdf.bin',brdf_lut(samples=samples)),('sky.bin',rgba(visible))]:
        if not np.isfinite(image).all() or image.min()<0 or image.max()>65504:
            raise ValueError(f'{name} cannot be represented in binary16.')
        data=image.astype('<f2').tobytes()
        atomic_write(output/name,data)
        files.append({'path':name,'bytes':len(data),'sha256':sha(data)})
    authoring_files=[]
    # Blender's review world must see the same authored lower hemisphere as IBL.
    for name,data in [('sky-visible.exr',visible_bytes),('sky-lighting.exr',exr_bytes(lighting,output))]:
        atomic_write(output/name,data)
        authoring_files.append({'path':name,'sha256':sha(data)})
    radiance=(np.array(settings['directSun']['color'])*settings['directSun']['energy']).tolist()
    presentation={'exposureStops':-.35,'environmentIntensity':.65,'sunDirection':sun.tolist(),'sunRadiance':radiance}
    manifest={
        'schema':3,'width':128,'height':64,'layers':7,'specularLayers':6,'diffuseLayer':6,'lutSize':128,
        'skyWidth':2048,'skyHeight':1024,'samples':samples,'roughness':[0,.2,.4,.6,.8,1],
        'environmentFormat':'rgba16f-le','skyFormat':'rgba16f-le','brdfFormat':'rg16f-le',
        'colorSpace':'linear-rec709','projection':'latlong-y-up','diffuseEncoding':'irradiance-over-pi',
        'solarDisc':'visible-only','sunDirection':sun.tolist(),'solarAlignmentErrorDegrees':error,
        'sunAngularDiameterDegrees':settings['directSun']['angularDiameterDegrees'],
        'source':'FootballEngine original Blender atmosphere and procedural cloud volume',
        'authorship':'first-party-original','authoringFiles':authoring_files,
        'originalSourceHashes':{name:sha(data) for name,data in [('sky-visible.exr',visible_bytes),('sky-lighting.exr',lighting_bytes)]},
        'authorScript':{'path':str((AUTHOR/'author_sky.py').relative_to(ROOT)),'sha256':sha(author_script)},
        'authorBlend':{'path':str((AUTHOR/'daylight-sky.blend').relative_to(ROOT)),'sha256':sha(author_blend)},
        'bakerScripts':[{'path':str(path.relative_to(ROOT)),'sha256':sha(path.read_bytes())}
                        for path in [Path(__file__).resolve(),Path(__file__).with_name('ibl_prefilter.py').resolve()]],
        'groundHemisphere':{'rgb':[.10,.15,.08],'horizonMultiplier':.65,'nadirMultiplier':1},
        'presentation':presentation,'files':files,
    }
    manifest_data=json_bytes(manifest)
    atomic_write(output/'manifest.json',manifest_data)
    sidecar={'schema':2,
             'environmentDirectory':output.name,'environmentManifestSha256':sha(manifest_data),**presentation}
    atomic_write(output.parent/'stadium.environment.json',json_bytes(sidecar))
    print(f'Original HDR pack: {output}; solar error {error:.5f} degrees; range {environment.min():.4f}..{environment.max():.4f}')


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--output',type=Path,default=ASSETS/'Lighting')
    parser.add_argument('--samples',type=int,default=256)
    args=parser.parse_args()
    build(args.output,args.samples)
