namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module DecideTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private minuteTick (minute: int) : int<subtick> =
        minute * int SimulationClock.minuteSubticks * 1<subtick>

    let private situation = { LeadSteps = 2 }

    let private interceptState (self: int) (opp: int) : InterceptState =
        { ComputedAt = 0<subtick>
          SelfStep = self
          TeammateStep = 0
          OpponentStep = opp
          BallStep = 0
          SlotSteps = Array.empty }

    let decideTests =
        testList
            "FootballEngine.Core.Systems.Team.DecideTests"
            [ testCase "losing late switches to Attack" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          HomeScore = 0
                          AwayScore = 3
                          Clock =
                              { world.State.Clock with
                                  Tick = minuteTick 80 } }

                  world.State <-
                      { world.State with
                          Tick = minuteTick 80 }

                  TeamDecide.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.equal world.Home.Directive.Kind DirectiveKind.Attack "home protecting deficit attacks late")

              testCase "winning late defends through win probability" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          HomeScore = 3
                          AwayScore = 0
                          Clock =
                              { world.State.Clock with
                                  Tick = minuteTick 82 } }

                  world.State <-
                      { world.State with
                          Tick = minuteTick 82 }

                  TeamDecide.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  let kind = world.Home.Directive.Kind

                  Expect.isTrue
                      (kind = DirectiveKind.Defend || kind = DirectiveKind.ParkTheBus)
                      "home protects the lead")

              testCase "winProbability starts near neutral" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Tick = minuteTick 45
                          Clock =
                              { world.State.Clock with
                                  Tick = minuteTick 45 } }

                  let wp = WinProbability.estimate ctx world ClubSide.Home
                  Expect.isTrue (wp >= 0.3 && wp <= 0.7) "neutral game yields balanced win probability")

              testCase "our team clearly faster to the ball -> Attack" (fun _ ->
                  let phase = Situation.resolve situation (interceptState 3 10)
                  Expect.equal phase (Some PhaseKind.Attacking) "our 7-step lead commits to Attack")

              testCase "opponent clearly faster -> Defend" (fun _ ->
                  let phase = Situation.resolve situation (interceptState 10 3)
                  Expect.equal phase (Some PhaseKind.Defending) "opponent's 7-step lead commits to Defend")

              testCase "contested ball -> neutral (None)" (fun _ ->
                  let phase = Situation.resolve situation (interceptState 5 6)
                  Expect.equal phase None "a 1-step gap stays disputed, caller keeps the current phase")

              testCase "lead threshold boundary" (fun _ ->
                  let atBoundary = Situation.resolve situation (interceptState 4 6)
                  Expect.equal atBoundary None "a lead of exactly LeadSteps is not enough to commit"

                  let overBoundary = Situation.resolve situation (interceptState 4 7)
                  Expect.equal overBoundary (Some PhaseKind.Attacking) "a lead beyond LeadSteps commits to Attack")

              testCase "integration: intercept steps drive the phase through TeamPerceive.step" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <- { world.State with Tick = 250<subtick> }

                  world.State <-
                      { world.State with
                          Possession =
                              { world.State.Possession with
                                  LastGainer = Some ClubSide.Home } }

                  world.Home.Intercept <-
                      { ComputedAt = 0<subtick>
                        SelfStep = 2
                        TeammateStep = 0
                        OpponentStep = 20
                        BallStep = 0
                        SlotSteps = Array.empty }

                  TeamPerceive.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.equal
                      world.Home.Blackboard.OurPhase
                      PhaseKind.Attacking
                      "a clear intercept lead outside the transition window commits the phase to Attack")

              testCase "deterministic: same world yields same phase" (fun _ ->
                  let run () =
                      let w = TestBuilders.minimalWorld ()

                      w.State <- { w.State with Tick = 250<subtick> }

                      w.State <-
                          { w.State with
                              Possession =
                                  { w.State.Possession with
                                      LastGainer = Some ClubSide.Home } }

                      w.Home.Intercept <-
                          { ComputedAt = 0<subtick>
                            SelfStep = 2
                            TeammateStep = 0
                            OpponentStep = 20
                            BallStep = 0
                            SlotSteps = Array.empty }

                      TeamPerceive.step sctx (Rng.ofSeed 42UL) w (buffer ()) |> ignore
                      w.Home.Blackboard.OurPhase

                  Expect.equal (run ()) (run ()) "two runs with identical setup agree") ]
