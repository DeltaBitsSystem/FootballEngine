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
            [ testCase "Carrier touches momentum without dragging ball position" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, pid) } }

                  place world ClubSide.Home 5 0.0<meter> 0.0<meter>
                  let before = world.State.Ball.Position.X

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  Expect.equal world.State.Ball.Position.X before "touch cannot teleport the ball"
                  Expect.isGreaterThan (float world.State.Ball.Position.Vx) 0.0 "touch drives the next physical step")

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

              testCase "Proximity cannot resolve a loose-ball contest" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

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

                  Expect.isEmpty buf.Events "a nearby player has not made physical contact"
                  Expect.equal world.State.Ball.Control (BallControl.Contesting ClubSide.Home) "the race remains unresolved")

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

              testCase "Elapsed time cannot pick up a free ball" (fun _ ->
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
                                  StationarySince = Some 0<subtick> } }

                  place world ClubSide.Home 1 1.0<meter> 0.0<meter>

                  let buf = buffer ()
                  BallPossession.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents world buf

                  Expect.equal world.State.Ball.Control BallControl.Free "free ball awaits physical contact"
                  Expect.isFalse
                      (buf.Events |> Seq.exists (fun event -> match event.Data with EventData.Possession _ -> true | _ -> false))
                      "stationary time is not a pickup")

              testCase "A reachable loose ball does not wait for an idle timer" (fun _ ->
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

                  Expect.isEmpty buf.Events "reach alone is not a recorded touch"
                  Expect.equal world.State.Ball.Control BallControl.Free "the ball remains loose until contact")

              testCase "press intent cannot win a loose ball without contact" (fun _ ->
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

                  place world ClubSide.Home 5 0.6<meter> 0.0<meter>
                  place world ClubSide.Away 5 0.7<meter> 0.0<meter>

                  (MatchWorld.team world ClubSide.Away).Frame.Intent.Kind.[5] <- IntentCode.Press

                  let buf = buffer ()
                  BallPossession.step sctx' (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.isEmpty buf.Events "intent changes movement, not the contact result"
                  Expect.equal world.State.Ball.Control (BallControl.Contesting ClubSide.Home) "neither side wins by scoring")

              testCase "keeper cannot acquire a contested ball by proximity" (fun _ ->
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

                  Expect.equal world.State.Ball.Control (BallControl.Contesting ClubSide.Home) "keeper needs a physical save or foot contact"
                  Expect.isEmpty buf.Events "keeper proximity cannot create a catch") ]
