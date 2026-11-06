"""Finalize and optionally integrate the isolated original-human package."""
from pathlib import Path
import importlib.util, json, shutil, sys

ROOT = Path(__file__).resolve().parents[3]
BASE = ROOT / 'FootballEngine.Client/Assets/MatchViewer'
OUT = BASE / 'PlayerBodyRework/Astra-Human'
def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module
load('athlete_mips', ROOT / 'Tools/MatchViewer3D/build_athlete_mips.py').build(OUT)
geometry = load('rig_geometry', Path(__file__).with_name('rig_geometry.py')).derive(OUT / 'player.glb')
manifest = json.loads((OUT / 'player-motion.json').read_text())
manifest.update(schemaVersion=3, rigSchemaVersion=2, rigGeometry=geometry)
(OUT / 'player-motion.json').write_text(json.dumps(manifest, indent=2))
if '--integrate' in sys.argv:
    for source in OUT.glob('player*'):
        if source.is_file():
            shutil.copy2(source, BASE / source.name)
print('Verified rig geometry and texture mips', OUT)
