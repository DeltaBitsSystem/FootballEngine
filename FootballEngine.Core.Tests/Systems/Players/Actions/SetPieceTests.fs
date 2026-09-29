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

    /// Applies the award first so the routine observes the authoritative restart state.
    let private runSetPiece (world: World) (kind: SetPieceKind) (side: ClubSide) : EventBuffer =
        let buf = buffer ()
        let event =
            { Tick = world.State.Tick
              Data = EventData.SetPiece(kind, side) }

        EventApply.apply ctx world event
        buf.Applied.Add event

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
                       world.Away.Frame.Intent.TargetX |> Array.exists (fun x -> abs (float x - anchorX) < 4.0)

                  Expect.isTrue hasWall "the defensive wall is placed 9.15 m from the ball")

              testCase "free kick too far forces a pass routine" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeBall world 5.0<meter> 0.0<meter>

                  runSetPiece world SetPieceKind.FreeKick ClubSide.Home |> ignore

                  match world.State.Flow with
                  | MatchFlow.Restart restart ->
                      Expect.equal restart.Side ClubSide.Home "free kick belongs to the awarded side"
                      Expect.isSome (Some restart.Executor) "free kick appoints an executor"

                      world.State <-
                          { world.State with
                              Flow = MatchFlow.Restart { restart with Phase = RestartPhase.Ready } }

                      SetPieceRoutines.step sctx (Rng.ofSeed 5UL) world (buffer ()) |> ignore

                      match world.State.Ball.Impulse with
                      | Some impulse ->
                          Expect.equal impulse.StrikeKind (StrikeKind.Restart SetPieceKind.FreeKick) "free kick uses restart impulse"
                      | None -> failtest "distant free kick did not execute"
                  | flow -> failtestf "expected free kick restart, got %A" flow)

              testCase "close free kick keeps restart identity for a shot trajectory" (fun _ ->
                  let shotCtx =
                      { ctx with
                          Config =
                              { ctx.Config with
                                  SetPiece =
                                      { ctx.Config.SetPiece with
                                          FreeKickShotChance = 1.0 } } }

                  let systemCtx =
                      { Ctx = shotCtx
                        Clock = SimulationClock.defaultClock }

                  let world = TestBuilders.minimalWorld ()
                  placeBall world (Pitch.awayGoalX - 20.0<meter>) 0.0<meter>
                  let setupBuffer = buffer ()
                  let event =
                      { Tick = world.State.Tick
                        Data = EventData.SetPiece(SetPieceKind.FreeKick, ClubSide.Home) }

                  EventApply.apply shotCtx world event
                  setupBuffer.Applied.Add event
                  SetPieceRoutines.step systemCtx (Rng.ofSeed 5UL) world setupBuffer |> ignore

                  match world.State.Flow with
                  | MatchFlow.Restart restart ->
                      world.State <-
                          { world.State with
                              Flow = MatchFlow.Restart { restart with Phase = RestartPhase.Ready } }

                      SetPieceRoutines.step systemCtx (Rng.ofSeed 5UL) world (buffer ()) |> ignore
                  | flow -> failtestf "expected free kick restart, got %A" flow

                  match world.State.Ball.Impulse with
                  | Some impulse ->
                      Expect.equal
                          impulse.StrikeKind
                          (StrikeKind.Restart SetPieceKind.FreeKick)
                          "free kick keeps restart identity"
                  | None -> failtest "close free kick did not produce an impulse")

              testCase "corner placement pushes attackers toward the box" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeBall world Pitch.awayGoalX Pitch.HalfWidth

                  runSetPiece world SetPieceKind.Corner ClubSide.Home |> ignore

                  let attackerNearBox =
                      world.Home.Frame.PosY |> Array.exists (fun y -> abs (float y) < 15.0)

                  Expect.isTrue attackerNearBox "corner placement brings attackers toward the box")

              testCase "every restart kind appoints a real executor" (fun _ ->
                  for kind in
                      [ SetPieceKind.ThrowIn
                        SetPieceKind.GoalKick
                        SetPieceKind.KickOff
                        SetPieceKind.Penalty ] do
                      let world = TestBuilders.minimalWorld ()
                      placeBall world 0.0<meter> 0.0<meter>
                      runSetPiece world kind ClubSide.Home |> ignore

                      match world.State.Flow with
                      | MatchFlow.Restart restart ->
                          Expect.equal restart.Kind kind (sprintf "%A preserves restart kind" kind)
                          Expect.equal restart.Side ClubSide.Home (sprintf "%A preserves restart side" kind)
                          Expect.isTrue
                              (MatchWorld.slotOf ctx ClubSide.Home restart.Executor).IsSome
                              (sprintf "%A appoints an active executor" kind)
                      | flow -> failtestf "%A did not create a restart: %A" kind flow) ]
