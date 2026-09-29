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

    let systems: SystemSpec list =
        [ { Name = "BallPhysics"
            Triggers = [ Trigger.WhenMovementAllowed Trigger.EveryTick ]
            Step = BallPhysics.step }
          { Name = "BallContact"
            Triggers = [ Trigger.WhenMovementAllowed Trigger.EveryTick ]
            Step = BallContact.step }
          { Name = "BallOutcome"
            Triggers = [ Trigger.WhenLive Trigger.EveryTick ]
            Step = BallOutcome.step }
          { Name = "BallPossession"
            Triggers = [ Trigger.WhenLive Trigger.EveryTick ]
            Step = BallPossession.step }
          { Name = "TeamPerceive"
            Triggers = [ Trigger.WhenLive(Trigger.EveryN 4<tickDelta>) ]
            Step = TeamPerceive.step }
          { Name = "TeamDecide"
            Triggers = [ Trigger.WhenLive(Trigger.EveryMinute(1<matchMin>, 0<matchMin>)) ]
            Step = TeamDecide.step }
          { Name = "TeamPosition"
            Triggers = [ Trigger.WhenMovementAllowed(Trigger.EveryN 4<tickDelta>) ]
            Step = TeamPosition.step }
          { Name = "TeamEmergent"
            Triggers = [ Trigger.WhenLive(Trigger.EveryN 8<tickDelta>) ]
            Step = TeamEmergent.step }
          { Name = "PlayerPerception"
            Triggers = [ Trigger.WhenLive(Trigger.EveryN 8<tickDelta>) ]
            Step = PlayerPerception.step }
          { Name = "PlayerIntent"
            Triggers = [ Trigger.WhenMovementAllowed(Trigger.EveryN 4<tickDelta>) ]
            Step = PlayerIntent.step }
          { Name = "PlayerSteering"
            Triggers = [ Trigger.WhenMovementAllowed(Trigger.EveryN 2<tickDelta>) ]
            Step = PlayerSteering.step }
          { Name = "RestartController"
            Triggers = [ Trigger.WhenMovementAllowed(Trigger.OnEventOrEveryN(setPieceFlags, 1<tickDelta>)) ]
            Step = RestartController.step }
          { Name = "PlayerDecision"
            Triggers = [ Trigger.WhenLive(Trigger.EveryN 4<tickDelta>) ]
            Step = PlayerDecision.step }
          { Name = "Actions"
            Triggers = [ Trigger.WhenLive(Trigger.OnEventOrEveryN(possessionFlags, 2<tickDelta>)) ]
            Step = Actions.step }
          { Name = "RefereeFouls"
            Triggers = [ Trigger.OnEvent(EventFlags.ofKind EventKind.Foul) ]
            Step = RefereeFouls.step }
          { Name = "RefereeCards"
            Triggers = [ Trigger.OnEvent(EventFlags.ofKind EventKind.Card) ]
            Step = RefereeCards.step }
          { Name = "ManagerSystem"
            Triggers = [ Trigger.EveryMinute(1<matchMin>, 0<matchMin>) ]
            Step = ManagerSystem.step } ]
