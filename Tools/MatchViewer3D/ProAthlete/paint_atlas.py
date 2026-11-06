"""Step 3b: paint the athlete atlas from 3D surface samples; every pixel is generated here.

python3 Tools/MatchViewer3D/ProAthlete/paint_atlas.py
Reads Pro-Athlete/surface-sample.npz, writes player-albedo.png and player-normal-roughness.png (4096 RGBA).
Normals are tangent-space in the runtime's glTF UV frame: +X along +U, +Y along +V (image rows downward).
"""
import importlib.util, json, sys, time
from pathlib import Path
import numpy as np
from PIL import Image

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('pa_common', Path(__file__).with_name('common.py'))
C = importlib.util.module_from_spec(spec); spec.loader.exec_module(C)
LOOK = json.loads(C.PARAMS_PATH.read_text())['look']
SIZE = 4096
H = SIZE // 2
T = SIZE // 4
S = np.load(C.OUT / 'surface-sample.npz')
L = {k: np.array(v) for k, v in json.loads(str(S['landmarks'])).items()}
started = time.time()

# ---------------------------------------------------------------- rasterisation
pos = np.zeros((H, SIZE, 3), np.float32)
nrm = np.zeros((H, SIZE, 3), np.float32)
obj = np.full((H, SIZE), -1, np.int8)
attrs = {}
OBJS = ['skin', 'eyes', 'hair', 'jersey', 'shorts', 'socks', 'boots']
for oi, key in enumerate(OBJS):
    uv = S[f'{key}_uv'] * SIZE
    P, N = S[f'{key}_pos'], S[f'{key}_nrm']
    extra = {k[len(key) + 1:]: S[k] for k in S.files if k.startswith(key + '_fe_')}
    for name in extra:
        attrs.setdefault(name, np.zeros((H, SIZE), np.float32))
    for t in range(len(uv)):
        a, b, c = uv[t]
        x0, x1 = int(max(0, np.floor(min(a[0], b[0], c[0]) - 1))), int(min(SIZE - 1, np.ceil(max(a[0], b[0], c[0]) + 1)))
        y0, y1 = int(max(0, np.floor(min(a[1], b[1], c[1]) - 1))), int(min(H - 1, np.ceil(max(a[1], b[1], c[1]) + 1)))
        ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]
        px, py = xs + .5, ys + .5
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12:
            continue
        w0 = ((b[1] - c[1]) * (px - c[0]) + (c[0] - b[0]) * (py - c[1])) / d
        w1 = ((c[1] - a[1]) * (px - c[0]) + (a[0] - c[0]) * (py - c[1])) / d
        w2 = 1 - w0 - w1
        # A small tolerance closes cracks between triangles of one island; it is bounded and the weights clamped,
        # because on sliver triangles an area-scaled tolerance extrapolates attributes far outside the face.
        eps = min(.5 / max(1e-6, np.sqrt(abs(d))), .08)
        inside = (w0 >= -eps) & (w1 >= -eps) & (w2 >= -eps)
        if not inside.any():
            continue
        yy, xx = ys[inside], xs[inside]
        W = np.clip(np.stack([w0[inside], w1[inside], w2[inside]], 1), 0, 1)
        W /= W.sum(1, keepdims=True)
        pos[yy, xx] = W @ P[t]
        n = W @ N[t]
        nrm[yy, xx] = n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-8)
        obj[yy, xx] = oi
        for name, v in extra.items():
            attrs[name][yy, xx] = W @ v[t]
print(f'rasterised in {time.time() - started:.1f}s', flush=True)
mask = obj >= 0

# ---------------------------------------------------------------- noise
def hash3(i, j, k, seed):
    h = (i * 73856093) ^ (j * 19349663) ^ (k * 83492791) ^ (seed * 2654435761)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xFFFFFF).astype(np.float32) / 0xFFFFFF


def vnoise(p, freq, seed=0):
    q = p * freq
    i = np.floor(q).astype(np.int64); f = q - i
    u = f * f * (3 - 2 * f)
    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (u[..., 0] if dx else 1 - u[..., 0]) * (u[..., 1] if dy else 1 - u[..., 1]) * (u[..., 2] if dz else 1 - u[..., 2])
                out = out + w * hash3(i[..., 0] + dx, i[..., 1] + dy, i[..., 2] + dz, seed)
    return out * 2 - 1


def fbm(p, freq, octaves=4, seed=0):
    total, amp, norm = 0, 1, 0
    for o in range(octaves):
        total = total + amp * vnoise(p, freq * 2 ** o, seed + o); norm += amp; amp *= .5
    return total / norm


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def srgb_to_linear(c):
    c = np.asarray(c, np.float32) / 255
    return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def gauss(p, centre, radius):
    return np.exp(-np.sum((p - centre) ** 2, axis=-1) / (2 * radius ** 2))


albedo = np.zeros((H, SIZE, 3), np.float32)
height = np.zeros((H, SIZE), np.float32)
rough = np.full((H, SIZE), .8, np.float32)


def region(i):
    return obj == i


# ---------------------------------------------------------------- skin
m = region(0); p = pos[m]; n = nrm[m]
tone = srgb_to_linear(LOOK['skinTone'])
skin = np.tile(tone, (len(p), 1))
skin *= (1 + .045 * fbm(p, 9, 3, 1))[:, None]
skin[:, 0] *= 1 + .02 * fbm(p, 22, 2, 2)
# MakeHuman's eye joint sits behind the globe; the visible eyeball centre is measured on the eye mesh itself.
EP = S['eyes_pos'].reshape(-1, 3)
eyeL, eyeR = EP[EP[:, 0] > 0].mean(0), EP[EP[:, 0] < 0].mean(0)
EYE_R = float(np.linalg.norm(EP[EP[:, 0] > 0] - eyeL, axis=1).mean())
mouth_z = (L['joint-l-lowerlid'][2] + L['joint-jaw'][2]) / 2
lips_w = attrs['fe_lips'][m]
lip_pts = p[lips_w > .5]
lip_c = lip_pts.mean(0) if len(lip_pts) else np.array([0, -.16, mouth_z])
front = p[:, 1] < eyeL[1] + .02
red = np.zeros(len(p), np.float32)
for sx in (1, -1):
    cheek = np.array([sx * (abs(eyeL[0]) + .012), eyeL[1] - .012, eyeL[2] - .032])
    red += .55 * gauss(p, cheek, .014)
red += .45 * gauss(p, np.array([0, lip_c[1] - .012, eyeL[2] - .028]), .009)
red += .5 * attrs['fe_ears'][m]
red = np.clip(red, 0, 1)
skin = skin * (1 - .16 * red[:, None]) + srgb_to_linear((190, 105, 95)) * (.16 * red[:, None])
# Clean-shaven athlete: a cool stubble shadow over jaw, chin and upper lip, broken up by follicle noise.
dx = np.abs(p[:, 0])
jaw = L['joint-jaw']
beard = smooth(lip_c[2] + .016, lip_c[2] + .004, p[:, 2]) * smooth(jaw[2] - .045, jaw[2] - .01, p[:, 2])
beard *= front * smooth(.075, .05, dx)
upper_lip = gauss(p, np.array([0, lip_c[1], lip_c[2] + .011]), .008) * (dx < .022)
beard = np.clip(beard + upper_lip * .8, 0, 1) * (1 - smooth(.3, .6, lips_w))
follicle = smooth(.15, .6, vnoise(p, 2600, 5))
skin = skin * (1 - (LOOK['stubble'] * beard * (.55 + .45 * follicle))[:, None]) + srgb_to_linear((70, 60, 55)) * (LOOK['stubble'] * .35 * beard)[:, None]
lip_col = srgb_to_linear(LOOK['lipTone'])
lw = (.8 * smooth(.4, .95, lips_w))[:, None]
skin = skin * (1 - lw) + lip_col * (1 + .05 * fbm(p, 120, 2, 7))[:, None] * lw
# Eyebrows are analytic in head space: thick medial head, arched body, tapering lateral tail.
brow = np.zeros(len(p), np.float32)
for eye, sx in ((eyeL, 1), (eyeR, -1)):
    d = (p[:, 0] * sx) - abs(eye[0])
    h = p[:, 2] - eye[2]
    s = np.clip((d + .022) / .052, 0, 1)
    centre = LOOK['browHeight'] + .004 * np.sin(np.pi * np.clip(s * 1.15, 0, 1)) - .002 * s
    half = .0052 * (1 - s) + .0022 * s
    band = smooth(half, half * .45, np.abs(h - centre)) * smooth(-.024, -.019, d) * smooth(.032, .025, d)
    hairs = smooth(-.1, .5, vnoise(np.stack([d * 1.0, h * 4.0, p[:, 1]], 1), 1400, 11 + (sx > 0)))
    brow = np.maximum(brow, band * (.55 + .45 * hairs) * (n[:, 1] < -.25))
hair_col = srgb_to_linear(LOOK['hairTone'])
skin = skin * (1 - .92 * brow[:, None]) + hair_col * .8 * (.92 * brow[:, None])
# Lash line: skin that wraps the eyeball, darkest along the upper lid.
lash = np.zeros(len(p), np.float32)
for eye in (eyeL, eyeR):
    r = np.linalg.norm(p - eye, axis=1)
    lash = np.maximum(lash, smooth(EYE_R + .0028, EYE_R + .0003, r) * (.35 + .65 * smooth(eye[2] - .002, eye[2] + .003, p[:, 2])))
skin = skin * (1 - .75 * lash[:, None]) + srgb_to_linear((30, 22, 20)) * (.75 * lash[:, None])
# Scalp under a short cut reads as a darker fade, following the same hairline curve as the hair shell.
HL = json.loads((C.OUT / 'kit-report.json').read_text())['hairline']
az = np.degrees(np.abs(np.arctan2(p[:, 0], -(p[:, 1] - HL['centreY']))))
line = HL['top'] - np.interp(az, HL['azimuthDeg'], HL['drop'])
scalp = smooth(line - LOOK['fadeBelowHairline'], line + .002, p[:, 2]) * (1 - smooth(.2, .6, attrs['fe_ears'][m]))
scalp *= .55 + .45 * smooth(-.3, .5, vnoise(p, 2200, 14))
skin = skin * (1 - .62 * scalp[:, None]) + hair_col * (.62 * scalp[:, None]) * (1 + .3 * vnoise(p, 2000, 13))[:, None]
nails = smooth(.3, .8, attrs['fe_nails'][m])
skin = skin * (1 - nails[:, None]) + srgb_to_linear((222, 190, 178)) * nails[:, None]
albedo[m] = skin
tzone = gauss(p, np.array([0, eyeL[1] - .02, eyeL[2] + .03]), .025) + gauss(p, np.array([0, lip_c[1] - .015, eyeL[2] - .025]), .01)
rough[m] = np.clip(.55 - .1 * np.clip(tzone, 0, 1) - .14 * lw[:, 0] - .25 * nails + .05 * beard + .03 * fbm(p, 60, 2, 21), .3, .75)
pores = vnoise(p, 1700, 31) * .00006 * (1 - lw[:, 0]) * (1 - nails) + vnoise(p, 420, 32) * .00005
forehead = smooth(eyeL[2] + .028, eyeL[2] + .04, p[:, 2]) * smooth(eyeL[2] + .075, eyeL[2] + .06, p[:, 2]) * front
pores += forehead * np.sin(p[:, 2] * 1400 + 2 * vnoise(p, 80, 33)) * .00004
height[m] = pores

# ---------------------------------------------------------------- eyes
m = region(1); p = pos[m]
eye_c = np.where((p[:, 0] > 0)[:, None], eyeL, eyeR)
d = p - eye_c; d /= np.maximum(np.linalg.norm(d, axis=1, keepdims=True), 1e-8)
ang = np.arccos(np.clip(-d[:, 1], -1, 1))
theta = np.arctan2(d[:, 2], d[:, 0])
iris_r, pupil_r = .48, .17
sclera = srgb_to_linear((222, 214, 204)) * (1 - .06 * smooth(.9, 1.6, ang))[:, None] + srgb_to_linear((200, 120, 110)) * (.05 * smooth(.9, 1.5, ang))[:, None]
fibres = .7 + .3 * np.sin(theta * 60 + 3 * vnoise(p, 500, 41)) * (.5 + .5 * vnoise(p, 900, 42))
iris_col = srgb_to_linear(LOOK['irisTone']) * fibres[:, None] * (1 + .5 * smooth(iris_r * .6, pupil_r, ang))[:, None]
limbal = smooth(iris_r * .82, iris_r, ang)[:, None]
iris_col = iris_col * (1 - .6 * limbal)
iris = smooth(iris_r + .03, iris_r - .01, ang)[:, None]
pupil = smooth(pupil_r + .02, pupil_r - .01, ang)[:, None]
eye = sclera * (1 - iris) + iris_col * iris
eye = eye * (1 - pupil) + srgb_to_linear((8, 7, 7)) * pupil
albedo[m] = eye
rough[m] = .12

# ---------------------------------------------------------------- hair
m = region(2); p = pos[m]; n = nrm[m]
fade = attrs['fe_hair_fade'][m]
top = smooth(.35, .8, n[:, 2])
# Strands follow front-to-back over the crown and downward over the sides.
along_top = np.stack([p[:, 0] * 1, p[:, 1] * .08, p[:, 2] * 1], 1)
along_side = np.stack([p[:, 0] * 1, p[:, 1] * 1, p[:, 2] * .1], 1)
strands = top * vnoise(along_top, 2400, 51) + (1 - top) * vnoise(along_side, 2400, 52)
clumps = fbm(p, 90, 3, 53)
hc = hair_col * (1 + .55 * strands + .3 * clumps)[:, None]
edge = smooth(.0, .65, fade)
stub = tone * (1 - .62) + hair_col * .62
albedo[m] = stub * (1 - edge[:, None]) + hc * edge[:, None]
# Hair is a matte fibre mass at match distance; low roughness made the shell read as a helmet.
rough[m] = .78 - .06 * edge
height[m] = (strands * .00012 + clumps * .00015) * edge

# ---------------------------------------------------------------- textiles (pattern, colour applied per kit)
pattern = np.ones((H, SIZE), np.float32)
accent = np.zeros((H, SIZE), np.float32)
m = region(3); p = pos[m]; n = nrm[m]
knit = np.sin(p[:, 0] * 2600) * np.sin(p[:, 2] * 2600)
side = smooth(.55, .8, np.abs(n[:, 0])) * (p[:, 2] < 1.38)
pattern[m] = 1 - .08 * side + .025 * fbm(p, 40, 2, 61)
# Original crest: a small rounded shield on the wearer's left chest, embroidered in the trim colour.
crest_c = np.array([.085, -.13, 1.36]); q = p - crest_c
shield = (np.abs(q[:, 0]) < .028 * (1 - smooth(-.01, -.03, q[:, 2]) * .55)) & (np.abs(q[:, 2]) < .033) & (n[:, 1] < -.5)
ring = shield & ~((np.abs(q[:, 0]) < .021 * (1 - smooth(-.006, -.026, q[:, 2]) * .55)) & (np.abs(q[:, 2]) < .026))
star = (np.hypot(q[:, 0], q[:, 2] - .003) < .009) & shield
accent[m] = np.where(ring | star, 1.0, 0.0)
folds = (np.exp(-((p[:, 2] - 1.0) / .09) ** 2) * fbm(np.stack([p[:, 0] * 3, p[:, 1] * 3, p[:, 2] * .4], 1), 25, 2, 62)
         + np.exp(-((p[:, 2] - 1.3) / .06) ** 2) * side * fbm(p, 30, 2, 63))
height[m] = knit * .00004 + folds * .0012 + accent[m] * .00025
rough[m] = .82
m = region(4); p = pos[m]
pattern[m] = 1 + .02 * fbm(p, 40, 2, 71)
outer = np.abs(nrm[m][:, 0]) > .8
accent[m] = (outer & (np.abs(p[:, 1] - p[:, 1].mean() + .0) < .012)).astype(np.float32)
height[m] = np.sin(p[:, 0] * 2200) * np.sin(p[:, 2] * 2200) * .00003 + fbm(p, 20, 2, 72) * .0008
rough[m] = .8
m = region(5); p = pos[m]
ang = np.arctan2(p[:, 1], p[:, 0] - np.sign(p[:, 0]) * .1)
pattern[m] = 1 + .015 * fbm(p, 40, 2, 81)
accent[m] = (p[:, 2] > p[:, 2].max() - .03).astype(np.float32) * (p[:, 2] < p[:, 2].max() - .018)
height[m] = np.sin(ang * 90) * .00012
rough[m] = .85
tile_x = np.arange(SIZE)[None, :] // T
tile_y = np.arange(H)[:, None] // T
trim_tile = (tile_x == 2) & (tile_y == 1) & mask
boot_upper = region(6) & ~trim_tile
pp = pos[boot_upper]; nn = nrm[boot_upper]
albedo[boot_upper] = srgb_to_linear(LOOK['bootTone']) * (1 + .1 * fbm(pp, 60, 2, 91))[:, None]
swoosh_z = .05 + .025 * np.sin((pp[:, 1] - pp[:, 1].min()) / .25 * np.pi)
swoosh = (np.abs(nn[:, 0]) > .5) & (np.abs(pp[:, 2] - swoosh_z) < .006)
albedo[boot_upper] = np.where(swoosh[:, None], srgb_to_linear(LOOK['bootAccent']), albedo[boot_upper])
rough[boot_upper] = np.where(swoosh, .45, .32)
height[boot_upper] = fbm(pp, 300, 2, 92) * .00004
# Trim tile is shared by collar, cuffs, sock tops and soles: white knit, its own roughness.
pt = pos[trim_tile]
albedo[trim_tile] = srgb_to_linear(LOOK['trimTone']) * (1 + .03 * fbm(pt, 60, 2, 101))[:, None]
height[trim_tile] = np.sin(pt[:, 0] * 1800 + pt[:, 1] * 1800) * .00004
rough[trim_tile] = .85

# ---------------------------------------------------------------- dilation and normal derivation
def dilate(arrays, valid, passes):
    valid = valid.copy()
    for _ in range(passes):
        grow = np.zeros_like(valid)
        acc = [np.zeros_like(a) for a in arrays]; cnt = np.zeros(valid.shape, np.float32)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            sv = np.roll(np.roll(valid, dy, 0), dx, 1)
            for a, s in zip(acc, arrays):
                a += np.where((sv & ~valid)[..., None] if s.ndim == 3 else (sv & ~valid), np.roll(np.roll(s, dy, 0), dx, 1), 0)
            cnt += sv & ~valid
        new = cnt > 0
        for a, s in zip(acc, arrays):
            s[new] = (a[new] / (cnt[new][:, None] if s.ndim == 3 else cnt[new]))
        valid |= new
    return valid


def slope(h, axis):
    dh = (np.roll(h, -1, axis) - np.roll(h, 1, axis))
    dp = np.linalg.norm(np.roll(pos, -1, axis) - np.roll(pos, 1, axis), axis=-1)
    ok = mask & np.roll(mask, -1, axis) & np.roll(mask, 1, axis) & (dp > 1e-7)
    return np.where(ok, dh / np.maximum(dp, 1e-7), 0)


su, sv = slope(height, 1), slope(height, 0)
normal = np.stack([-su, -sv, np.ones_like(su)], -1)
normal /= np.linalg.norm(normal, axis=-1, keepdims=True)


def kit_layer(palette):
    """Compose home or away colours: textile tiles take kit colour, trim stays trim, skin/hair/eyes/boots are shared."""
    out = albedo.copy()
    for i, key in ((3, 'jersey'), (4, 'shorts'), (5, 'socks')):
        m = region(i) & ~trim_tile
        base = srgb_to_linear(palette[key]) * pattern[m][:, None]
        acc = srgb_to_linear(palette[key + 'Accent'])
        out[m] = base * (1 - accent[m][:, None]) + acc * accent[m][:, None]
    return out


def encode(colour, nmap, r, valid):
    colour, nmap, r = colour.copy(), nmap.copy(), r.copy()
    dilate([colour, nmap, r], valid, 16)
    srgb = np.where(colour <= .0031308, colour * 12.92, 1.055 * np.clip(colour, 0, None) ** (1 / 2.4) - .055)
    a = np.concatenate([np.clip(srgb, 0, 1), np.ones((H, SIZE, 1), np.float32)], -1)
    nm = nmap / np.linalg.norm(nmap, axis=-1, keepdims=True)
    d = np.concatenate([nm * .5 + .5, r[..., None]], -1)
    return (np.clip(a * 255 + .5, 0, 255).astype(np.uint8), np.clip(d * 255 + .5, 0, 255).astype(np.uint8))


home_a, home_d = encode(kit_layer(LOOK['home']), normal, rough, mask)
away_a, away_d = encode(kit_layer(LOOK['away']), normal, rough, mask)
# Unused texels keep a flat neutral normal and their tile's average colour after dilation.
A = np.concatenate([home_a, away_a], 0); D = np.concatenate([home_d, away_d], 0)
Image.fromarray(A, 'RGBA').save(C.OUT / 'player-albedo.png')
Image.fromarray(D, 'RGBA').save(C.OUT / 'player-normal-roughness.png')
coverage = {k: float((obj == i).sum()) / (T * T) for i, k in enumerate(OBJS)}
(C.OUT / 'atlas-report.json').write_text(json.dumps({'tileCoverage': coverage, 'seconds': time.time() - started}, indent=2))
print('ATLAS_PAINTED', json.dumps(coverage), f'{time.time() - started:.1f}s', flush=True)
