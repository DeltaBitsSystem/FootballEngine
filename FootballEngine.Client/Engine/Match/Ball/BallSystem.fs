namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open OutcomeResolver

module BallSystem =

    let run (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : DomainEvent[] =
        let pcfg = ctx.Config.Physics
        let dt = SimulationClock.dt clock
        let homeFrame = SimStateOps.getFrame state HomeClub
        let awayFrame = SimStateOps.getFrame state AwayClub
        let homeRoster = ctx.HomeRoster
        let awayRoster = ctx.AwayRoster
        let subTick = state.SubTick

        let attDir = MatchSpatial.attackDirFor state.AttackingSide state
        let defDir = MatchSpatial.attackDirFor (ClubSide.flip state.AttackingSide) state

        let trajectoryBefore = state.Ball.Trajectory
        let withStationary = BallPhysics.update pcfg dt state.Ball

        let wasStationary = state.Ball.StationarySinceSubTick.IsSome

        let isNowStopped =
            let vSq =
                withStationary.Position.Vx * withStationary.Position.Vx
                + withStationary.Position.Vy * withStationary.Position.Vy
                + withStationary.Position.Vz * withStationary.Position.Vz

            vSq < 1.0<meter / second> * 1.0<meter / second>

        let withStationary =
            if isNowStopped && not wasStationary then
                { withStationary with
                    StationarySinceSubTick = Some subTick }
            elif not isNowStopped then
                { withStationary with
                    StationarySinceSubTick = None }
            else
                withStationary

        let contact =
            ContactResolver.find
                withStationary
                homeFrame
                awayFrame
                homeRoster
                awayRoster
                pcfg
                withStationary.Trajectory
                (int subTick)

        let (outcome, updatedBall) =
            OutcomeResolver.resolve contact withStationary (int subTick) attDir defDir homeRoster awayRoster ctx clock

        let trajectoryAfter = updatedBall.Trajectory

        let launchDetected =
            match trajectoryBefore, trajectoryAfter with
            | None, Some _ -> true
            | Some t1, Some t2 -> int t1.LaunchSubTick <> int t2.LaunchSubTick
            | _ -> false

        let outputs = ResizeArray<DomainEvent>(8)
        outputs.Add(DomainEvent.BallUpdate updatedBall)

        let delta =
            { PossessionChanged =
                match outcome with
                | PossessionGained _
                | BallContested _ -> true
                | _ -> false
              BallInFlight =
                match outcome with
                | BallInFlight -> true
                | _ -> false
              SetPieceAwarded =
                match outcome with
                | SetPieceAwarded _ -> true
                | _ -> false
              ReceivedByPlayer =
                match outcome with
                | PossessionGained(_, p, _) -> Some p.Id
                | _ -> None }

        if delta.PossessionChanged || delta.BallInFlight || delta.SetPieceAwarded then
            outputs.Add(DomainEvent.PossessionHistoryUpdate delta)

        match outcome with
        | PossessionGained(club, player, events) ->
            events |> List.iter (fun e -> outputs.Add(DomainEvent.Emit e))
            outputs.Add(DomainEvent.EmitSemantic(SemanticEvent.BallSecured(club, player.Id)))
        | BallLoose events ->
            events |> List.iter (fun e -> outputs.Add(DomainEvent.Emit e))
            outputs.Add(DomainEvent.EmitSemantic SemanticEvent.BallLoose)
        | BallContested club -> ()
        | GoalScored(club, scorerId) -> outputs.Add(DomainEvent.EmitSemantic(SemanticEvent.GoalScored(club, scorerId)))
        | SetPieceAwarded flow -> outputs.Add(DomainEvent.FlowChange flow)
        | BallInFlight ->
            if launchDetected then
                outputs.Add(DomainEvent.PossessionHistoryUpdate { delta with BallInFlight = true })
        | NoChange -> ()

        outputs.ToArray()
