namespace FootballEngine.Client.Match3D

open System
open System.IO
open Avalonia
open Avalonia.Controls
open Avalonia.Platform

module MatchPitchOverlay =
    let margin = 14.0
    let managerWidth = 250.0
    let managerHeight = 380.0
    let intelligenceWidth = 285.0
    let intelligenceHeight = 390.0

module MatchPitchNative =
    let private toPixelBounds (control: Control) =
        match TopLevel.GetTopLevel(control) with
        | null -> PixelRect(0, 0, 0, 0)
        | topLevel ->
            let scale = topLevel.RenderScaling

            let origin = control.TranslatePoint(Point(0.0, 0.0), topLevel)

            if not origin.HasValue then
                PixelRect(0, 0, 0, 0)
            else
                PixelRect(
                    int (Math.Round(origin.Value.X * scale)),
                    int (Math.Round(origin.Value.Y * scale)),
                    max 1 (int (Math.Round(control.Bounds.Width * scale))),
                    max 1 (int (Math.Round(control.Bounds.Height * scale)))
                )

    let rendererFactory (control: Control) =
        match TopLevel.GetTopLevel(control) with
        | null -> None
        | topLevel ->
            match topLevel.TryGetPlatformHandle() with
            | null -> None
            | handle when handle.HandleDescriptor <> "XID" ->
                invalidOp
                    $"WebGPU stadium rendering requires an X11/XWayland XID; Avalonia returned '{handle.HandleDescriptor}'."
            | handle ->
                let parent = unativeint (uint64 handle.Handle)

                let assetPath =
                    Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", "stadium.glb")

                Some(fun bounds -> new WebGpuStadiumRenderer(parent, bounds, assetPath))

    let overlayCutouts (control: Control) =
        match TopLevel.GetTopLevel(control) with
        | null -> [||]
        | topLevel when topLevel.ClientSize.Width < 1300.0 -> [||]
        | topLevel ->
            let scale = topLevel.RenderScaling
            let bounds = toPixelBounds control
            let pixels value = int (Math.Round(value * scale))
            let margin = pixels MatchPitchOverlay.margin
            let managerWidth = pixels MatchPitchOverlay.managerWidth

            let managerHeight =
                max 0 (min (pixels MatchPitchOverlay.managerHeight) (bounds.Height - margin))

            let intelligenceWidth = pixels MatchPitchOverlay.intelligenceWidth

            let intelligenceHeight =
                max 0 (min (pixels MatchPitchOverlay.intelligenceHeight) (bounds.Height - margin))

            if bounds.Width < managerWidth + intelligenceWidth + margin * 2 + pixels 220.0 then
                [||]
            else
                [| PixelRect(margin, margin, managerWidth, managerHeight)
                   PixelRect(bounds.Width - margin - intelligenceWidth, margin, intelligenceWidth, intelligenceHeight) |]

    let bounds control = toPixelBounds control
