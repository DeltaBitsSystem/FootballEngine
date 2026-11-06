namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// The ordinary loose-ball race and the physical arrival estimate must remain deterministic.
module InterceptPressingTests =

    let private sctx (ctx: MatchContext) =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    /// Home attacks +X. Ball loose at the origin; Home striker (slot 9) is by
    /// far the closest Home player; every Away outfielder sits beyond the
    /// intercept horizon so OpponentStep is MaxSteps. Both teams defend.
    let private pressWorld () : World * MatchContext =
        let ctx = TestBuilders.formationCtx ()
        let world = TestBuilders.formationWorld ()

        world.State <-
            { world.State with
                Ball =
                    { world.State.Ball with
                        Position =
                            { world.State.Ball.Position with
                                X = 0.0<meter>
                                Y = 0.0<meter> } } }

        let home = world.Home.Frame

        let homePlacement =
            [ 0, -45.0, 0.0
              1, -25.0, -20.0
              2, -25.0, -6.0
              3, -25.0, 6.0
              4, -25.0, 20.0
              5, -12.0, 0.0
              6, -10.0, -8.0
              7, -10.0, 8.0
              8, -8.0, -18.0
              9, 3.0, 0.0
              10, -8.0, 18.0 ]

        for slot, x, y in homePlacement do
            home.PosX.[slot] <- float32 x
            home.PosY.[slot] <- float32 y

        let away = world.Away.Frame

        let awayPlacement =
            [ 0, 45.0, 0.0
              1, 35.0, -20.0
              2, 35.0, -6.0
              3, 35.0, 6.0
              4, 35.0, 20.0
              5, 30.0, 0.0
              6, 30.0, -8.0
              7, 30.0, 8.0
              8, 28.0, -18.0
              9, 25.0, 0.0
              10, 28.0, 18.0 ]

        for slot, x, y in awayPlacement do
            away.PosX.[slot] <- float32 x
            away.PosY.[slot] <- float32 y

        world.Home.Blackboard.OurPhase <- PhaseKind.Defending
        world.Away.Blackboard.OurPhase <- PhaseKind.Defending
        world.State <- { world.State with Tick = 4<subtick> }
        world, ctx

    /// The scheduler cadence that makes the intercept cache fresh before the
    /// intent pass reads it: TeamPosition refreshes on EveryN 4 and runs before
    /// PlayerIntent in the same tick.
    let private runIntents (ctx: MatchContext) (world: World) : unit =
        TeamPosition.step (sctx ctx) (Rng.ofSeed 11UL) world (buffer ()) |> ignore
        PlayerIntent.step (sctx ctx) (Rng.ofSeed 11UL) world (buffer ()) |> ignore

    let interceptPressingTests =
        testList
            "FootballEngine.Core.Systems.Team.InterceptPressingTests"
            [ testCase "best-of-team slot pursues the loose ball" (fun _ ->
                  let world, ctx = pressWorld ()
                  runIntents ctx world

                  Expect.equal
                      world.Home.Frame.Intent.Kind.[9]
                      IntentCode.Press
                      "the best-of-team slot presses the loose ball"

                  for slot in MatchWorld.activeSlots world.Home.Frame do
                      if slot <> 9 then
                          Expect.notEqual
                              world.Home.Frame.Intent.Kind.[slot]
                              IntentCode.Press
                              (sprintf "slot %d must not press" slot))

              testCase "first defender presses an opponent ball carrier" (fun _ ->
                  let world, ctx = pressWorld ()
                  let carrier = ctx.AwayLineup.Profiles.[9].Id
                  world.Home.Tactics <- { world.Home.Tactics with PressIntensity = 1.0f; PressTriggerZone = 1.0f }
                  world.Home.Frame.PosX[9] <- -2.0f
                  world.Home.Frame.PosX[5] <- -4.0f

                  world.State <-
                      { world.State with
                          Tick = 400<subtick>
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Away, carrier) } }

                  TeamPerceive.step (sctx ctx) (Rng.ofSeed 11UL) world (buffer ()) |> ignore
                  runIntents ctx world

                  let pressers =
                      MatchWorld.activeSlots world.Home.Frame
                      |> Array.filter (fun slot -> world.Home.Frame.Intent.Kind.[slot] = IntentCode.Press)

                  Expect.equal
                      world.Home.Blackboard.OurPhase
                      PhaseKind.Defending
                      "rival control sets the defensive phase"

                  Expect.equal pressers.Length 1 "one coordinated first defender presses"

                  Expect.equal
                      world.Home.Frame.DefensiveRoles.[pressers.[0]]
                      DefensiveRole.FirstDefender
                      "the assigned first defender owns the press"

                  let target = TransitionGeometry.pressPoint ctx world ClubSide.Home
                  Expect.equal world.Home.Frame.Intent.TargetX.[pressers.[0]] (float32 target.X) "the press uses the shared approach point"

                  Expect.equal
                      world.Home.Frame.Intent.TargetY.[pressers.[0]]
                      (float32 target.Y)
                      "the press uses the shared approach point"

                  let before = MatchMath.dist (MatchWorld.posX world.Home.Frame pressers[0]) (MatchWorld.posY world.Home.Frame pressers[0]) target.X target.Y
                  let movementContext = sctx ctx
                  let movementEvents = buffer ()
                  PlayerSteering.step movementContext (Rng.ofSeed 11UL) world movementEvents |> ignore
                  ContactDynamics.step movementContext (Rng.ofSeed 11UL) world movementEvents |> ignore

                  Expect.isLessThan
                      (MatchMath.dist (MatchWorld.posX world.Home.Frame pressers[0]) (MatchWorld.posY world.Home.Frame pressers[0]) target.X target.Y)
                      before
                      "the first defender physically closes down the carrier")

              testCase "arrival estimate includes acceleration" (fun _ ->
                  let world, ctx = pressWorld ()
                  let time = TransitionGeometry.arrivalTime ctx world.Home.Frame 9 (Vec2.create 0.0<meter> 0.0<meter>)
                  Expect.isGreaterThan time ((3.0<meter> - ctx.Config.Ball.FootReach) / ctx.Config.Movement.SprintSpeed) "cannot instantly sprint")
              testCase "motion toward the contact reduces arrival time" (fun _ ->
                  let world, ctx = pressWorld ()
                  let target = Vec2.create 0.0<meter> 0.0<meter>
                  let before = TransitionGeometry.arrivalTime ctx world.Home.Frame 9 target
                  world.Home.Frame.VelX[9] <- -3.0f
                  Expect.isLessThan (TransitionGeometry.arrivalTime ctx world.Home.Frame 9 target) before "current sporting velocity matters")
              testCase "distant candidate cannot arrive within the loss window" (fun _ ->
                  let world, ctx = pressWorld ()
                  let time = TransitionGeometry.arrivalTime ctx world.Home.Frame 2 (Vec2.create 0.0<meter> 0.0<meter>)
                  Expect.isGreaterThan time ctx.Config.DefensiveTransition.BaseArrivalWindow "finite arrival horizon")
              testCase "keeper responsibilities do not participate in outfield arrival selection" (fun _ ->
                  let world, _ = pressWorld ()
                  Expect.isFalse (TransitionGeometry.outfielder world.Home.Frame 0) "goalkeeper excluded by sporting role")

              testCase "GK never presses" (fun _ ->
                  let world, ctx = pressWorld ()
                  runIntents ctx world
                  Expect.notEqual world.Home.Frame.Intent.Kind.[0] IntentCode.Press "the goalkeeper never presses")

              testCase "deterministic" (fun _ ->
                  let runOnce () =
                      let ctx = TestBuilders.formationCtx ()
                      let world = MatchFactory.create ctx

                      world.State <-
                          { world.State with
                              Flow = MatchFlow.Live
                              Ball = TestBuilders.emptyBall }

                      let buf = buffer ()
                      let mutable rng = Rng.ofSeed 0xBADC0FFEUL

                      for _ in 1..12 do
                          let rng', _, _ = Stepper.step Scheduler.systems (sctx ctx) rng world buf
                          rng <- rng'

                      world.Home.Frame.Intent.Kind |> Array.copy, world.Away.Frame.Intent.Kind |> Array.copy

                  let ha, aa = runOnce ()
                  let hb, ab = runOnce ()

                  Expect.equal ha hb "home intent codes identical across runs"
                  Expect.equal aa ab "away intent codes identical across runs") ]
