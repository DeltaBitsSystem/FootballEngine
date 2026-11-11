namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// Fouls come from where a challenge's boot lands, not from dice: a boot on the legs without the ball is a trip,
/// a boot that played the ball first is fair unless the follow-through is dangerous, and a boot clear of the legs
/// is nothing. A slide buys reach by putting the tackler on the ground.
module TackleFoulTests =
    let private ctx = TestBuilders.formationCtx ()
    let private system = { Ctx = ctx; Clock = SimulationClock.defaultClock }

    let private buffer () : EventBuffer =
        { Events = ResizeArray()
          Applied = ResizeArray()
          Flags = { Bits = 0UL } }

    /// Home slot 5 carries at the origin; away slot 5 challenges from +x. Everyone else is far away, and the ball is
    /// placed ahead of the carrier where no boot can reach it.
    let private duelWorld (tacklerX: float32) (closingSpeed: float32) (phase: ContactActionPhase) (kind: ContactActionKind) =
        let world = MatchFactory.create ctx

        for side in [ ClubSide.Home; ClubSide.Away ] do
            let team = MatchWorld.team world side

            for slot in MatchWorld.activeSlots team.Frame do
                team.Frame.PosX[slot] <- if side = ClubSide.Home then -30.0f else 30.0f
                team.Frame.PosY[slot] <- 15.0f + float32 slot * 2.0f

        let home, away = world.Home.Frame, world.Away.Frame
        let carrier = world.Home.RosterIds[5]
        home.PosX[5] <- 0.0f
        home.PosY[5] <- 0.0f
        away.PosX[5] <- tacklerX
        away.PosY[5] <- 0.0f
        away.BodyFacing[5] <- float32 System.Math.PI
        away.TargetFacing[5] <- float32 System.Math.PI
        away.VelX[5] <- -closingSpeed
        away.TargetVelX[5] <- -closingSpeed

        world.State <-
            { world.State with
                Flow = MatchFlow.Live
                Ball =
                    { TestBuilders.emptyBall with
                        Position = { TestBuilders.emptyBall.Position with X = -6.0<meter> }
                        Control = BallControl.Controlled(ClubSide.Home, carrier) } }

        away.ContactActions[5] <-
            Some
                { Id = ActionId 91UL
                  Actor = world.Away.RosterIds[5]
                  Generation = away.ContactGeneration[5]
                  Kind = kind
                  Target = Some carrier
                  Phase = phase
                  StartedAt = 0<subtick>
                  CommitAt = 0<subtick>
                  EndsAt = 20<subtick>
                  Foot = Some ContactFoot.Right
                  // Aimed at the carrier's feet, where the ball was when the tackle was launched.
                  PredictedX = 0.0<meter>
                  PredictedY = 0.0<meter>
                  PredictedZ = 0.0<meter>
                  ErrorX = 0.0f
                  ErrorY = 0.0f
                  ErrorZ = 0.0f }

        world

    let private run world =
        let events = buffer ()
        ContactDynamics.step system (Rng.ofSeed 3UL) world events |> ignore
        events.Events |> Seq.toList

    let private legContact (events: MatchEvent list) =
        events
        |> List.tryPick (fun event ->
            match event.Data with
            | EventData.PhysicalContact record when record.SurfaceB = ContactSurface.ContactLeg -> Some record
            | _ -> None)

    let private fouls (events: MatchEvent list) =
        events
        |> List.choose (fun event ->
            match event.Data with
            | EventData.Foul(fouler, victim, severity, _, _) -> Some(fouler, victim, severity)
            | _ -> None)

    let tests =
        testList
            "TackleFoul"
            [ testCase "a tackle that meets the legs without the ball is a foul even at walking speed" (fun _ ->
                  // 1 m/s closing is below the careless body-charge speed: only the trip rule can whistle this.
                  let world = duelWorld 0.75f 1.0f ContactActionPhase.ContactActionCommitted ContactActionKind.ContactStandingTackle
                  let events = run world

                  match legContact events with
                  | Some record ->
                      Expect.equal record.SurfaceA ContactSurface.ContactFoot "the boot is the striking surface"
                      Expect.equal record.Actor world.Away.RosterIds[5] "the tackler struck"
                      Expect.equal record.Other (Some world.Home.RosterIds[5]) "the carrier's legs were struck"
                      Expect.isFalse record.BallTouched "the ball was nowhere near"
                  | None -> failtest "a boot planted on the carrier's feet did not meet his legs"

                  Expect.equal
                      (fouls events)
                      [ world.Away.RosterIds[5], world.Home.RosterIds[5], FoulSeverity.Tactical ]
                      "a slow trip is a careless foul by the tackler"

                  match world.Away.Frame.ContactActions[5] with
                  | Some action -> Expect.equal action.Phase ContactActionPhase.ContactActionRecovering "the boot is spent"
                  | None -> failtest "the spent challenge must still be recovering")

              testCase "the same leg contact after playing the ball is a fair challenge" (fun _ ->
                  let world = duelWorld 0.75f 1.0f ContactActionPhase.ContactActionCompleted ContactActionKind.ContactStandingTackle
                  let events = run world

                  Expect.isSome (legContact events) "the follow-through still meets the legs"
                  Expect.isEmpty (fouls events) "ball first at walking speed is not an offence")

              testCase "a dangerous follow-through after the ball is still a foul" (fun _ ->
                  let speed = float32 ctx.Config.ContactAction.DangerousBallFirstSpeed + 1.0f
                  let world = duelWorld 0.75f speed ContactActionPhase.ContactActionCompleted ContactActionKind.ContactStandingTackle
                  let events = run world

                  match fouls events with
                  | [ fouler, _, severity ] ->
                      Expect.equal fouler world.Away.RosterIds[5] "the tackler is penalised"
                      Expect.notEqual severity FoulSeverity.Minor "endangering the opponent is not trivial"
                  | other -> failtestf "expected exactly one foul, got %A" other)

              testCase "a boot that stays clear of the legs is neither a contact nor a foul" (fun _ ->
                  // Negative control: same challenge, launched from beyond any reach and not closing.
                  let world = duelWorld 3.0f 0.0f ContactActionPhase.ContactActionCommitted ContactActionKind.ContactStandingTackle
                  let events = run world

                  Expect.isNone (legContact events) "no leg within reach"
                  Expect.isEmpty (fouls events) "no contact, no foul")

              testCase "a sliding tackle reaches further than a standing one and puts the tackler down" (fun _ ->
                  // 1.3 m is beyond a standing lunge but inside a slide's reach.
                  let standing = duelWorld 1.3f 1.0f ContactActionPhase.ContactActionCommitted ContactActionKind.ContactStandingTackle
                  Expect.isNone (legContact (run standing)) "a standing boot cannot reach 1.3 m"

                  let world = duelWorld 1.3f 1.0f ContactActionPhase.ContactActionPreparing ContactActionKind.ContactSlidingTackle
                  let events = run world

                  Expect.equal
                      world.Away.Frame.ContactState[5].Status
                      BodyContactStatus.BodyFalling
                      "committing to a slide takes the tackler off his feet"

                  Expect.isSome (legContact events) "the sliding boot meets the legs at 1.3 m")

              testCase "a controlled touch leaves the ball rolling with its new velocity" (fun _ ->
                  let world = MatchFactory.create ctx

                  for side in [ ClubSide.Home; ClubSide.Away ] do
                      let team = MatchWorld.team world side

                      for slot in MatchWorld.activeSlots team.Frame do
                          team.Frame.PosX[slot] <- if side = ClubSide.Home then -30.0f else 30.0f
                          team.Frame.PosY[slot] <- 15.0f + float32 slot * 2.0f

                  let home = world.Home.Frame
                  home.PosX[5] <- 0.0f
                  home.PosY[5] <- 0.0f

                  // Spin about the travel axis and a vertical axis: neither is rolling, both must be absorbed.
                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Live
                          Ball =
                              { TestBuilders.emptyBall with
                                  Position =
                                      { TestBuilders.emptyBall.Position with
                                          X = 0.9<meter>
                                          Vx = -3.0<meterPerSecond> }
                                  Spin =
                                      { X = -60.0<radianPerSecond>
                                        Y = 0.0<radianPerSecond>
                                        Z = 40.0<radianPerSecond> } } }

                  home.ContactActions[5] <-
                      Some
                          { Id = ActionId 92UL
                            Actor = world.Home.RosterIds[5]
                            Generation = home.ContactGeneration[5]
                            Kind = ContactActionKind.ContactTrap
                            Target = None
                            Phase = ContactActionPhase.ContactActionCommitted
                            StartedAt = 0<subtick>
                            CommitAt = 0<subtick>
                            EndsAt = 20<subtick>
                            Foot = Some ContactFoot.Right
                            PredictedX = 0.45<meter>
                            PredictedY = 0.0<meter>
                            PredictedZ = 0.0<meter>
                            ErrorX = 0.0f
                            ErrorY = 0.0f
                            ErrorZ = 0.0f }

                  let mutable received = false

                  for _ in 1..10 do
                      if not received then
                          run world |> ignore

                          match world.State.Ball.Control with
                          | BallControl.Receiving _ -> received <- true
                          | _ -> ()

                  Expect.isTrue received "the committed trap controls a 3 m/s ball"

                  let ball = world.State.Ball
                  let radius = float ball.Radius
                  Expect.floatClose Accuracy.medium (float ball.Spin.X) (-float ball.Position.Vy / radius) "rolls about x"
                  Expect.floatClose Accuracy.medium (float ball.Spin.Y) (float ball.Position.Vx / radius) "rolls about y"
                  Expect.floatClose Accuracy.medium (float ball.Spin.Z) 0.0 "no spin about the vertical survives") ]
