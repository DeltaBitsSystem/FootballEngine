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
        let duty slot x y = { Player = ctx.HomeLineup.Profiles[slot].Id; Slot = slot; Target = Vec2.create (x * 1.0<meter>) (y * 1.0<meter>) }
        { Id = 1UL; Pattern = ThirdMan; Context = 1; StartedAt = tick; Deadline = tick + 480<subtick>
          Stage = Preparing; Carrier = homeSlot5Id; Owner = homeSlot5Id; Link = duty 6 25.0 -8.0; Runner = duty 9 26.0 8.0; Security = duty 4 -9.0 8.0
          Origin = Vec2.create 0.0<meter> 0.0<meter>; BaselineSpace = 0.5; Connections = 0; LastFact = None; Threat = 0.0
          OwnedPoint = Vec2.create 0.0<meter> 0.0<meter>; OwnedSpace = 0.5; OwnedExposure = 0.0; FactTick = tick
          Tactics = ctx.HomeClub.BaseTactics; BlockedSince = None }

    let private stepBoth (world: World) =
        let buf =
            { Events = ResizeArray()
              Flags = { EventFlags.Bits = 0UL }
              Applied = ResizeArray() }

        let sctx =
            { Ctx = ctx
              Clock = SimulationClock.defaultClock }

        let _ = TeamAttack.step sctx (Rng.ofSeed 1UL) world buf
        let _ = PlayerDecision.step sctx (Rng.ofSeed 1UL) world buf
        let _ = PlayerIntent.step sctx (Rng.ofSeed 1UL) world buf
        ()

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
                  world.Home.Attack.Episode <- Some(planNow 100<subtick>)

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Away, awaySlot5Id) } }

                  stepBoth world
                  Expect.isNone world.Home.Attack.Episode "plan cleared on possession loss"
                  Expect.equal (world.Home.Attack.Evidence |> Map.toSeq |> Seq.sumBy (fun (_, value) -> value.Samples)) 1.0 "loss is learned once")

              testCase "completed connection retains the collective episode across carrier change" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 100<subtick> }

                  world.Home.Attack.Episode <- Some(planNow 100<subtick>)

                  // The previous carrier must be recorded, or the clear is
                  // skipped: a plan born on this very tick stays alive.
                  world.Home.LastCarrierSlot <- 5
                  EventApply.apply ctx world { Tick = 100<subtick>; Data = EventData.Pass(homeSlot5Id, Some homeSlot6Id, 12.0<meter>, 1.0, true, 0.0<meter>, 0.0<meter>, 12.0<meter>, 8.0<meter>) }

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, homeSlot6Id) } }

                  stepBoth world

                  Expect.equal (world.Home.Attack.Episode |> Option.map _.Id) (Some 1UL) "expected teammate connection retains identity"
                  Expect.equal (world.Home.Attack.Episode |> Option.map _.Stage) (Some Connected) "real pass advances one milestone"
                  Expect.equal world.Home.Attack.Evidence Map.empty "an intermediate connection is not terminal reward"

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

              testCase "incoming pass receiver keeps the trajectory chase over role intent" (fun _ ->
                   let world = TestBuilders.formationWorld ()
                   let passer = ctx.HomeLineup.Profiles.[5].Id
                   let receiver = ctx.HomeLineup.Profiles.[6].Id
                   world.State <-
                       { world.State with
                           Tick = 100<subtick>
                           Ball =
                               { world.State.Ball with
                                   Control = BallControl.Free
                                   Trajectory = Some(TrajectoryIntent.Aimed(passer, receiver, 1.0))
                                   ArrivalGate = Some 140<subtick>
                                   Position =
                                       { world.State.Ball.Position with
                                           X = 3.0<meter>
                                           Y = 2.0<meter>
                                           Vx = 4.0<meterPerSecond>
                                           Vy = -1.5<meterPerSecond> } } }

                   stepIntent world

                   Expect.equal world.Home.Frame.Intent.Kind.[6] (byte IntentCode.ExecuteRun) "the intended receiver follows the live flight projection"
                   Expect.equal world.Home.Frame.Intent.TargetX.[6] 7.0f "the receiver targets one second of horizontal flight"
                   Expect.equal world.Home.Frame.Intent.TargetY.[6] 0.5f "the receiver targets one second of lateral flight")

              testCase "expired plan does not generate intent" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 100<subtick> }

                  world.Home.Attack.Episode <- Some { planNow 0<subtick> with Deadline = 50<subtick> }

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, homeSlot5Id) } }

                  stepBoth world

                  Expect.notEqual
                      world.Home.Frame.Intent.Kind.[6]
                      (byte IntentCode.ExecuteRun)
                      "no ExecuteRun for expired plan")

              testCase "a plan's future receiver runs toward its point" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 100<subtick> }

                  world.Home.Attack.Episode <- Some(planNow 100<subtick>)

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, homeSlot5Id) } }

                  stepIntent world
                  Expect.equal world.Home.Frame.Intent.Kind.[6] (byte IntentCode.Support) "coordinated support set"
                  Expect.equal world.Home.Frame.Intent.TargetX.[6] 25.0f "TargetX"
                  Expect.equal world.Home.Frame.Intent.TargetY.[6] -8.0f "TargetY")

              testCase "players outside the plan keep their own intent" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.State <- { world.State with Tick = 100<subtick> }

                  world.Home.Attack.Episode <- Some(planNow 100<subtick>)

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, homeSlot5Id) } }

                  stepIntent world
                  Expect.notEqual world.Home.Frame.Intent.Kind.[7] (byte IntentCode.ExecuteRun) "slot 7 no ExecuteRun") ]
