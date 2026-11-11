namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module KeeperSaveTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    type private Outcome =
        { Dived: bool
          Saved: bool
          Crossed: bool
          Trace: string list }

    /// A home striker shoots, rising at `lift` m/s, from `distance` metres straight out from the away goal, aimed
    /// `aimY` metres beside the away keeper who stands on his line; everyone else is far away. Keeper and contact
    /// systems run as in a match until the ball is saved or crosses the line.
    let private shootWith (config: BalanceConfig) (distance: float) (aimY: float) (speed: float) (lift: float) =
        let ctx = { ctx with Config = config }
        let sctx = { sctx with Ctx = ctx }
        let world = MatchFactory.create ctx
        world.State <- { world.State with Flow = MatchFlow.Live }
        let away = world.Away
        // In a match the position system resolves the keeper slot on its first run; it does not run here.
        let keeper =
            ctx.AwayLineup.Profiles |> Array.findIndex (fun p -> p.Role = SlotRole.Goalkeeper)

        away.GoalkeeperSlot <- Some keeper
        let goalX = float (MatchMath.ownGoalX ClubSide.Away)
        let towardGoal = float (sign goalX)

        for slot in MatchWorld.activeSlots away.Frame do
            away.Frame.PosX[slot] <- 0.0f
            away.Frame.PosY[slot] <- float32 slot

        for slot in MatchWorld.activeSlots world.Home.Frame do
            world.Home.Frame.PosX[slot] <- float32 (-20.0 * towardGoal)
            world.Home.Frame.PosY[slot] <- float32 slot

        away.Frame.PosX[keeper] <- float32 (goalX - 0.3 * towardGoal)
        away.Frame.PosY[keeper] <- 0.0f
        away.Frame.BodyFacing[keeper] <- float32 (if towardGoal > 0.0 then System.Math.PI else 0.0)

        let shooter = ctx.HomeLineup.Profiles[9].Id
        let originX = goalX - distance * towardGoal
        let dx, dy = goalX - originX, aimY
        let length = sqrt (dx * dx + dy * dy)

        world.State <-
            { world.State with
                Ball =
                    { world.State.Ball with
                        Position =
                            { world.State.Ball.Position with
                                X = originX * 1.0<meter>
                                Y = 0.0<meter>
                                Z = 0.0<meter>
                                Vx = 0.0<meterPerSecond>
                                Vy = 0.0<meterPerSecond>
                                Vz = 0.0<meterPerSecond> }
                        Control = BallControl.Free } }

        let impulse =
            BallPhysics.createImpulse
                shooter
                { X = originX * 1.0<meter>; Y = 0.0<meter>; Z = 0.0<meter> }
                { X = dx / length * speed * 1.0<meterPerSecond>
                  Y = dy / length * speed * 1.0<meterPerSecond>
                  Z = lift * 1.0<meterPerSecond> }
                { Top = 0.0<radianPerSecond>; Side = 0.0<radianPerSecond>; Roll = 0.0<radianPerSecond> }
                (Some { X = goalX * 1.0<meter>; Y = aimY * 1.0<meter> })
                StrikeKind.Shot
                (SimTick.ofSubTicks world.State.Tick)
                ExecutionContext.OpenPlay

        BallPhysics.applyImpulse world impulse (TrajectoryIntent.Struck(shooter, speed * 1.0<meterPerSecond>, 0.0, 0.3)) None

        let mutable dived, saved, crossed = false, false, false
        let trace = ResizeArray<string>()

        for _ in 1..60 do
            if not saved && not crossed then
                world.State <- { world.State with Tick = world.State.Tick + 1<subtick> }
                let events = buffer ()
                KeeperSave.step sctx (Rng.ofSeed 1UL) world events |> ignore
                ContactDynamics.step sctx (Rng.ofSeed 1UL) world events |> ignore
                let ball = world.State.Ball

                for event in events.Events do
                    match event.Data with
                    | EventData.KeeperDive _ -> dived <- true
                    | EventData.PhysicalContact record when record.BallTouched && record.SurfaceA = ContactSurface.ContactHand ->
                        saved <- true
                    | _ -> ()

                if (float ball.Position.X - goalX) * towardGoal >= 0.0 then
                    crossed <- true

                trace.Add(
                    sprintf
                        "t%d ball (%.2f, %.2f, %.2f) trajectory %s dive %b"
                        (int world.State.Tick)
                        (float ball.Position.X)
                        (float ball.Position.Y)
                        (float ball.Position.Z)
                        (match ball.Trajectory with
                         | Some(TrajectoryIntent.Struck _) -> "struck"
                         | other -> sprintf "%A" other)
                        away.Dive.IsSome
                )

        { Dived = dived; Saved = saved; Crossed = crossed; Trace = List.ofSeq trace }

    let private shoot = shootWith BalanceConfig.defaultConfig

    let private explain outcome = String.concat "\n" outcome.Trace

    let keeperSaveTests =
        testList
            "FootballEngine.Core.Systems.Players.KeeperSaveTests"
            [ testCase "a keeper dives to and saves a shot he has time to reach" (fun _ ->
                  // 18 m at 22 m/s leaves ~0.8 s: reaction, set-up and a 1.2 m dive fit with time to spare.
                  let outcome = shoot 18.0 1.2 22.0 1.5
                  Expect.isTrue outcome.Dived $"he commits his gloves to the shot\n{explain outcome}"
                  Expect.isTrue outcome.Saved $"and they meet the ball\n{explain outcome}")

              testCase "a keeper gets down to a low shot beside him" (fun _ ->
                  // A ground shot 1.6 m wide from 16 m at 22 m/s arrives in ~0.75 s: a routine save, made by
                  // collapsing sideways. Reach measured only from a standing shoulder put it 2 m away and out of reach.
                  let outcome = shoot 16.0 1.6 22.0 0.0
                  Expect.isTrue outcome.Dived $"he goes down to it\n{explain outcome}"
                  Expect.isTrue outcome.Saved $"and gets his gloves on it\n{explain outcome}")

              testCase "a shot into the corner from close range beats the keeper" (fun _ ->
                  // 8 m at 28 m/s leaves under 0.3 s, about a keeper's reaction time, for a ball 3 m to his side.
                  let outcome = shoot 8.0 3.2 28.0 1.5
                  Expect.isFalse outcome.Saved $"no keeper covers 3 m in that time\n{explain outcome}"
                  Expect.isTrue outcome.Crossed "the ball reaches the line") 
              testCase "a quicker reaction turns a goal into a save" (fun _ ->
                  // 12 m at 24 m/s leaves 0.5 s for a ball 1.9 m wide: the default reaction leaves too little dive,
                  // a keeper who reads the strike 0.2 s sooner covers it.
                  let shot config = shootWith config 12.0 1.9 24.0 0.5
                  let slow = shot BalanceConfig.defaultConfig

                  let quick =
                      shot
                          { BalanceConfig.defaultConfig with
                              Keeper =
                                  { BalanceConfig.defaultConfig.Keeper with
                                      ReactionBase = BalanceConfig.defaultConfig.Keeper.ReactionBase - 0.2<second> } }

                  Expect.isFalse slow.Saved $"the default keeper is beaten\n{explain slow}"
                  Expect.isTrue quick.Saved $"the quicker keeper saves it\n{explain quick}") ]
