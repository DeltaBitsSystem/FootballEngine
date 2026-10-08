namespace FootballEngine.Client.Match3D

open System
open SkiaSharp
open FootballEngine
open FootballEngine.Client.Views.Components
open FootballEngine.LiveMatch

module MatchHudRenderer =
    let periodLabel (period: MatchPeriod) =
        match period with
        | FirstHalf -> "1T"
        | HalfTimeInterval -> "DESC"
        | SecondHalf -> "2T"
        | ExtraTimeFirstHalf -> "1TE"
        | ExtraTimeInterval -> "DESC"
        | ExtraTimeSecondHalf -> "2TE"
        | PenaltyShootout -> "PEN"
        | FullTime -> "FIN"

    let private toColor fallback packed =
        let packed = if packed = 0u then fallback else packed

        SKColor(byte ((packed >>> 16) &&& 255u), byte ((packed >>> 8) &&& 255u), byte (packed &&& 255u), 255uy)

    let private initials (name: string) =
        if String.IsNullOrWhiteSpace name then
            "FC"
        else
            name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            |> Array.truncate 2
            |> Array.map (fun part -> part[0].ToString())
            |> String.concat ""

    let private abbreviation (name: string) =
        if String.IsNullOrWhiteSpace name then
            "FC"
        else
            name.Trim().ToUpperInvariant()
            |> fun text -> if text.Length > 3 then text.Substring(0, 3) else text

    let draw
        (canvas: SKCanvas)
        (width: int)
        (height: int)
        (frame: RenderFrame)
        (stats: MatchHudStats)
        (performance: (float * float) option)
        =
        canvas.Clear(SKColors.Transparent)

        let scale = MatchHudLayout.scale width
        let px value = value * scale
        let tokens = Theme.currentTokens.Value
        let colors = tokens.Colors
        let localized key = Localization.resolve key []
        let homeColor = toColor 0xC93643u stats.HomeColor
        let awayColor = toColor 0x1457A6u stats.AwayColor
        let white = SKColor.Parse colors.TextPrimary
        let muted = SKColor.Parse colors.TextSecondary
        let accent = SKColor.Parse colors.Accent
        let warning = SKColor.Parse colors.Warning
        let negative = SKColor.Parse colors.Negative
        let border = SKColor.Parse colors.BorderSubtle
        let panelColor = SKColor.Parse(colors.Canvas).WithAlpha 245uy
        let miniPanelColor = SKColor.Parse(colors.Surface1).WithAlpha 210uy

        use panelPaint =
            new SKPaint(Color = panelColor, IsAntialias = true, Style = SKPaintStyle.Fill)

        use miniPanelPaint =
            new SKPaint(Color = miniPanelColor, IsAntialias = true, Style = SKPaintStyle.Fill)

        use whitePaint =
            new SKPaint(Color = white, IsAntialias = true, Style = SKPaintStyle.Fill)

        use mutedPaint =
            new SKPaint(Color = muted, IsAntialias = true, Style = SKPaintStyle.Fill)

        use accentPaint =
            new SKPaint(Color = accent, IsAntialias = true, Style = SKPaintStyle.Fill)

        use homePaint =
            new SKPaint(Color = homeColor, IsAntialias = true, Style = SKPaintStyle.Fill)

        use awayPaint =
            new SKPaint(Color = awayColor, IsAntialias = true, Style = SKPaintStyle.Fill)

        use font = SKTypeface.FromFamilyName(tokens.Typography.Family, SKFontStyle.Normal)
        use boldFont = SKTypeface.FromFamilyName(tokens.Typography.Family, SKFontStyle.Bold)

        let text (color: SKColor) (typeface: SKTypeface) (size: float32) (x: float32) (y: float32) (content: string) =
            use paint = new SKPaint(Color = color, IsAntialias = true)
            use skFont = new SKFont(typeface, px size, 1.0f, 0.0f)
            skFont.Subpixel <- true
            canvas.DrawText(content, px x, px y, SKTextAlign.Left, skFont, paint)

        let fittedText color typeface size x y maxWidth (content: string) =
            use skFont = new SKFont(typeface, px size, 1.0f, 0.0f)
            let widthLimit = px maxWidth
            let mutable label = content

            while label.Length > 1 && skFont.MeasureText(label) > widthLimit do
                label <- label.Substring(0, label.Length - 1)

            text color typeface size x y (if label <> content then label.TrimEnd() + "…" else label)

        let panel x y w h paint =
            let bounds = SKRect(px x, px y, px (x + w), px (y + h))
            let radius = px (float32 tokens.Radii.Panel)
            canvas.DrawRoundRect(bounds, radius, radius, paint)

            use edge =
                new SKPaint(
                    Color = border.WithAlpha 190uy,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = max 1.0f (px 1.0f)
                )

            canvas.DrawRoundRect(bounds, radius, radius, edge)

        let drawCircle x y radius paint =
            canvas.DrawCircle(px x, px y, px radius, paint)

        let line color thickness x1 y1 x2 y2 =
            use paint =
                new SKPaint(
                    Color = color,
                    IsAntialias = true,
                    StrokeWidth = px thickness,
                    StrokeCap = SKStrokeCap.Round
                )

            canvas.DrawLine(px x1, px y1, px x2, px y2, paint)

        let top = MatchHudLayout.top
        let scoreX = MatchHudLayout.scoreX
        let scoreY = top
        let scoreWidth = MatchHudLayout.scoreWidth
        let scoreHeight = MatchHudLayout.scoreHeight
        panel scoreX scoreY scoreWidth scoreHeight panelPaint

        fittedText
            muted
            boldFont
            11.0f
            (scoreX + 16.0f)
            (scoreY + 19.0f)
            312.0f
            (frame.CompetitionName |> Option.defaultValue "MATCH DAY")

        drawCircle (scoreX + scoreWidth - 21.0f) (scoreY + 16.0f) 3.5f accentPaint

        let badgeY = scoreY + 43.0f
        drawCircle (scoreX + 39.0f) badgeY 17.0f homePaint
        text white boldFont 10.0f (scoreX + 30.0f) (badgeY + 4.0f) (initials frame.HomeName)
        text white boldFont 19.0f (scoreX + 66.0f) (badgeY + 7.0f) (abbreviation stats.HomeShortName)
        text white boldFont 32.0f (scoreX + 170.0f) (badgeY + 11.0f) (string frame.HomeScore)
        text muted font 25.0f (scoreX + 206.0f) (badgeY + 9.0f) "–"
        text white boldFont 32.0f (scoreX + 232.0f) (badgeY + 11.0f) (string frame.AwayScore)

        let awayBadgeX = scoreX + scoreWidth - 36.0f
        drawCircle awayBadgeX badgeY 17.0f awayPaint
        text white boldFont 10.0f (awayBadgeX - 9.0f) (badgeY + 4.0f) (initials frame.AwayName)
        text white boldFont 19.0f (scoreX + 269.0f) (badgeY + 7.0f) (abbreviation stats.AwayShortName)

        let elapsed = max 0 (int frame.TimeSeconds)
        let clock = sprintf "%02d:%02d" (elapsed / 60) (elapsed % 60)
        let clockX = scoreX + 123.0f
        let clockY = scoreY + scoreHeight
        let clockWidth = 184.0f
        panel clockX clockY clockWidth 35.0f panelPaint
        text white boldFont 23.0f (clockX + 23.0f) (clockY + 27.0f) clock
        line (border.WithAlpha 190uy) 1.0f (clockX + 133.0f) (clockY + 8.0f) (clockX + 133.0f) (clockY + 27.0f)
        text muted boldFont 13.0f (clockX + 145.0f) (clockY + 24.0f) (periodLabel stats.Period)

        let rightX = MatchHudLayout.rightX width
        panel rightX top 286.0f 34.0f panelPaint

        let fpsLabel, frameTimeLabel =
            match performance with
            | Some(fps, milliseconds) -> sprintf "%.0f FPS" fps, sprintf "FRAME TIME  %.2f ms" milliseconds
            | None -> "— FPS", "FRAME TIME  — ms"

        text white boldFont 14.0f (rightX + 16.0f) (top + 14.0f) fpsLabel
        text muted font 10.0f (rightX + 16.0f) (top + 29.0f) frameTimeLabel

        let statsX = rightX
        let statsY = top + 37.0f
        let statsWidth = 286.0f
        let statsHeight = 194.0f
        panel statsX statsY statsWidth statsHeight panelPaint
        text white boldFont 12.0f (statsX + 16.0f) (statsY + 23.0f) (localized TranslationKey.MatchStats)

        line
            (border.WithAlpha 190uy)
            1.0f
            (statsX + 16.0f)
            (statsY + 31.0f)
            (statsX + statsWidth - 16.0f)
            (statsY + 31.0f)

        let drawStatsRow row label homeValue awayValue =
            let y = statsY + 49.0f + float32 row * 35.0f
            text muted font 11.0f (statsX + 16.0f) y label
            text white boldFont 12.0f (statsX + 16.0f) (y + 17.0f) (string homeValue)
            text white boldFont 12.0f (statsX + statsWidth - 38.0f) (y + 17.0f) (string awayValue)

            let barX = statsX + 54.0f
            let barY = y + 10.0f
            let barWidth = statsWidth - 108.0f
            let barBounds = SKRect(px barX, px barY, px (barX + barWidth), px (barY + 4.0f))

            use barBackground =
                new SKPaint(Color = SKColor.Parse colors.Surface3, IsAntialias = true)

            canvas.DrawRoundRect(barBounds, px 2.0f, px 2.0f, barBackground)

            let leftShare =
                if row = 0 then
                    float32 (max 0 stats.HomePossession) / 100.0f
                elif homeValue + awayValue = 0 then
                    0.5f
                else
                    float32 homeValue / float32 (homeValue + awayValue)

            use barFill = new SKPaint(Color = homeColor, IsAntialias = true)

            let fillBounds =
                SKRect(px barX, px barY, px (barX + barWidth * max 0.0f (min 1.0f leftShare)), px (barY + 4.0f))

            canvas.DrawRoundRect(fillBounds, px 2.0f, px 2.0f, barFill)

            use awayFill = new SKPaint(Color = awayColor, IsAntialias = true)
            let rightStart = barX + barWidth * max 0.0f (min 1.0f leftShare) + 2.0f

            let rightBounds =
                SKRect(px rightStart, px barY, px (barX + barWidth), px (barY + 4.0f))

            if rightBounds.Width > 0.0f then
                canvas.DrawRoundRect(rightBounds, px 2.0f, px 2.0f, awayFill)

        drawStatsRow 0 (localized TranslationKey.MatchPossession) stats.HomePossession stats.AwayPossession
        drawStatsRow 1 (localized TranslationKey.MatchShots) stats.HomeShots stats.AwayShots
        drawStatsRow 2 (localized TranslationKey.MatchOnTarget) stats.HomeOnTarget stats.AwayOnTarget
        drawStatsRow 3 (localized TranslationKey.MatchPasses) stats.HomePasses stats.AwayPasses

        match frame.Players |> Array.tryFind _.HasBall with
        | Some carrier ->
            let cardX = 18.0f
            let cardY = MatchHudLayout.carrierY width height
            let cardWidth = 350.0f
            let cardHeight = 104.0f
            panel cardX cardY cardWidth cardHeight panelPaint

            use iconBack = new SKPaint(Color = border, IsAntialias = true)

            use iconPaint = new SKPaint(Color = muted, IsAntialias = true)

            drawCircle (cardX + 47.0f) (cardY + 50.0f) 31.0f iconBack
            drawCircle (cardX + 47.0f) (cardY + 39.0f) 10.0f iconPaint

            let torso =
                SKRect(px (cardX + 31.0f), px (cardY + 49.0f), px (cardX + 63.0f), px (cardY + 70.0f))

            canvas.DrawRoundRect(torso, px 10.0f, px 10.0f, iconPaint)

            text accent boldFont 11.0f (cardX + 91.0f) (cardY + 23.0f) (localized TranslationKey.MatchBallCarrier)
            fittedText white boldFont 19.0f (cardX + 91.0f) (cardY + 49.0f) 240.0f carrier.ShortName

            let tired = carrier.Condition < 65

            let conditionColor =
                if carrier.Condition < 45 then negative
                elif tired then warning
                else accent

            text
                conditionColor
                boldFont
                11.0f
                (cardX + 91.0f)
                (cardY + 73.0f)
                (localized (
                    if tired then
                        TranslationKey.MatchTired
                    else
                        TranslationKey.MatchFit
                ))

            text white font 11.0f (cardX + 236.0f) (cardY + 73.0f) (sprintf "%d%%" carrier.Condition)

            use conditionBackground = new SKPaint(Color = border, IsAntialias = true)

            use conditionFill = new SKPaint(Color = conditionColor, IsAntialias = true)
            let conditionY = cardY + 84.0f

            let conditionBounds =
                SKRect(px (cardX + 91.0f), px conditionY, px (cardX + 328.0f), px (conditionY + 7.0f))

            let fillWidth = 237.0f * float32 (max 0 (min 100 carrier.Condition)) / 100.0f

            let fillBounds =
                SKRect(px (cardX + 91.0f), px conditionY, px (cardX + 91.0f + fillWidth), px (conditionY + 7.0f))

            canvas.DrawRoundRect(conditionBounds, px 3.5f, px 3.5f, conditionBackground)
            canvas.DrawRoundRect(fillBounds, px 3.5f, px 3.5f, conditionFill)
        | None -> ()

        let mapWidth = MatchHudLayout.mapWidth
        let mapHeight = MatchHudLayout.mapHeight
        let mapX = MatchHudLayout.mapX width
        let mapY = MatchHudLayout.mapY width height
        panel mapX mapY mapWidth mapHeight miniPanelPaint

        let pitchX = mapX + 10.0f
        let pitchY = mapY + 12.0f
        let pitchWidth = mapWidth - 20.0f
        let pitchHeight = mapHeight - 24.0f
        let pitchColor = muted.WithAlpha 185uy
        line pitchColor 1.0f pitchX pitchY (pitchX + pitchWidth) pitchY
        line pitchColor 1.0f pitchX (pitchY + pitchHeight) (pitchX + pitchWidth) (pitchY + pitchHeight)
        line pitchColor 1.0f pitchX pitchY pitchX (pitchY + pitchHeight)
        line pitchColor 1.0f (pitchX + pitchWidth) pitchY (pitchX + pitchWidth) (pitchY + pitchHeight)
        line pitchColor 1.0f (pitchX + pitchWidth / 2.0f) pitchY (pitchX + pitchWidth / 2.0f) (pitchY + pitchHeight)
        let boxWidth = 29.0f
        let boxTop = pitchY + pitchHeight * 0.23f
        let boxBottom = pitchY + pitchHeight * 0.77f
        line pitchColor 1.0f (pitchX + boxWidth) boxTop (pitchX + boxWidth) boxBottom
        line pitchColor 1.0f pitchX boxTop (pitchX + boxWidth) boxTop
        line pitchColor 1.0f pitchX boxBottom (pitchX + boxWidth) boxBottom
        line pitchColor 1.0f (pitchX + pitchWidth - boxWidth) boxTop (pitchX + pitchWidth - boxWidth) boxBottom
        line pitchColor 1.0f (pitchX + pitchWidth - boxWidth) boxTop (pitchX + pitchWidth) boxTop
        line pitchColor 1.0f (pitchX + pitchWidth - boxWidth) boxBottom (pitchX + pitchWidth) boxBottom

        use centerCirclePaint =
            new SKPaint(Color = pitchColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = px 1.0f)

        canvas.DrawCircle(
            px (pitchX + pitchWidth / 2.0f),
            px (pitchY + pitchHeight / 2.0f),
            px (pitchHeight * 0.18f),
            centerCirclePaint
        )

        for player in frame.Players do
            let playerX = pitchX + float32 ((player.Position.X + 52.5) / 105.0) * pitchWidth
            let playerY = pitchY + float32 ((player.Position.Y + 34.0) / 68.0) * pitchHeight

            let paint = if player.IsHome then homePaint else awayPaint
            drawCircle playerX playerY (if player.HasBall then 4.2f else 2.7f) paint

            if player.HasBall then
                use carrierRing =
                    new SKPaint(Color = accent, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = px 1.5f)

                canvas.DrawCircle(px playerX, px playerY, px 6.5f, carrierRing)

        let ballX = pitchX + float32 ((frame.Ball.Position.X + 52.5) / 105.0) * pitchWidth
        let ballY = pitchY + float32 ((frame.Ball.Position.Y + 34.0) / 68.0) * pitchHeight

        use ballPaint =
            new SKPaint(Color = SKColor(255uy, 221uy, 111uy, 255uy), IsAntialias = true)

        drawCircle ballX ballY 2.5f ballPaint


[<Sealed>]
type RetainedMatchHud(width: int, height: int) =
    let bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul)
    let canvas = new SKCanvas(bitmap)
    let mutable previous: MatchHudSnapshot option = None
    let mutable tokens = Theme.currentTokens.Value
    let mutable locale = Localization.active ()

    member _.Pixels = bitmap.GetPixels()
    member _.RowBytes = bitmap.RowBytes

    member _.Update(frame, stats, performance) =
        let current =
            { Frame = frame
              Stats = stats
              Performance = performance }

        let nextTokens = Theme.currentTokens.Value
        let nextLocale = Localization.active ()

        if tokens <> nextTokens || locale <> nextLocale then
            previous <- None

        let regions = MatchHudInvalidation.regions width height previous current

        if regions.Length > 0 then
            // A union clip redraws overlapping widgets once, preserving premultiplied alpha.
            use clip = new SKRegion()
            clip.SetRects regions |> ignore
            canvas.Save() |> ignore

            try
                canvas.ClipRegion clip
                MatchHudRenderer.draw canvas width height frame stats performance
                canvas.Flush()
            finally
                canvas.Restore()

        tokens <- nextTokens
        locale <- nextLocale
        previous <- Some current
        regions

    interface IDisposable with
        member _.Dispose() =
            canvas.Dispose()
            bitmap.Dispose()
