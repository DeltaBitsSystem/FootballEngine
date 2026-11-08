"""Original shirt typeface: monoline athletic glyphs defined as strokes, rasterised to an SDF atlas.

    python3 Tools/MatchViewer3D/Kits/build_shirt_font.py [--preview OUT.png]

Every glyph is a list of stroke polylines designed here (no font file is read). The signed distance is exact:
the distance to the nearest stroke segment minus the half stroke width, so the runtime can draw sharp numbers
and surnames at any size, with outlines, from one 8-bit channel.
Output: FootballEngine.Client/Assets/MatchViewer/Kits/{shirt-font-sdf.png, shirt-font.json}.
"""

import hashlib
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "FootballEngine.Client", "Assets", "MatchViewer", "Kits")

HALF = 0.075  # half stroke width in em units: a heavy athletic weight readable at broadcast distance
L, R, B, T, M = 0.08, 0.54, 0.0, 1.0, 0.5
CX = (L + R) / 2
CELL_Y0, CELL_Y1 = -0.12, 1.32  # room for accents above the cap height
PX = 64  # atlas cell height in pixels
RANGE = 0.14  # SDF encodes +-RANGE em around the edge


def arc(cx, cy, rx, ry, a0, a1, n=24):
    return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def line(*pts):
    return list(pts)


def oval(cx, cy, rx, ry):
    return arc(cx, cy, rx, ry, 0, 360, 48)


W = 0.72  # width of M and W
ACUTE = [line((CX - 0.05, 1.08), (CX + 0.09, 1.22))]
GLYPHS = {
    "0": [arc(CX, T - 0.23, 0.23, 0.23, 0, 180) + arc(CX, B + 0.23, 0.23, 0.23, 180, 360) + [(CX + 0.23, T - 0.23)]],
    "1": [line((CX + 0.06, B), (CX + 0.06, T), (CX - 0.14, T - 0.16))],
    "2": [arc(CX, T - 0.23, 0.23, 0.23, 160, -20) + [(L, B), (R, B)]],
    "3": [arc(CX, T - 0.21, 0.21, 0.21, 150, -90) + arc(CX, B + 0.24, 0.24, 0.24, 90, -150)],
    "4": [line((R - 0.07, B), (R - 0.07, T), (L, B + 0.3), (R + 0.02, B + 0.3))],
    "5": [line((R, T), (L + 0.03, T), (L, M + 0.08)) + arc(CX, B + 0.26, 0.24, 0.27, 125, -145)],
    "6": [arc(CX, T - 0.25, 0.23, 0.25, 35, 180) + arc(CX, B + 0.25, 0.23, 0.25, 180, 540)],
    "7": [line((L, T), (R, T), (CX - 0.04, B))],
    "8": [oval(CX, T - 0.2, 0.19, 0.2), oval(CX, B + 0.25, 0.23, 0.25)],
    "9": [arc(CX, T - 0.25, 0.23, 0.25, 0, 360, 48) + arc(CX, B + 0.25, 0.23, 0.25, 0, -145)],
    "A": [line((L, B), (CX, T), (R, B)), line((L + 0.1, B + 0.32), (R - 0.1, B + 0.32))],
    "B": [line((L, B), (L, T), (R - 0.2, T)) + arc(R - 0.2, T - 0.24, 0.2, 0.24, 90, -90) + [(L, M + 0.02)],
          line((L, M + 0.02), (R - 0.17, M + 0.02)) + arc(R - 0.17, B + 0.26, 0.21, 0.24, 90, -90) + [(L, B)]],
    "C": [arc(CX + 0.03, M, 0.25, 0.5, 45, 315)],
    "D": [line((L, B), (L, T), (CX - 0.04, T)) + arc(CX - 0.04, M, 0.27, 0.5, 90, -90) + [(L, B)]],
    "E": [line((R, T), (L, T), (L, B), (R, B)), line((L, M), (R - 0.08, M))],
    "F": [line((R, T), (L, T), (L, B)), line((L, M), (R - 0.1, M))],
    "G": [arc(CX + 0.03, M, 0.25, 0.5, 45, 335) + [(R, M - 0.04), (CX + 0.06, M - 0.04)]],
    "H": [line((L, B), (L, T)), line((R, B), (R, T)), line((L, M), (R, M))],
    # Monoline I: serifs would be clipped by its narrow advance and read as a joined "IJ".
    "I": [line((0.2, B), (0.2, T))],
    "J": [line((R - 0.05, T), (R - 0.05, B + 0.23)) + arc(CX, B + 0.23, 0.23, 0.23, 0, -180)],
    "K": [line((L, B), (L, T)), line((R, T), (L, M - 0.06)), line((L + 0.12, M + 0.04), (R, B))],
    "L": [line((L, T), (L, B), (R, B))],
    "M": [line((L, B), (L, T), (W / 2, M - 0.12), (W - L, T), (W - L, B))],
    "N": [line((L, B), (L, T), (R, B), (R, T))],
    "O": [oval(CX, M, 0.24, 0.5)],
    "P": [line((L, B), (L, T), (R - 0.2, T)) + arc(R - 0.2, T - 0.24, 0.2, 0.24, 90, -90) + [(L, M - 0.04)]],
    "Q": [oval(CX, M, 0.24, 0.5), line((CX + 0.06, B + 0.2), (R + 0.03, B - 0.04))],
    "R": [line((L, B), (L, T), (R - 0.2, T)) + arc(R - 0.2, T - 0.24, 0.2, 0.24, 90, -90) + [(L, M - 0.04)],
          line((CX, M - 0.04), (R, B))],
    "S": [arc(CX, T - 0.22, 0.22, 0.22, 20, 270) + arc(CX, B + 0.24, 0.24, 0.24, 90, -160)],
    "T": [line((L - 0.03, T), (R + 0.03, T)), line((CX, T), (CX, B))],
    "U": [line((L, T), (L, B + 0.24)) + arc(CX, B + 0.24, 0.23, 0.24, 180, 360) + [(R, T)]],
    "V": [line((L, T), (CX, B), (R, T))],
    "W": [line((L, T), (W * 0.27, B), (W / 2, M + 0.12), (W * 0.73, B), (W - L, T))],
    "X": [line((L, T), (R, B)), line((R, T), (L, B))],
    "Y": [line((L, T), (CX, M)), line((R, T), (CX, M), (CX, B))],
    "Z": [line((L, T), (R, T), (L, B), (R, B))],
    "-": [line((L + 0.04, M - 0.05), (0.36, M - 0.05))],
    ".": [line((0.13, B + 0.02), (0.13, B + 0.03))],
    "'": [line((0.14, T), (0.11, T - 0.2))],
    " ": [],
}
# Spanish and Portuguese surnames: accents are strokes added to the base letters.
for base, mark in (("A", "Á"), ("E", "É"), ("O", "Ó"), ("U", "Ú")):
    GLYPHS[mark] = GLYPHS[base] + ACUTE
GLYPHS["Í"] = GLYPHS["I"] + [line((0.15, 1.08), (0.29, 1.22))]
GLYPHS["Ñ"] = GLYPHS["N"] + [[(L + 0.02, 1.12), (L + 0.14, 1.2), (R - 0.14, 1.1), (R - 0.02, 1.18)]]
GLYPHS["Ü"] = GLYPHS["U"] + [line((CX - 0.12, 1.12), (CX - 0.12, 1.13)), line((CX + 0.12, 1.12), (CX + 0.12, 1.13))]
ADVANCE = {"M": W + 0.06, "W": W + 0.06, "I": 0.4, "Í": 0.4, "1": 0.5, " ": 0.36, ".": 0.25, "'": 0.25, "-": 0.5}


def segments(strokes):
    segs = []
    for s in strokes:
        for a, b in zip(s, s[1:]):
            segs.append((a, b))
        if len(s) == 1:
            segs.append((s[0], s[0]))
    return np.array(segs, dtype=np.float64).reshape(-1, 2, 2) if segs else np.zeros((0, 2, 2))


def sdf(strokes, width):
    h = PX
    w = max(8, int(round(PX * width / (CELL_Y1 - CELL_Y0))))
    ys = CELL_Y1 - (np.arange(h) + 0.5) / h * (CELL_Y1 - CELL_Y0)
    xs = (np.arange(w) + 0.5) / w * width
    px = np.stack(np.meshgrid(xs, ys), -1).reshape(-1, 2)
    segs = segments(strokes)
    if len(segs) == 0:
        d = np.full(len(px), RANGE)
    else:
        a, b = segs[:, 0], segs[:, 1]
        ab = b - a
        denom = np.maximum((ab * ab).sum(-1), 1e-12)
        ap = px[:, None, :] - a[None]
        t = np.clip((ap * ab[None]).sum(-1) / denom[None], 0, 1)
        nearest = a[None] + t[..., None] * ab[None]
        d = np.sqrt(((px[:, None, :] - nearest) ** 2).sum(-1)).min(1) - HALF
    v = np.clip(0.5 - d / (2 * RANGE), 0, 1)
    return (v.reshape(h, w) * 255 + 0.5).astype(np.uint8)


def build():
    os.makedirs(OUT, exist_ok=True)
    cells = []
    for ch, strokes in GLYPHS.items():
        adv = ADVANCE.get(ch, R + 0.12)
        width = max(adv, 0.3)
        cells.append((ch, sdf(strokes, width), adv, width))
    atlas_w = 1024
    x = y = 0
    placed = []
    for ch, img, adv, width in cells:
        if x + img.shape[1] > atlas_w:
            x, y = 0, y + PX
        placed.append((ch, x, y, img, adv, width))
        x += img.shape[1] + 2
    atlas = np.zeros((y + PX, atlas_w), np.uint8)
    glyphs = {}
    for ch, gx, gy, img, adv, width in placed:
        atlas[gy:gy + PX, gx:gx + img.shape[1]] = img
        glyphs[ch] = {"x": gx, "y": gy, "w": img.shape[1], "h": PX, "advanceEm": round(adv, 4), "cellWidthEm": round(width, 4)}
    png = os.path.join(OUT, "shirt-font-sdf.png")
    Image.fromarray(atlas, "L").save(png)
    meta = {"schema": 1, "cellEm": [CELL_Y0, CELL_Y1], "capHeightEm": 1.0, "baselineEm": 0.0,
            "sdfRangeEm": RANGE, "encoding": "value = 0.5 - distance / (2 * range); inside > 0.5",
            "glyphs": glyphs, "files": {"shirt-font-sdf.png": hashlib.sha256(open(png, "rb").read()).hexdigest()},
            "source": "Tools/MatchViewer3D/Kits/build_shirt_font.py",
            "provenance": "Original FootballEngine typeface: every glyph is a stroke design in this script; no font file is used."}
    json.dump(meta, open(os.path.join(OUT, "shirt-font.json"), "w"), indent=1)
    return atlas, glyphs


def preview(path, atlas, glyphs, texts):
    scale = 3
    rows = []
    for text in texts:
        width = sum(glyphs[c]["w"] for c in text) * scale + 40
        canvas = np.zeros((PX * scale + 20, width), np.float32)
        x = 20
        for c in text:
            g = glyphs[c]
            cell = atlas[g["y"]:g["y"] + PX, g["x"]:g["x"] + g["w"]].astype(np.float32) / 255
            big = np.array(Image.fromarray((cell * 255).astype(np.uint8)).resize((g["w"] * scale, PX * scale), Image.BILINEAR)) / 255
            canvas[10:10 + PX * scale, x:x + g["w"] * scale] = np.maximum(canvas[10:10 + PX * scale, x:x + g["w"] * scale], big)
            x += int(g["advanceEm"] / g["cellWidthEm"] * g["w"] * scale)
        # Threshold at 0.5 with a one-pixel ramp: what the runtime shader does with the SDF.
        rows.append(np.clip((canvas - 0.5) * 12 + 0.5, 0, 1))
    w = max(r.shape[1] for r in rows)
    img = np.ones((sum(r.shape[0] for r in rows), w), np.float32) * 0.0
    y = 0
    for r in rows:
        img[y:y + r.shape[0], :r.shape[1]] = r
        y += r.shape[0]
    Image.fromarray((img * 255).astype(np.uint8)).save(path)


if __name__ == "__main__":
    atlas, glyphs = build()
    if "--preview" in sys.argv:
        preview(sys.argv[sys.argv.index("--preview") + 1], atlas, glyphs,
                ["0123456789", "ABCDEFGHIJKLM", "NOPQRSTUVWXYZ", "FERNÁNDEZ 7", "MUÑOZ 23 GÜEMES", "O'NEILL-DÍAZ 10"])
    print("glyphs", len(glyphs), "atlas", atlas.shape)
