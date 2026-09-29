namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module ChainLifetimeTests =

    let private ctx = TestBuilders.formationCtx ()
    let private homeSlot5Id = ctx.HomeLineup.Profiles.[5].Id
    let private homeSlot6Id = ctx.HomeLineup.Profiles.[6].Id
    let private awaySlot5Id = ctx.AwayLineup.Profiles.[5].Id

    let private planNow (tick: int<subtick>) =
        { FutureReceivers = [| (9, 26.0<meter>, 0.0<meter>) |]
          ExpiresAt = tick + 480<subtick> }

    let private stepBoth (world: World) =
        let buf =
            { Events = ResizeArray()
              Flags = { EventFlags.Bits = 0UL }
              Applied = ResizeArray() }

        let sctx =
            { Ctx = ctx
              Clock = SimulationClock.defaultClock }

        let _ = PlayerDecision.step sctx (Rng.ofSeed 1UL) world buf
        let _ = PlayerIntent.step sctx (Rng.ofSeed 1UL) world buf
        ()

    /// Intent is the system that turns a plan into movement. Decision rewrites
    /// plans every tick, so intent-behaviour tests run it alone: they assert
    /// how the plan is READ, not how the plan is WRITTEN.
    let private stepIntent (world: World) =
        let buf =
            { Events = ResizeArray()
              Flags = { EventFlags.Bits = 0UL }
              Applied = ResizeArray() }

        let sctx =
            { Ctx = ctx
              Clock = SimulationClock.defaultClock }

        let _ = PlayerIntent.step sctx (Rng.ofSeed 1UL) world buf
        ()

    let chainLifetimeTests =
        testList
            "FootballEngine.Core.Systems.PlayerDecision.Chain"
            [ testCase "possession loss clears the plan" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 100<subtick> }
                  world.Home.ActivePlan <- Some(planNow 100<subtick>)

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Away, awaySlot5Id) } }

                  stepBoth world
                  Expect.isNone world.Home.ActivePlan "plan cleared on possession loss")

              testCase "carrier change clears the old plan's intent" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 100<subtick> }

                  world.Home.ActivePlan <-
                      Some
                          { FutureReceivers = [| (6, 25.0<meter>, -8.0<meter>) |]
                            ExpiresAt = 580<subtick> }

                  // The previous carrier must be recorded, or the clear is
                  // skipped: a plan born on this very tick stays alive.
                  world.Home.LastCarrierSlot <- 5

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, homeSlot6Id) } }

                  stepBoth world

                  Expect.notEqual
                      world.Home.Frame.Intent.Kind.[6]
                      (byte IntentCode.ExecuteRun)
                      "slot 6 no longer follows old plan")

              testCase "replaced receive target cancels the old run" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  let passer = ctx.HomeLineup.Profiles.[5].Id
                  let newReceiver = ctx.HomeLineup.Profiles.[7].Id
                  let frame = world.Home.Frame
                  world.State <- { world.State with Tick = 100<subtick> }
                  frame.Intent.Kind.[6] <- IntentCode.ExecuteRun
                  frame.Intent.Phase.[6] <- IntentPhase.Committed
                  frame.Intent.CommittedUntil.[6] <- 200<subtick>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Free
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, newReceiver, 1.0))
                                  ArrivalGate = Some 140<subtick> } }

                  stepIntent world

                  Expect.equal frame.Intent.Phase.[6] IntentPhase.Completed "old receiver run is completed"
                  Expect.equal frame.Intent.ExitTrigger.[6] ExitTrigger.BallNearby "replacement is observable"
                  Expect.equal frame.Intent.Kind.[7] IntentCode.ExecuteRun "new receiver keeps the run")

              testCase "expired plan does not generate intent" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 100<subtick> }

                  world.Home.ActivePlan <-
                      Some
                          { FutureReceivers = [| (6, 25.0<meter>, -8.0<meter>) |]
                            ExpiresAt = 50<subtick> }

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, homeSlot5Id) } }

                  stepIntent world

                  Expect.notEqual
                      world.Home.Frame.Intent.Kind.[6]
                      (byte IntentCode.ExecuteRun)
                      "no ExecuteRun for expired plan")

              testCase "a plan's future receiver runs toward its point" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 100<subtick> }

                  world.Home.ActivePlan <-
                      Some
                          { FutureReceivers = [| (6, 25.0<meter>, -8.0<meter>) |]
                            ExpiresAt = 580<subtick> }

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, homeSlot5Id) } }

                  stepIntent world
                  Expect.equal world.Home.Frame.Intent.Kind.[6] (byte IntentCode.ExecuteRun) "ExecuteRun set"
                  Expect.equal world.Home.Frame.Intent.TargetX.[6] 25.0f "TargetX"
                  Expect.equal world.Home.Frame.Intent.TargetY.[6] -8.0f "TargetY")

              testCase "players outside the plan keep their own intent" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 100<subtick> }

                  world.Home.ActivePlan <-
                      Some
                          { FutureReceivers = [| (6, 25.0<meter>, -8.0<meter>) |]
                            ExpiresAt = 580<subtick> }

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, homeSlot5Id) } }

                  stepIntent world
                  Expect.notEqual world.Home.Frame.Intent.Kind.[7] (byte IntentCode.ExecuteRun) "slot 7 no ExecuteRun") ]
