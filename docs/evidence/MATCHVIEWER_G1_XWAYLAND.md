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

## 4x MSAA image-quality update

**Image:** `matchviewer-msaa4-xwayland.png`

**SHA-256:** `8bb539337e403de0229bd841d6951d09cc62df36d5a600914d3a8155edc3f55b`

The stadium renderer now rasterizes into a 4-sample color target, resolves to the XWayland surface, and uses a matching 4-sample depth target. The multisample color/depth attachments are discarded after each resolve/pass because every frame clears them. Visual inspection of the real probe capture shows cleaner pitch markings, goal nets, and stand edges than the earlier 1-sample capture; the HUD remains Avalonia-rendered and crisp. This is a visual check, not a measured image-quality or performance benchmark.

After this change, the 50-cycle run again reported `Lifecycle cycles=50, renderer attaches=50, detaches=50`, exercising cleanup of the 4× attachments too. It still does not measure retained GPU memory or frame time.

## Turf pattern correction

**Image:** `matchviewer-grass-noise-fixed.png`

**SHA-256:** `eb2ac6fe8879c6a78fa177dd31152774ef8238bdc1c6c97665909fc164d90c19`

The ten GLB mowing strips meet edge-to-edge on the same plane. The prior fragment shader added a high-frequency world-space sine modulation over those authored stripes, creating an extra fixed diagonal pattern. That modulation is removed; the baked turf strips and diffuse lighting remain. The new probe capture was inspected and no longer shows that fine, dark diagonal pattern. This is visual confirmation on the probe, not a cross-GPU golden test.

## Scope and limits

This is a renderer/host integration probe with a synthetic 22-player frame; it is not a live authoritative match. It has no officials, proper human assets, production HUD, resize/DPI sweep, fault injection, lifecycle soak, performance profile, or Wayland-native validation. The screenshot proves one XWayland configuration only; it does not close G1.

Environment at capture: CachyOS Linux x86_64, KDE Wayland with X.Org display `:0`/XWayland, AMD Ryzen 5 5600X, Radeon RX 5500 XT (Mesa 26.2.2), .NET SDK 10.0.302, Blender 5.2.1 LTS.
