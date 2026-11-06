"""Shared paths and contract constants for the ProAthlete player pipeline."""
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
# Staged packages are large binaries kept out of git; FE_ASSET_ROOT lets a worktree write into the main checkout.
ASSET_ROOT = Path(os.environ.get('FE_ASSET_ROOT', ROOT))
ASSETS = ASSET_ROOT / 'FootballEngine.Client/Assets/MatchViewer'
# Variants (body studies) build from their own parameter file into their own staging folder.
PARAMS_PATH = Path(os.environ.get('FE_ATHLETE_PARAMS', Path(__file__).with_name('athlete.json')))
OUT = ASSETS / 'PlayerBodyRework' / os.environ.get('FE_ATHLETE_OUT', 'Pro-Athlete')
OUT.mkdir(parents=True, exist_ok=True)

# Core's physical proxy fixes these bind lengths at 1.8 m; RigDefinition.conform rejects drift above 5 mm.
THIGH_M = 0.4402556
SHIN_M = 0.4352585
STATURE_M = 1.80

ATLAS_MATERIALS = ['jersey', 'shorts', 'socks', 'skin', 'hair', 'boots', 'trim', 'eyes']

SIDES = ('L', 'R')
FINGERS = ('thumb', 'index', 'middle', 'ring', 'little')
SUPPORTS = [('hip_support', 'thigh'), ('knee_support', 'shin'),
            ('elbow_support', 'forearm'), ('axilla_support', 'upper_arm')]


def runtime_bones():
    """Runtime joint names with parents, in an order where every parent precedes its children."""
    bones = [('root', None), ('pelvis', 'root'), ('spine', 'pelvis'), ('chest', 'spine'),
             ('neck', 'chest'), ('head', 'neck')]
    for s in SIDES:
        bones += [(f'clavicle.{s}', 'chest'), (f'upper_arm.{s}', f'clavicle.{s}'),
                  (f'forearm.{s}', f'upper_arm.{s}'), (f'hand.{s}', f'forearm.{s}')]
        for f in FINGERS:
            parent = f'hand.{s}'
            for i in (1, 2, 3):
                bones.append((f'{f}_0{i}.{s}', parent))
                parent = f'{f}_0{i}.{s}'
        bones += [(f'elbow_support.{s}', f'upper_arm.{s}'), (f'axilla_support.{s}', f'clavicle.{s}')]
    for s in SIDES:
        bones += [(f'thigh.{s}', 'pelvis'), (f'shin.{s}', f'thigh.{s}'), (f'foot.{s}', f'shin.{s}'),
                  (f'toe.{s}', f'foot.{s}'), (f'knee_support.{s}', f'thigh.{s}'), (f'hip_support.{s}', 'pelvis')]
    return bones


def ge_name(bone):
    """MPFB game_engine bone supplying the joint placement and skin weights for one runtime bone."""
    fixed = {'root': 'Root', 'pelvis': 'pelvis', 'spine': 'spine_01', 'chest': 'spine_03',
             'neck': 'neck_01', 'head': 'head'}
    if bone in fixed:
        return fixed[bone]
    base, side = bone.rsplit('.', 1)
    s = side.lower()
    limb = {'clavicle': 'clavicle', 'upper_arm': 'upperarm', 'forearm': 'lowerarm', 'hand': 'hand',
            'thigh': 'thigh', 'shin': 'calf', 'foot': 'foot', 'toe': 'ball'}
    if base in limb:
        return f'{limb[base]}_{s}'
    finger, index = base.rsplit('_', 1)
    return f"{'pinky' if finger == 'little' else finger}_{index}_{s}"
