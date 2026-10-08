namespace FootballEngine.Core.Tests.Stepper

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module StepperTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let stepperTests =
        testList
            "FootballEngine.Core.Stepper.StepperTests"
            [ testCase "Possession event controls the ball and counts a touch" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id
                  let buf = buffer ()

                  buf.Events.Add
                      { Tick = 0<subtick>
                        Data = EventData.Possession(ClubSide.Home, pid) }

                  let _, _, applied = Stepper.step [] sctx (Rng.ofSeed 3UL) world buf

                  Expect.equal applied.Count 1 "applied"
                  Expect.equal world.Home.Stats.Touches 1 "touch counted"

                  match world.State.Ball.Control with
                  | BallControl.Controlled(ClubSide.Home, pid') -> Expect.equal pid' pid "controlled"
                  | other -> failtestf "expected Controlled, got %A" other)

              testCase "Goal event scores and enters the pause" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let scorer = ctx.HomeLineup.Profiles.[9].Id
                  let buf = buffer ()

                  buf.Events.Add
                      { Tick = 0<subtick>
                        Data = EventData.Goal(ClubSide.Home, Some scorer, None, 0.55) }

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world buf

                  Expect.equal world.State.HomeScore 1 "home scored"
                  Expect.equal world.State.AwayScore 0 "away did not"

                  match world.State.Flow with
                  | MatchFlow.GoalPause(_, ClubSide.Home, Some s) -> Expect.equal s scorer "scorer"
                  | other -> failtestf "expected GoalPause, got %A" other

                  Expect.equal world.State.LastAttackingClub (Some ClubSide.Home) "attacking side recorded")

              testCase "Stoppage time is decided at minute 44" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Tick = 105600<subtick>
                          Clock =
                              { world.State.Clock with
                                  Tick = 105600<subtick> } }

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world (buffer ())

                  let added = world.State.Stoppage.FirstHalfAdded
                  Expect.isGreaterThan (int added) 0 "stoppage decided"
                  Expect.isFalse world.State.Stoppage.Decided "second half not decided yet")

              testCase "First half ends at 45 minutes plus added time" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Tick = 105600<subtick> }

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world (buffer ())
                  let added = world.State.Stoppage.FirstHalfAdded

                  world.State <-
                      { world.State with
                          Tick = 108000<subtick> + added
                          Clock =
                              { world.State.Clock with
                                  Tick = 108000<subtick> + added } }

                  let buf = buffer ()
                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world buf

                  Expect.exists buf.Events (fun ev -> ev.Data = EventData.HalfTime) "half time emitted")

              testCase "Second half stoppage subtracts first half events" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  world.Home.Stats.Fouls <- 7
                  world.Away.Stats.Fouls <- 4

                  world.State <-
                      { world.State with
                          HomeScore = 2
                          AwayScore = 1
                          Tick = 105600<subtick>
                          Clock =
                              { world.State.Clock with
                                  Tick = 105600<subtick> } }

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world (buffer ())

                  let g1 = world.State.Stoppage.Half1Fingerprint / 10000
                  let f1 = (world.State.Stoppage.Half1Fingerprint % 10000) / 100
                  Expect.equal g1 3 "fingerprint captures goals"
                  Expect.equal f1 11 "fingerprint captures fouls"

                  world.Away.Stats.Fouls <- 5

                  world.State <-
                      { world.State with
                          Tick = 213600<subtick>
                          Clock =
                              { world.State.Clock with
                                  Tick = 213600<subtick> } }

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world (buffer ())

                  Expect.isTrue world.State.Stoppage.Decided "second half decided"

                  let expectedAdded =
                      int (
                          round (
                              float ctx.Config.Match.StoppageBasePerHalf
                              + float 1 * float ctx.Config.Match.StoppagePerFoul
                          )
                          * float sctx.Clock.SubTicksPerSecond
                      )
                      * 1<subtick>

                  Expect.equal world.State.Stoppage.SecondHalfAdded expectedAdded "half two foul counted")

              testCase "Full time is emitted at 90 minutes plus added time" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Tick = 213600<subtick>
                          Clock =
                              { world.State.Clock with
                                  Tick = 213600<subtick>
                                  Phase = ClockPhase.SecondHalf } }

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world (buffer ())
                  let added = world.State.Stoppage.SecondHalfAdded

                  world.State <-
                      { world.State with
                          Tick = 216000<subtick> + added
                          Clock =
                              { world.State.Clock with
                                  Tick = 216000<subtick> + added } }

                  let buf = buffer ()
                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world buf

                  Expect.exists buf.Events (fun ev -> ev.Data = EventData.FullTime) "full time emitted")

              testCase "VAR overturns an offside goal on review expiry" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          HomeScore = 1
                          LastAttackingClub = Some ClubSide.Home }

                  RefereeVAR.markOffsideSnapshot world ClubSide.Home 9 52.0<meter> 40.0<meter> 0<subtick>

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.VARReview(1<tickDelta>, VARIncident.Offside) }

                  let buf = buffer ()
                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world buf

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.VARDecision VARResult.GoalOverturned -> true
                          | _ -> false)
                      "overturn decided"

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world buf
                  Expect.equal world.State.HomeScore 0 "goal cancelled"
                  Expect.isFalse (RefereeVAR.isOffside ctx world ClubSide.Home) "markers cleared")

              testCase "VAR keeps a legal goal" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          HomeScore = 1
                          LastAttackingClub = Some ClubSide.Home }

                  RefereeVAR.markOffsideSnapshot world ClubSide.Home 9 30.0<meter> 40.0<meter> 0<subtick>

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.VARReview(1<tickDelta>, VARIncident.Offside) }

                  let buf = buffer ()
                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world buf

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.VARDecision VARResult.GoalStands -> true
                          | _ -> false)
                      "goal stands")

              testCase "Restart expiry remains dead until a real impulse" (fun _ ->
                  let world, restart =
                      TestBuilders.minimalWorld (),
                      TestBuilders.restartState
                          SetPieceKind.KickOff
                          ClubSide.Home
                          (Vec2.create 0.0<meter> 0.0<meter>)
                          RestartPhase.Positioning

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Restart restart }

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world (buffer ())

                  match world.State.Flow with
                  | MatchFlow.Restart { Phase = RestartPhase.Positioning } -> ()
                  | other -> failtestf "expected dead preparation without an impulse, got %A" other)

              testCase "Momentum decays toward zero" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  world.State <- { world.State with Momentum = 0.5 }

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world (buffer ())

                  Expect.isLessThan world.State.Momentum 0.5 "decayed"
                  Expect.isGreaterThan world.State.Momentum 0.49 "not too fast")

              testCase "Tick always advances" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.MatchEnded }

                  let _, _, _ = Stepper.step [] sctx (Rng.ofSeed 3UL) world (buffer ())

                  Expect.equal world.State.Tick 1<subtick> "tick advanced") ]
