"""Install the staged ProAthlete pack as the live MatchViewer player, keeping a restorable backup.

python3 Tools/MatchViewer3D/ProAthlete/promote.py [target-assets-dir ...]
Without arguments the target is the asset root's FootballEngine.Client/Assets/MatchViewer.
"""
import datetime, hashlib, importlib.util, json, shutil, sys
from pathlib import Path

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('pa_common', Path(__file__).with_name('common.py'))
C = importlib.util.module_from_spec(spec); spec.loader.exec_module(C)

manifest = json.loads((C.OUT / 'player-motion.json').read_text())
files = ['player.glb', 'player-motion.json'] + [t['path'] for t in manifest['textures']]
files += [m['path'] for chain in manifest['mips'].values() for m in chain]
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
# Refuse to install a pack whose manifest no longer describes its files.
assert sha(C.OUT / 'player.glb') == manifest['file']['sha256'] == manifest['rigGeometry']['glbSha256']
for t in manifest['textures'] + [m for chain in manifest['mips'].values() for m in chain]:
    assert sha(C.OUT / t['path']) == t['sha256'], t['path']

targets = [Path(a) for a in sys.argv[1:]] or [C.ASSETS]
stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
for target in targets:
    backup = C.ASSETS / 'PlayerBodyRework' / f'Live-Backup-{stamp}' / target.parent.parent.parent.name
    backup.mkdir(parents=True, exist_ok=True)
    for path in target.glob('player*'):
        if path.is_file() and path.suffix in ('.glb', '.json', '.png'):
            shutil.copy2(path, backup / path.name)
    for name in files:
        shutil.copy2(C.OUT / name, target / name)
    print('PROMOTED', target, 'backup', backup, flush=True)
