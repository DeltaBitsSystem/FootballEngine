namespace FootballEngine.Components

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.FuncUI.Builder
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Platform
open Avalonia.Rendering.SceneGraph
open Avalonia.Skia
open FootballEngine.Simulation
open FootballEngine.Types
open FootballEngine.Types.SimulationClock
open SkiaSharp
open FootballEngine
open FootballEngine.Domain
open FootballEngine.AppTypes
open FootballEngine.AppMsgs
open FootballEngine.Icons

// ---------------------------------------------------------------------------
// Coordinate mapping
// ---------------------------------------------------------------------------

module PitchCoords =

    [<Literal>]
    let PitchW = 1050.0f

    [<Literal>]
    let PitchH = 680.0f

    let inline toCanvas (x: float) (y: float) : float32 * float32 =
        float32 x * PitchW / float32 PhysicsContract.PitchLength,
        float32 y * PitchH / float32 PhysicsContract.PitchWidth

    let inline toCanvasF (x: float32) (y: float32) : float32 * float32 =
        x * PitchW / float32 PhysicsContract.PitchLength, y * PitchH / float32 PhysicsContract.PitchWidth

    let inline lerp (t: float32) (a: float32) (b: float32) : float32 =
        let t' = t * t * (3.0f - 2.0f * t)
        a + (b - a) * t'

    let lerpPos (t: float32) (ax: float32, ay: float32) (bx: float32, by: float32) = lerp t ax bx, lerp t ay by


module PitchGeometry =

    open PhysicsContract

    let inline private toCanvasX (meters: float<meter>) : float32 =
        float32 (float meters) * PitchCoords.PitchW / float32 PitchLength

    let inline private toCanvasY (meters: float<meter>) : float32 =
        float32 (float meters) * PitchCoords.PitchH / float32 PitchWidth

    let PenaltyAreaDepthX = toCanvasX PenaltyAreaDepth
    let PenaltyAreaHalfW = toCanvasY PenaltyAreaHalfWidth
    let GoalAreaDepthX = toCanvasX GoalAreaDepth
    let GoalAreaHalfW = toCanvasY GoalAreaHalfWidth
    let CenterCircleRadius = toCanvasX CenterCircleRadius
    let PenaltyArcRadius = toCanvasX PenaltyArcRadius
    let CornerArcRadius = toCanvasX CornerArcRadius
    let PenaltySpotX = toCanvasX PenaltySpotDistance
    let GoalHalfW = toCanvasY (PostFarY - PostNearY) / 2.0f
    let GoalDepth = 16.0f


// ---------------------------------------------------------------------------
// SKPaint cache — allocate once, reuse across frames
// ---------------------------------------------------------------------------

module Paints =

    let private mkFill (r: byte) (g: byte) (b: byte) (a: byte) =
        new SKPaint(Color = SKColor(r, g, b, a), IsAntialias = true, Style = SKPaintStyle.Fill)

    let private mkStroke (r: byte) (g: byte) (b: byte) (a: byte) (width: float32) =
        new SKPaint(Color = SKColor(r, g, b, a), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = width)

    let private mkText (r: byte) (g: byte) (b: byte) (size: float32) (bold: bool) =
        let tf =
            if bold then
                SKTypeface.FromFamilyName(
                    "Inter",
                    SKFontStyleWeight.Bold,
                    SKFontStyleWidth.Normal,
                    SKFontStyleSlant.Upright
                )
            else
                SKTypeface.FromFamilyName(
                    "Inter",
                    SKFontStyleWeight.SemiBold,
                    SKFontStyleWidth.Normal,
                    SKFontStyleSlant.Upright
                )

        let tf =
            match tf with
            | null -> SKTypeface.Default
            | t -> t

        new SKPaint(
            Color = SKColor(r, g, b, 255uy),
            IsAntialias = true,
            TextSize = size,
            Typeface = tf,
            TextAlign = SKTextAlign.Center
        )

    // ── Pitch surface ──────────────────────────────────────────────────────

    let pitchDark = mkFill 28uy 74uy 23uy 255uy
    let pitchLight = mkFill 33uy 85uy 27uy 255uy
    let pitchVignette = new SKPaint(IsAntialias = true, Style = SKPaintStyle.Fill)

    let lineWhite = mkStroke 255uy 255uy 255uy 255uy 2.5f

    let dotWhite = mkFill 255uy 255uy 255uy 255uy

    // ── Players ────────────────────────────────────────────────────────────

    let playerShadow =
        new SKPaint(
            Color = SKColor(0uy, 0uy, 0uy, 55uy),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4.0f)
        )

    let playerRingHome = mkStroke 30uy 130uy 255uy 220uy 2.5f
    let playerRingAway = mkStroke 239uy 68uy 68uy 220uy 2.5f

    let playerFillHome = new SKPaint(IsAntialias = true, Style = SKPaintStyle.Fill)
    let playerFillAway = new SKPaint(IsAntialias = true, Style = SKPaintStyle.Fill)

    let playerNumber = mkText 255uy 255uy 255uy 11.0f true
    let playerName = mkText 240uy 240uy 240uy 8.5f false

    let playerNameBg = mkFill 15uy 23uy 42uy 185uy

    let condGood = mkStroke 16uy 185uy 129uy 230uy 2.0f
    let condMid = mkStroke 245uy 158uy 11uy 230uy 2.0f
    let condLow = mkStroke 239uy 68uy 68uy 230uy 2.0f

    let velocityArrow = mkStroke 255uy 255uy 255uy 45uy 1.5f

    // ── Ball ───────────────────────────────────────────────────────────────

    let ballShadow =
        new SKPaint(
            Color = SKColor(0uy, 0uy, 0uy, 80uy),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6.0f)
        )

    let ballBase = mkFill 245uy 245uy 245uy 255uy

    let ballPanel =
        new SKPaint(Color = SKColor(30uy, 30uy, 30uy, 160uy), IsAntialias = true, Style = SKPaintStyle.Fill)

    let ballHighlight =
        new SKPaint(Color = SKColor(255uy, 255uy, 255uy, 190uy), IsAntialias = true, Style = SKPaintStyle.Fill)

    let ballOutline = mkStroke 180uy 180uy 180uy 120uy 0.8f

    // ── HUD ────────────────────────────────────────────────────────────────

    let hudBg = mkFill 15uy 23uy 42uy 215uy
    let hudBorder = mkStroke 51uy 65uy 85uy 180uy 1.0f
    let hudText = mkText 241uy 245uy 249uy 13.0f true
    let hudTextSub = mkText 148uy 163uy 184uy 9.5f false
    let hudAccentHome = mkFill 59uy 130uy 246uy 255uy
    let hudAccentAway = mkFill 239uy 68uy 68uy 255uy
    let momentumHome = mkFill 59uy 130uy 246uy 255uy
    let momentumAway = mkFill 239uy 68uy 68uy 255uy
    let momentumTrack = mkFill 30uy 41uy 59uy 255uy

    let vignetteShader =
        SKShader.CreateRadialGradient(
            SKPoint(PitchCoords.PitchW / 2.0f, PitchCoords.PitchH / 2.0f),
            MathF.Max(PitchCoords.PitchW, PitchCoords.PitchH) * 0.72f,
            [| SKColor(0uy, 0uy, 0uy, 0uy); SKColor(0uy, 0uy, 0uy, 80uy) |],
            [| 0.55f; 1.0f |],
            SKShaderTileMode.Clamp
        )

    let glowHome =
        new SKPaint(
            Color = SKColor(59uy, 130uy, 246uy, 80uy),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8.0f)
        )

    let glowAway =
        new SKPaint(
            Color = SKColor(239uy, 68uy, 68uy, 80uy),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8.0f)
        )


// ---------------------------------------------------------------------------
// Pitch renderer
// ---------------------------------------------------------------------------

module PitchRenderer =

    open PitchCoords
    open PitchGeometry

    let private stripeCount = 10

    let drawStripes (canvas: SKCanvas) =
        let stripeH = PitchH / float32 stripeCount

        for i in 0 .. stripeCount - 1 do
            let paint = if i % 2 = 0 then Paints.pitchDark else Paints.pitchLight
            let r = SKRect(0.0f, float32 i * stripeH, PitchW, float32 (i + 1) * stripeH)
            canvas.DrawRect(r, paint)

    let private lineW = PitchW
    let private lineH = PitchH

    let drawMarkings (canvas: SKCanvas) =
        let p = Paints.lineWhite
        let cx = PitchW / 2.0f
        let cy = PitchH / 2.0f
        let mid = PitchH / 2.0f

        canvas.DrawRect(SKRect(0.0f, 0.0f, PitchW, PitchH), p)
        canvas.DrawLine(cx, 0.0f, cx, PitchH, p)

        canvas.DrawCircle(cx, cy, CenterCircleRadius, p)
        canvas.DrawCircle(cx, cy, 3.5f, Paints.dotWhite)

        let lpaRect =
            SKRect(0.0f, mid - PenaltyAreaHalfW, PenaltyAreaDepthX, mid + PenaltyAreaHalfW)

        canvas.DrawRect(lpaRect, p)

        canvas.DrawRect(SKRect(0.0f, mid - GoalAreaHalfW, GoalAreaDepthX, mid + GoalAreaHalfW), p)

        canvas.DrawCircle(PenaltySpotX, mid, 3.0f, Paints.dotWhite)

        use arcPath = new SKPath()

        arcPath.AddArc(
            SKRect(
                PenaltySpotX - PenaltyArcRadius,
                mid - PenaltyArcRadius,
                PenaltySpotX + PenaltyArcRadius,
                mid + PenaltyArcRadius
            ),
            -53.0f,
            106.0f
        )

        canvas.DrawPath(arcPath, p)

        canvas.DrawRect(SKRect(PitchW - PenaltyAreaDepthX, mid - PenaltyAreaHalfW, PitchW, mid + PenaltyAreaHalfW), p)

        canvas.DrawRect(SKRect(PitchW - GoalAreaDepthX, mid - GoalAreaHalfW, PitchW, mid + GoalAreaHalfW), p)

        let rSpotX = PitchW - PenaltySpotX
        canvas.DrawCircle(rSpotX, mid, 3.0f, Paints.dotWhite)

        use arcPathR = new SKPath()

        arcPathR.AddArc(
            SKRect(rSpotX - PenaltyArcRadius, mid - PenaltyArcRadius, rSpotX + PenaltyArcRadius, mid + PenaltyArcRadius),
            127.0f,
            106.0f
        )

        canvas.DrawPath(arcPathR, p)

        for cx', cy', startA in
            [ 0.0f, 0.0f, 0.0f
              PitchW, 0.0f, 90.0f
              0.0f, PitchH, 270.0f
              PitchW, PitchH, 180.0f ] do
            use cornerArc = new SKPath()

            cornerArc.AddArc(
                SKRect(cx' - CornerArcRadius, cy' - CornerArcRadius, cx' + CornerArcRadius, cy' + CornerArcRadius),
                startA,
                90.0f
            )

            canvas.DrawPath(cornerArc, p)

        let mkGoalPaint () =
            new SKPaint(Color = SKColor(255uy, 255uy, 255uy, 30uy), Style = SKPaintStyle.Fill, IsAntialias = true)

        let goalPaint = mkGoalPaint ()
        canvas.DrawRect(SKRect(-GoalDepth, mid - GoalHalfW, 0.0f, mid + GoalHalfW), goalPaint)
        canvas.DrawRect(SKRect(PitchW, mid - GoalHalfW, PitchW + GoalDepth, mid + GoalHalfW), goalPaint)

    let drawVignette (canvas: SKCanvas) =
        Paints.pitchVignette.Shader <- Paints.vignetteShader
        canvas.DrawRect(SKRect(0.0f, 0.0f, PitchW, PitchH), Paints.pitchVignette)


// ---------------------------------------------------------------------------
// Player renderer
// ---------------------------------------------------------------------------

module PlayerRenderer =

    open PitchCoords

    let private playerR = 14.0f
    let private outerR = 17.5f

    let private condPaint (cond: int) =
        if cond >= 70 then Paints.condGood
        elif cond >= 45 then Paints.condMid
        else Paints.condLow

    let private jerseyGradient (cx: float32) (cy: float32) (isHome: bool) =
        let r, g, b = if isHome then 30uy, 100uy, 230uy else 200uy, 40uy, 40uy
        let darkR, darkG, darkB = if isHome then 10uy, 50uy, 170uy else 140uy, 15uy, 15uy

        SKShader.CreateRadialGradient(
            SKPoint(cx - 3.0f, cy - 5.0f),
            playerR * 2.0f,
            [| SKColor(r, g, b, 255uy); SKColor(darkR, darkG, darkB, 255uy) |],
            [| 0.0f; 1.0f |],
            SKShaderTileMode.Clamp
        )

    let private drawVelocityIndicator (canvas: SKCanvas) (cx: float32) (cy: float32) (vx: float) (vy: float) =
        let speed = sqrt (vx * vx + vy * vy)

        if speed > 1.5 then
            let nx = float32 vx / float32 speed
            let ny = float32 vy / float32 speed
            let scale = min (float32 speed * 4.0f) 20.0f
            canvas.DrawLine(cx, cy, cx + nx * scale, cy + ny * scale, Paints.velocityArrow)

    let drawPlayer
        (canvas: SKCanvas)
        (x: float)
        (y: float)
        (vx: float)
        (vy: float)
        (jerseyNum: int)
        (shortName: string)
        (condition: int)
        (isHome: bool)
        (hasBall: bool)
        =
        let cx, cy = toCanvas x y

        // Velocity indicator (behind player)
        drawVelocityIndicator canvas cx cy vx vy

        // Drop shadow
        canvas.DrawOval(
            SKRect(cx - outerR, cy - 2.0f + outerR * 0.4f, cx + outerR, cy + outerR * 0.85f),
            Paints.playerShadow
        )

        // Ball possession glow ring
        if hasBall then
            let glowPaint = if isHome then Paints.glowHome else Paints.glowAway
            canvas.DrawCircle(cx, cy, outerR + 6.0f, glowPaint)

        // Jersey body
        let fillPaint =
            if isHome then
                Paints.playerFillHome
            else
                Paints.playerFillAway

        fillPaint.Shader <- jerseyGradient cx cy isHome
        canvas.DrawCircle(cx, cy, playerR, fillPaint)

        // Condition ring
        canvas.DrawCircle(cx, cy, outerR, condPaint condition)

        // Jersey number
        canvas.DrawText(string jerseyNum, cx, cy + 4.0f, Paints.playerNumber)

        // Name label
        let nameW = Paints.playerName.MeasureText(shortName)
        let nameX = cx - nameW / 2.0f - 4.0f
        let nameY = cy + outerR + 4.0f
        let bgRect = SKRect(nameX - 2.0f, nameY, nameX + nameW + 8.0f, nameY + 11.0f)
        canvas.DrawRoundRect(bgRect, 3.0f, 3.0f, Paints.playerNameBg)
        canvas.DrawText(shortName, cx, nameY + 9.0f, Paints.playerName)


// ---------------------------------------------------------------------------
// Ball renderer
// ---------------------------------------------------------------------------

module BallRenderer =

    open PitchCoords

    let private ballR = 10.0f

    let private lightDirX = -0.6f
    let private lightDirY = -0.8f

    let private mkShadowPaint (height: float32) =
        let alpha = max 15uy (80uy - byte (min (height * 3.0f) 65.0f))
        let blur = 3.0f + height * 0.4f

        new SKPaint(
            Color = SKColor(0uy, 0uy, 0uy, alpha),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur)
        )

    let private mkTrailShader (speed: float32) (angle: float32) (r: float32) =
        let len = min (speed * 2.5f) 22.0f

        if len < 3.0f then
            None
        else
            let x1 = -MathF.Cos(angle) * len
            let y1 = -MathF.Sin(angle) * len
            let x2 = r * 0.3f * MathF.Cos(angle)
            let y2 = r * 0.3f * MathF.Sin(angle)

            Some(
                SKShader.CreateLinearGradient(
                    SKPoint(x1, y1),
                    SKPoint(x2, y2),
                    [| SKColor(245uy, 245uy, 245uy, 0uy); SKColor(245uy, 245uy, 245uy, 90uy) |],
                    [| 0.0f; 1.0f |],
                    SKShaderTileMode.Clamp
                )
            )

    let private drawPentagon
        (canvas: SKCanvas)
        (cx: float32)
        (cy: float32)
        (radius: float32)
        (rotation: float32)
        (paint: SKPaint)
        =
        use path = new SKPath()
        let mutable first = true

        for i in 0..4 do
            let a = rotation + float32 i * 2.0f * MathF.PI / 5.0f - MathF.PI / 2.0f
            let px = cx + MathF.Cos(a) * radius
            let py = cy + MathF.Sin(a) * radius
            if first then path.MoveTo(px, py) else path.LineTo(px, py)
            first <- false

        path.Close()
        canvas.DrawPath(path, paint)

    let draw
        (canvas: SKCanvas)
        (bx: float)
        (by: float)
        (height: float)
        (vx: float)
        (vy: float)
        (vz: float)
        (spinTop: float)
        (dt: float)
        =
        let cx, cy = toCanvas bx by
        let speed = float32 (sqrt (vx * vx + vy * vy))
        let h = float32 height

        let heightScale = max 0.85f (1.0f - h * 0.004f)
        let r = ballR * heightScale

        let moveAngle = float32 (Math.Atan2(vy, vx))
        let isBouncing = h < 0.15f && vz > 1.5
        let squashX, squashY = if isBouncing then 1.18f, 0.82f else 1.0f, 1.0f

        canvas.Save() |> ignore
        canvas.Translate(cx, cy)

        let shadowOffsetX = lightDirX * (12.0f + h * 1.8f)
        let shadowOffsetY = lightDirY * (12.0f + h * 1.8f)
        let shadowScale = 1.0f + h * 0.02f
        let shadowR = r * shadowScale

        use shadowPaint = mkShadowPaint h

        canvas.DrawOval(
            SKRect(
                shadowOffsetX - shadowR * 1.2f,
                shadowOffsetY - shadowR * 0.5f,
                shadowOffsetX + shadowR * 1.2f,
                shadowOffsetY + shadowR * 0.7f
            ),
            shadowPaint
        )

        if speed > 5.0f then
            match mkTrailShader speed moveAngle r with
            | Some shader ->
                use trailPaint =
                    new SKPaint(IsAntialias = true, Style = SKPaintStyle.Fill, Shader = shader)

                canvas.DrawCircle(0.0f, 0.0f, r * 0.9f, trailPaint)
            | None -> ()

        canvas.Save() |> ignore
        canvas.Scale(squashX, squashY)

        if speed > 5.0f then
            canvas.RotateDegrees(moveAngle * 180.0f / MathF.PI)

        let motionStretch = 1.0f + min (speed * 0.006f) 0.25f
        canvas.Scale(motionStretch, 1.0f)

        canvas.DrawCircle(0.0f, 0.0f, r, Paints.ballBase)

        let spinRad = float32 spinTop
        let spinRotation = spinRad * float32 dt * 8.0f

        canvas.Save() |> ignore
        canvas.RotateRadians(spinRotation)

        drawPentagon canvas 0.0f 0.0f (r * 0.38f) 0.0f Paints.ballPanel

        for i in 0..4 do
            let a = float32 i * 2.0f * MathF.PI / 5.0f
            let px = MathF.Cos(a) * r * 0.62f
            let py = MathF.Sin(a) * r * 0.62f
            drawPentagon canvas px py (r * 0.32f) (a + MathF.PI / 5.0f) Paints.ballPanel

        canvas.Restore()

        let hlX = lightDirX * r * 0.35f
        let hlY = lightDirY * r * 0.35f
        let hlIntensity = max 0.4f (1.0f - speed * 0.02f)
        let hlAlpha = byte (int (float32 190 * hlIntensity))

        use hlPaint =
            new SKPaint(Color = SKColor(255uy, 255uy, 255uy, hlAlpha), IsAntialias = true, Style = SKPaintStyle.Fill)

        canvas.DrawOval(SKRect(hlX - r * 0.25f, hlY - r * 0.35f, hlX + r * 0.15f, hlY + r * 0.05f), hlPaint)

        canvas.DrawCircle(0.0f, 0.0f, r, Paints.ballOutline)

        canvas.Restore()
        canvas.Restore()


// ---------------------------------------------------------------------------
// HUD elements (drawn in Skia, overlaid on top)
// ---------------------------------------------------------------------------

module HudRenderer =

    open PitchCoords

    let private roundedRect (canvas: SKCanvas) (x: float32) (y: float32) (w: float32) (h: float32) =
        canvas.DrawRoundRect(SKRect(x, y, x + w, y + h), 8.0f, 8.0f, Paints.hudBg)
        canvas.DrawRoundRect(SKRect(x, y, x + w, y + h), 8.0f, 8.0f, Paints.hudBorder)

    let drawScore
        (canvas: SKCanvas)
        (homeName: string)
        (awayName: string)
        (homeScore: int)
        (awayScore: int)
        (minute: int)
        =
        let w = 280.0f
        let h = 42.0f
        let x = PitchW / 2.0f - w / 2.0f
        let y = 12.0f

        roundedRect canvas x y w h

        // Home name
        let homeNamePaint = Paints.hudTextSub
        homeNamePaint.Color <- SKColor(59uy, 130uy, 246uy, 230uy)
        homeNamePaint.TextAlign <- SKTextAlign.Right
        canvas.DrawText(homeName.ToUpper(), x + w / 2.0f - 28.0f, y + 16.0f, homeNamePaint)

        // Score
        let scoreTxt = $"{homeScore}  –  {awayScore}"
        canvas.DrawText(scoreTxt, x + w / 2.0f, y + 27.0f, Paints.hudText)

        // Away name
        let awayNamePaint = Paints.hudTextSub
        awayNamePaint.Color <- SKColor(239uy, 68uy, 68uy, 230uy)
        awayNamePaint.TextAlign <- SKTextAlign.Left
        canvas.DrawText(awayName.ToUpper(), x + w / 2.0f + 28.0f, y + 16.0f, awayNamePaint)

        // Minute pill (top right corner)
        let minW = 58.0f
        let minX = PitchW - minW - 12.0f
        roundedRect canvas minX y minW h

        let minutePaint = Paints.hudText
        minutePaint.Color <- SKColor(245uy, 158uy, 11uy, 255uy)
        minutePaint.TextAlign <- SKTextAlign.Center
        canvas.DrawText($"{minute}'", minX + minW / 2.0f, y + 27.0f, minutePaint)

    let drawPossession (canvas: SKCanvas) (teamName: string) (isHome: bool) =
        let w = 170.0f
        let h = 32.0f
        let x = 12.0f
        let y = 12.0f

        roundedRect canvas x y w h

        let dotColor =
            if isHome then
                SKColor(59uy, 130uy, 246uy, 255uy)
            else
                SKColor(239uy, 68uy, 68uy, 255uy)

        use dotPaint =
            new SKPaint(Color = dotColor, IsAntialias = true, Style = SKPaintStyle.Fill)

        canvas.DrawCircle(x + 14.0f, y + h / 2.0f, 5.0f, dotPaint)

        let namePaint = Paints.hudTextSub
        namePaint.Color <- dotColor
        namePaint.TextAlign <- SKTextAlign.Left
        canvas.DrawText(teamName.ToUpper(), x + 26.0f, y + h / 2.0f + 4.0f, namePaint)

    let drawSituationBanner (canvas: SKCanvas) (text: string) (color: SKColor) =
        let w = 320.0f
        let h = 52.0f
        let x = PitchW / 2.0f - w / 2.0f
        let y = PitchH / 2.0f - h / 2.0f - 60.0f

        use bgPaint =
            new SKPaint(Color = SKColor(15uy, 23uy, 42uy, 220uy), IsAntialias = true, Style = SKPaintStyle.Fill)

        use borderPaint =
            new SKPaint(Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.0f)

        use textPaint =
            new SKPaint(
                Color = color,
                IsAntialias = true,
                TextSize = 20.0f,
                TextAlign = SKTextAlign.Center,
                Typeface =
                    SKTypeface.FromFamilyName(
                        "Inter",
                        SKFontStyleWeight.Bold,
                        SKFontStyleWidth.Normal,
                        SKFontStyleSlant.Upright
                    )
            )

        canvas.DrawRoundRect(SKRect(x, y, x + w, y + h), 10.0f, 10.0f, bgPaint)
        canvas.DrawRoundRect(SKRect(x, y, x + w, y + h), 10.0f, 10.0f, borderPaint)
        canvas.DrawText(text, PitchW / 2.0f, y + 32.0f, textPaint)

    let drawMomentum (canvas: SKCanvas) (momentum: float) (homeName: string) (awayName: string) =
        let barW = 260.0f
        let barH = 6.0f
        let pillH = 34.0f
        let x = PitchW / 2.0f - barW / 2.0f - 14.0f
        let y = PitchH - pillH - 12.0f

        roundedRect canvas x y (barW + 28.0f) pillH

        let innerX = x + 14.0f
        let barY = y + pillH / 2.0f - barH / 2.0f + 7.0f

        canvas.DrawRoundRect(SKRect(innerX, barY, innerX + barW, barY + barH), 3.0f, 3.0f, Paints.momentumTrack)

        let norm = float32 ((momentum + 10.0) / 20.0) |> max 0.04f |> min 0.96f
        let homeW = norm * barW
        let awayW = barW - homeW

        canvas.DrawRoundRect(SKRect(innerX, barY, innerX + homeW, barY + barH), 3.0f, 3.0f, Paints.momentumHome)

        canvas.DrawRoundRect(
            SKRect(innerX + homeW, barY, innerX + homeW + awayW, barY + barH),
            3.0f,
            3.0f,
            Paints.momentumAway
        )

        let labelPaint = Paints.hudTextSub
        labelPaint.TextAlign <- SKTextAlign.Left
        labelPaint.Color <- SKColor(59uy, 130uy, 246uy, 190uy)
        canvas.DrawText(homeName.ToUpper(), innerX, y + 13.0f, labelPaint)

        labelPaint.TextAlign <- SKTextAlign.Right
        labelPaint.Color <- SKColor(239uy, 68uy, 68uy, 190uy)
        canvas.DrawText(awayName.ToUpper(), innerX + barW, y + 13.0f, labelPaint)


// ---------------------------------------------------------------------------
// ICustomDrawOperation — bridges Avalonia to Skia
// ---------------------------------------------------------------------------

open FootballEngine.Client.Views.Components // For RenderFrame, RenderPlayer, RenderBall, MatchInterp

type MatchDrawOp
    (ctx: MatchContext, renderFrame: RenderFrame, snapshot: SimSnapshot, userClubId: ClubId option, bounds: Rect) =
    let isUserAway = userClubId |> Option.exists (fun id -> id = ctx.Away.Id)

    interface ICustomDrawOperation with
        member _.Bounds = bounds
        member _.HitTest _ = false
        member _.Equals _ = false

        member _.Render(renderCtx) =
            let skia = renderCtx.TryGetFeature<ISkiaSharpApiLeaseFeature>()

            if isNull skia then
                ()
            else

                use lease = skia.Lease()
                let canvas = lease.SkCanvas
                canvas.Save() |> ignore

                // Scale canvas to fit bounds while preserving aspect ratio
                let scaleX = float32 bounds.Width / PitchCoords.PitchW
                let scaleY = float32 bounds.Height / PitchCoords.PitchH
                let scale = min scaleX scaleY

                let offX =
                    (float32 bounds.Width - PitchCoords.PitchW * scale) / 2.0f + float32 bounds.X

                let offY =
                    (float32 bounds.Height - PitchCoords.PitchH * scale) / 2.0f + float32 bounds.Y

                canvas.Translate(offX, offY)
                canvas.Scale(scale)

                // ── Pitch ──────────────────────────────────────────────────────

                PitchRenderer.drawStripes canvas
                PitchRenderer.drawMarkings canvas
                PitchRenderer.drawVignette canvas

                // ── Players ────────────────────────────────────────────────────

                renderFrame.Players
                |> Array.iter (fun rp ->
                    if not rp.IsSidelined then
                        PlayerRenderer.drawPlayer
                            canvas
                            rp.Position.X
                            rp.Position.Y
                            rp.Velocity.X
                            rp.Velocity.Y
                            rp.JerseyNumber
                            rp.ShortName
                            rp.Condition
                            rp.IsHome
                            rp.HasBall)

                // ── Ball trajectory line ───────────────────────────────────────

                match snapshot.BallTrajectory with
                | Some traj ->
                    let progress =
                        if traj.EstimatedArrivalSubTick > traj.LaunchSubTick then
                            float32 (snapshot.SubTick - int traj.LaunchSubTick)
                            / float32 (int traj.EstimatedArrivalSubTick - int traj.LaunchSubTick)
                        else
                            1.0f

                    if progress >= 0.0f && progress <= 1.0f then
                        // Verificar que la pelota interpolada esté cerca del origen de la trayectoria
                        // Si no lo está, significa que el interpolador la movió antes de que se lance
                        // y la línea quedaría desconectada visualmente
                        let bx = float renderFrame.Ball.Position.X
                        let by = float renderFrame.Ball.Position.Y
                        let odx = bx - float traj.OriginX
                        let ody = by - float traj.OriginY
                        let distFromOriginSq = odx * odx + ody * ody

                        if distFromOriginSq < 25.0 then // solo dibujar si la pelota está dentro de ~5m del origen
                            use trajPaint =
                                new SKPaint(
                                    Color = SKColor(255uy, 200uy, 50uy, 80uy),
                                    IsAntialias = true,
                                    Style = SKPaintStyle.Stroke,
                                    StrokeWidth = 2.0f,
                                    PathEffect = SKPathEffect.CreateDash([| 4.0f; 4.0f |], 0.0f)
                                )

                            let path = new SKPath()
                            let ox, oy = PitchCoords.toCanvas (float traj.OriginX) (float traj.OriginY)
                            let tx, ty = PitchCoords.toCanvas (float traj.TargetX) (float traj.TargetY)
                            path.MoveTo(ox, oy)
                            path.LineTo(tx, ty)
                            canvas.DrawPath(path, trajPaint)
                | None -> ()

                // ── Ball ───────────────────────────────────────────────────────

                let ballX = renderFrame.Ball.Position.X
                let ballY = renderFrame.Ball.Position.Y
                let ballHeight = renderFrame.Ball.Height
                let ballVx = renderFrame.Ball.Velocity.X
                let ballVy = renderFrame.Ball.Velocity.Y
                let ballVz = renderFrame.Ball.VelocityZ

                BallRenderer.draw canvas ballX ballY ballHeight ballVx ballVy ballVz renderFrame.Ball.Spin.Y 0.0333

                // ── HUD ────────────────────────────────────────────────────────

                let minute = int (renderFrame.TimeSeconds / 60.0)

                HudRenderer.drawScore
                    canvas
                    renderFrame.HomeName
                    renderFrame.AwayName
                    renderFrame.HomeScore
                    renderFrame.AwayScore
                    minute

                let possessingName, possessingIsHome =
                    match renderFrame.AttackingClubId with
                    | Some clubId when clubId = ctx.Home.Id -> renderFrame.HomeName, true
                    | Some clubId when clubId = ctx.Away.Id -> renderFrame.AwayName, false
                    | _ -> "", false // Default if no club is attacking or ID not matched

                if possessingName <> "" then
                    HudRenderer.drawPossession canvas possessingName possessingIsHome

                HudRenderer.drawMomentum canvas renderFrame.Momentum renderFrame.HomeName renderFrame.AwayName

                let bannerYellow = SKColor(245uy, 158uy, 11uy, 255uy)
                let bannerGreen = SKColor(16uy, 185uy, 129uy, 255uy)

                match snapshot.Flow with
                | MatchFlow.GoalPause _ -> HudRenderer.drawSituationBanner canvas "⚽  GOAL!" bannerGreen
                | MatchFlow.RestartDelay { Kind = SetPieceKind.Corner } ->
                    HudRenderer.drawSituationBanner canvas "Corner Kick" bannerYellow
                | MatchFlow.RestartDelay { Kind = SetPieceKind.GoalKick } ->
                    HudRenderer.drawSituationBanner canvas "Goal Kick" bannerYellow
                | MatchFlow.RestartDelay { Kind = SetPieceKind.ThrowIn } ->
                    HudRenderer.drawSituationBanner canvas "Throw In" bannerYellow
                | MatchFlow.RestartDelay { Kind = SetPieceKind.FreeKick } ->
                    HudRenderer.drawSituationBanner canvas "Free Kick" bannerYellow
                | MatchFlow.RestartDelay { Kind = SetPieceKind.KickOff } ->
                    HudRenderer.drawSituationBanner canvas "Kick Off" bannerYellow
                | MatchFlow.InjuryPause _ -> HudRenderer.drawSituationBanner canvas "Injury" bannerYellow
                | _ -> ()

                canvas.Restore()

        member _.Dispose() = ()

// ---------------------------------------------------------------------------
// Avalonia FuncUI custom control wrapper
// ---------------------------------------------------------------------------

type MatchPitchControl() =
    inherit Control()

    static let ctxProp =
        AvaloniaProperty.Register<MatchPitchControl, MatchContext option>("MatchCtx", None)

    static let renderFrameProp =
        AvaloniaProperty.Register<MatchPitchControl, RenderFrame option>("RenderFrame", None)

    static let snapshotProp =
        AvaloniaProperty.Register<MatchPitchControl, SimSnapshot option>("Snapshot", None)

    static let userProp =
        AvaloniaProperty.Register<MatchPitchControl, ClubId option>("UserClubId", None)

    static member MatchCtxProperty = ctxProp
    static member RenderFrameProperty = renderFrameProp
    static member SnapshotProperty = snapshotProp
    static member UserClubIdProperty = userProp

    override this.OnPropertyChanged(change: AvaloniaPropertyChangedEventArgs) =
        base.OnPropertyChanged(change)

        if
            change.Property = ctxProp
            || change.Property = renderFrameProp
            || change.Property = snapshotProp
            || change.Property = userProp
        then
            this.InvalidateVisual()

    member this.MatchCtx
        with get () = this.GetValue(ctxProp)
        and set v = this.SetValue(ctxProp, v) |> ignore

    member this.RenderFrame
        with get () = this.GetValue(renderFrameProp)
        and set v = this.SetValue(renderFrameProp, v) |> ignore

    member this.Snapshot
        with get () = this.GetValue(snapshotProp)
        and set v = this.SetValue(snapshotProp, v) |> ignore

    member this.UserClubId
        with get () = this.GetValue(userProp)
        and set v = this.SetValue(userProp, v) |> ignore

    override this.Render(drawingContext) =
        match this.MatchCtx, this.RenderFrame, this.Snapshot with
        | Some ctx, Some renderFrame, Some snapshot ->
            let op = new MatchDrawOp(ctx, renderFrame, snapshot, this.UserClubId, this.Bounds)

            drawingContext.Custom(op)
        | _ -> ()


// ---------------------------------------------------------------------------
// FuncUI DSL extension for MatchPitchControl
// ---------------------------------------------------------------------------

module MatchPitchView =

    let create (attrs: IAttr<MatchPitchControl> list) : IView<MatchPitchControl> =
        ViewBuilder.Create<MatchPitchControl>(attrs)

    type MatchPitchControl with

        static member matchCtx<'t when 't :> MatchPitchControl>(v: MatchContext option) : IAttr<'t> =
            AttrBuilder<'t>.CreateProperty<MatchContext option>(MatchPitchControl.MatchCtxProperty, v, ValueNone)

        static member renderFrame<'t when 't :> MatchPitchControl>(v: RenderFrame option) : IAttr<'t> =
            AttrBuilder<'t>.CreateProperty<RenderFrame option>(MatchPitchControl.RenderFrameProperty, v, ValueNone)

        static member userClubId<'t when 't :> MatchPitchControl>(v: ClubId option) : IAttr<'t> =
            AttrBuilder<'t>.CreateProperty<ClubId option>(MatchPitchControl.UserClubIdProperty, v, ValueNone)

        static member snapshot<'t when 't :> MatchPitchControl>(v: SimSnapshot option) : IAttr<'t> =
            AttrBuilder<'t>.CreateProperty<SimSnapshot option>(MatchPitchControl.SnapshotProperty, v, ValueNone)


// ---------------------------------------------------------------------------
// Interpolation helpers
// ---------------------------------------------------------------------------

module MatchViewer =

    open MatchPitchView

    let view
        (ctx: MatchContext)
        (renderFrame: RenderFrame)
        (snapshot: SimSnapshot option)
        (userClubId: ClubId option)
        : IView =
        let pitch =
            create
                [ MatchPitchControl.matchCtx (Some ctx)
                  MatchPitchControl.renderFrame (Some renderFrame)
                  MatchPitchControl.snapshot snapshot
                  MatchPitchControl.userClubId userClubId ]

        Border.create [ Border.child pitch; Border.margin (Thickness(8.0, 12.0, 8.0, 12.0)) ]


// ---------------------------------------------------------------------------
// MatchDayView — top-level page view wired to app State + Dispatch
// Resolves the `MatchDayView.view state dispatch` call in Program.fs
// ---------------------------------------------------------------------------

module MatchDayView =

    let private noMatchView () : IView =
        TextBlock.create
            [ TextBlock.text "No active match."
              TextBlock.horizontalAlignment HorizontalAlignment.Center
              TextBlock.verticalAlignment VerticalAlignment.Center ]
        :> IView

    let view (state: State) (dispatch: Msg -> unit) : IView =
        match state.ActiveMatchReplay with
        | None -> noMatchView ()
        | Some replay ->
            let snapCount = replay.Snapshots.Length

            if snapCount = 0 then
                noMatchView ()
            else

                let snapIdx = min state.ActiveMatchSnapshot (snapCount - 1)
                let currSnap = replay.Snapshots[snapIdx]

                let ctx = replay.Context
                let clock = defaultClock

                let currFrame =
                    MatchProjection.project ctx currSnap (subTicksToSeconds clock (currSnap.SubTick * 1<subtick>))

                let renderFrame =
                    let t = float32 state.InterpolationT

                    if snapIdx + 1 < snapCount && t > 0.0f then
                        let nextSnap = replay.Snapshots[snapIdx + 1]

                        let nextFrame =
                            MatchProjection.project ctx nextSnap (subTicksToSeconds clock (nextSnap.SubTick * 1<subtick>))

                        let actualDt =
                            float (nextSnap.SubTick - currSnap.SubTick) / float clock.SubTicksPerSecond

                        MatchInterp.hermite currFrame nextFrame (float t) actualDt
                    else
                        currFrame

                let userClubId =
                    match state.Mode with
                    | AppMode.InGame(gs, _) -> Some gs.UserClubId
                    | _ -> None

                let totalSnaps = snapCount - 1

                let progressPct =
                    if totalSnaps > 0 then
                        float snapIdx / float totalSnaps
                    else
                        0.0

                let stats = StatsPanel.computeStats ctx.Home.Id replay.Events
                let statsPanel = StatsPanel.buildStatsPanel stats

                let lastEvent = replay.Events |> List.tryLast

                let commentary =
                    match lastEvent with
                    | Some e ->
                        let playerName = "Player"
                        CommentaryOverlay.commentaryForEvent e playerName
                    | None -> ""

                DockPanel.create
                    [ DockPanel.lastChildFill true
                      DockPanel.children
                          [
                            // ── Commentary overlay ───────────────────────────
                            if commentary <> "" then
                                Border.create
                                    [ Border.dock Dock.Bottom
                                      Border.padding (Thickness(12.0, 4.0))
                                      Border.background "#1a1a2e"
                                      Border.child (CommentaryOverlay.buildCommentary commentary) ]
                            else
                                Border.create [ Border.dock Dock.Bottom; Border.height 0.0 ]

                            // ── Stats panel ──────────────────────────────────
                            Border.create
                                [ Border.dock Dock.Right
                                  Border.width 220.0
                                  Border.padding (Thickness(8.0))
                                  Border.background "#0f172a"
                                  Border.child (statsPanel) ]

                            // ── Playback controls bar ──────────────────────────
                            Border.create
                                [ Border.dock Dock.Bottom
                                  Border.padding (Thickness(12.0, 8.0))
                                  Border.background "#0f172a"
                                  Border.child (
                                      StackPanel.create
                                          [ StackPanel.orientation Orientation.Horizontal
                                            StackPanel.spacing 8.0
                                            StackPanel.horizontalAlignment HorizontalAlignment.Center
                                            StackPanel.children
                                                [
                                                  // Step back
                                                  Button.create
                                                      [ Button.content (Icons.iconMd Nav.skipFirst Theme.TextMain)
                                                        Button.onClick (fun _ -> dispatch (StepActiveMatch -1)) ]

                                                  // Play / pause
                                                  Button.create
                                                      [ Button.content (
                                                            if state.IsPlaying then
                                                                Icons.iconMd Nav.pause Theme.TextMain
                                                            else
                                                                Icons.iconMd Nav.play Theme.TextMain
                                                        )
                                                        Button.onClick (fun _ -> dispatch TogglePlayback) ]

                                                  // Step forward
                                                  Button.create
                                                      [ Button.content (Icons.iconMd Nav.skipLast Theme.TextMain)
                                                        Button.onClick (fun _ -> dispatch (StepActiveMatch 1)) ]

                                                  // Speed selector
                                                  ComboBox.create
                                                      [ ComboBox.width 80.0
                                                        ComboBox.selectedIndex (
                                                            match state.PlaybackSpeed with
                                                            | 1 -> 0
                                                            | 5 -> 1
                                                            | 10 -> 2
                                                            | 20 -> 3
                                                            | 50 -> 4
                                                            | _ -> 3
                                                        )
                                                        ComboBox.onSelectedIndexChanged (fun i ->
                                                            let speed =
                                                                match i with
                                                                | 0 -> 1
                                                                | 1 -> 5
                                                                | 2 -> 10
                                                                | 3 -> 20
                                                                | 4 -> 50
                                                                | _ -> 20

                                                            dispatch (SetPlaybackSpeed speed))
                                                        ComboBox.dataItems [ "1×"; "5×"; "10×"; "20×"; "50×" ] ]

                                                  // Snapshot counter
                                                  TextBlock.create
                                                      [ TextBlock.text $"{snapIdx} / {totalSnaps}"
                                                        TextBlock.verticalAlignment VerticalAlignment.Center
                                                        TextBlock.foreground Theme.TextSub
                                                        TextBlock.width 80.0 ]

                                                  // Progress percentage
                                                  TextBlock.create
                                                      [ TextBlock.text $"{progressPct * 100.0 |> int}%%"
                                                        TextBlock.verticalAlignment VerticalAlignment.Center
                                                        TextBlock.foreground Theme.TextMuted
                                                        TextBlock.width 40.0 ]

                                                  // Close
                                                  Button.create
                                                      [ Button.content (Icons.iconMd IconName.close Theme.TextMain)
                                                        Button.onClick (fun _ -> dispatch CloseActiveMatch) ] ] ]
                                  ) ]

                            // ── Pitch ─────────────────────────────────────────
                            MatchViewer.view ctx renderFrame (Some currSnap) userClubId ] ]
                :> IView
