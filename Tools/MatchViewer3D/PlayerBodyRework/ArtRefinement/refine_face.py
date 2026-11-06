"""Edit the existing connected facial surface in a separate Blender batch.

Run with --inspect to print the input cage domains before choosing masks.
"""
import json
import hashlib
import math
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[4]
BASE = ROOT / 'FootballEngine.Client/Assets/MatchViewer/PlayerBodyRework/Final-Body'
SOURCE = BASE / 'Art-Refinement/staging-source.blend'
META = BASE / 'Revision-Sol/final-body-authoring.json'
OUT = BASE / 'Art-Refinement'

bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
scene = bpy.context.scene
scene.frame_set(1)
body = bpy.data.objects['FINAL_Body_Connected']
rig = bpy.data.objects['FINAL_Rig']
mesh = body.data
names = json.loads(META.read_text())['regionNames']
regions = mesh.attributes['Anatomical_region_id'].data
groups = {g.index: g.name for g in body.vertex_groups}

def inspect():
    for name in ('head', 'lip_border', 'lip_vermilion', 'lip_contact', 'mouth_cavity', 'torso'):
        points = [v for v in mesh.vertices if names[regions[v.index].value] == name]
        bounds = [[round(min(v.co[a] for v in points), 5), round(max(v.co[a] for v in points), 5)] for a in range(3)]
        influences = sorted({groups[g.group] for v in points for g in v.groups if g.weight > .00001})
        print('REGION', name, len(points), bounds, influences, flush=True)
    for z in (1.575, 1.595, 1.615, 1.625, 1.64, 1.652, 1.664, 1.68, 1.695):
        points = [v.co for v in mesh.vertices if names[regions[v.index].value] == 'head' and abs(v.co.z-z)<.0015]
        if points:
            print('SECTION', z, 'n', len(points), 'x', round(min(p.x for p in points),4), round(max(p.x for p in points),4), 'y', round(min(p.y for p in points),4),round(max(p.y for p in points),4), flush=True)
    print('RIG', len(rig.data.bones), 'UV', [u.name for u in mesh.uv_layers], flush=True)

inspect()

if '--inspect' not in sys.argv:
    allowed = {'head', 'lip_border', 'lip_vermilion', 'lip_contact', 'mouth_cavity'}
    original = [v.co.copy() for v in mesh.vertices]

    def smooth(x):
        x = min(1.0, max(0.0, x))
        return x*x*(3.0-2.0*x)

    def band(x, low, full_low, full_high, high):
        return smooth((x-low)/(full_low-low)) * (1.0-smooth((x-full_high)/(high-full_high)))

    changed = []
    for v in mesh.vertices:
        region = names[regions[v.index].value]
        if region not in allowed:
            continue
        x, y, z = v.co
        ax = abs(x)
        # Restrict the work to the head-weighted surface, not the shared neck region.
        hw = sum(g.weight for g in v.groups if groups[g.group] == 'head')
        gate = smooth((hw-.8)/.19)
        front = 1.0-smooth((y+.070)/.030)

        # Reduce the tip's projection while retaining a distinct, broader nasal base.
        tip = band(ax, -.001, .000, .008, .023) * band(z, 1.650, 1.659, 1.667, 1.678)
        ala = band(ax, .006, .012, .018, .029) * band(z, 1.641, 1.649, 1.658, 1.668)
        dy = .0055*tip*front - .0024*ala*front

        # A broad muzzle-to-cheek transition and a shallow mentolabial plane.
        muzzle = band(ax, -.001, .000, .022, .047) * band(z, 1.628, 1.636, 1.643, 1.651)
        chin_break = band(ax, -.001, .000, .026, .047) * band(z, 1.603, 1.610, 1.615, 1.622)
        cheek = band(ax, .023, .034, .047, .064) * band(z, 1.621, 1.633, 1.645, 1.658)
        dy += (-.0006*muzzle + .0008*chin_break - .0010*cheek)*front

        # Taper only the posterior jaw silhouette, leaving the neck and skull intact.
        jaw = band(z, 1.567, 1.582, 1.614, 1.637)
        posterior = smooth((y-.006)/.065)
        dy -= .0065*jaw*posterior
        v.co.y += gate*dy
        if (v.co-original[v.index]).length > 1e-8:
            changed.append(v.index)

    mesh.update()
    assert changed and all(names[regions[i].value] in allowed for i in changed)
    assert all((mesh.vertices[i].co-original[i]).length < 1e-8 for i in range(len(original)) if names[regions[i].value] not in allowed)
    assert len(rig.data.bones) == 60
    scene.frame_set(1)
    path = OUT / 'face-refinement.blend'
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(path))
    record = {'source': str(SOURCE), 'result': str(path), 'sourceSha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
              'resultSha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'changedVertices': len(changed),
              'changedByRegion': {r: sum(names[regions[i].value] == r for i in changed) for r in sorted(allowed)},
              'maxDisplacementMm': max((mesh.vertices[i].co-original[i]).length for i in changed)*1000,
              'untouchedOtherRegions': True, 'bones': len(rig.data.bones), 'uvLayers': [u.name for u in mesh.uv_layers]}
    (OUT/'face-edit.json').write_text(json.dumps(record, indent=2))
    print('REFINEMENT', json.dumps(record), flush=True)
