module FootballEngine.Client.Tests.MatchHudTests

open System
open System.Runtime.InteropServices
open Expecto
open SkiaSharp
open FootballEngine
open FootballEngine.Client.Match3D
open FootballEngine.Client.Views.Components
open FootballEngine.LiveMatch

let private player: RenderPlayer =
    { Id = 1
      Position = Vector2.zero
      Height = 0.0
      VelocityZ = 0.0
      Velocity = Vector2.zero
      Acceleration = Vector2.zero
      BodyFacing = 0.0
      AngularVelocity = 0.0
      TravelDistance = 0.0
      State = Idle
      VisualSlot = 0
      ShortName = "PLAYER"
      Condition = 90
      IsHome = true
      HasBall = true
      IsSidelined = false }

let private frame: RenderFrame =
    { Players = [| player |]
      Ball =
        { Position = Vector2.zero
          Velocity = Vector2.zero
          VelocityZ = 0.0
          Orientation = System.Numerics.Quaternion.Identity
          AngularVelocity = System.Numerics.Vector3.Zero
          Radius = 0.11
          FootMotion = None
          Height = 0.0 }
      Officials = [||]
      TimeSeconds = 1.1
      HomeScore = 0
      AwayScore = 0
      AttackingClubId = None
      Momentum = 0.0
      Situation = MatchSituation.Live
      HomeName = "Home FC"
      AwayName = "Away FC"
      CompetitionName = Some "LEAGUE" }

let private stats: MatchHudStats =
    { HomeShortName = "HOM"
      AwayShortName = "AWY"
      Period = FirstHalf
      HomePossession = 50
      AwayPossession = 50
      HomeShots = 0
      AwayShots = 0
      HomeOnTarget = 0
      AwayOnTarget = 0
      HomePasses = 0
      AwayPasses = 0
      HomeColor = 0xC93643u
      AwayColor = 0x1457A6u }

let private snapshot =
    { Frame = frame
      Stats = stats
      Performance = Some(120.0, 8.33) }

let private regions next =
    MatchHudInvalidation.regions 1160 698 (Some snapshot) next

let private pixels (hud: RetainedMatchHud) height =
    let result = Array.zeroCreate<byte> (hud.RowBytes * height)
    Marshal.Copy(hud.Pixels, result, 0, result.Length)
    result

let tests =
    testSequenced
    <| testList
        "MatchHud"
        [ testCase "first image and resize initialize every pixel" (fun _ ->
              for width, height in [ 1160, 698; 800, 520 ] do
                  Expect.equal
                      (MatchHudInvalidation.regions width height None snapshot)
                      [| SKRectI(0, 0, width, height) |]
                      "full initialization")
          testCase "unchanged visible state has no upload" (fun _ ->
              Expect.isEmpty
                  (regions
                      { snapshot with
                          Frame =
                              { frame with
                                  TimeSeconds = 1.9
                                  Momentum = 0.2 } })
                  "fractional clock and invisible data do not repaint")
          testCase "clock and performance invalidate only their small widgets" (fun _ ->
              for next in
                  [ { snapshot with
                        Frame = { frame with TimeSeconds = 2.0 } }
                    { snapshot with
                        Performance = Some(144.0, 6.94) } ] do
                  let dirty = regions next
                  Expect.equal dirty.Length 1 "one widget"
                  Expect.isLessThan (dirty[0].Width * dirty[0].Height) 15000 "not a full-frame upload")
          testCase "moving minimap is updated without repainting all match text" (fun _ ->
              let next =
                  { snapshot with
                      Frame =
                          { frame with
                              Players =
                                  [| { player with
                                         Position = { X = 4.0; Y = 2.0 } } |] } }

              let dirty = regions next
              Expect.equal dirty.Length 1 "minimap only"
              Expect.isLessThan (dirty[0].Width * dirty[0].Height) 30000 "bounded moving region")
          testCase "carrier loss clears the old card and selection ring" (fun _ ->
              Expect.equal
                  (regions
                      { snapshot with
                          Frame =
                              { frame with
                                  Players = [| { player with HasBall = false } |] } })
                      .Length
                  2
                  "card and minimap")
          testCase "stats and team colours invalidate all affected widgets" (fun _ ->
              Expect.equal
                  (regions
                      { snapshot with
                          Stats = { stats with HomeShots = 1 } })
                      .Length
                  1
                  "stats only"

              Expect.equal
                  (regions
                      { snapshot with
                          Stats = { stats with HomeColor = 0xFFFFFFu } })
                      .Length
                  3
                  "score, stats and minimap")
          testCase "theme and language changes repaint the entire retained image" (fun _ ->
              let oldTokens = Theme.currentTokens.Value
              let oldLocale = Localization.active ()
              use hud = new RetainedMatchHud(1160, 698)

              try
                  hud.Update(frame, stats, snapshot.Performance) |> ignore

                  Theme.currentTokens.Value <-
                      { oldTokens with
                          Colors =
                              { oldTokens.Colors with
                                  Accent = "#FF0077" } }

                  Expect.equal
                      (hud.Update(frame, stats, snapshot.Performance))
                      [| SKRectI(0, 0, 1160, 698) |]
                      "theme revision"

                  Localization.setActive (if oldLocale = Locale.Es then Locale.En else Locale.Es)

                  Expect.equal
                      (hud.Update(frame, stats, snapshot.Performance))
                      [| SKRectI(0, 0, 1160, 698) |]
                      "locale revision"
              finally
                  Theme.currentTokens.Value <- oldTokens
                  Localization.setActive oldLocale)
          testCase "incremental pixels equal a full repaint including overlap and carrier removal" (fun _ ->
              for width, height in [ 1160, 698; 600, 400 ] do
                  use retained = new RetainedMatchHud(width, height)
                  retained.Update(frame, stats, snapshot.Performance) |> ignore

                  let updates =
                      [ { snapshot with
                            Frame =
                                { frame with
                                    HomeScore = 1
                                    TimeSeconds = 2.0
                                    Players = [| { player with Condition = 44 } |] } }
                        { snapshot with
                            Stats =
                                { stats with
                                    HomeColor = 0xF0F0F0u
                                    HomeShots = 2 }
                            Frame = { frame with Players = [||] } }
                        snapshot ]

                  for next in updates do
                      retained.Update(next.Frame, next.Stats, next.Performance) |> ignore
                      use fresh = new RetainedMatchHud(width, height)
                      fresh.Update(next.Frame, next.Stats, next.Performance) |> ignore
                      let actual, expected = pixels retained height, pixels fresh height
                      // Skia's clipped round-rect raster path can round an antialiased edge by one byte.
                      match
                          Array.zip actual expected
                          |> Array.tryFindIndex (fun (a, b) -> abs (int a - int b) > 1)
                      with
                      | Some index ->
                          failtestf
                              "dirty pixel differs at %dx%d: (%d,%d) channel %d, actual %d expected %d"
                              width
                              height
                              ((index / 4) % width)
                              (index / retained.RowBytes)
                              (index % 4)
                              actual[index]
                              expected[index]
                      | None -> ()) ]
