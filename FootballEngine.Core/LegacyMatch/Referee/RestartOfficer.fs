namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Player.Actions
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open SimStateOps
open MatchSpatial

module RestartOfficer =

    let private defGoalX (dir: AttackDir) =
        match dir with
        | LeftToRight -> PitchLength
        | RightToLeft -> 0.0<meter>

    let ballSpot (kind: SetPieceKind) (team: ClubSide) (ctx: MatchContext) (state: SimState) : Spatial =
        let dir = attackDirFor team state
        let ball = state.Ball.Position

        match kind with
        | SetPieceKind.KickOff -> kickOffSpatial
        | SetPieceKind.Corner ->
            let x = defGoalX dir
            let y = if ball.Y < PitchWidth / 2.0 then 0.0<meter> else PitchWidth

            defaultSpatial x y
        | SetPieceKind.ThrowIn ->
            let outY = ball.Y < 0.1<meter> || ball.Y > PitchWidth - 0.1<meter>

            if outY then
                let y = if ball.Y < 0.1<meter> then 0.0<meter> else PitchWidth

                defaultSpatial (PhysicsContract.clamp ball.X 0.0<meter> PitchLength) y
            else
                let x = if ball.X < 0.1<meter> then 0.0<meter> else PitchLength

                defaultSpatial x (PhysicsContract.clamp ball.Y 0.0<meter> PitchWidth)
        | SetPieceKind.GoalKick ->
            let x =
                match dir with
                | LeftToRight -> GoalAreaDepth
                | RightToLeft -> PitchLength - GoalAreaDepth

            defaultSpatial x (PitchWidth / 2.0)
        | SetPieceKind.FreeKick -> defaultSpatial ball.X ball.Y
        | SetPieceKind.Penalty ->
            let x =
                match dir with
                | LeftToRight -> PitchLength - PenaltySpotDistance
                | RightToLeft -> PenaltySpotDistance

            defaultSpatial x (PitchWidth / 2.0)

    let placeBall (kind: SetPieceKind) (team: ClubSide) (ctx: MatchContext) (state: SimState) : BallPhysicsState =
        let spot = ballSpot kind team ctx state

        { state.Ball with
            Position =
                { spot with
                    Vx = 0.0<meter / second>
                    Vy = 0.0<meter / second>
                    Vz = 0.0<meter / second> }
            Spin = Spin.zero
            Control = Free
            LastTouchBy = None
            Trajectory = None
            PendingOffsideSnapshot = None
            StationarySinceSubTick = None
            GKHoldSinceSubTick = None
            PlayerHoldSinceSubTick = None
            GKJudged = false }

    let arrangeBoth (ctx: MatchContext) (state: SimState) (restart: RestartPlan) : unit =
        let attack = restart.Team
        let defense = ClubSide.flip attack

        (getTeam state attack).SetPiecePositions <- Some(SetPiecePositioning.computePositions ctx state attack)

        (getTeam state defense).SetPiecePositions <- Some(SetPiecePositioning.computeDefensePositions ctx state defense)

    let clearPositions (state: SimState) : unit =
        (getTeam state HomeClub).SetPiecePositions <- None
        (getTeam state AwayClub).SetPiecePositions <- None
