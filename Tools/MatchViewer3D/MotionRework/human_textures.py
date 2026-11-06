"""Original seeded textile pixels, preserving the authored skin UV bake."""
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / 'FootballEngine.Client/Assets/MatchViewer'
OUT = BASE / 'PlayerBodyRework/Astra-Human'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = BASE / 'PlayerBodyRework/Final-Body/Art-Refinement/Runtime-Base'
albedo = np.array(Image.open(SOURCE / 'player-albedo.png').convert('RGBA'))
detail = np.array(Image.open(SOURCE / 'player-normal-roughness.png').convert('RGBA'))
n = albedo.shape[0] // 4
y, x = np.mgrid[:n, :n].astype(float)
u, v = x / (n - 1), 1 - y / (n - 1)
rng = np.random.default_rng(20261031)
weave = np.sin(x * np.pi / 2) * np.sin(y * np.pi / 2)
grain = rng.normal(0, .6, (n, n))
for team in range(2):
    for slot in range(3):
        ys, xs = slice(team * 2 * n, (team * 2 + 1) * n), slice(slot * n, (slot + 1) * n)
        base = albedo[ys, xs, :3].astype(float)
        panel = np.exp(-((u - .25) / .025) ** 2) + np.exp(-((u - .75) / .025) ** 2)
        hem = (v < .045) | (v > .955)
        variation = 1 - .12 * panel - .09 * hem
        albedo[ys, xs, :3] = np.clip(base * variation[..., None] + (weave * .7 + grain)[..., None], 0, 255).astype('uint8')
        height = .003 * np.sin(x * np.pi / 2) * np.sin(y * np.pi / 2) + .013 * panel + .008 * hem
        gy, gx = np.gradient(height)
        normals = np.stack((-gx * 4, gy * 4, np.ones_like(x)), axis=-1)
        normals /= np.linalg.norm(normals, axis=-1)[..., None]
        detail[ys, xs, :3] = np.clip((normals * .5 + .5) * 255, 0, 255).astype('uint8')
        detail[ys, xs, 3] = np.clip(196 + weave * 3 + grain, 0, 255).astype('uint8')
    # The source normal bake is retained; attenuating its high contrast avoids stony facial highlights at match distance.
    ys, xs = slice(team * 2 * n, (team * 2 + 1) * n), slice(3 * n, 4 * n)
    normal = detail[ys, xs, :3].astype(float) / 127.5 - 1
    normal[..., :2] *= .55
    normal /= np.maximum(.001, np.linalg.norm(normal, axis=-1)[..., None])
    detail[ys, xs, :3] = np.clip((normal * .5 + .5) * 255, 0, 255).astype('uint8')
    ys, xs = slice((team * 2 + 1) * n, (team * 2 + 2) * n), slice(0, n)
    strand = np.sin(x * 1.7 + np.sin(y * .04) * 2) * 1.8 + rng.normal(0, .8, (n, n))
    fade = 5 * (1 - v) ** 2
    albedo[ys, xs, :3] = np.clip(np.array([32, 25, 22]) + strand[..., None] + fade[..., None], 0, 255).astype('uint8')
    detail[ys, xs, 3] = 185
Image.fromarray(albedo).save(OUT / 'player-albedo.png')
Image.fromarray(detail).save(OUT / 'player-normal-roughness.png')
print(OUT)
