namespace FootballEngine.Client.Views.MatchPitch

open System
open System.IO
open SkiaSharp

/// Owned by the drawing operation so Avalonia may safely retain it on its render thread.
type RenderResources() =
    let owned = ResizeArray<IDisposable>()

    let own (resource: 'T :> IDisposable) =
        owned.Add resource
        resource

    let fill color =
        own (new SKPaint(Color = SKColor.Parse color, IsAntialias = true))

    let stroke color width =
        own (
            new SKPaint(
                Color = SKColor.Parse color,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = width,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            )
        )

    let font = own (SKTypeface.FromFamilyName("Inter", SKFontStyle.Bold))

    let text color size =
        own (
            new SKPaint(
                Color = SKColor.Parse color,
                IsAntialias = true,
                TextSize = size,
                Typeface = font,
                TextAlign = SKTextAlign.Center
            )
        )

    let sprite name =
        let path =
            Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", $"player-{name}.png")

        if File.Exists path then
            match SKBitmap.Decode path with
            | null -> None
            | bitmap -> Some(own bitmap)
        else
            None

    let torso =
        let path = own (new SKPath())
        path.MoveTo(-6.8f, -7.5f)
        path.QuadTo(0.0f, -9.5f, 6.8f, -7.5f)
        path.LineTo(4.6f, 6.0f)
        path.QuadTo(0.0f, 8.5f, -4.6f, 6.0f)
        path.Close()
        path

    let shorts =
        let path = own (new SKPath())
        path.MoveTo(-4.7f, 4.5f)
        path.LineTo(4.7f, 4.5f)
        path.LineTo(5.2f, 9.5f)
        path.LineTo(0.8f, 10.2f)
        path.LineTo(0.0f, 7.8f)
        path.LineTo(-0.8f, 10.2f)
        path.LineTo(-5.2f, 9.5f)
        path.Close()
        path

    member val GrassDark = fill "#1C4A17"
    member val GrassLight = fill "#21551B"
    member val Stadium = fill "#0B1022"
    member val Line = stroke "#E2EFE8" 2.5f
    member val Spot = fill "#E2EFE8"
    member val Goal = fill "#30FFFFFF"
    member val Shadow = fill "#37000000"
    member val Home = fill "#1865F2"
    member val Away = fill "#F43F5E"
    member val HomeSleeve = stroke "#1865F2" 5.2f
    member val AwaySleeve = stroke "#F43F5E" 5.2f
    member val HomeShorts = stroke "#0A235C" 5.4f
    member val AwayShorts = stroke "#5C0C1E" 5.4f
    member val ShortsFill = fill "#14181F"
    member val Socks = stroke "#EBF2FF" 4.4f
    member val Skin = fill "#E0AC7E"
    member val SkinLimb = stroke "#E0AC7E" 3.6f
    member val Boot = stroke "#080C12" 4.6f
    member val Keeper = fill "#F4B942"
    member val KeeperSleeve = stroke "#F4B942" 5.2f
    member val Official = fill "#BEF23E"
    member val OfficialSleeve = stroke "#BEF23E" 4.6f
    member val Flag = fill "#FACC15"
    member val Number = text "#FFFFFF" 11.0f
    member val Name = text "#F0F0F0" 10.0f
    member val NameBackground = fill "#B90F172A"
    member val Possession = stroke "#FFF2A8" 2.0f
    member val Condition = stroke "#10B981" 2.0f
    member val Tired = stroke "#F59E0B" 2.0f
    member val Exhausted = stroke "#EF4444" 2.0f
    member val Ball = fill "#F8F8F4"
    member val BallPanel = fill "#191E20"
    member val BallOutline = stroke "#4B5250" 0.9f
    member val BallSeam = stroke "#2E3436" 0.65f
    member val BallTrail = stroke "#50FFFFFF" 2.4f
    member val Highlight = fill "#96FFFFFF"
    member val Banner = fill "#E8080F1C"
    member val BannerBorder = stroke "#FBBF24" 2.0f
    member val BannerText = text "#FBBF24" 20.0f
    member val HomeSprite = sprite "home"
    member val AwaySprite = sprite "away"
    member val KeeperSprite = sprite "keeper"
    member val OfficialSprite = sprite "official"
    member _.Torso = torso
    member _.Shorts = shorts

    interface IDisposable with
        member _.Dispose() =
            for index = owned.Count - 1 downto 0 do
                owned[index].Dispose()

            owned.Clear()
