namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open OutcomeResolver

module BallSystem =

    let run (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : SystemOutput[] =
        let pcfg = ctx.Config.Physics
        let dt = SimulationClock.dt clock
        let homeFrame = SimStateOps.getFrame state HomeClub
        let awayFrame = SimStateOps.getFrame state AwayClub
        let homeRoster = ctx.HomeRoster
        let awayRoster = ctx.AwayRoster
        let subTick = state.SubTick

        let attDir = MatchSpatial.attackDirFor state.AttackingSide state
        let defDir = MatchSpatial.attackDirFor (ClubSide.flip state.AttackingSide) state

        // 1. Update ball physics
        let trajectoryBefore = state.Ball.Trajectory
        let withStationary = BallPhysics.update pcfg dt state.Ball

        // 2. Check if ball is stopped
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

        // 3. Find contact
        let contact =
            ContactResolver.find
                withStationary
                homeFrame
                awayFrame
                homeRoster
                awayRoster
                pcfg
                withStationary.Trajectory
                subTick

        // 4. Resolve outcome
        let (outcome, updatedBall) =
            OutcomeResolver.resolve contact withStationary subTick attDir defDir homeRoster awayRoster ctx clock

        // 5. Check launch detected
        let trajectoryAfter = updatedBall.Trajectory

        let launchDetected =
            match trajectoryBefore, trajectoryAfter with
            | None, Some _ -> true
            | Some t1, Some t2 -> t1.LaunchSubTick <> t2.LaunchSubTick
            | _ -> false

        // 6. Build outputs
        let outputs = ResizeArray<SystemOutput>()
        outputs.Add(BallUpdate updatedBall)

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
            outputs.Add(PossessionHistoryUpdate delta)

        match outcome with
        | PossessionGained(club, player, events) ->
            events |> List.iter (fun e -> outputs.Add(Emit e))
            outputs.Add(EmitSemantic(SemanticEvent.BallSecured(club, player.Id)))
        | BallLoose events ->
            events |> List.iter (fun e -> outputs.Add(Emit e))
            outputs.Add(EmitSemantic SemanticEvent.BallLoose)
        | BallContested club -> ()
        | GoalScored(club, scorerId) ->
            outputs.Add(EmitSemantic(SemanticEvent.GoalScored(club, scorerId)))
        | SetPieceAwarded flow -> outputs.Add(FlowChange flow)
        | BallInFlight ->
            if launchDetected then
                outputs.Add(PossessionHistoryUpdate { delta with BallInFlight = true })
        | NoChange -> ()

        outputs.ToArray()
