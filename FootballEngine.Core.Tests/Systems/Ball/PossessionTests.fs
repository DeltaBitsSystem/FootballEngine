namespace FootballEngine.Core.Tests.Systems.Ball

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module PossessionTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private place (world: World) (side: ClubSide) (slot: int) (x: float<meter>) (y: float<meter>) : unit =
        let team = MatchWorld.team world side
        team.Frame.PosX.[slot] <- float32 x
        team.Frame.PosY.[slot] <- float32 y

    let private placeAllFar (world: World) : unit =
        for s in [ ClubSide.Home; ClubSide.Away ] do
            for i in MatchWorld.activeSlots (MatchWorld.team world s).Frame do
                place world s i -60.0<meter> 40.0<meter>

    let private applyEvents (world: World) (buf: EventBuffer) : unit =
        for ev in buf.Events do
            EventApply.apply ctx world ev

    let possessionTests =
        testList
            "FootballEngine.Core.Systems.Ball.PossessionTests"
            [ testCase "Carrier drags the ball toward the goal" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, pid) } }

                  place world ClubSide.Home 5 10.0<meter> 0.0<meter>
                  let before = world.State.Ball.Position.X

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  Expect.isGreaterThan (float world.State.Ball.Position.X) (float before) "ball carried forward")

              testCase "Controlled ball remains inside when its carrier reaches a pitch boundary" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = Pitch.HalfLength - 0.1<meter>
                                          Y = Pitch.HalfWidth }
                                  Control = BallControl.Controlled(ClubSide.Home, pid) } }

                  place world ClubSide.Home 5 Pitch.HalfLength Pitch.HalfWidth
                  BallPossession.step sctx (Rng.ofSeed 3UL) world (buffer ()) |> ignore

                  Expect.isLessThanOrEqual world.State.Ball.Position.X Pitch.HalfLength "ball x remains legal"
                  Expect.isLessThanOrEqual world.State.Ball.Position.Y Pitch.HalfWidth "ball y remains legal")

              testCase "Receiving grace ends after the grace period" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id
                  let grace = ctx.Config.Ball.ReceptionGrace

                  world.State <-
                      { world.State with
                          Tick = 0<subtick>
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Receiving(ClubSide.Home, pid, 0<subtick>) } }

                  for t in 1 .. (int grace - 1) do
                      world.State <-
                          { world.State with
                              Tick = t * 1<subtick> }

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  match world.State.Ball.Control with
                  | BallControl.Receiving _ -> ()
                  | other -> failtestf "expected still Receiving, got %A" other

                  world.State <-
                      { world.State with
                          Tick = (int grace + 1) * 1<subtick> }

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  match world.State.Ball.Control with
                  | BallControl.Controlled(ClubSide.Home, pid') -> Expect.equal pid' pid "controlled after grace"
                  | other -> failtestf "expected Controlled, got %A" other)

              testCase "Nearest side wins a loose-ball contest" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let homePid = ctx.HomeLineup.Profiles.[5].Id

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 5.0<meter>
                                          Y = 0.0<meter> }
                                  Control = BallControl.Contesting ClubSide.Home } }

                  place world ClubSide.Home 5 5.0<meter> 0.0<meter>
                  place world ClubSide.Away 5 5.5<meter> 0.0<meter>

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.Possession(ClubSide.Home, pid) -> pid = homePid
                          | _ -> false)
                      "home won the contest")

              testCase "Nobody near a loose ball: no contest" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 40.0<meter>
                                          Y = 0.0<meter> }
                                  Control = BallControl.Contesting ClubSide.Home } }

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.isEmpty buf.Events "no contest")

              testCase "Long-stopped free ball is picked up" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[1].Id

                  world.State <-
                      { world.State with
                          Tick = 100<subtick>
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 1.0<meter>
                                          Y = 0.0<meter> }
                                  Control = BallControl.Free
                                  StationarySince = Some 0<subtick> } }

                  place world ClubSide.Home 1 1.0<meter> 0.0<meter>

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  match world.State.Ball.Control with
                  | BallControl.Controlled(ClubSide.Home, pid') -> Expect.equal pid' pid "picked up"
                  | other -> failtestf "expected Controlled, got %A" other)

              testCase "Not stationary long enough: no pickup" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Tick = 100<subtick>
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 1.0<meter>
                                          Y = 0.0<meter> }
                                  Control = BallControl.Free
                                  StationarySince = Some 90<subtick> } }

                  place world ClubSide.Home 1 1.0<meter> 0.0<meter>

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.isEmpty buf.Events "no pickup yet")

              testCase "press intent wins a loose ball over a faster idle player" (fun _ ->
                  let c = TestBuilders.defaultCtx ()

                  c.HomeLineup.Profiles.[5] <-
                      { c.HomeLineup.Profiles.[5] with
                          Pace = 90.0f }

                  c.AwayLineup.Profiles.[5] <-
                      { c.AwayLineup.Profiles.[5] with
                          Pace = 50.0f }

                  let sctx' =
                      { Ctx = c
                        Clock = SimulationClock.defaultClock }

                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 0.0<meter>
                                          Y = 0.0<meter> }
                                  Control = BallControl.Contesting ClubSide.Home } }

                  place world ClubSide.Home 5 6.0<meter> 0.0<meter>
                  place world ClubSide.Away 5 7.0<meter> 0.0<meter>

                  (MatchWorld.team world ClubSide.Away).Frame.Intent.Kind.[5] <- IntentCode.Press

                  let buf = buffer ()
                  BallPossession.step sctx' (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.Possession(ClubSide.Away, pid) -> pid = c.AwayLineup.Profiles.[5].Id
                          | _ -> false)
                      "slower pressing player won the contest")

              testCase "keeper picks up a loose ball in his own area" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  place world ClubSide.Home 0 -50.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = -50.0<meter>
                                          Y = 0.0<meter> }
                                  Control = BallControl.Contesting ClubSide.Home } }

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  match world.State.Ball.Control with
                  | BallControl.Controlled(ClubSide.Home, pid) ->
                      Expect.equal pid ctx.HomeLineup.Profiles.[0].Id "keeper held the ball"
                  | other -> failtestf "expected Controlled, got %A" other) ]
