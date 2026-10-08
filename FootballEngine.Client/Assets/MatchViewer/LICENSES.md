# Match Viewer Asset Licenses

The code-generated pitch and ball, and the player sprite generator, are implemented in this repository. Their entries in `manifest.json` identify their source and the project's internal license reference; the repository currently has no standalone root `LICENSE` file, so distribution rights still need a project-level legal confirmation.

`stadium.glb` is accompanied by its Blender source files and SHA-256 hashes in `manifest.json`. Its author, exact rights, and reproducible export settings have not been attested. It is development-only and must not be distributed until those facts are recorded and approved.

No external asset should be added without its exact source, SPDX identifier or full license text, attribution/redistribution terms, source and processed-file hashes, and a clean offline build path. Do not infer permission from the fact that a file is present in the repository.

## Anatomical player pack

`player.glb` and its texture atlases contain adaptations of MakeHuman's CC0 anatomical assets, with FootballEngine-authored equipment, rig adaptation and motion. Exact upstream revision, URLs, SHA-256 checksums and the vendored CC0 legal text are in `Sources/MakeHuman/README.md`, `sources.json` and `LICENSE.ASSETS.md`. No MakeHuman application code or third-party motion is included. `player-motion.json` pins the generated model and texture hashes; the authoring sources are shipped alongside them.

## Surface pack

`Surfaces/Sources/concrete_floor_02_*.jpg`: **Concrete Floor 02**, Rob Tuytel / Poly Haven, [asset](https://polyhaven.com/a/concrete_floor_02), **CC0-1.0**. [Poly Haven asset licence](https://polyhaven.com/license) confirms commercial use, modification and redistribution, including in products, without required attribution. CC0 legal text: https://creativecommons.org/publicdomain/zero/1.0/legalcode. Website/logo/sample-render rights are not included and those materials are not imported.

`Surfaces/Sources/Grass004_1K-PNG_*.png`: **Grass004**, ambientCG, [asset](https://ambientcg.com/a/Grass004), **CC0-1.0**. [ambientCG licence](https://docs.ambientcg.com/license/) expressly permits commercial use, modification, redistribution and inclusion of raw files in a video game without required attribution. The imported channels are Color, NormalGL, Roughness and AmbientOcclusion; no EA/FIFA assets or branding are imported. Source size is 1.4 × 1.4 metres. This is a procedural PBR asset, not a claimed scan of a professional pitch.

Exact download URLs, source SHA-256 hashes, processed-file SHA-256 hashes and channel/mip layout are in `Surfaces/manifest.json`. `Tools/MatchViewer3D/build_surface_pack.py` verifies pinned grass SHA-256s and published concrete MD5s before processing, calibrates grass colour, and builds offline GPU mip chains. Normal execution does not download; `--download` is an explicit asset-authoring operation. Blender scenes, existing player assets and stadium distribution rights are unchanged.
