# Original daylight sky — isolated staging

This is a new original Blender-authored environment, **not a replacement of the active Lighting pack**. No external textures, HDRIs, photos, meshes or generation services are used. Blender's built-in physical atmosphere, procedural noise, volume shader and Cycles are reused as authoring tools. No live Blender session is accessed.

## Reproduce

From the repository root, with Blender 5.2.2 LTS:

```sh
blender -b --factory-startup -t 8 --python Tools/MatchViewer3D/OriginalSky/author_sky.py -- --preview
blender -b --factory-startup -t 8 --python Tools/MatchViewer3D/OriginalSky/author_sky.py
```

The first invocation is a 768×384, 16-sample look-development gate; the second produces 2048×1024, 64-sample scene-linear RGB **32-bit floating point ZIP EXRs**, an editable `daylight-sky.blend`, embedded source and display-only PNG previews. No exposure/display transform is baked into the EXRs. `settings.json` records the parameters; the fixed seed is 31871. Reproduction across different Blender/CPU/denoiser versions need not be byte-identical.

## Contract and coordinates

Runtime staging output: `FootballEngine.Client/Assets/MatchViewer/OriginalSky/sky-visible.exr` and `sky-lighting.exr`. These are source environment maps, not packed runtime IBL/BRDF resources.

Blender Z-up maps to runtime Y-up using the right-handed rotation **B(x,y,z) → R(x,z,-y)**. Inverse: **R(x,y,z) → B(x,-z,y)**. Sun direction is normalized `(-.48,.78,.40)` in runtime space. Top-down image sampling is `phi=(u-.5)*2π`, `theta=v*π`, direction `(sin(theta)cos(phi), cos(theta), sin(theta)sin(phi))`. Image top is zenith; centre is runtime +X; right of centre points toward +Z. No source-to-world post-rotation should be applied when consuming these maps.

The existing `build_hdr_environment.py` was inspected read-only: it currently hard-codes and verifies a photographic source, then rotates/rescales it. It **cannot ingest these files unchanged**. Integration must explicitly consume the two already-oriented maps, preserve scene-linear values and retain the existing irradiance/pi versus radiance distinction. This task deliberately does not implement that integration.

## Solar energy ownership

`sky-visible` includes the physical 0.545° sun disc, camera-only. Its excess above the disc-free atmosphere is multiplied by 0.25 to retain binary16 packing headroom; atmosphere and cloud lighting are not dimmed by this adjustment. `sky-lighting` removes the physical solar disc at its source, not by painting/replacing pixels. Both retain the same atmosphere and cloud scattering. Non-camera rays always see a disc-free world; a separate Blender SUN (energy 3.2, colour `(1,.966,.90)`) lights the cloud volume exactly once. For runtime use, convolve **only sky-lighting** for IBL, use sky-visible only for background, and retain exactly one separately evaluated directional sun.

The world strength is 0.1. This is explicit artistic radiance calibration, **not a measured photometric match to the game's lighting**. Do not rescale from a brightest-pixel guess. Runtime exposure/tonemapping and directional-sun radiometry need a native comparison before adoption.

## Cloud construction and limitations

A coherent, 4D-seeded fractal density field fills a 48 km wide layer at 2400–3300 m altitude. An altitude erosion envelope, broken coverage threshold, five noise octaves and anisotropic scattering shape distant banks without sphere piles or opaque surface clouds. Stadium air below 2400 m is volume-free. No fog is added around players/cameras. The layer is finite and stationary; this is art-directed weather, not a meteorological simulation. The physical atmosphere supplies the lower hemisphere; it is not a rendering of stadium/pitch ground.

Previews use neutral AgX at −1 EV, gamma 1, no glare/grade. They are display evidence, not runtime screenshots. No runtime validation, frame-rate claim or renderer-parity claim is made. Native broadcast/touchline/behind-goal camera review remains an integration responsibility.

## Inspection record

The 768×384 draft was inspected before final baking. A measured sun-location error exposed the physical-sky node's azimuth convention (`atan2(blenderX, blenderY)`); the corrected draft measured 0.125° centre-pixel error. Final panoramas and both 1024×576 rectilinear views were opened and inspected. Cloud banks have soft, irregular edges and darker undersides; fine detail is softened by volume integration/denoising, and the horizon has a noticeably uniform cloud-base band. These are remaining artistic limitations, not claims of photographic parity. The low horizon is clear of volumetric stadium fog.

An initial float32 final had solar values above binary16 range; that version was rejected and rebaked with the explicit 0.25 camera-disc calibration. `validation.json` records the delivered maps' measurements; `hashes.json` records source, blend and image SHA-256 values. Independent adaptive/denoised renders can differ slightly away from the disc; those differences are recorded rather than represented as bit-identical cloud pixels. The lower hemisphere is dark atmospheric output, **not** a calibrated ground-bounce map: integration should deliberately retain/author its ground-hemisphere policy rather than mistaking this for pitch radiometry.

The saved blend was separately opened in another headless process: the active authored scene contains only the cloud volume, sun and panorama camera; there are no linked libraries or external image dependencies, and the embedded script exactly matches `author_sky.py`. See `blend-inspection.json`. The repository's existing asset allowlist ignores the new runtime staging directory; the delivered EXRs exist on disk but will require an explicit review/add decision. No ignore/configuration files were changed.

Delivered-map checks: both are 2048×1024 RGB float32, finite and nonnegative. Visible maximum is 49746.371; disc-free lighting maximum is 2.07510, both within binary16 dynamic range. Measured solar-peak centre differs from the requested direction by 0.06349°. Integrated positive visible-minus-lighting radiance within a 0.7° cap is RGB `(2.84853, 2.33227, 1.86272)`; this is cloud-attenuated, not an unoccluded irradiance calibration. Independent denoising/adaptive differences outside that cap reach 0.19877 in a channel. The final two panoramas and two perspective views took 160.1 seconds on the isolated 8-thread CPU job. Blender emitted only future `use_nodes` deprecation warnings, not rendering errors. See `render.log`.
