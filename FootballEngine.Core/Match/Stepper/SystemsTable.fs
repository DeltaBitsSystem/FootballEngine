namespace FootballEngine.Core.Match

/// The ONLY scheduler table in the engine. Systems run in this order every tick.
/// Layer D (agents) propose; the controllers execute. All triggers are event or
/// cadence based — no system polls a timer to "wake up".
module Scheduler =

    let private possessionFlags =
        EventFlags.union (EventFlags.ofKind EventKind.PossessionGained) (EventFlags.ofKind EventKind.BallLoose)

    let private setPieceFlags =
        EventFlags.union
            (EventFlags.union (EventFlags.ofKind EventKind.SetPieceAwarded) (EventFlags.ofKind EventKind.Corner))
            (EventFlags.union
                (EventFlags.ofKind EventKind.GoalKick)
                (EventFlags.union (EventFlags.ofKind EventKind.ThrowIn) (EventFlags.ofKind EventKind.KickOff)))

    let private transitionFlags =
        [ EventKind.PossessionGained
          EventKind.BallLoose
          EventKind.PassLaunched
          EventKind.Interception
          EventKind.ShotLaunched
          EventKind.RedCard
          EventKind.Injury
          EventKind.Substitution
          EventKind.SubstitutionApplied
          EventKind.Goal
          EventKind.HalfTime
          EventKind.FullTime
          EventKind.SetPieceAwarded
          EventKind.TeamInstruction ]
        |> List.fold (fun flags kind -> EventFlags.union flags (EventFlags.ofKind kind)) setPieceFlags

    let private decisionFlags =
        [ EventKind.PossessionGained
          EventKind.BallLoose
          EventKind.Interception
          EventKind.RedCard
          EventKind.Injury
          EventKind.Substitution
          EventKind.SubstitutionApplied
          EventKind.TeamInstruction ]
        |> List.fold (fun flags kind -> EventFlags.union flags (EventFlags.ofKind kind)) setPieceFlags

    let private reactionFlags =
        [ EventKind.PassLaunched; EventKind.ShotLaunched ]
        |> List.fold (fun flags kind -> EventFlags.union flags (EventFlags.ofKind kind)) decisionFlags

    // A goal, a red card or the break schedules a rival manager's review: a minute cadence alone would see them late.
    let private managerFlags =
        [ EventKind.Goal; EventKind.RedCard; EventKind.HalfTime ]
        |> List.fold (fun flags kind -> EventFlags.union flags (EventFlags.ofKind kind)) { Bits = 0UL }

    let systems: SystemSpec list =
        [ { Name = "TeamPerceive"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.WhenLive(Trigger.OnTransitionOrGeometry transitionFlags) ]
            Step = TeamPerceive.step }
          { Name = "TeamTransition"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.OnTransitionOrGeometry transitionFlags ]
            Step = TeamTransition.step }
          { Name = "TeamDecide"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.WhenLive(Trigger.EveryMinute(1<matchMin>, 0<matchMin>)) ]
            Step = TeamDecide.step }
          { Name = "TeamPosition"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.WhenMovementAllowed(Trigger.OnTransitionOrGeometry transitionFlags) ]
            Step = TeamPosition.step }
          { Name = "TeamEmergent"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.WhenLive(Trigger.EveryN 8<tickDelta>) ]
            Step = TeamEmergent.step }
          { Name = "TeamAttack"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.OnTransitionOrGeometry transitionFlags ]
            Step = TeamAttack.step }
          { Name = "PlayerPerception"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.WhenMovementAllowed(Trigger.OnEventOrEveryN(reactionFlags, 8<tickDelta>)) ]
            Step = PlayerPerception.step }
          { Name = "PlayerIntent"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.WhenMovementAllowed(Trigger.OnTransitionOrGeometry transitionFlags) ]
            Step = PlayerIntent.step }
          { Name = "PlayerSteering"
            Side = SystemSide.Body
            Triggers = [ Trigger.WhenMovementAllowed(Trigger.OnEventOrEveryN(reactionFlags, 2<tickDelta>)) ]
            Step = PlayerSteering.step }
          { Name = "RestartPlanning"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.WhenMovementAllowed(Trigger.OnEventOrEveryN(setPieceFlags, 1<tickDelta>)) ]
            Step = RestartPlanning.step }
          { Name = "RestartController"
            Side = SystemSide.Body
            Triggers = [ Trigger.WhenMovementAllowed(Trigger.OnEventOrEveryN(setPieceFlags, 1<tickDelta>)) ]
            Step = RestartController.step }
          { Name = "PlayerDeliberation"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.EveryTick ]
            Step = PlayerDecision.deliberate }
          { Name = "PlayerDecision"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.WhenLive(Trigger.OnEventOrEveryN(decisionFlags, 4<tickDelta>)) ]
            Step = PlayerDecision.step }
          { Name = "PlayerReflex"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.WhenLive(Trigger.OnEventOrEveryN(possessionFlags, 2<tickDelta>)) ]
            Step = PlayerDecision.reflex }
          { Name = "Actions"
            Side = SystemSide.Body
            Triggers = [ Trigger.WhenLive(Trigger.OnEventOrEveryN(possessionFlags, 2<tickDelta>)) ]
            Step = Actions.step }
          { Name = "BallFootContact"
            Side = SystemSide.Body
            Triggers = [ Trigger.EveryTick ]
            Step = BallFootContact.step }
          // Before ContactDynamics, so gloves committed this tick are already in place for the swept contact test.
          { Name = "KeeperSave"
            Side = SystemSide.Body
            Triggers = [ Trigger.WhenLive Trigger.EveryTick ]
            Step = KeeperSave.step }
          { Name = "ContactDynamics"
            Side = SystemSide.Body
            Triggers = [ Trigger.WhenMovementAllowed Trigger.EveryTick ]
            Step = ContactDynamics.step }
          { Name = "BallContact"
            Side = SystemSide.Body
            Triggers = [ Trigger.WhenMovementAllowed Trigger.EveryTick ]
            Step = BallContact.step }
          { Name = "BallDeadFlight"
            Side = SystemSide.Body
            Triggers = [ Trigger.WhenBallDead Trigger.EveryTick ]
            Step = BallPhysics.step }
          { Name = "BallOutcome"
            Side = SystemSide.Body
            Triggers = [ Trigger.WhenLive Trigger.EveryTick ]
            Step = BallOutcome.step }
          { Name = "BallPossession"
            Side = SystemSide.Body
            Triggers = [ Trigger.WhenLive Trigger.EveryTick ]
            Step = BallPossession.step }
          { Name = "RefereeFouls"
            Side = SystemSide.Body
            Triggers = [ Trigger.OnEvent(EventFlags.ofKind EventKind.Foul) ]
            Step = RefereeFouls.step }
          { Name = "RefereeCards"
            Side = SystemSide.Body
            Triggers = [ Trigger.OnEvent(EventFlags.ofKind EventKind.Card) ]
            Step = RefereeCards.step }
          { Name = "ManagerSystem"
            Side = SystemSide.Cognition
            Triggers = [ Trigger.EveryMinute(1<matchMin>, 0<matchMin>); Trigger.OnEvent managerFlags ]
            Step = ManagerSystem.step } ]
