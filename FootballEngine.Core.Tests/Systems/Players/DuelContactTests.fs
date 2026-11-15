namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module DuelContactTests =
    let private ctx = TestBuilders.formationCtx ()

    let private system =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () : EventBuffer =
        { Events = ResizeArray()
          Applied = ResizeArray()
          Flags = { Bits = 0UL } }

    let private carrierWorld side =
        let world = MatchFactory.create ctx

        for teamSide in [ ClubSide.Home; ClubSide.Away ] do
            let team = MatchWorld.team world teamSide

            for slot in MatchWorld.activeSlots team.Frame do
                team.Frame.PosX[slot] <- if teamSide = ClubSide.Home then -30.0f else 30.0f
                team.Frame.PosY[slot] <- 15.0f + float32 slot * 2.0f

        let team = MatchWorld.team world side
        let carrier = team.RosterIds[5]
        team.Frame.PosX[5] <- 0.0f
        team.Frame.PosY[5] <- 0.0f

        world.State <-
            { world.State with
                Flow = MatchFlow.Live
                Ball =
                    { TestBuilders.emptyBall with
                        Control = BallControl.Controlled(side, carrier) } }

        world

    let private rollingTowards (distance: float32) =
        let world = MatchFactory.create ctx

        for teamSide in [ ClubSide.Home; ClubSide.Away ] do
            let team = MatchWorld.team world teamSide

            for slot in MatchWorld.activeSlots team.Frame do
                team.Frame.PosX[slot] <- if teamSide = ClubSide.Home then -30.0f else 30.0f
                team.Frame.PosY[slot] <- 15.0f + float32 slot * 2.0f

        let frame = world.Home.Frame
        frame.PosX[5] <- distance
        frame.PosY[5] <- 0.0f
        frame.SteerIntent.Kind[5] <- IntentCode.Recover
        frame.SteerIntent.Phase[5] <- IntentPhase.Executing

        world.State <-
            { world.State with
                Flow = MatchFlow.Live
                Ball =
                    { TestBuilders.emptyBall with
                        Control = BallControl.Free
                        Position =
                            { TestBuilders.emptyBall.Position with
                                Vx = 10.0<meterPerSecond> } } }

        world

    let tests =
        testList
            "DuelContact"
            [ testCase "a trap is armed only once the ball comes closest within the commit window" (fun _ ->
                  // At 10 m/s a ball 2 m away is closest after the 0.15 s preparation-and-commit horizon; arming
                  // then left the boot recovering as the ball rolled by, and the pass ran out for a corner.
                  let far = rollingTowards 2.0f
                  Actions.step system (Rng.ofSeed 1UL) far (buffer ()) |> ignore
                  Expect.isNone far.Home.Frame.ContactActions[5] "a ball still too far away is not trapped yet"

                  let near = rollingTowards 1.0f
                  Actions.step system (Rng.ofSeed 1UL) near (buffer ()) |> ignore

                  match near.Home.Frame.ContactActions[5] with
                  | Some action ->
                      Expect.equal action.Kind ContactActionKind.ContactTrap "the receiver sets to trap it"
                  | None -> failtest "a ball arriving inside the window is trapped")

              testCase "take-on proposes a timed shield without awarding possession" (fun _ ->
                  for side in [ ClubSide.Home; ClubSide.Away ] do
                      let world = carrierWorld side
                      let team = MatchWorld.team world side
                      let ballBefore = world.State.Ball
                      let rng = Rng.ofSeed 1UL
                      let events = buffer ()

                      let next = Duel.adjudicate system rng world events side 5

                      Expect.equal next rng "choosing a take-on is not a random duel result"
                      Expect.equal world.State.Ball ballBefore "the proposal cannot change ball control"
                      Expect.isEmpty events.Events "an action proposal is not a sporting contact"

                      match team.Frame.ContactActions[5] with
                      | Some action ->
                          Expect.equal action.Kind ContactActionKind.ContactShield "take-on protects the carrier"
                          Expect.equal action.Target None "shield has no opponent winner"
                          Expect.equal action.Phase ContactActionPhase.ContactActionPreparing "protection has a windup"

                          Expect.equal
                              action.Generation
                              team.Frame.ContactGeneration[5]
                              "action belongs to this participation"
                      | None -> failtest "reachable carrier did not receive a shield action")

              testCase "unreachable or inactive carrier cannot prepare a shield" (fun _ ->
                  for kind in [ 0; 1 ] do
                      let world = carrierWorld ClubSide.Home
                      let frame = world.Home.Frame

                      if kind = 0 then
                          world.State <-
                              { world.State with
                                  Ball =
                                      { world.State.Ball with
                                          Position =
                                              { world.State.Ball.Position with
                                                  X = 3.0<meter> } } }
                      else
                          frame.Occupancy[5] <-
                              OccupancyKind.Sidelined
                                  { Id = world.Home.RosterIds[5]
                                    Reason = PlayerOutReason.Injury }

                          frame.ActiveCache.Revision <- frame.ActiveCache.Revision + 1

                      let events = buffer ()
                      let rng = Rng.ofSeed 4UL

                      Expect.equal
                          (Duel.adjudicate system rng world events ClubSide.Home 5)
                          rng
                          "rejection consumes no sporting randomness"

                      Expect.isNone frame.ContactActions[5] "rejected proposal leaves no action"
                      Expect.isEmpty events.Events "rejection emits no sporting result"

                      Expect.isTrue
                          (world.Observation.Snapshot().Entries
                           |> Array.exists (fun entry ->
                               match entry.Data with
                               | ActionRejected(_, DuelCarrierUnavailable) -> true
                               | _ -> false))
                          "rejection remains observable")

              testCase "a challenge is reported only after physical player contact" (fun _ ->
                  let world = carrierWorld ClubSide.Home
                  let homeId = world.Home.RosterIds[5]
                  let awayId = world.Away.RosterIds[5]
                  let home = world.Home.Frame
                  let away = world.Away.Frame

                  for slot in MatchWorld.activeSlots home do
                      if slot <> 5 then
                          home.PosX[slot] <- -30.0f
                          home.PosY[slot] <- 20.0f + float32 slot * 2.0f

                  for slot in MatchWorld.activeSlots away do
                      if slot <> 5 then
                          away.PosX[slot] <- 30.0f
                          away.PosY[slot] <- 20.0f + float32 slot * 2.0f

                  home.PosX[5] <- -0.22f
                  home.PosY[5] <- 0.0f
                  home.VelX[5] <- 8.0f
                  home.TargetVelX[5] <- 8.0f
                  away.PosX[5] <- 0.22f
                  away.PosY[5] <- 0.0f
                  away.VelX[5] <- -8.0f
                  away.TargetVelX[5] <- -8.0f

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 4.0<meter>
                                          Y = 0.0<meter> } } }

                  let actionId = ActionId 77UL

                  away.ContactActions[5] <-
                      Some
                          { Id = actionId
                            Actor = awayId
                            Generation = away.ContactGeneration[5]
                            Kind = ContactActionKind.ContactStandingTackle
                            Target = Some homeId
                            Phase = ContactActionPhase.ContactActionCommitted
                            StartedAt = 0<subtick>
                            CommitAt = 0<subtick>
                            EndsAt = 20<subtick>
                            Foot = Some ContactFoot.Right
                            PredictedX = 0.0<meter>
                            PredictedY = 0.0<meter>
                            PredictedZ = 0.0<meter>
                            ErrorX = 0.0f
                            ErrorY = 0.0f
                            ErrorZ = 0.0f }

                  let events = buffer ()
                  let rng = Rng.ofSeed 17UL
                  let next = ContactDynamics.step system rng world events

                  Expect.equal next rng "the physical solver does not draw a winner"

                  let contact =
                      events.Events
                      |> Seq.tryPick (fun event ->
                          match event.Data with
                          | EventData.PhysicalContact record when record.Other = Some homeId -> Some record
                          | _ -> None)
                      |> Option.defaultWith (fun () ->
                          failtest "the body collision produced no physical contact record")

                  Expect.equal contact.Actor awayId "the committed challenge actor is retained"
                  Expect.equal contact.Action (Some actionId) "contact links to the committed action"

                  Expect.equal
                      contact.ActionKind
                      (Some ContactActionKind.ContactStandingTackle)
                      "the action kind is evidence, not a result"

                  Expect.equal
                      world.State.Ball.Control
                      (BallControl.Controlled(ClubSide.Home, homeId))
                      "body contact alone cannot transfer the ball"

                  Expect.isFalse
                      (events.Events
                       |> Seq.exists (fun event ->
                           match event.Data with
                           | EventData.Tackle _ -> true
                           | _ -> false))
                      "contact is not automatically a successful tackle") ]
