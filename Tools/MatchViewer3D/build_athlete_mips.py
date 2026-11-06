"""Bake athlete colour/normal/roughness mips offline, without Blender or network access."""
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image


def reduce_four(image):
    return (image[::2, ::2] + image[1::2, ::2] + image[::2, 1::2] + image[1::2, 1::2]) * 0.25


def build(output):
    output = Path(output)
    manifest_path = output / 'player-motion.json'
    manifest = json.loads(manifest_path.read_text())
    for entry in manifest['textures']:
        data = (output / entry['path']).read_bytes()
        if hashlib.sha256(data).hexdigest() != entry['sha256']:
            raise ValueError(f"Unverified athlete source: {entry['path']}")

    colour = np.asarray(Image.open(output / 'player-albedo.png').convert('RGBA'), dtype=np.float32) / 255
    detail = np.asarray(Image.open(output / 'player-normal-roughness.png').convert('RGBA'), dtype=np.float32) / 255
    if colour.shape != (4096, 4096, 4) or detail.shape != colour.shape:
        raise ValueError('Athlete source atlases must be 4096-square RGBA')
    colour[..., :3] = np.where(colour[..., :3] <= 0.04045, colour[..., :3] / 12.92, ((colour[..., :3] + 0.055) / 1.055) ** 2.4)
    detail[..., :3] = detail[..., :3] * 2 - 1
    detail[..., :3] /= np.maximum(np.linalg.norm(detail[..., :3], axis=-1, keepdims=True), 1e-8)
    detail[..., 3] **= 4
    chains = {'albedo': [], 'normalRoughness': []}
    # Stop at 16 pixels so neither the home/away halves nor adjacent material tiles collapse together.
    for level in range(1, 9):
        colour = reduce_four(colour)
        detail = reduce_four(detail)
        length = np.linalg.norm(detail[..., :3], axis=-1, keepdims=True)
        detail[..., 3] = np.clip(detail[..., 3] + np.clip(1 - length[..., 0], 0, 1) * 0.5, 0, 1)
        detail[..., :3] /= np.maximum(length, 1e-8)
        encoded_colour = colour.copy()
        encoded_colour[..., :3] = np.where(colour[..., :3] <= 0.0031308, colour[..., :3] * 12.92, 1.055 * colour[..., :3] ** (1 / 2.4) - 0.055)
        encoded_detail = detail.copy()
        encoded_detail[..., :3] = detail[..., :3] * 0.5 + 0.5
        encoded_detail[..., 3] **= 0.25
        for kind, encoded, stem in [('albedo', encoded_colour, 'player-albedo'), ('normalRoughness', encoded_detail, 'player-normal-roughness')]:
            path = output / f'{stem}-mip-{level:02}.png'
            Image.fromarray(np.clip(encoded * 255 + 0.5, 0, 255).astype(np.uint8)).save(path)
            chains[kind].append(dict(path=path.name, width=encoded.shape[1], height=encoded.shape[0], sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    manifest['schemaVersion'] = 2
    manifest['atlas'] = dict(columns=4, homeRows=2, materials=['jersey', 'shorts', 'socks', 'skin', 'hair', 'boots', 'trim', 'eyes'])
    manifest['mipFiltering'] = 'linear-light albedo; normalized tangent normals; GGX alpha-squared roughness with accumulated normal variance'
    manifest['mips'] = chains
    manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + '\n')
    print(f'Baked eight verified mip levels per athlete atlas: {output}')


if __name__ == '__main__':
    build(Path(__file__).resolve().parents[2] / 'FootballEngine.Client/Assets/MatchViewer')
