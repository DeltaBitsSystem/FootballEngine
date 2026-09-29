# Match viewer G1 — XWayland probe evidence

**Captured:** 2026-09-28, local development workstation.  
**Image:** `matchviewer-xwayland-probe.png`  
**SHA-256:** `5d3a6c3e29062d45f19983287a32b9259b7893694edc68d7514ff6608da19499`

## Reproduction

From the repository root, with the Linux desktop session and Blender-independent stadium asset available:

```bash
dotnet build Tools/MatchViewer3D/StadiumRendererProbe.fsproj --no-restore
dotnet run --project Tools/MatchViewer3D/StadiumRendererProbe.fsproj --no-build
MATCHVIEWER_PROBE_CYCLES=50 dotnet run --project Tools/MatchViewer3D/StadiumRendererProbe.fsproj --no-build
```

The probe reports 23 static stadium batches (1,065,448 triangles) and 13 live batches (70,272 triangles). The image is an active-window capture of the actual Avalonia/XWayland window, not a Blender render or desktop composite. It shows the stadium, animated placeholder players, WebGPU child surface and Avalonia HUD together. Tab followed by Enter changed the HUD button label to **HUD CLICK OK**, confirming keyboard activation while the child surface was active. Pointer clicks were attempted but did not activate the button; mouse interaction remains an open G1 issue.

The 50-cycle command completed with `Lifecycle cycles=50, renderer attaches=50, detaches=50`. This covers repeated normal attach/close, not resize, device loss, failures during initialization, or memory soak.

## Scope and limits

This is a renderer/host integration probe with a synthetic 22-player frame; it is not a live authoritative match. It has no officials, proper human assets, production HUD, resize/DPI sweep, fault injection, lifecycle soak, performance profile, or Wayland-native validation. The screenshot proves one XWayland configuration only; it does not close G1.

Environment at capture: CachyOS Linux x86_64, KDE Wayland with X.Org display `:0`/XWayland, AMD Ryzen 5 5600X, Radeon RX 5500 XT (Mesa 26.2.2), .NET SDK 10.0.302, Blender 5.2.1 LTS.
