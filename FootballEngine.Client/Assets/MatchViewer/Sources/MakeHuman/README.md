# Anatomical source provenance

Source: [MakeHuman Community](https://github.com/makehumancommunity/makehuman), commit `a8bc2d54ff0ac92e78ff71431b1023eda42bf482`.

The hm08 base mesh and male/muscle targets explicitly state their release under CC0 in September 2020. The skeleton and weights declare `CC0` in their JSON metadata. `LICENSE.ASSETS.md` is the upstream CC0 license, not the application's code license. No MakeHuman application code is included.

Original contributors acknowledged by upstream: Manuel Bastioni; Data Collection AB; Joel Palmius; Jonas Hauquier. Exact original URLs and SHA-256 checksums for every vendored file are recorded in `sources.json` and verified by the authoring tool.

FootballEngine's `human_authoring.py` adapts the anatomy to the existing 22-joint rig, reshapes clothing, authors footwear and paints deterministic kit, skin, eye and hair atlases. The locomotion and action clips remain FootballEngine-authored. These are openly licensed anatomical derivatives, **not** a clean-room reconstruction of FIFA/Football Manager, a scan, or a likeness of a real player. Club colors are used without copying third-party crests or sponsors.
