# Match Viewer — code units (specs for implementation sessions)

Companion to `MATCH_VIEWER_WEBGPU_PLAN.md`. Each unit is implemented by an implementation session against the
assets already authored in Blender (see "Assets" in each unit). Rules for every unit: AGENTS.md conventions
(comments explain why, no `failwith`/`Unchecked.defaultof` in runtime code, numbers in validated config), Ultra is
the development tier, no backward compatibility, deterministic sport (the viewer never changes Core results),
tests through `dotnet fsi scripts/test.fsx`, native WebGPU captures through the `--viewer-scenes` harness from C1.
Each unit ends with: builds (slnx, Client.Tests, Benchmarks), all tests green with counts, before/after captures
of the six fixed scenes inspected, per-pass GPU ms, and an honest limits list. No commits by the implementer.

## C2 — Stadium assembly + GPU-driven scene (F2 + F3)

Goal: replace `stadium.glb` (unattested, must leave the product) with the club's procedural stadium built from
the original kit, and move per-instance work to the GPU.

Assets (read-only):
- `Assets/MatchViewer/StadiumKit/kit.json`, `stadium-kit.glb`, `Textures/*.png` (albedo sRGB, normal OpenGL,
  ORM = occlusion/roughness/metallic). Material variants `base:variant` share textures; `tint` multiplies albedo;
  `tintRole` (`clubPrimary`, `clubSeat`) replaces the tint with the home club colour. `led_screen` is analytic:
  subpixel grid from UV × `screens[i]` size / `ledPixelPitchMetres` (0.01 m), content is runtime emissive
  (placeholder: scrolling bands in club colours). `net` alpha = albedo red (alpha-to-coverage under MSAA).
  `lamp_lens` emissive. Every piece has lod0/1/2 nodes and `fromDistance` thresholds.
- `Tools/MatchViewer3D/StadiumKit/stadium_layout.py` is the executable specification; port it exactly.
  `Assets/MatchViewer/StadiumKit/layout-golden.json` holds six test vectors (input, full layout, per-tier
  profile + seat digests, lamp/target/beam/roof-sheet digests).
- `Assets/MatchViewer/Ball/ball.json` + `ball.glb` (regions panel/accent/seam; replaces the stadium.glb ball).
- `Assets/MatchViewer/Surfaces/*` (turf tile is now **1.0 m**: update `PitchMarkings.grassTileMetres`).

Work:
1. `Match3D/StadiumLayout.fs`: port `Rng` (SplitMix64), `prestige_of`, `_tiers_for`, `generate`, `front_frames`,
   `tier_profile`, `seat_positions`, `roof_sheets`, `lamp_positions`, `aiming_targets`, `beam_half_angles`,
   `broadcast_gantry`, piece placements (incl. `scale`). Python `round` is banker's rounding = .NET default.
   Tests compare against every golden field: exact for ints/strings, |Δ| ≤ 1e-3 for rounded floats.
2. `StadiumIdentity` = { Reputation; LeagueLevel; Seed = home ClubId; Primary; Secondary } built from the
   fixture's home club (GameState + ClubIdentity kit). Flows into the renderer at match start and in the harness;
   add `--viewer-stadium <rep>,<level>,<seed>` to the harness.
3. Geometry: terraces/tier bands (extrude `tier_profile` along `front_frames`, 2 m step, caps on straight runs,
   inter-tier band glass if `vip_band` else charcoal), roof sheets (`roof_sheets` quads), procedural pitch
   (105×68 + runoff to the stand fronts, existing PitchSurface shader), all with world-box UVs / tileMetres.
4. Instancing: one instance buffer per (piece, lod); seats from `seat_positions` with per-seat colour by the
   layout's `seat_colours` rule (solid / tiered / blocks: rows < 3 and last 2 in secondary). Compute pass:
   frustum + distance LOD selection writing `drawIndexedIndirect` args. Static passes recorded in render bundles.
5. Compute skinning for the 22 players (+ officials later): one dispatch writes skinned positions/normals reused
   by shadow, prepass and opaque passes.
6. Kit material shader: albedo × tint, ORM, normal map, world-space macro variation (±8% albedo, 25 m noise) to
   hide tiling; glass as alpha-blended with IBL reflection; specular AA from C1.
7. Cameras: broadcast camera from the gantry piece (+0.8 m forward, +1.6 m up) aimed at the pitch; existing camera
   presets re-expressed relative to the stadium (behind goal at goal line −25 m, etc.).
8. Delete `stadium.glb`, `StadiumAsset.fs`, its manifest entry and gitignore line; update LICENSES.md.

Acceptance: golden tests pass; three stadiums (giant 9600/0, second_div 2800/1, third_div 1200/2) captured in
the harness look like the Blender look-dev renders (tv/aerial/goal) at Ultra; per-pass GPU ms reported; seat
count on GPU equals layout; no stadium.glb references remain (grep).

**Status (C2b, 2026-11-09):** steps 1–3, 6–8 done; step 4 done on the CPU (frustum culling of 8 m × 8 m
seat clusters + distance LOD, consecutive clusters merged into `firstInstance` ranges). The compute-pass culling,
indirect args and render bundles move to C2c; compute skinning (step 5) is not part of C2b. The broadcast camera
from the gantry is not wired yet; BehindGoal/Touchline presets were moved inside the smallest grounds' runoff.
Captures: `artifacts/viewer-engine/c2b/{giant,second_div,third_div}`. `stadium.glb` is deleted.

## C3 — Night (F7)

Assets: `OriginalSky/sky-{visible,lighting}-{dusk,night}.exr` (same projection and split as daylight);
floodlight emitters in kit.json; aiming/beam functions from C2.
Work: pack dusk/night environments like daylight (`build_hdr_environment.py` generalised to a variant
argument; IBL prefilter); `MatchLighting = Day | Dusk | Night` chosen deterministically from the fixture's kickoff
hour (night ≥ 19:00, dusk 17:30–19:00) with a harness override. Night: no sun; one spot per lamp
(150,000 lm, intensity Φ/Ω with Ω from the per-lamp beam half-angle, soft edge 0.8, colour 4,000 K-ish
(1.0, 0.97, 0.93)); clustered forward lighting (16×9×24 froxels, compute culling). Shadows: shadow atlas for the N
most important lamp groups (cluster lamps per mast/gantry segment for shadow casting) + per-player capsule
shadows from every lamp group so each player shows the characteristic 4-way floodlight shadows. Exposure for
night +1 EV relative to daylight (broadcast practice). Lamp lenses emissive + bloom; subtle volumetric halo
around lamp heads (screen-space, cheap). Stands receive spill from the beams' soft edge.
Acceptance: scene 6 renders at night with uniform pitch light (min/avg illuminance ratio reported), 4 shadows
per player visible, matches the Blender night references (giant roof ring, third-division masts).

## C4 — Crowd (F4)

Assets: `Assets/MatchViewer/Crowd/{crowd.glb, crowd-vat.bin, crowd.json}`: 4 bodies × 2 LODs, `_VID` attribute
= VAT column, `_REGION` (skin, top, sleeve, trousers, shoes, hair, scarf), 9 clips at 15 fps, float16 VAT
(positions then normals, Blender Z-up → glTF/runtime Y-up as documented), origin = seat reference (seat tread,
facing the pitch).
Work: one spectator instance per seat position (C2 layout) filled to an occupancy from match importance and
club reputation (deterministic per seed); standing terraces get standing instances at 0.45 m spacing. Per
instance: body variant, palette (home/away supporters by stand: away fans in one end block; tops from club
primary/secondary/neutral mix, skin tones, trousers), clip and phase offset. Clip state machine driven by
semantic events from the replay/observation stream: idle → tense (dangerous attack near goal), celebrate (goal,
home or away sections only), despair (conceded), protest (foul/card against the section's team), clap/chant
background cycles. Transitions blend over 0.3 s by sampling two clips. LOD: VAT lod0 < 25 m, lod1 < 90 m, beyond
an impostor (camera-facing quad shaded with the palette). Compute culling + indirect draws.
Acceptance: 60k spectators at Ultra in the giant stadium within the C1 harness; goal scene shows the home end
celebrating and the away block in despair; per-pass ms.

## C5 — Players, officials, ball (F5)

Assets: `player-hair.glb/.json` (6 styles, same skin, `_FADE`), `Kits/shirt-font-sdf.png/.json`,
`Ball/ball.glb`.
Work: kit textures generated on the GPU per team from `ClubKit` (pattern, colours) into the jersey/shorts/socks
atlas tiles; numbers on back and front + surname on back from the SDF font (uppercase, accents), outlined in the
secondary colour; goalkeeper and official kits (referee black/yellow/etc. chosen to contrast both teams).
Variety per player id (deterministic): hairstyle, hair colour, skin tone, height scale from `Player.Height`,
build from `Player.Weight` (bone scale on spine/limbs within ±6%). Referee + two assistants on the player rig
with the existing official clips; assistants carry a flag (simple mesh, follows the hand bone).
Acceptance: close-up scene shows correct name/number of the real player from the squad (test against data),
two teams with different patterns readable at broadcast distance.

## C6 — Living pitch and weather (F6)

Work: wear map (R16F, 1 texel = 0.25 m) accumulated in compute from authoritative player positions and
sprint/slide events (goalmouths and channels wear more); rendered as soil/thatch blend in the pitch shader.
Mowing pattern per club (stripes/checker/circles, from seed). Rain/snow: GPU particles, wet roughness and
puddle darkening on pitch/ball/kits; the goal net as a mass-spring cloth in compute, excited only by the
authoritative goal ball trajectory. Acceptance: minute-80 scene wear matches Core trajectories (test against a
CPU reference accumulation), rain scene within budget.

## C7 — Broadcast direction (F8)

Work: camera director choosing TV main / tight follow / behind goal / reverse angle by semantic events with
hysteresis; automatic replays of goals and near misses from the replay buffer with DOF and motion blur, slow
motion; transitions synced with the HUD. The clock and score never run ahead of Core.
Acceptance: a seeded goal produces cut → replay → return; replay frames come from recorded state, not new sim.
