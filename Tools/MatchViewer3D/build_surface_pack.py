"""Offline PBR pack: CC0 ambientCG turf and Poly Haven concrete.

Network is only used with --download; the game and normal builds are offline.
"""
import argparse
import hashlib
import json
from pathlib import Path
import urllib.request
import zipfile
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/Surfaces'
SOURCE = OUT / 'Sources'
FILES = {
    'diff': '1cfee53d2f82fa59726ab7ae6a2e8c09',
    'nor_gl': 'c2065fd81f571f4e39163ea5661128fb',
    'arm': '5995ee8efda4e7966a277b3d9049eb2b',
}
args = argparse.ArgumentParser()
args.add_argument('--download', action='store_true')
download = args.parse_args().download
SOURCE.mkdir(parents=True, exist_ok=True)
sources = []
for kind, checksum in FILES.items():
    name = f'concrete_floor_02_{kind}_1k.jpg'
    url = f'https://dl.polyhaven.org/file/ph-assets/Textures/jpg/1k/concrete_floor_02/{name}'
    path = SOURCE / name
    if download and not path.exists():
        with urllib.request.urlopen(url, timeout=60) as response:
            path.write_bytes(response.read())
    data = path.read_bytes()
    if hashlib.md5(data).hexdigest() != checksum:
        raise ValueError(f'Source checksum mismatch: {path}')
    sources.append(dict(path=f'Sources/{name}', url=url, sha256=hashlib.sha256(data).hexdigest()))

size = 1024
grass_url = 'https://ambientcg.com/get?file=Grass004_1K-PNG.zip'
grass_hashes = {
    'Color': '1c504b956e5550fedd8822e7e2135fe45f513bc1212f72ed67885de1002fabb6',
    'NormalGL': 'a6fce3c4f2c5d486b5c993bf47b47ce9d4c1dbb04ecef424ad2c0b703715c3c6',
    'Roughness': 'c3bac30c64d246019e80dd6448544ac381706d43eb583e5ac875cb4f6eece6d8',
    'AmbientOcclusion': '2b90f33cb488385e18d698b6d8e8c649116ebc7478bf7dbd5d08214884adabaf',
}
if download and any(not (SOURCE/f'Grass004_1K-PNG_{channel}.png').exists() for channel in grass_hashes):
    request = urllib.request.Request(grass_url, headers={'User-Agent': 'Mozilla/5.0', 'Referer': 'https://ambientcg.com/'})
    with urllib.request.urlopen(request, timeout=60) as response:
        data = response.read()
    if hashlib.sha256(data).hexdigest() != '9bdb8a28536f437e910824096dd8a3d8276d5186a27ad067dea4e2e762a6981b':
        raise ValueError('ambientCG archive differs from the approved source')
    import io
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for channel in grass_hashes:
            name = f'Grass004_1K-PNG_{channel}.png'
            (SOURCE/name).write_bytes(archive.read(name))
grass = {}
for channel, checksum in grass_hashes.items():
    name = f'Grass004_1K-PNG_{channel}.png'
    data = (SOURCE/name).read_bytes()
    if hashlib.sha256(data).hexdigest() != checksum:
        raise ValueError(f'Grass source checksum mismatch: {name}')
    grass[channel] = np.asarray(Image.open(SOURCE/name).convert('RGB'), dtype=np.float32)/255
    sources.append(dict(path=f'Sources/{name}', url=grass_url, sha256=checksum))
# Neutralize the source's yellow lawn tint while preserving individual blade/occlusion detail.
colour = grass['Color']
linear = np.where(colour <= 0.04045, colour/12.92, ((colour+0.055)/1.055)**2.4)
linear = np.clip((linear/linear.mean(axis=(0, 1)))**0.82 * [0.055, 0.115, 0.018], 0, 1)
turf = np.where(linear <= 0.0031308, linear*12.92, 1.055*linear**(1/2.4)-0.055)
normal = grass['NormalGL']
arm = np.stack([grass['AmbientOcclusion'][..., 0], np.clip(grass['Roughness'][..., 0], 0.78, 1), np.zeros((size, size))], axis=-1)
concrete = {k: np.asarray(Image.open(SOURCE/f'concrete_floor_02_{k}_1k.jpg').convert('RGB'), dtype=np.float32)/255 for k in FILES}
channels = {'albedo': [turf, concrete['diff']], 'normal': [normal, concrete['nor_gl']], 'arm': [arm, concrete['arm']]}
outputs = []
for kind, layers in channels.items():
    path = OUT / f'{kind}.bin'
    packed = bytearray()
    # Mip-major/layer-major RGBA8; full chain through 1x1.
    for level in range(11):
        for layer in layers:
            rgb = np.clip(layer*255+0.5, 0, 255).astype(np.uint8)
            rgba = np.concatenate([rgb, np.full((*rgb.shape[:2], 1), 255, dtype=np.uint8)], axis=-1)
            packed.extend(rgba.tobytes())
        if level == 10:
            break
        reduced = []
        for layer in layers:
            if kind == 'albedo':
                layer = np.where(layer <= 0.04045, layer/12.92, ((layer+0.055)/1.055)**2.4)
            elif kind == 'normal':
                layer = layer*2-1
            layer = (layer[::2, ::2]+layer[1::2, ::2]+layer[::2, 1::2]+layer[1::2, 1::2])*0.25
            if kind == 'albedo':
                layer = np.where(layer <= 0.0031308, layer*12.92, 1.055*layer**(1/2.4)-0.055)
            elif kind == 'normal':
                layer /= np.maximum(np.linalg.norm(layer, axis=-1, keepdims=True), 1e-8)
                layer = layer*0.5+0.5
            reduced.append(layer)
        layers = reduced
    path.write_bytes(packed)
    outputs.append(dict(path=path.name, sha256=hashlib.sha256(packed).hexdigest(), bytes=len(packed)))
(OUT/'manifest.json').write_text(json.dumps(dict(schema=1, size=size, layers=2, levels=11,
    layout='mip-major/layer-major RGBA8', albedo='sRGB', normal='OpenGL tangent-space', arm='AO/roughness/metallic linear',
    materials=['ambientCG Grass004: 1.4 metres/tile, pitch colour calibration', 'Poly Haven concrete_floor_02: 2 metres/tile'],
    author='ambientCG (Grass004); Rob Tuytel (concrete)', license='CC0-1.0',
    source=['https://ambientcg.com/a/Grass004', 'https://polyhaven.com/a/concrete_floor_02'],
    licenseUrl=['https://docs.ambientcg.com/license/', 'https://polyhaven.com/license'],
    sources=sources, files=outputs), indent=2)+'\n')
print(f'Wrote verified offline surface pack to {OUT}')
