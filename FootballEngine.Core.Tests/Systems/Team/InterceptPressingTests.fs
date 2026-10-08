namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// F2 — pressing by RoboCup intercept steps instead of euclidean distance: the
/// best-of-team slot chases the loose ball, with a role-dependent allowance
/// (Gliders: defenders 13, midfielders 7, full backs 23) versus the rival's
/// best interceptor.
module InterceptPressingTests =

    let private sctx (ctx: MatchContext) =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private pressCfg: PressConfig =
        { BaseAllowance = 13
          MidfieldAllowance = 7
          FullBackAllowance = 23
          MaxPressStep = 30 }

    let private interceptWith (selfStep: int) (oppStep: int) : InterceptState =
        { ComputedAt = 0<subtick>
          SelfStep = selfStep
          TeammateStep = selfStep + 5
          OpponentStep = oppStep
          BallStep = min selfStep oppStep
          SlotSteps = Array.empty }

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

                  Expect.equal
                      world.Home.Frame.Intent.TargetX.[pressers.[0]]
                      0.0f
                      "the press targets the carrier position"

                  Expect.equal
                      world.Home.Frame.Intent.TargetY.[pressers.[0]]
                      0.0f
                      "the press targets the carrier position"

                  let before = abs world.Home.Frame.PosX.[pressers.[0]]
                  PlayerSteering.step (sctx ctx) (Rng.ofSeed 11UL) world (buffer ()) |> ignore

                  Expect.isLessThan
                      (abs world.Home.Frame.PosX.[pressers.[0]])
                      before
                      "the first defender physically closes down the carrier")

              testCase "high pressing allowance chases even if rival arrives earlier" (fun _ ->
                  let intercept = interceptWith 18 10

                  Expect.isTrue
                      (Pressing.shouldPress pressCfg intercept SlotRole.FullBack 18)
                      "full back presses despite a losing step race")

              testCase "midfielder needs smaller margin" (fun _ ->
                  let intercept = interceptWith 18 10

                  Expect.isFalse
                      (Pressing.shouldPress pressCfg intercept SlotRole.CenterMid 18)
                      "midfielder needs a 7-step margin, 18 vs 10 is not enough")

              testCase "no chase when beaten by a lot" (fun _ ->
                  let intercept = interceptWith 29 10

                  Expect.isFalse
                      (Pressing.shouldPress pressCfg intercept SlotRole.CenterMid 29)
                      "a step gap beyond the allowance stops the chase")

              testCase "allowance maps roles" (fun _ ->
                  Expect.equal (Pressing.allowance pressCfg SlotRole.FullBack) 23 "full back allowance"
                  Expect.equal (Pressing.allowance pressCfg SlotRole.CenterMid) 7 "central midfielder allowance"
                  Expect.equal (Pressing.allowance pressCfg SlotRole.DefensiveMid) 7 "defensive midfielder allowance"
                  Expect.equal (Pressing.allowance pressCfg SlotRole.AttackingMid) 7 "attacking midfielder allowance"
                  Expect.equal (Pressing.allowance pressCfg SlotRole.Winger) 7 "winger allowance"
                  Expect.equal (Pressing.allowance pressCfg SlotRole.Defender) 13 "defender allowance"
                  Expect.equal (Pressing.allowance pressCfg SlotRole.Goalkeeper) 13 "goalkeeper allowance"
                  Expect.equal (Pressing.allowance pressCfg SlotRole.Striker) 13 "striker allowance")

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
