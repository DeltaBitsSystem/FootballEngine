namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Types
open SimStateOps
open PhysicsContract


module RefereeApplicator =

    let private goalKickOff (scoringClub: ClubSide) : BallPhysicsState =
        { Position = kickOffSpatial
          Spin = Spin.zero
          Control = Free
          LastTouchBy = None
          PendingOffsideSnapshot = None
          StationarySinceSubTick = None
          GKHoldSinceSubTick = None
          PlayerHoldSinceSubTick = None
          Trajectory = None }

    let private awardGoal
        (scoringClub: ClubSide)
        (scorerId: PlayerId option)
        (subTick: int)
        (ctx: MatchContext)
        (state: SimState)
        : MatchEvent list * SystemOutput list =
        let clubId = if scoringClub = HomeClub then ctx.Home.Id else ctx.Away.Id
        let momentumDelta = if scoringClub = HomeClub then 3.0 else -3.0
        let kickOffBall = goalKickOff (ClubSide.flip scoringClub)
        let scoreOutputs =
            [ ScoreGoal(scoringClub, scorerId, false)
              MomentumUpdate momentumDelta
              BallUpdate kickOffBall
              LastAttackingClubSet (ClubSide.flip scoringClub)
              StoppageTimeAdd(subTick, StoppageReason.GoalDelay) ]

        let events =
            match scorerId with
            | Some pid ->
                [ { SubTick = subTick
                    PlayerId = pid
                    ClubId = clubId
                    Type = Goal
                    Context = EventContext.empty } ]
            | None -> []

        events, scoreOutputs

    let apply
        (subTick: int)
        (action: RefereeAction)
        (ctx: MatchContext)
        (state: SimState)
        : MatchEvent list * SystemOutput list =
        match action with
        | RefereeIdle -> [], []

        | ConfirmGoal(scoringClub, scorerId, isOwnGoal) ->
            let events, goalOutputs = awardGoal scoringClub scorerId subTick ctx state
            let semantic = EmitSemantic(GoalScored(scoringClub, scorerId))
            let outputs = semantic :: goalOutputs

            if isOwnGoal then
                events |> List.map (fun e -> { e with Type = OwnGoal }), outputs
            else
                events, outputs

        | AnnulGoal ->
            let resetX =
                match state.PendingOffsideSnapshot with
                | Some snap -> snap.BallXAtPass
                | None -> HalfwayLineX
            let resetBall =
                { state.Ball with
                    Position = defaultSpatial resetX (PitchWidth / 2.0)
                    Spin = Spin.zero
                    LastTouchBy = None
                    Control = Free
                    PendingOffsideSnapshot = None }
            [], [ BallUpdate resetBall ]

        | AwardThrowIn team ->
            let throwX =
                match team with
                | HomeClub -> PenaltyAreaDepth
                | AwayClub -> PitchLength - PenaltyAreaDepth
            let throwBall =
                { state.Ball with
                    Position =
                        { state.Ball.Position with
                            X = throwX
                            Y = PitchWidth / 2.0
                            Z = 0.0<meter>
                            Vx = 0.0<meter / second>
                            Vy = 0.0<meter / second>
                            Vz = 0.0<meter / second> }
                    Spin = Spin.zero
                    LastTouchBy = None
                    Control = Free }
            [], [ EmitSemantic(SetPieceAwarded(SetPieceKind.ThrowIn, team))
                  BallUpdate throwBall ]

        | AwardCorner team ->
            let cornerX =
                match team with
                | HomeClub -> PitchLength - 0.5<meter>
                | AwayClub -> 0.5<meter>
            let cornerBall =
                { state.Ball with
                    Position =
                        { state.Ball.Position with
                            X = cornerX
                            Y = PitchWidth / 2.0
                            Z = 0.0<meter>
                            Vx = 0.0<meter / second>
                            Vy = 0.0<meter / second>
                            Vz = 0.0<meter / second> }
                    Spin = Spin.zero
                    LastTouchBy = None
                    Control = Free }
            let clubId = if team = HomeClub then ctx.Home.Id else ctx.Away.Id
            [ { SubTick = subTick
                PlayerId = 0
                ClubId = clubId
                Type = MatchEventType.Corner
                Context = EventContext.empty } ],
            [ EmitSemantic(SetPieceAwarded(SetPieceKind.Corner, team))
              BallUpdate cornerBall ]

        | AwardGoalKick team ->
            let gkX =
                match team with
                | HomeClub -> GoalAreaDepth
                | AwayClub -> PitchLength - GoalAreaDepth
            let gkBall =
                { state.Ball with
                    Position = defaultSpatial gkX (PitchWidth / 2.0)
                    Spin = Spin.zero
                    LastTouchBy = None
                    Control = Free }
            [], [ EmitSemantic(SetPieceAwarded(SetPieceKind.GoalKick, team))
                  BallUpdate gkBall ]

        | AwardIndirectFreeKick team ->
            let fkBall =
                { state.Ball with
                    Position =
                        { state.Ball.Position with
                            Vx = 0.0<meter / second>
                            Vy = 0.0<meter / second>
                            Vz = 0.0<meter / second> }
                    Spin = Spin.zero
                    LastTouchBy = None
                    Control = Free }
            let clubId = if team = HomeClub then ctx.Home.Id else ctx.Away.Id
            [ { SubTick = subTick
                PlayerId = 0
                ClubId = clubId
                Type = MatchEventType.IndirectFreeKickAwarded team
                Context = EventContext.empty } ],
            [ EmitSemantic(SetPieceAwarded(SetPieceKind.FreeKick, team))
              BallUpdate fkBall ]

        | DropBall _ ->
            let dropBall =
                { state.Ball with
                    Position =
                        { state.Ball.Position with
                            Z = 0.0<meter>
                            Vx = 0.0<meter / second>
                            Vy = 0.0<meter / second>
                            Vz = 0.0<meter / second> }
                    Spin = Spin.zero
                    Control = Free }
            [], [ BallUpdate dropBall ]

        | IssueYellow(player, clubId) ->
            let isHome = clubId = ctx.Home.Id
            let side = if isHome then HomeClub else AwayClub
            let currentYellows = getYellows state side |> Map.tryFind player.Id |> Option.defaultValue 0
            let newCount = currentYellows + 1
            let yellowOutputs =
                [ YellowsWrite(side, player.Id, newCount)
                  StoppageTimeAdd(subTick, StoppageReason.CardDelay) ]

            if currentYellows >= 1 then
                let events =
                    [ createEvent subTick player.Id clubId YellowCard
                      createEvent subTick player.Id clubId RedCard ]
                events,
                yellowOutputs
                @ [ SidelinedWrite(side, player.Id, SidelinedByRedCard)
                    EmitSemantic(RedCardIssued player.Id) ]
            else
                [ createEvent subTick player.Id clubId YellowCard ], yellowOutputs

        | IssueRed(player, clubId) ->
            let isHome = clubId = ctx.Home.Id
            let side = if isHome then HomeClub else AwayClub
            [ createEvent subTick player.Id clubId RedCard ],
            [ EmitSemantic(RedCardIssued player.Id)
              SidelinedWrite(side, player.Id, SidelinedByRedCard)
              StoppageTimeAdd(subTick, StoppageReason.CardDelay) ]

        | IssueInjury(player, clubId) ->
            let isHome = clubId = ctx.Home.Id
            let side = if isHome then HomeClub else AwayClub
            [ createEvent subTick player.Id clubId (MatchEventType.Injury "match") ],
            [ SidelinedWrite(side, player.Id, SidelinedByInjury)
              StoppageTimeAdd(subTick, StoppageReason.InjuryDelay 1) ]
