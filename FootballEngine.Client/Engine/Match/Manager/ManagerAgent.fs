namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Player
open FootballEngine.Stats

open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Types.SimulationClock
open FootballEngine.Types.TacticsConfig
open SimStateOps


module ManagerAgent =

    type Situation =
        | Winning
        | Drawing
        | Losing

    let private situation (clubId: ClubId) (ctx: MatchContext) (state: SimState) =
        match goalDiff clubId ctx state with
        | d when d > 0 -> Winning
        | d when d < 0 -> Losing
        | _ -> Drawing

    let private maxSubs = 3

    let private preferredPositions =
        function
        | Losing -> [ ST; AML; AMR; AMC ]
        | Drawing -> [ MC; AMC; ST ]
        | Winning -> [ DC; DM; MC ]

    let private reactiveTactics (sit: Situation) (current: TeamTactics) : TeamTactics =
        match sit, current with
        | Losing, _ -> TeamTactics.Attacking
        | Drawing, TeamTactics.Defensive -> TeamTactics.Balanced
        | Drawing, TeamTactics.Balanced -> TeamTactics.Attacking
        | Drawing, _ -> TeamTactics.Balanced
        | Winning, TeamTactics.Attacking -> TeamTactics.Balanced
        | Winning, TeamTactics.Counter -> TeamTactics.Balanced
        | Winning, _ -> TeamTactics.Defensive

    let private pickOutgoing
        (players: Player[])
        (positions: Spatial[])
        (conditions: int[])
        (sidelined: Map<PlayerId, PlayerOut>)
        (subsUsed: int)
        (threshold: int)
        : int option =
        players
        |> Array.mapi (fun i p -> i, p)
        |> Array.filter (fun (i, p) -> not (Map.containsKey p.Id sidelined))
        |> Array.filter (fun (i, p) -> p.Position <> GK && conditions[i] < threshold)
        |> Array.sortBy (fun (i, _) -> conditions[i])
        |> Array.tryHead
        |> Option.map fst

    let private pickIncoming
        (squadPlayers: Player list)
        (manager: Staff)
        (players: Player[])
        (sidelined: Map<PlayerId, PlayerOut>)
        (preferred: Position list)
        : Player option =
        let startingIds =
            manager.Attributes.Coaching.Lineup
            |> Option.map (fun lu -> lu.Slots |> List.choose _.PlayerId |> Set.ofList)
            |> Option.defaultValue Set.empty

        let onPitchIds = players |> Array.map _.Id |> Set.ofArray

        let bench =
            squadPlayers
            |> List.filter (fun p ->
                not (Set.contains p.Id startingIds)
                && not (Set.contains p.Id onPitchIds)
                && not (Map.containsKey p.Id sidelined)
                && p.Status = Available)

        bench
        |> List.filter (fun p -> List.contains p.Position preferred)
        |> List.sortByDescending _.CurrentSkill
        |> List.tryHead
        |> Option.orElseWith (fun () -> bench |> List.sortByDescending _.CurrentSkill |> List.tryHead)

    let private trySub
        (clubId: ClubId)
        (ctx: MatchContext)
        (state: SimState)
        (squad: Player list)
        (coach: Staff)
        (sit: Situation)
        : ManagerAction =
        let subsUsed, sidelined, frame, roster =
            if clubId = ctx.Home.Id then
                getSubsUsed state HomeClub, getSidelined state HomeClub, state.Home.Frame, getRoster ctx HomeClub
            else
                getSubsUsed state AwayClub, getSidelined state AwayClub, state.Away.Frame, getRoster ctx AwayClub

        if subsUsed >= maxSubs then
            AdjustTactics(clubId, reactiveTactics sit (getTacticsByClubId clubId ctx state))
        else
            let threshold =
                match sit with
                | Losing -> ctx.Config.Manager.ConditionThresholdLosing
                | Drawing -> ctx.Config.Manager.ConditionThresholdDrawing
                | Winning -> ctx.Config.Manager.ConditionThresholdWinning

            let mutable outgoingIdx: int option = None
            let mutable bestCond = 100

            for i = 0 to frame.SlotCount - 1 do
                match frame.Physics.Occupancy[i] with
                | OccupancyKind.Active rosterIdx ->
                    let p = roster.Players[rosterIdx]
                    let cond = int frame.Condition[i]

                    if p.Position <> GK && cond < threshold && cond < bestCond then
                        bestCond <- cond
                        outgoingIdx <- Some i
                | _ -> ()

            match outgoingIdx with
            | Some outIdx ->
                match
                    pickIncoming squad coach (activePlayersFromFrame frame roster) sidelined (preferredPositions sit)
                with
                | Some incoming -> MakeSubstitution(clubId, outIdx, incoming)
                | None -> AdjustTactics(clubId, reactiveTactics sit (getTacticsByClubId clubId ctx state))
            | None -> AdjustTactics(clubId, reactiveTactics sit (getTacticsByClubId clubId ctx state))

    let private handleFatigueAlert
        (clubId: ClubId)
        (ctx: MatchContext)
        (state: SimState)
        (squad: Player list)
        (coach: Staff)
        : ManagerAction =
        trySub clubId ctx state squad coach (situation clubId ctx state)

    let private handleMomentumSwing
        (disadvantagedClub: ClubSide)
        (ctx: MatchContext)
        (state: SimState)
        (squad: Player list)
        (coach: Staff)
        : ManagerAction =
        let clubId =
            if disadvantagedClub = HomeClub then
                ctx.Home.Id
            else
                ctx.Away.Id

        trySub clubId ctx state squad coach (situation clubId ctx state)

    let private handleRedCard (playerId: PlayerId) (ctx: MatchContext) (state: SimState) : ManagerAction list =
        let clubId =
            if playerOnSide ctx state HomeClub playerId then
                ctx.Home.Id
            else
                ctx.Away.Id

        let currentTactics = getTacticsByClubId clubId ctx state

        let compactTactics =
            match currentTactics with
            | TeamTactics.Attacking
            | TeamTactics.Pressing -> TeamTactics.Balanced
            | TeamTactics.Counter -> TeamTactics.Defensive
            | t -> t

        [ AdjustTactics(clubId, compactTactics) ]

    let private handleSubstitutionWindow
        (homeSquad: Player list)
        (awaySquad: Player list)
        (ctx: MatchContext)
        (state: SimState)
        : ManagerAction list =
        let homeCoachRating = float ctx.HomeCoach.CurrentSkill / 100.0
        let awayCoachRating = float ctx.AwayCoach.CurrentSkill / 100.0

        [ if getSubsUsed state HomeClub < maxSubs && bernoulli (0.7 + homeCoachRating * 0.2) then
              let homeFrame = state.Home.Frame
              let homeRoster = getRoster ctx HomeClub

              let homeSit = situation ctx.Home.Id ctx state

              let homeThreshold =
                  match homeSit with
                  | Losing -> ctx.Config.Manager.ConditionThresholdLosing
                  | Drawing -> ctx.Config.Manager.ConditionThresholdDrawing
                  | Winning -> ctx.Config.Manager.ConditionThresholdWinning

              let mutable homeOutIdx: int option = None
              let mutable homeBestCond = 100

              for i = 0 to homeFrame.SlotCount - 1 do
                  match homeFrame.Physics.Occupancy[i] with
                  | OccupancyKind.Active rosterIdx ->
                      let p = homeRoster.Players[rosterIdx]
                      let cond = int homeFrame.Condition[i]

                      if p.Position <> GK && cond < homeThreshold && cond < homeBestCond then
                          homeBestCond <- cond
                          homeOutIdx <- Some i
                  | _ -> ()

              match homeOutIdx with
              | Some outIdx ->
                  match
                      pickIncoming
                          homeSquad
                          ctx.HomeCoach
                          (activePlayersFromFrame homeFrame homeRoster)
                          (getSidelined state HomeClub)
                          (preferredPositions (situation ctx.Home.Id ctx state))
                  with
                  | Some incoming -> yield MakeSubstitution(ctx.Home.Id, outIdx, incoming)
                  | None -> yield ManagerIdle
              | None -> yield ManagerIdle

          if getSubsUsed state AwayClub < maxSubs && bernoulli (0.7 + awayCoachRating * 0.2) then
              let awayFrame = state.Away.Frame
              let awayRoster = getRoster ctx AwayClub

              let awaySit = situation ctx.Away.Id ctx state

              let awayThreshold =
                  match awaySit with
                  | Losing -> ctx.Config.Manager.ConditionThresholdLosing
                  | Drawing -> ctx.Config.Manager.ConditionThresholdDrawing
                  | Winning -> ctx.Config.Manager.ConditionThresholdWinning

              let mutable awayOutIdx: int option = None
              let mutable awayBestCond = 100

              for i = 0 to awayFrame.SlotCount - 1 do
                  match awayFrame.Physics.Occupancy[i] with
                  | OccupancyKind.Active rosterIdx ->
                      let p = awayRoster.Players[rosterIdx]
                      let cond = int awayFrame.Condition[i]

                      if p.Position <> GK && cond < awayThreshold && cond < awayBestCond then
                          awayBestCond <- cond
                          awayOutIdx <- Some i
                  | _ -> ()

              match awayOutIdx with
              | Some outIdx ->
                  match
                      pickIncoming
                          awaySquad
                          ctx.AwayCoach
                          (activePlayersFromFrame awayFrame awayRoster)
                          (getSidelined state AwayClub)
                          (preferredPositions (situation ctx.Away.Id ctx state))
                  with
                  | Some incoming -> yield MakeSubstitution(ctx.Away.Id, outIdx, incoming)
                  | None -> yield ManagerIdle
              | None -> yield ManagerIdle ]

    let decide (subTick: int) (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : ManagerAction list =
        let elapsedSec = int (subTicksToSeconds clock (subTick * 1<subtick>))

        let isSubMinute =
            ctx.Config.Manager.SubWindowMinutes
            |> Array.exists (fun m -> elapsedSec = m * 60)

        let homeSquad = ctx.HomePlayers |> List.ofArray
        let awaySquad = ctx.AwayPlayers |> List.ofArray

        if isSubMinute then
            handleSubstitutionWindow homeSquad awaySquad ctx state
        else
            [ ManagerIdle ]

    let resolve (subTick: int) (action: ManagerAction) (ctx: MatchContext) (state: SimState) : DomainEvent[] =
        match action with
        | ManagerIdle -> [||]

        | MakeSubstitution(clubId, outIdx, incoming) ->
            let isHome = clubId = ctx.Home.Id
            let side = if isHome then HomeClub else AwayClub
            let team = getTeam state side

            if team.SubsUsed >= maxSubs then
                [||]
            else
                let frame = team.Frame
                let roster = getRoster ctx side

                match frame.Physics.Occupancy[outIdx] with
                | OccupancyKind.Active _ ->
                    let playerOut =
                        match tryGetPlayerFromFrame frame roster outIdx with
                        | Some p -> p
                        | None -> incoming

                    let inheritedPos =
                        { X = float frame.Physics.PosX[outIdx] * 1.0<meter>
                          Y = float frame.Physics.PosY[outIdx] * 1.0<meter>
                          Z = 0.0<meter>
                          Vx = 0.0<meter / second>
                          Vy = 0.0<meter / second>
                          Vz = 0.0<meter / second> }

                    FrameMutate.setPos frame.Physics outIdx inheritedPos.X inheritedPos.Y
                    FrameMutate.setCondition frame outIdx (float32 incoming.Condition)
                    FrameMutate.setIntent frame.Intent outIdx IntentKind.Idle 0.0f 0.0f 0

                    let team = getTeam state side
                    team.SubsUsed <- team.SubsUsed + 1
                    team.Sidelined <- Map.add playerOut.Id SidelinedBySub team.Sidelined

                    state.StoppageTime.Add(subTick, StoppageReason.SubstitutionDelay) |> ignore

                    [| DomainEvent.Emit { SubTick = subTick; PlayerId = playerOut.Id; ClubId = clubId; Type = SubstitutionOut; Context = EventContext.empty }
                       DomainEvent.Emit { SubTick = subTick; PlayerId = incoming.Id; ClubId = clubId; Type = SubstitutionIn; Context = EventContext.empty } |]
                | _ -> [||]

        | AdjustTactics(clubId, newTactics) ->
            setTacticsByClubId clubId ctx state newTactics
            let side = if clubId = ctx.Home.Id then HomeClub else AwayClub
            let newKind = TeamDirectiveOps.kindFromTactics newTactics

            let tactics =
                SimStateOps.tacticsConfig newTactics (SimStateOps.getInstructions state side)

            let emergent = SimStateOps.getEmergentState state side

            let directiveParams = defaultParams tactics emergent

            let current = SimStateOps.getDirective state side

            let updated =
                match current with
                | TeamDirectiveState.Active d
                | TeamDirectiveState.Suspended d ->
                    TeamDirectiveState.Active
                        { d with
                            Kind = newKind
                            Params = directiveParams
                            ActiveSince = state.SubTick }
                | TeamDirectiveState.Transitioning(from, _, _) ->
                    TeamDirectiveState.Active
                        { from with
                            Kind = newKind
                            Params = directiveParams
                            ActiveSince = state.SubTick }

            SimStateOps.setDirective state side updated
            [||]

    let agent ctx (state: SimState) (clock: SimulationClock) : DomainEvent[] =
        let actions = decide (int state.SubTick) ctx state clock

        actions
        |> List.collect (fun action -> resolve (int state.SubTick) action ctx state |> Array.toList)
        |> List.rev
        |> Array.ofList
