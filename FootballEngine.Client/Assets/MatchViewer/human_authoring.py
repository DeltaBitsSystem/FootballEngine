"""CC0 anatomical topology adapted to FE's rig; original clothing and PBR painting.

The vendored sources are pinned and attributed in Sources/MakeHuman/sources.json.
No MakeHuman application code or proprietary game assets are used.
"""
import hashlib
import json
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector
from PIL import Image, ImageFilter

SOURCE = Path(__file__).resolve().parent / "Sources" / "MakeHuman"
NAMES = ["jersey", "shorts", "socks", "skin", "hair", "boots", "trim", "eyes"]
TILE = 1024


def read_anatomy():
    for entry in json.loads((SOURCE / "sources.json").read_text()):
        if hashlib.sha256((SOURCE / entry["file"]).read_bytes()).hexdigest() != entry["sha256"]:
            raise ValueError(f"Anatomical source checksum mismatch: {entry['file']}")
    positions, uvs, faces = [], [], []
    group = ""
    for line in (SOURCE / "base.obj").read_text().splitlines():
        parts = line.split()
        if not parts:
            continue
        if parts[0] == "v":
            positions.append(Vector(tuple(map(float, parts[1:4]))))
        elif parts[0] == "vt":
            uvs.append(tuple(map(float, parts[1:3])))
        elif parts[0] == "g":
            group = parts[1]
        elif parts[0] == "f" and group in ("body", "helper-l-eye", "helper-r-eye"):
            corners = [tuple(int(value) - 1 for value in item.split("/")[:2]) for item in parts[1:]]
            faces.append((group, corners))
    for name, amount in [("male.target", 1.0), ("muscle.target", 0.65)]:
        for line in (SOURCE / name).read_text().splitlines():
            if line and not line.startswith("#"):
                index, x, y, z = line.split()
                positions[int(index)] += Vector((float(x), float(y), float(z))) * amount
    # MakeHuman is decimetres, Y-up and +Z facing. FE authors in metres, Z-up and -Y facing.
    positions = [Vector((p.x * 0.1, -p.z * 0.1, p.y * 0.1)) for p in positions]
    skeleton = json.loads((SOURCE / "skeleton.json").read_text())
    weights = json.loads((SOURCE / "weights.json").read_text())["weights"]
    return positions, uvs, faces, skeleton, weights


def canonical_bone(name):
    suffix = name[-2:] if name.endswith((".L", ".R")) else ""
    stem = name[:-2] if suffix else name
    for prefix, target in [("upperarm", "upper_arm"), ("lowerarm", "forearm"),
                           ("upperleg", "thigh"), ("lowerleg", "shin"),
                           ("finger", "hand"), ("metacarpal", "hand"), ("wrist", "hand"),
                           ("toe", "toe"), ("foot", "foot"), ("clavicle", "clavicle"),
                           ("shoulder", "upper_arm")]:
        if stem.startswith(prefix):
            return target + suffix
    if stem.startswith("neck"):
        return "neck"
    if stem in ("root", "spine05", "spine04", "pelvis"):
        return "pelvis"
    if stem == "spine03":
        return "spine"
    if stem in ("spine01", "spine02", "breast"):
        return "chest"
    return "head"


def adapt_anatomy(rig, positions, skeleton, source_weights):
    def joint(name, end="head"):
        entry = skeleton["bones"][name][end]
        indices = skeleton["joints"][entry]
        return sum((positions[i] for i in indices), Vector()) / len(indices)

    ground = min(point.z for point in positions[:13380])
    anchors = [(ground, 0.0), (joint("upperleg01.L").z, 0.97),
               (joint("spine03").z, 1.11), (joint("spine02").z, 1.33),
               (joint("neck01").z, 1.52), (joint("head").z, 1.65)]
    anchors.sort()

    def torso(p):
        z = p.z
        for (a, b), (c, d) in zip(anchors, anchors[1:]):
            if z <= c:
                return Vector((p.x, p.y, b + (z - a) * (d - b) / (c - a)))
        return Vector((p.x, p.y, 1.65 + (z - anchors[-1][0]) * 1.03))

    transforms = {}
    for side in ("L", "R"):
        for source, end_source, target in [("upperarm01", "lowerarm01", "upper_arm"),
                                           ("lowerarm01", "wrist", "forearm"),
                                           ("upperleg01", "lowerleg01", "thigh"),
                                           ("lowerleg01", "foot", "shin")]:
            start, end = joint(source + "." + side), joint(end_source + "." + side)
            bone = rig.data.bones[target + "." + side]
            direction = end - start
            rotation = direction.rotation_difference(bone.tail_local - bone.head_local)
            transforms[target + "." + side] = (start, bone.head_local.copy(), direction.normalized(), rotation, bone.length / direction.length)
        for target, source in [("hand", "wrist"), ("foot", "foot"), ("toe", "toe1-1")]:
            # Fingers remain individually modelled but share the current gameplay hand bone.
            start = joint(source + "." + side)
            bone = rig.data.bones[target + "." + side]
            rotation = transforms[("forearm" if target == "hand" else "shin") + "." + side][3] if target == "hand" else None
            transforms[target + "." + side] = (start, bone.head_local.copy(), None, rotation, 1.0)

    weights = [{} for _ in positions]
    for name, entries in source_weights.items():
        target = canonical_bone(name)
        for index, value in entries:
            weights[index][target] = weights[index].get(target, 0.0) + value

    def deform(point, bone):
        if bone not in transforms:
            return torso(point)
        start, destination, direction, rotation, scale = transforms[bone]
        delta = point - start
        if direction is not None:
            along = direction * delta.dot(direction)
            delta += along * (scale - 1)
        if rotation is not None:
            delta = rotation @ delta
        return destination + delta

    adapted = []
    for index, point in enumerate(positions):
        influences = dict(sorted(weights[index].items(), key=lambda pair: pair[1], reverse=True)[:4])
        total = sum(influences.values())
        influences = {name: value / total for name, value in influences.items()} if total else {"head": 1.0}
        weights[index] = influences
        adapted.append(sum((deform(point, name) * value for name, value in influences.items()), Vector()))
    return adapted, weights


def paint_surface(points, name, away):
    x, y, z = points[..., 0], points[..., 1], points[..., 2]
    noise = np.sin(x * 1433 + np.sin(z * 671)) * np.sin(y * 1241 + z * 941)
    if name == "skin":
        color = np.broadcast_to([0.64, 0.40, 0.275], (*x.shape, 3)).copy()
        color += noise[..., None] * 0.011
        # Small regional changes avoid uniformly orange skin; lips and beard are painted on anatomical coordinates.
        front = np.clip((-y - 0.04) * 30, 0, 1)
        mouth = np.exp(-((x / 0.029) ** 6 + ((z - 1.617) / 0.007) ** 2)) * front
        color = color * (1 - mouth[..., None] * 0.5) + np.array([0.36, 0.13, 0.115]) * mouth[..., None] * 0.5
        beard = np.exp(-((x / 0.061) ** 6 + ((z - 1.589) / 0.027) ** 4)) * front
        color *= (1 - beard[..., None] * (0.14 + noise[..., None] * 0.025))
        blush = np.exp(-(((np.abs(x) - 0.052) / 0.018) ** 2 + ((z - 1.661) / 0.020) ** 2)) * front
        color[..., 0] += blush * 0.035
    elif name == "jersey":
        color = np.broadcast_to([0.91, 0.92, 0.88] if not away else [0.025, 0.13, 0.40], (*x.shape, 3)).copy()
        stripe = (np.abs(x + (z - 1.30) * 0.58) < 0.038) if not away else ((z > 1.285) & (z < 1.36))
        color[stripe] = [0.69, 0.022, 0.035] if not away else [0.96, 0.64, 0.045]
        collar = (z > 1.505) & (np.abs(x) < 0.08)
        color[collar] = [0.25, 0.025, 0.035] if not away else [0.92, 0.60, 0.025]
        badge = ((x + 0.09) / 0.016) ** 2 + ((z - 1.435) / 0.023) ** 2 < 1
        badge &= y < -0.07
        color[badge] = [0.90, 0.81, 0.65] if not away else [0.98, 0.70, 0.08]
        color += (np.sin(z * 1100) * np.sin(x * 1400))[..., None] * 0.012
        color *= (0.97 + 0.03 * np.sin(z * 70 + x * 25))[..., None]
    elif name == "shorts":
        color = np.broadcast_to([0.035, 0.043, 0.052] if not away else [0.022, 0.09, 0.26], (*x.shape, 3)).copy()
        seam = np.abs(np.abs(x) - 0.18) < 0.009
        color[seam] = [0.74, 0.035, 0.05] if not away else [0.94, 0.66, 0.07]
        color += noise[..., None] * 0.006
    elif name == "socks":
        color = np.broadcast_to([0.82, 0.84, 0.81] if not away else [0.025, 0.13, 0.35], (*x.shape, 3)).copy()
        stripe = ((z > 0.46) & (z < 0.478)) | ((z > 0.432) & (z < 0.44))
        color[stripe] = [0.69, 0.022, 0.035] if not away else [0.94, 0.66, 0.07]
        color += np.sin(z * 1500)[..., None] * 0.01
    elif name == "hair":
        color = np.broadcast_to([0.033, 0.022, 0.017], (*x.shape, 3)).copy()
        strands = np.sin(x * 1500 + z * 230 + np.sin(y * 30) * 4)
        color += strands[..., None] * 0.011
    elif name == "eyes":
        color = np.broadcast_to([0.81, 0.80, 0.75], (*x.shape, 3)).copy()
        centre = np.where(x > 0, 0.0294, -0.0294)
        radius = np.sqrt((x - centre) ** 2 + (z - 1.6905) ** 2)
        iris = radius < 0.006
        color[iris] = [0.12, 0.17, 0.095]
        color[radius < 0.0028] = [0.005, 0.007, 0.008]
    elif name == "trim":
        color = np.broadcast_to([0.73, 0.77, 0.75], (*x.shape, 3)).copy()
    else:
        color = np.broadcast_to([0.033, 0.043, 0.06], (*x.shape, 3)).copy()
        color += noise[..., None] * 0.008
    return np.clip(color, 0, 1)


def make_atlases(output, points, faces, face_uvs, materials):
    atlas = np.zeros((TILE * 4, TILE * 4, 4), dtype=np.uint8)
    detail = np.zeros_like(atlas)
    yy, xx = np.mgrid[0:TILE, 0:TILE]
    rng = np.random.default_rng(731)
    pores = rng.normal(0, 0.65, (TILE, TILE))
    for away in (False, True):
        for slot, name in enumerate(NAMES):
            image = np.full((TILE, TILE, 3), 80, dtype=np.uint8)
            coverage = np.zeros((TILE, TILE), dtype=np.uint8)
            for face, uv, material in zip(faces, face_uvs, materials):
                if material != name:
                    continue
                for index in range(1, len(face) - 1):
                    ids = [0, index, index + 1]
                    tex = np.array([uv[i] for i in ids]) * (TILE - 1)
                    tex[:, 1] = TILE - 1 - tex[:, 1]
                    minimum = np.maximum(np.floor(tex.min(axis=0)).astype(int), 0)
                    maximum = np.minimum(np.ceil(tex.max(axis=0)).astype(int), TILE - 1)
                    if np.any(maximum < minimum):
                        continue
                    grid_y, grid_x = np.mgrid[minimum[1]:maximum[1]+1, minimum[0]:maximum[0]+1]
                    a, b, c = tex
                    determinant = (b[1]-c[1])*(a[0]-c[0]) + (c[0]-b[0])*(a[1]-c[1])
                    if abs(determinant) < 1e-8:
                        continue
                    wa = ((b[1]-c[1])*(grid_x-c[0]) + (c[0]-b[0])*(grid_y-c[1])) / determinant
                    wb = ((c[1]-a[1])*(grid_x-c[0]) + (a[0]-c[0])*(grid_y-c[1])) / determinant
                    wc = 1 - wa - wb
                    inside = (wa >= -0.02) & (wb >= -0.02) & (wc >= -0.02)
                    xyz = np.array([points[face[i]] for i in ids])
                    interpolated = wa[..., None]*xyz[0] + wb[..., None]*xyz[1] + wc[..., None]*xyz[2]
                    rgb = (paint_surface(interpolated, name, away) * 255).astype(np.uint8)
                    image[grid_y[inside], grid_x[inside]] = rgb[inside]
                    coverage[grid_y[inside], grid_x[inside]] = 255
            # Padding protects UV seams under bilinear filtering; island backgrounds are not painted onto the body.
            for _ in range(6):
                expanded = np.array(Image.fromarray(coverage).filter(ImageFilter.MaxFilter(3)))
                for dy, dx in [(0, 1), (0, -1), (1, 0), (-1, 0)]:
                    shifted = np.roll(coverage, (dy, dx), (0, 1))
                    mask = (coverage == 0) & (shifted > 0)
                    image[mask] = np.roll(image, (dy, dx), (0, 1))[mask]
                coverage = expanded
            tile_y = (slot // 4 + (2 if away else 0)) * TILE
            tile_x = slot % 4 * TILE
            atlas[tile_y:tile_y+TILE, tile_x:tile_x+TILE, :3] = image
            atlas[tile_y:tile_y+TILE, tile_x:tile_x+TILE, 3] = 255
            textile = name in ("jersey", "shorts", "socks")
            strength = 2.0 if textile else (0.8 if name == "skin" else 1.0)
            nx = np.sin(xx * math.pi / 2) * strength + pores
            ny = np.sin(yy * math.pi / 2) * strength + pores
            normal = np.empty((TILE, TILE, 4), dtype=np.uint8)
            normal[..., 0] = np.clip(128 + nx, 0, 255)
            normal[..., 1] = np.clip(128 + ny, 0, 255)
            normal[..., 2] = 255
            roughness = {"skin": 0.57, "hair": 0.74, "boots": 0.39, "eyes": 0.20}.get(name, 0.86)
            normal[..., 3] = np.clip(roughness * 255 + pores * 2, 0, 255)
            detail[tile_y:tile_y+TILE, tile_x:tile_x+TILE] = normal
    albedo_path, detail_path = output / "player-albedo.png", output / "player-normal-roughness.png"
    Image.fromarray(atlas).save(albedo_path)
    Image.fromarray(detail).save(detail_path)
    return albedo_path, detail_path


def build_human(scene, rig, output):
    from athlete_equipment import build_layered
    return build_layered(scene, rig, output)
