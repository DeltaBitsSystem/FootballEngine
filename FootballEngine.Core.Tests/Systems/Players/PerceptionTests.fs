namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module PerceptionTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private ballTo (world: World) (side: ClubSide) (slot: int) : unit =
        let pid = (MatchWorld.lineup ctx side).Profiles.[slot].Id

        MatchWorld.updateBall world (fun b ->
            { b with
                Control = BallControl.Controlled(side, pid)
                LastTouch = Some pid })

    let perceptionTests =
        testList
            "FootballEngine.Core.Systems.Players.PerceptionTests"
            [ testCase "Cognitive reflects condition" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  world.Home.Frame.Condition.[5] <- 1.0f
                  world.Home.Frame.Condition.[6] <- 0.2f

                  PlayerPerception.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  let fresh = float world.Home.Frame.Cognitive.[5]
                  let tired = float world.Home.Frame.Cognitive.[6]

                  Expect.isGreaterThan fresh 0.9 "fresh condition yields high cognition"
                  Expect.isLessThan tired 0.9 "tired condition yields low cognition"
                  Expect.isGreaterThan tired ctx.Config.Actions.CognitiveFatigueFloor "never below the floor")

              testCase "two blockers close the lane" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  // Ball carrier Home slot 5 at the centre.
                  world.Home.Frame.PosX.[5] <- 0.0f
                  world.Home.Frame.PosY.[5] <- 0.0f
                  // Teammate receiver slot 6 ahead.
                  world.Home.Frame.PosX.[6] <- 10.0f
                  world.Home.Frame.PosY.[6] <- 0.0f
                  ballTo world ClubSide.Home 5

                  // Two away opponents straddling the segment carrier→receiver.
                  world.Away.Frame.PosX.[3] <- 4.0f
                  world.Away.Frame.PosY.[3] <- 0.8f
                  world.Away.Frame.PosX.[7] <- 6.0f
                  world.Away.Frame.PosY.[7] <- -0.8f

                  PlayerPerception.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.isFalse world.Home.Frame.LaneClear.[6] "two blockers on the lane close it"

                  // Move the blockers away and re-run.
                  world.Away.Frame.PosX.[3] <- 20.0f
                  world.Away.Frame.PosY.[3] <- 20.0f
                  world.Away.Frame.PosX.[7] <- 20.0f
                  world.Away.Frame.PosY.[7] <- -20.0f

                  PlayerPerception.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.isTrue world.Home.Frame.LaneClear.[6] "free lane stays open")

              testCase "receiver facing away does not see the carrier" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.Home.Frame.PosX.[5] <- 0.0f
                  world.Home.Frame.PosY.[5] <- 0.0f
                  // Receiver at +10 heading further away (facing +X).
                  world.Home.Frame.PosX.[6] <- 10.0f
                  world.Home.Frame.PosY.[6] <- 0.0f
                  world.Home.Frame.VelX.[6] <- 8.0f
                  world.Home.Frame.VelY.[6] <- 0.0f
                  ballTo world ClubSide.Home 5

                  PlayerPerception.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.isFalse world.Home.Frame.FaceCone.[6] "receiver moving away cannot see the carrier")

              testCase "press count counts nearby opponents" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  // Spread both teams so only two opponents are near slot 5.
                  for s in 0..10 do
                      world.Home.Frame.PosX.[s] <- float32 (-40.0 + float (10 * s))
                      world.Home.Frame.PosY.[s] <- 20.0f
                      world.Away.Frame.PosX.[s] <- float32 (40.0 + float (10 * s))
                      world.Away.Frame.PosY.[s] <- 20.0f

                  world.Home.Frame.PosX.[5] <- 0.0f
                  world.Home.Frame.PosY.[5] <- 0.0f

                  world.Away.Frame.PosX.[3] <- 1.0f
                  world.Away.Frame.PosY.[3] <- 1.0f
                  world.Away.Frame.PosX.[7] <- 2.0f
                  world.Away.Frame.PosY.[7] <- -1.5f

                  PlayerPerception.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.equal world.Home.Frame.PressCount.[5] 2 "two opponents pressing the slot") ]
