# Offline stadium surface pack

Two 1024² material layers with complete mip chains:

0. ambientCG **Grass004**, CC0-1.0, at the source's **1.4 metres per tile**. Dense short lawn, with pitch-specific colour calibration in linear light. Source: https://ambientcg.com/a/Grass004; licence: https://docs.ambientcg.com/license/. Source is procedural PBR, not a photographed FIFA pitch or an EA asset. Its checked-in colour, OpenGL normal, roughness and occlusion PNGs have pinned SHA-256 hashes. The shader blends three translated samples continuously to avoid recognizable repeated tufts.
1. Poly Haven **Concrete Floor 02**, Rob Tuytel, CC0-1.0. Exact URLs, source SHA-256 and output SHA-256 are in `manifest.json`. Redistribution/modification and commercial use are permitted by https://polyhaven.com/license; no attribution obligation, but the author/source are recorded here. This does not grant rights to the existing stadium geometry.

`albedo.bin`, `normal.bin`, `arm.bin` contain mip-major, layer-major RGBA8 bytes, with all 11 levels through 1×1. Albedo is sRGB; normal and ARM are linear. ARM channels are occlusion, roughness and metallic. OpenGL normal Y is positive in texture tangent space. Mip colour averaging happens in linear light; normal vectors are renormalized after reduction. Normal variance is not stored, so this pack is intended for rough surfaces, not a glossy microfacet material.

```bash
# Explicit authoring download; verifies pinned grass archive/map SHA-256 and published concrete MD5.
python3 Tools/MatchViewer3D/build_surface_pack.py --download

# Deterministic offline rebuild using checked-in Sources files.
python3 Tools/MatchViewer3D/build_surface_pack.py
```

Requires Python, NumPy and Pillow. The game and .NET builds do not run Python or access the network. The runtime validates schema, dimensions, full byte count and SHA-256 before uploading. An incompatible/missing/tampered pack returns a load error, without legacy/default material fallback. Shader samples use repeat addressing, trilinear mip filtering and 8× anisotropy.

`Match3D/PitchMarkings.fs` defines the presentation geometry in metres and emits locale-independent WGSL constants. `Shaders/PitchSurface.wgsl` owns directional mowing, restrained colour/clump/goal-mouth variation and all painted markings. Fourteen 7.5-metre mowing passes do not inherit the old GLB stripe colours. Paint shares the grass normals, roughness and occlusion, with physical 12-cm strokes and screen-footprint filtering. The GLB's raised chalk meshes are not imported; there is one authoritative marking path. Neither this material nor its wear masks affect match simulation.
