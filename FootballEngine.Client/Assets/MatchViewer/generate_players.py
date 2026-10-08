"""Rebuild the project's own rig, motion library and sprites with Blender.

    blender --background --factory-startup --python generate_players.py -- --output-dir PATH

Uses the pinned CC0 anatomical sources in Sources/MakeHuman. Clothing, textures,
rig adaptation and motion are authored here; no proprietary assets or online generator.
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from player_authoring import main

if __name__ == "__main__":
    main()
