# Broadcast camera review

Validated on 2026-10-02 using the actual Avalonia/WebGPU stadium probe, with fixed synthetic player poses.

`matchviewer-camera-broadcast-review.png` compares the default attacking position, midfield, both goals, and both touchlines. The base camera uses a 48 m distance, 30-degree vertical field of view, 31-degree inclination and the existing 6-degree sideline alignment. The framing looks 2.5 m beyond the carrier toward the far side. Lateral focus is constrained; inclination increases continuously toward 45 degrees on the near side to clear the stand and keep the carrier above the radar.

An intermediate near-touchline capture exposed complete stand occlusion. Merely pulling back still hid the carrier behind stadium geometry. Raising the camera instead was verified in the final near-touchline capture. The other five images use the same final camera behavior for their positions (the near-side inclination only activates beyond 16 m).

Validation:

```bash
dotnet build Tools/MatchViewer3D/StadiumRendererProbe.fsproj --no-restore -clp:ErrorsOnly
MATCHVIEWER_PROBE_FIXED_POSE=1 MATCHVIEWER_PROBE_CAMERA_SCENE=near-touchline dotnet run --project Tools/MatchViewer3D/StadiumRendererProbe.fsproj --no-build
MATCHVIEWER_PROBE_CYCLES=5 dotnet run --project Tools/MatchViewer3D/StadiumRendererProbe.fsproj --no-build
```

Build: zero errors, 67 existing project warnings. Lifecycle: five attaches and five detaches. Visual captures cover a single desktop viewport, not animated camera transitions, narrow layouts or a real match. This improves camera framing only; the reference's asset, turf and lighting quality is not reproduced by these changes.
