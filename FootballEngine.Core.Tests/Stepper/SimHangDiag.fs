namespace FootballEngine.Core.Tests.Stepper

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// Simulation health checks. Restarts must re-enter the ball into play cleanly:
/// a throw-in (or any restart) can never wedge the ball against the touchline or
/// fail to hand play back to the kicking side.
module SimHangDiag =

    let private runWindow (ticks: int) (seed: uint64) : int * int * int * int =
        let ctx = TestBuilders.defaultCtx ()
        let world = MatchFactory.create ctx

        let sctx =
            { Ctx = ctx
              Clock = SimulationClock.defaultClock }

        let buf =
            { Events = ResizeArray()
              Flags = { EventFlags.Bits = 0UL }
              Applied = ResizeArray() }

        let mutable rng = Rng.ofSeed seed

        let mutable throwIns = 0
        let mutable maxHug = 0L
        let mutable curHug = 0L
        let mutable shotSameSpot = 0

        let pending = ResizeArray<int<subtick>>()
        let lastThrowSpot = ResizeArray<float<meter> * float<meter>>()

        for _ in 1..ticks do
            let rng', _, applied = Stepper.step Scheduler.systems sctx rng world buf
            rng <- rng'
            let ball = world.State.Ball.Position

            for ev in applied do
                match ev.Data with
                | EventData.SetPiece(SetPieceKind.ThrowIn, _) ->
                    throwIns <- throwIns + 1
                    pending.Add(ev.Tick)

                    let repeated =
                        lastThrowSpot
                        |> Seq.exists (fun (x, y) ->
                            abs (float x - float ball.X) < 3.0 && abs (float y - float ball.Y) < 3.0)

                    if repeated then
                        shotSameSpot <- shotSameSpot + 1

                    if lastThrowSpot.Count > 16 then
                        lastThrowSpot.RemoveAt 0

                    lastThrowSpot.Add(ball.X, ball.Y)
                | EventData.Pass(_, _, _, _, true, _, _, _, _) when pending.Count > 0 -> pending.RemoveAt 0 |> ignore
                | _ -> ()

            let py = abs (float world.State.Ball.Position.Y)

            if world.State.Flow = MatchFlow.Live && py >= float Pitch.HalfWidth - 0.25 then
                curHug <- curHug + 1L
                maxHug <- max maxHug curHug
            else
                curHug <- 0L

        throwIns, pending.Count, int maxHug, shotSameSpot

    let simHangDiag =
        testList
            "FootballEngine.Core.Stepper.SimHangDiag"
            [ testCase "throw-ins re-enter the ball into play" (fun _ ->
                  // Seed 1 reliably produces throw-ins; the invariant is that the
                  // ball never glues to the touchline and every throw is resolved
                  // by a completed pass. Seed 1 exposes the historical stick bug.
                  let throwIns, unresolved, maxHug, shotSameSpot = runWindow 90000 1UL

                  eprintfn
                      "seed window: throws=%d unresolved=%d maxHugAtTouchline=%d repeatSpot=%d"
                      throwIns
                      unresolved
                      maxHug
                      shotSameSpot

                  Expect.isGreaterThan throwIns 0 "the window should produce throw-ins"
                  Expect.equal unresolved 0 "every throw-in must be followed by a completed pass"
                  Expect.isLessThan maxHug 20 "the ball must never sit glued to the touchline during live play"
                  Expect.isLessThan shotSameSpot 4 "throw-ins should not repeatedly re-award at the same spot")
              testCase "deterministic restart hygiene across two identical seeds" (fun _ ->
                  Expect.equal
                      (runWindow 30000 3UL)
                      (runWindow 30000 3UL)
                      "identical seeds must produce identical restart hygiene")
              testCase "throw-in restart places the ball inside the field" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()

                  MatchWorld.updateBall world (fun b ->
                      { b with
                          Position =
                              { b.Position with
                                  X = 20.0<meter>
                                  Y = Pitch.HalfWidth } })

                  EventApply.apply
                      ctx
                      world
                      { Tick = 0<subtick>
                        Data = EventData.SetPiece(SetPieceKind.ThrowIn, ClubSide.Home) }

                  match world.State.Flow with
                  | MatchFlow.RestartDelay(_, plan) ->
                      Expect.isLessThan
                          (abs (float plan.Y))
                          (float Pitch.HalfWidth - 0.1)
                          "the throw-in ball must sit inside the touchline, not on it"
                  | other -> failtestf "expected a restart delay, got %A" other) ]
