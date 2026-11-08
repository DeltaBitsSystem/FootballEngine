"""Offline PBR pack built only from project-authored sources.

Turf: Tools/MatchViewer3D/Surfaces/author_turf.py (modelled blades rendered top-down, 1 m tile) + turf_pack.py.
Concrete: the stadium kit's baked concrete (Tools/MatchViewer3D/StadiumKit, 2 m tile). Nothing is downloaded.
"""
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/Surfaces'
TURF = ROOT / 'Tools/MatchViewer3D/Surfaces'
KIT = ROOT / 'FootballEngine.Client/Assets/MatchViewer/StadiumKit/Textures'
size = 1024


def load(path):
    data = path.read_bytes()
    image = Image.open(path).convert('RGB')
    if image.size != (size, size):
        raise ValueError(f'{path} must be {size}x{size}')
    return np.asarray(image, dtype=np.float32) / 255, dict(path=str(path.relative_to(ROOT)), sha256=hashlib.sha256(data).hexdigest())


sources = []
grass = {}
for key, name in (('Color', 'turf_albedo.png'), ('NormalGL', 'turf_normal.png'), ('ARM', 'turf_arm.png')):
    grass[key], record = load(TURF / name)
    sources.append(record)
concrete = {}
for key, name in (('diff', 'concrete_albedo.png'), ('nor_gl', 'concrete_normal.png'), ('arm', 'concrete_orm.png')):
    concrete[key], record = load(KIT / name)
    sources.append(record)
# Calibrate the rendered blades to the pitch's target linear colour while keeping per-blade variation.
colour = grass['Color']
linear = np.where(colour <= 0.04045, colour/12.92, ((colour+0.055)/1.055)**2.4)
linear = np.clip((linear/linear.mean(axis=(0, 1)))**0.82 * [0.055, 0.115, 0.018], 0, 1)
turf = np.where(linear <= 0.0031308, linear*12.92, 1.055*linear**(1/2.4)-0.055)
normal = grass['NormalGL']
arm = np.stack([grass['ARM'][..., 0], np.clip(grass['ARM'][..., 1], 0.78, 1), np.zeros((size, size))], axis=-1)
channels = {'albedo': [turf, concrete['diff']], 'normal': [normal, concrete['nor_gl']], 'arm': [arm, concrete['arm']]}
outputs = []
# Normal variance survives in roughness even after the normal itself is renormalized at each mip.
normal_variance = {}
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
        for layer_index, layer in enumerate(layers):
            if kind == 'albedo':
                layer = np.where(layer <= 0.04045, layer/12.92, ((layer+0.055)/1.055)**2.4)
            elif kind == 'normal':
                layer = layer*2-1
            elif kind == 'arm':
                layer = layer.copy()
                layer[..., 1] **= 4
            layer = (layer[::2, ::2]+layer[1::2, ::2]+layer[::2, 1::2]+layer[1::2, 1::2])*0.25
            if kind == 'albedo':
                layer = np.where(layer <= 0.0031308, layer*12.92, 1.055*layer**(1/2.4)-0.055)
            elif kind == 'normal':
                length = np.linalg.norm(layer, axis=-1, keepdims=True)
                normal_variance[level, layer_index] = np.clip(1-length[..., 0], 0, 1)*0.5
                layer /= np.maximum(length, 1e-8)
                layer = layer*0.5+0.5
            elif kind == 'arm':
                layer[..., 1] = np.clip(layer[..., 1] + normal_variance[level, layer_index], 0, 1)**0.25
            reduced.append(layer)
        layers = reduced
    path.write_bytes(packed)
    outputs.append(dict(path=path.name, sha256=hashlib.sha256(packed).hexdigest(), bytes=len(packed)))
(OUT/'manifest.json').write_text(json.dumps(dict(schema=1, size=size, layers=2, levels=11,
    layout='mip-major/layer-major RGBA8', albedo='sRGB', normal='OpenGL tangent-space', arm='AO/roughness/metallic linear',
    mipFiltering='linear-light albedo; normalized normals; GGX alpha-squared roughness with accumulated normal variance',
    materials=['FootballEngine turf: 1.0 metre/tile, pitch colour calibration', 'FootballEngine kit concrete: 2 metres/tile'],
    author='FootballEngine', license='LicenseRef-FootballEngine',
    provenance='Original: turf blades modelled and rendered in Blender by author_turf.py; concrete baked from the stadium kit procedural material. No third-party pixels.',
    sources=sources, files=outputs), indent=2)+'\n')
print(f'Wrote verified offline surface pack to {OUT}')
