namespace FootballEngine.Core.Tests.Stepper

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// Contracts of restart hygiene: a throw-in must place the ball inside the
/// touchline, and during live play a loose ball must never sit still glued to
/// the line (a stuck ball is a broken restart). These are behavioural
/// invariants, not windowed diagnostics.
module RestartContractTests =

    let private runWindow (ticks: int) (seed: uint64) : unit =
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

        for _ in 1..ticks do
            let rng', _, _ = Stepper.step Scheduler.systems sctx rng world buf
            rng <- rng'

            if world.State.Flow = MatchFlow.Live then
                let ball = world.State.Ball
                let nearTouchline = abs (float ball.Position.Y) > float Pitch.HalfWidth - 0.5

                match ball.Control with
                | BallControl.Free when nearTouchline ->
                    let speed = MatchMath.speedOf ball.Position.Vx ball.Position.Vy
                    Expect.isGreaterThan speed 0.05 "a loose ball near the touchline must be moving during live play"
                | _ -> ()

    let restartContractTests =
        testList
            "FootballEngine.Core.Stepper.RestartContractTests"
            [ testCase "throw-in restart places the ball inside the field" (fun _ ->
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
                  | other -> failtestf "expected a restart delay, got %A" other)

              testCase "a loose ball never sticks to the touchline during live play" (fun _ ->
                  // Seed 1 exercises throw-ins; the invariant is behavioural: a
                  // loose ball in the touchline band must keep moving (re-entering
                  // play), never sitting still glued to the line.
                  runWindow 90000 1UL) ]
