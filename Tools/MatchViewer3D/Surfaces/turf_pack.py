"""Turn the turf render passes into the surface pack's original turf sources (PNG, 1 m tile).

    python3 Tools/MatchViewer3D/Surfaces/turf_pack.py
"""
from pathlib import Path

import Imath
import numpy as np
import OpenEXR
from PIL import Image

HERE = Path(__file__).resolve().parent


def channel(exr, name):
    dw = exr.header()["dataWindow"]
    w, h = dw.max.x - dw.min.x + 1, dw.max.y - dw.min.y + 1
    return np.frombuffer(exr.channel(name, Imath.PixelType(Imath.PixelType.FLOAT)), np.float32).reshape(h, w)


def rgb(name):
    exr = OpenEXR.InputFile(str(HERE / "_passes" / f"{name}.exr"))
    return np.stack([channel(exr, c) for c in "RGB"], -1)


def main():
    col = rgb("albedo")
    nrm = rgb("normal") * 2 - 1
    ao = rgb("ao")[..., 0]
    # Top-down ortho camera, image up = +Y: world normals are the OpenGL tangent-space normal.
    nrm /= np.maximum(np.linalg.norm(nrm, axis=-1, keepdims=True), 1e-6)
    srgb = np.where(col <= 0.0031308, col * 12.92, 1.055 * np.power(np.clip(col, 0, None), 1 / 2.4) - 0.055)
    Image.fromarray((np.clip(srgb, 0, 1) * 255 + 0.5).astype(np.uint8)).save(HERE / "turf_albedo.png")
    Image.fromarray(((nrm * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)).save(HERE / "turf_normal.png")
    # Shaded crevices between blades are also rougher (dust, thatch): AO drives roughness up slightly.
    arm = np.stack([ao, 0.8 + 0.15 * (1 - ao), np.zeros_like(ao)], -1)
    Image.fromarray((np.clip(arm, 0, 1) * 255 + 0.5).astype(np.uint8)).save(HERE / "turf_arm.png")
    print("turf sources written", col.shape)


main()
