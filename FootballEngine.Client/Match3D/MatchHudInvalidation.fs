namespace FootballEngine.Client.Match3D

open System
open SkiaSharp
open FootballEngine.Client.Views.Components

type MatchHudSnapshot =
    { Frame: RenderFrame
      Stats: MatchHudStats
      Performance: (float * float) option }

module MatchHudLayout =
    let scale width =
        max 0.88f (min 1.3f (float32 width / 1536.0f))

    let scoreX = 18.0f
    let top = 16.0f
    let scoreWidth = 430.0f
    let scoreHeight = 64.0f
    let rightWidth = 286.0f
    let mapWidth = 238.0f
    let mapHeight = 128.0f

    let rightX width =
        float32 width / scale width - rightWidth - top

    let carrierY width height = float32 height / scale width - 122.0f

    let mapX width =
        (float32 width / scale width - mapWidth) / 2.0f

    let mapY width height =
        float32 height / scale width - mapHeight - 17.0f

    // Include the antialiasing fringe so partial uploads cannot leave stale edge pixels.
    let rectangle width height x y w h =
        let scale = scale width

        SKRectI(
            max 0 (int (MathF.Floor(x * scale)) - 2),
            max 0 (int (MathF.Floor(y * scale)) - 2),
            min width (int (MathF.Ceiling((x + w) * scale)) + 2),
            min height (int (MathF.Ceiling((y + h) * scale)) + 2)
        )

module MatchHudInvalidation =
    let private carrier (frame: RenderFrame) =
        frame.Players
        |> Array.tryFind _.HasBall
        |> Option.map (fun player -> player.ShortName, player.Condition)

    let private sameMinimap (left: RenderFrame) (right: RenderFrame) =
        left.Ball.Position = right.Ball.Position
        && left.Players.Length = right.Players.Length
        && Array.forall2
            (fun (a: RenderPlayer) (b: RenderPlayer) ->
                a.Position = b.Position && a.IsHome = b.IsHome && a.HasBall = b.HasBall)
            left.Players
            right.Players

    let regions width height (previous: MatchHudSnapshot option) (current: MatchHudSnapshot) =
        match previous with
        | None -> [| SKRectI(0, 0, width, height) |]
        | Some previous ->
            let left, right = previous.Frame, current.Frame
            let oldStats, stats = previous.Stats, current.Stats
            let rectangle = MatchHudLayout.rectangle width height
            let rightX = MatchHudLayout.rightX width

            [| if
                   left.HomeName <> right.HomeName
                   || left.AwayName <> right.AwayName
                   || left.HomeScore <> right.HomeScore
                   || left.AwayScore <> right.AwayScore
                   || left.CompetitionName <> right.CompetitionName
                   || oldStats.HomeShortName <> stats.HomeShortName
                   || oldStats.AwayShortName <> stats.AwayShortName
                   || oldStats.HomeColor <> stats.HomeColor
                   || oldStats.AwayColor <> stats.AwayColor
               then
                   yield
                       rectangle
                           MatchHudLayout.scoreX
                           MatchHudLayout.top
                           MatchHudLayout.scoreWidth
                           MatchHudLayout.scoreHeight

               if int left.TimeSeconds <> int right.TimeSeconds || oldStats.Period <> stats.Period then
                   yield
                       rectangle
                           (MatchHudLayout.scoreX + 123.0f)
                           (MatchHudLayout.top + MatchHudLayout.scoreHeight)
                           184.0f
                           35.0f

               if previous.Performance <> current.Performance then
                   yield rectangle rightX MatchHudLayout.top MatchHudLayout.rightWidth 34.0f

               if oldStats <> stats then
                   yield rectangle rightX (MatchHudLayout.top + 37.0f) MatchHudLayout.rightWidth 194.0f

               if carrier left <> carrier right then
                   yield rectangle MatchHudLayout.scoreX (MatchHudLayout.carrierY width height) 350.0f 104.0f

               if
                   not (sameMinimap left right)
                   || oldStats.HomeColor <> stats.HomeColor
                   || oldStats.AwayColor <> stats.AwayColor
               then
                   yield
                       rectangle
                           (MatchHudLayout.mapX width)
                           (MatchHudLayout.mapY width height)
                           MatchHudLayout.mapWidth
                           MatchHudLayout.mapHeight |]
            |> Array.filter (fun region -> region.Width > 0 && region.Height > 0)
