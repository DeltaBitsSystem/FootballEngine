# A2: motion matching on real play

Phase A2 of `docs/plans/ANIMATION_AND_BALL_PLAN.md`. Locomotion poses now come from the CMU capture (A1) through
motion matching with inertialization. Foot-lock and IK are layered on top. Acceptance is measured with the same
corpus meter as A0, on real match motion.

## How to reproduce

```bash
dotnet build Tools/MatchViewer3D/StadiumRendererProbe.fsproj -c Release
dotnet Tools/MatchViewer3D/bin/Release/net10.0/StadiumRendererProbe.dll --motion-corpus docs/evidence/motion-corpus/procedural-a2.json
dotnet Tools/MatchViewer3D/bin/Release/net10.0/StadiumRendererProbe.dll --motion-corpus --matched docs/evidence/motion-corpus/matched-a2.json
dotnet fsi scripts/test.fsx --project Client
```

Both reports run against the same Core: seed 1, first half, same windows.
- The procedural gait is rerun on the current Core instead of reusing A0's numbers. The recalibration changes
  the play, so the windows fall on different actors and ticks.
- In matching mode, "planted" means a full foot lock (`LeftLock`/`RightLock` = 1), which is what the evaluator
  actually holds still.
- Residuals are judged against the real target, not the softened one.

Database: `motion-db.bin`, sha256 `f82d269f4de7b2b4…`.
- 148 CMU clips, 57,375 frames at 60 Hz.
- 384 frames repaired as source flips.

## Results (2026-11-09)

Procedural (current Core):

| Family | Actor | Ticks | Skating (cm) | Max sole speed (m/s) | Max accel. (m/s²) | Pops | Contacts | Contact error (cm) | Residuals |
|---|---|---|---|---|---|---|---|---|---|
| Start | 8 | 3457–3577 | 162 | 3.73 | 1374 | 11 | 0 | 0 | 168 |
| Stop | 11 | 3855–3975 | 30 | 2.99 | 1445 | 41 | 1 | 60 | 65 |
| Turn | 2 | 2341–2461 | 6 | 1.13 | 712 | 5 | 0 | 0 | 17 |
| Sprint | 8 | 2478–2598 | 34 | 3.14 | 989 | 67 | 0 | 0 | 31 |
| Dribble | 108 | 5106–5226 | 25 | 3.51 | 3073 | 71 | 1 | 48 | 54 |
| Pass | 11 | 4056–4176 | 18 | 2.72 | 1352 | 20 | 2 | 8 | 14 |
| Shot | 8 | 2707–2827 | 44 | 2.83 | 1371 | 57 | 2 | 240 | 132 |
| Reception | 105 | 3588–3708 | 4 | 1.02 | 2365 | 15 | 9 | 113 | 51 |
| Tackle | 105 | 2954–3074 | 9 | 1.20 | 789 | 8 | 3 | 83 | 23 |

Motion matching:

| Family | Actor | Ticks | Skating (cm) | Max sole speed (m/s) | Max accel. (m/s²) | Pops | Contacts | Contact error (cm) | Residuals |
|---|---|---|---|---|---|---|---|---|---|
| Start | 8 | 3457–3577 | 1 | 0.53 | 453 | 2 | 0 | 0 | 25 |
| Stop | 11 | 3855–3975 | 0 | 0.05 | 375 | 0 | 1 | 164 | 11 |
| Turn | 2 | 2341–2461 | 0 | 0.37 | 232 | 0 | 0 | 0 | 19 |
| Sprint | 8 | 2478–2598 | 0 | 0.00 | 379 | 0 | 0 | 0 | 0 |
| Dribble | 108 | 5106–5226 | 0 | 0.00 | 437 | 1 | 1 | 97 | 15 |
| Pass | 11 | 4056–4176 | 0 | 0.00 | 274 | 0 | 2 | 90 | 0 |
| Shot | 8 | 2707–2827 | 0 | 0.00 | 451 | 1 | 2 | 235 | 11 |
| Reception | 105 | 3588–3708 | 3 | 0.55 | 619 | 6 | 9 | 135 | 27 |
| Tackle | 105 | 2954–3074 | 0 | 0.39 | 218 | 0 | 3 | 99 | 2 |

Totals over 1,620 frames:
- Skating: 3.32 m → 0.04 m.
- Pops: 295 → 10. The maximum joint acceleration falls from 3,073 m/s² to 619 m/s².
- The ten remaining pops (above 400 m/s²) are in start, dribble, shot and reception. That's where Core changes
  the root at a rate no capture follows, and where foot tasks take over the leg.

## Unit acceptance (`FootballEngine.Client.MotionMatching`, `MotionDatabase`)

- A 6 m/s run is played at 5.1–6.9 m/s and is not taken from walking capture. Standing still uses capture slower
  than 0.8 m/s.
- A cold evaluation reproduces the sequential palette exactly at 300, 301.5 and 333.25 ticks (seek and recovery).
- Over a varied path (accelerate, turn, brake, stand, sprint), sampled every quarter tick:
  - At least one switch occurs.
  - No joint moves more than 12.5 cm per 1/160 s.
  - No joint has a second difference above 3 cm.
- A planted sole drifts less than 1 cm over more than 100 locked samples.
- 22 players are evaluated in under 1 ms per 60 Hz frame, measured sequentially (`testSequenced`). Under the
  parallel suite, with the tuner occupying cores, the same measurement gave 3 ms; that figure doesn't represent
  the budget.
- The shipped capture has no step above 40° per frame. The heading of three straight-running clips stays within
  6°.

## Decisions taken during the work

- **C1 sampling.** Interpolation between capture frames is a normalized Catmull-Rom on quaternions, with the
  velocity taken from the derivative of the spline itself.
  - Slerp left velocity jumps at every frame, which show as fingertip flicks at playback 1.25×.
  - At a switch, inertialization receives the velocity the viewer actually saw.
- **Repairing capture, not runtime.** Fixed in the builder, `Tools/MatchViewer3D/Mocap/build_motion_db.py`.
  - Spike return is judged at the joint's speed before the flip.
  - The fill is a Hermite curve capped at chord speed, falling back to constant speed if it would itself step
    like a flip.
  - Quaternions are realigned to one hemisphere after each repair.
  - Arms (shoulder, elbow, wrist) get a 5-tap binomial filter: CMU markers jitter at 30 Hz and the retargeted
    elbow stops dead at full extension. Legs stay raw.
- **Lock weight on release and switch.** The fading offset keeps the weight it had when it was taken. A stance cut
  short before engaging used to release from full strength, and since lock also governs soft IK, the knee jumped.
- **Soft IK always on.** A planted foot keeps a 1.2 cm easing (`LockedSoftReach`). At most it costs the anchor
  ~4 mm; without it, an anchor at the edge of reach snapped the knee straight as the lock engaged.
- **Search.** Bounds over groups of 16 chunks, and the search prunes directly against "the stay's cost minus the
  margin". Same answer as an exhaustive scan.

## Honest limits

- **Contacts are A4's.** Errors of 0.9–2.4 m between the boot and Core's contact point remain in pass, shot,
  reception and tackle, the same order as the procedural gait. The matcher fills foot tasks with IK over a
  locomotion pose; reaching the authoritative point needs action clips with time-warp.
  - The residuals the procedural gait reported as zero in those windows were real. The matcher now reports them.
- **Missing families.** In this half Core produces no header, jump or keeper dive. The meter flags it and exits
  with code 2; the gap is Core's (B2/A5).
- **No video.** These numbers come from the meter, not from looking at the screen. The 60 Hz visual review on real
  play is still pending.
