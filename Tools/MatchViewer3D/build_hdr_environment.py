"""Bake project-owned linear HDR daylight IBL and a split-sum GGX BRDF LUT.

No runtime bake/network dependency. The analytic sky excludes the solar disc:
direct sunlight is evaluated exactly once by the scene shader.
"""
import hashlib
import json
from pathlib import Path
import numpy as np

OUT = Path(__file__).resolve().parents[2] / 'FootballEngine.Client/Assets/MatchViewer/Lighting'
OUT.mkdir(parents=True, exist_ok=True)
WIDTH, HEIGHT, SAMPLES, LUT = 128, 64, 256, 128


def radical_inverse(value):
    result, weight = 0.0, 0.5
    while value:
        result += (value & 1) * weight
        value >>= 1
        weight *= 0.5
    return result


def sky(direction):
    y = direction[..., 1:2]
    upper = np.clip(y, 0, 1)
    horizon = np.array([1.2, 1.35, 1.5])
    zenith = np.array([0.32, 0.58, 1.05])
    daylight = horizon * (1-upper)**0.7 + zenith * (1-(1-upper)**0.7)
    ground = np.array([0.10, 0.15, 0.08]) * (0.65+0.35*np.clip(-y, 0, 1))
    return np.where(y >= 0, daylight, ground)


u, v = np.meshgrid((np.arange(WIDTH)+0.5)/WIDTH, (np.arange(HEIGHT)+0.5)/HEIGHT)
phi, theta = (u-0.5)*2*np.pi, v*np.pi
normal = np.stack([np.sin(theta)*np.cos(phi), np.cos(theta), np.sin(theta)*np.sin(phi)], axis=-1)
up = np.where(np.abs(normal[..., 1:2]) < 0.99, [0, 1, 0], [1, 0, 0])
tangent = np.cross(up, normal)
tangent /= np.linalg.norm(tangent, axis=-1, keepdims=True)
bitangent = np.cross(normal, tangent)


def to_world(local):
    return tangent*local[0] + bitangent*local[1] + normal*local[2]


def half_vector(x, y, roughness):
    alpha2 = roughness**4
    cos_theta = np.sqrt((1-y)/(1+(alpha2-1)*y))
    sin_theta = np.sqrt(np.maximum(0, 1-cos_theta**2))
    return np.stack([np.cos(2*np.pi*x)*sin_theta, np.sin(2*np.pi*x)*sin_theta, cos_theta], axis=-1)


layers = []
for roughness in np.linspace(0, 1, 6):
    if roughness == 0:
        layers.append(sky(normal))
        continue
    total, weight = np.zeros_like(normal), np.zeros((*normal.shape[:2], 1))
    for i in range(SAMPLES):
        h = to_world(half_vector(i/SAMPLES, radical_inverse(i), roughness))
        light = 2*np.sum(normal*h, axis=-1, keepdims=True)*h-normal
        nl = np.maximum(0, np.sum(normal*light, axis=-1, keepdims=True))
        total += sky(light)*nl
        weight += nl
    layers.append(total/np.maximum(weight, 1e-8))
# Cosine-weighted convolution stores irradiance/pi, so Lambertian shader uses albedo directly.
diffuse = np.zeros_like(normal)
for i in range(SAMPLES):
    x, y = i/SAMPLES, radical_inverse(i)
    local = [np.sqrt(y)*np.cos(2*np.pi*x), np.sqrt(y)*np.sin(2*np.pi*x), np.sqrt(1-y)]
    diffuse += sky(to_world(local))/SAMPLES
layers.append(diffuse)
environment = np.stack(layers)
rgba = np.concatenate([environment, np.ones((*environment.shape[:-1], 1))], axis=-1)

nv, roughness = np.meshgrid((np.arange(LUT)+0.5)/LUT, (np.arange(LUT)+0.5)/LUT)
view = np.stack([np.sqrt(1-nv**2), np.zeros_like(nv), nv], axis=-1)
brdf = np.zeros((LUT, LUT, 2))
for i in range(SAMPLES):
    h = half_vector(i/SAMPLES, radical_inverse(i), roughness)
    vh = np.maximum(0, np.sum(view*h, axis=-1))
    light = 2*vh[..., None]*h-view
    nl, nh = np.maximum(0, light[..., 2]), np.maximum(1e-8, h[..., 2])
    # IBL Smith geometry uses alpha/2, not the direct-light remapping.
    k = roughness**2/2
    geometry = nv/(nv*(1-k)+k) * nl/np.maximum(nl*(1-k)+k, 1e-8)
    visibility = geometry*vh/np.maximum(nh*nv, 1e-8)
    fresnel = (1-vh)**5
    brdf[..., 0] += (1-fresnel)*visibility/SAMPLES
    brdf[..., 1] += fresnel*visibility/SAMPLES

files = []
for name, array in [('environment.bin', rgba), ('brdf.bin', brdf)]:
    data = array.astype('<f2').tobytes()
    (OUT/name).write_bytes(data)
    files.append(dict(path=name, bytes=len(data), sha256=hashlib.sha256(data).hexdigest()))
(OUT/'manifest.json').write_text(json.dumps(dict(schema=1, width=WIDTH, height=HEIGHT,
    layers=7, specularLayers=6, diffuseLayer=6, lutSize=LUT, samples=SAMPLES,
    encoding='little-endian IEEE754 binary16, scene-linear RGB; environment RGBA, BRDF RG',
    projection='equirectangular: atan2(z,x), acos(y), right-handed Y-up',
    source='project-owned analytic daylight sky without solar disc; not a photographed HDRI',
    roughness=[0, 0.2, 0.4, 0.6, 0.8, 1], files=files), indent=2)+'\n')
print(f'Baked HDR IBL: range {environment.min():.3f}..{environment.max():.3f}; {OUT}')
