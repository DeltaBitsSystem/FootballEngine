namespace FootballEngine.Core.Tests.Systems.Players.Actions

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module SetPieceTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    /// Runs SetPieceRoutines on a SetPiece event and returns the buffer.
    let private runSetPiece (world: World) (kind: SetPieceKind) (side: ClubSide) : EventBuffer =
        let buf = buffer ()

        buf.Applied.Add
            { Tick = world.State.Tick
              Data = EventData.SetPiece(kind, side) }

        SetPieceRoutines.step sctx (Rng.ofSeed 5UL) world buf |> ignore
        buf

    let private placeBall (world: World) (x: float<meter>) (y: float<meter>) : unit =
        world.State <-
            { world.State with
                Ball =
                    { world.State.Ball with
                        Position =
                            { X = x
                              Y = y
                              Z = 0.0<meter>
                              Vx = 0.0<meterPerSecond>
                              Vy = 0.0<meterPerSecond>
                              Vz = 0.0<meterPerSecond> } } }

    let setPieceTests =
        testList
            "FootballEngine.Core.Systems.Players.Actions.SetPieceTests"
            [ testCase "direct free kick within range places a defensive wall" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeBall world (Pitch.awayGoalX - 20.0<meter>) 3.0<meter>

                  runSetPiece world SetPieceKind.FreeKick ClubSide.Home |> ignore

                  // The wall stands ~9.15 m from the ball toward the goal.
                  let anchorX = float (Pitch.awayGoalX - 20.0<meter>) + 9.15

                  let hasWall =
                      world.Away.Frame.PosX |> Array.exists (fun x -> abs (float x - anchorX) < 4.0)

                  Expect.isTrue hasWall "the defensive wall is placed 9.15 m from the ball")

              testCase "free kick too far forces a pass routine" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeBall world 5.0<meter> 0.0<meter>

                  // > DirectFreeKickRange (32 m) from the goal: no shot routine.
                  runSetPiece world SetPieceKind.FreeKick ClubSide.Home |> ignore

                  // The executor marker (Phase 201) should target a pass.
                  let hasPassMarker =
                      world.Home.Frame.RunLookup.Values
                      |> Seq.exists (fun a -> a.Phase = 201 && int a.TargetY = 1)

                  Expect.isTrue hasPassMarker "distant free kick is delivered by pass")

              testCase "corner placement pushes attackers toward the box" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeBall world Pitch.awayGoalX Pitch.HalfWidth

                  runSetPiece world SetPieceKind.Corner ClubSide.Home |> ignore

                  let attackerNearBox =
                      world.Home.Frame.PosY |> Array.exists (fun y -> abs (float y) < 15.0)

                  Expect.isTrue attackerNearBox "corner placement brings attackers toward the box") ]
