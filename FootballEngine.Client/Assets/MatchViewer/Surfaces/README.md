# Offline stadium surface pack

Two 1024² material layers with complete mip chains, both original FootballEngine content:

0. **Turf**, 1.0 metre per tile: modelled grass blades (~30k shoots/m², seeded) instanced with Geometry Nodes on a
   3x3 copy of one tile and rendered top-down (albedo, OpenGL normal, occlusion) by
   `Tools/MatchViewer3D/Surfaces/author_turf.py` + `turf_pack.py`. Seamless by construction. Colour is calibrated to
   the pitch target in linear light by `build_surface_pack.py`.
1. **Concrete**, 2 metres per tile: the stadium kit's baked procedural concrete
   (`Tools/MatchViewer3D/StadiumKit/kit_materials.py`).

`Tools/MatchViewer3D/build_surface_pack.py` builds `albedo.bin`, `normal.bin` and `arm.bin` (mip-major/layer-major
RGBA8) and records source/output SHA-256 hashes in `manifest.json`. Nothing is downloaded.
