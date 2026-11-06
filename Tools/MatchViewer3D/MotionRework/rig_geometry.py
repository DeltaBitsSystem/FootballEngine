#!/usr/bin/env python3
"""Derive the runtime contact geometry from the unchanged first-party GLB."""
import hashlib
import json
import math
import pathlib
import struct
import sys
import numpy as np


def node_matrix(node):
    if 'matrix' in node:
        return np.array(node['matrix'], dtype=float).reshape((4, 4), order='F')
    x, y, z, w = node.get('rotation', [0, 0, 0, 1])
    if not math.isclose(x*x + y*y + z*z + w*w, 1.0, abs_tol=1e-5):
        raise ValueError('non-unit node rotation')
    rotation = np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w), 0],
                         [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w), 0],
                         [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y), 0],
                         [0, 0, 0, 1]], dtype=float)
    scale = np.diag([*node.get('scale', [1, 1, 1]), 1])
    translation = np.eye(4)
    translation[:3, 3] = node.get('translation', [0, 0, 0])
    return translation @ rotation @ scale


def derive(path):
    data = path.read_bytes()
    magic, version, length = struct.unpack_from('<4sII', data)
    if (magic, version, length) != (b'glTF', 2, len(data)):
        raise ValueError('invalid GLB header')
    json_length, json_type = struct.unpack_from('<II', data, 12)
    if json_type != 0x4E4F534A:
        raise ValueError('missing GLB JSON chunk')
    gltf = json.loads(data[20:20 + json_length])
    bin_header = 20 + json_length
    bin_length, bin_type = struct.unpack_from('<II', data, bin_header)
    if bin_type != 0x004E4942 or bin_header + 8 + bin_length > len(data):
        raise ValueError('missing GLB binary chunk')
    binary = memoryview(data)[bin_header + 8:bin_header + 8 + bin_length]
    nodes = gltf['nodes']
    parents = {}
    for parent, node in enumerate(nodes):
        for child in node.get('children', []):
            if child in parents:
                raise ValueError('node has multiple parents')
            parents[child] = parent
    def world(index, seen=frozenset()):
        if index in seen:
            raise ValueError('cyclic node graph')
        local = node_matrix(nodes[index])
        return world(parents[index], seen | {index}) @ local if index in parents else local

    boot_node = next(i for i, node in enumerate(nodes) if node.get('name') == 'FE Boots')
    boot_world = world(boot_node)
    skin = gltf['skins'][nodes[boot_node]['skin']]
    inverse_accessor = gltf['accessors'][skin['inverseBindMatrices']]
    inverse_view = gltf['bufferViews'][inverse_accessor['bufferView']]
    if inverse_accessor['componentType'] != 5126 or inverse_accessor['type'] != 'MAT4':
        raise ValueError('unsupported inverse bind accessor')
    base = inverse_view.get('byteOffset', 0) + inverse_accessor.get('byteOffset', 0)
    inverse_stride = inverse_view.get('byteStride', 64)
    if inverse_view.get('buffer', 0) != 0 or inverse_stride < 64 or inverse_accessor['count'] != len(skin['joints']) or not skin['joints']:
        raise ValueError('invalid inverse bind cardinality or stride')
    inverse_end = base + (inverse_accessor['count'] - 1) * inverse_stride + 64
    if inverse_end > len(binary) or inverse_end > inverse_view.get('byteOffset', 0) + inverse_view['byteLength']:
        raise ValueError('inverse bind accessor outside buffer')
    for index, joint in enumerate(skin['joints']):
        inverse = np.array(struct.unpack_from('<16f', binary, base + index * inverse_stride)).reshape((4, 4), order='F')
        if not np.allclose(world(joint) @ inverse, boot_world, atol=1e-4):
            raise ValueError(f'unsupported non-rest skin transform at {nodes[joint].get("name")}')
    mesh = next(m for m in gltf['meshes'] if m.get('name') == 'FE Boots Mesh')
    primitive, = mesh['primitives']
    accessor = gltf['accessors'][primitive['attributes']['POSITION']]
    if accessor['componentType'] != 5126 or accessor['type'] != 'VEC3' or 'sparse' in accessor:
        raise ValueError('unsupported boot position accessor')
    view = gltf['bufferViews'][accessor['bufferView']]
    stride = view.get('byteStride', 12)
    start = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    if view.get('buffer', 0) != 0 or accessor['count'] <= 0 or stride < 12 or start < 0 or start + (accessor['count'] - 1) * stride + 12 > view.get('byteOffset', 0) + view['byteLength']:
        raise ValueError('boot position accessor outside buffer view')
    if start + (accessor['count'] - 1) * stride + 12 > len(binary):
        raise ValueError('boot position accessor outside binary chunk')
    vertices = [struct.unpack_from('<3f', binary, start + i * stride) for i in range(accessor['count'])]
    vertices = [(boot_world @ np.array([*v, 1.0]))[:3].tolist() for v in vertices]
    if not all(math.isfinite(c) for v in vertices for c in v):
        raise ValueError('nonfinite boot vertex')
    sockets = {}
    for side, sign in [('L', 1), ('R', -1)]:
        points = [v for v in vertices if v[0] * sign > 0]
        if not points or any(v[0] * sign <= 0 for v in points):
            raise ValueError(f'no boot vertices on {side}')
        sole_y = min(v[1] for v in points)
        sole = [v for v in points if abs(v[1] - sole_y) < 1e-5]
        front_z = max(v[2] for v in points)
        front = [v for v in points if abs(v[2] - front_z) < 1e-5]
        if len(sole) < 3 or len(front) < 2:
            raise ValueError(f'insufficient sole/front geometry for {side}')
        if max(v[0] for v in sole) - min(v[0] for v in sole) < 1e-4 or max(v[2] for v in sole) - min(v[2] for v in sole) < 1e-4:
            raise ValueError(f'degenerate sole footprint for {side}')
        midpoint = lambda values: (min(values) + max(values)) / 2
        sockets[side] = {
            'groundWorldBind': [midpoint([v[0] for v in sole]), sole_y, midpoint([v[2] for v in sole])],
            'ballWorldBind': [midpoint([v[0] for v in front]), midpoint([v[1] for v in front]), front_z],
        }
    supports = [
        {'support': f'{helper}.{side}', 'source': f'{source}.{side}', 'weight': 0.5}
        for side in ('L', 'R')
        for helper, source in [('hip_support', 'thigh'), ('knee_support', 'shin'),
                               ('elbow_support', 'forearm'), ('axilla_support', 'upper_arm')]
    ]
    names = {node.get('name'): i for i, node in enumerate(nodes)}
    for rule in supports:
        source, support = names[rule['source']], names[rule['support']]
        if source not in parents or parents.get(source) != parents.get(support):
            raise ValueError(f'support and source must have the same parent: {rule}')
    return {'version': 1, 'glbSha256': hashlib.sha256(data).hexdigest(), 'sockets': sockets, 'supports': supports}


if __name__ == '__main__':
    if len(sys.argv) not in (3, 4):
        raise SystemExit('usage: rig_geometry.py player.glb output.json [manifest.json]')
    result = derive(pathlib.Path(sys.argv[1]))
    pathlib.Path(sys.argv[2]).write_text(json.dumps(result, indent=2, sort_keys=True) + '\n')
    if len(sys.argv) == 4:
        manifest_path = pathlib.Path(sys.argv[3])
        manifest = json.loads(manifest_path.read_text())
        if manifest['file']['sha256'].lower() != result['glbSha256']:
            raise ValueError('manifest GLB hash does not match the source')
        manifest['schemaVersion'] = 3
        manifest['rigSchemaVersion'] = 2
        manifest['rigGeometry'] = result
        manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + '\n')
