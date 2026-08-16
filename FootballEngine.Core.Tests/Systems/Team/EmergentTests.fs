namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module EmergentTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private placeBallWing (world: World) (y: float<meter>) : unit =
        MatchWorld.updateBall world (fun b ->
            { b with
                Position =
                    { b.Position with
                        X = 30.0<meter>
                        Y = y } })

    let emergentTests =
        testList
            "FootballEngine.Core.Systems.Team.EmergentTests"
            [ testCase "wing play raises the wing channel" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  // Repeated runs with the ball on the right wing converge the EMA.
                  TeamEmergent.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore
                  placeBallWing world 20.0<meter>

                  for _ in 1..400 do
                      TeamEmergent.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.isGreaterThan
                      (float world.Home.Emergent.ChannelRight)
                      (float world.Home.Emergent.ChannelCenter)
                      "attacks down the wing dominate the right channel")

              testCase "center play dominates the center channel" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeBallWing world 0.0<meter>

                  for _ in 1..400 do
                      TeamEmergent.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.isGreaterThan
                      (float world.Home.Emergent.ChannelCenter)
                      (float world.Home.Emergent.ChannelLeft)
                      "central attacks dominate the center channel")

              testCase "channels normalize to a bounded fraction" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeBallWing world 8.0<meter>

                  for _ in 1..200 do
                      TeamEmergent.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  let total =
                      float world.Home.Emergent.ChannelCenter
                      + float world.Home.Emergent.ChannelLeft
                      + float world.Home.Emergent.ChannelRight

                  Expect.isLessThan total 1.001 "normalized channels sum to ~1") ]
