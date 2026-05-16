namespace FootballEngine.Simulation

open FootballEngine
open FootballEngine.Domain
open FootballEngine.MatchSpatial
open FootballEngine.Player.Actions
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open SimStateOps

open SimulationClock

module MatchSimulator =

    type SimulationError =
        | MissingLineup of clubName: string
        | IncompleteLineup of clubName: string * playerCount: int
        | SameClub of clubName: string

    let private runLoop
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        (takeSnapshot: SimState -> int -> unit)
        : SimState * MatchEvent list =
        let events = ResizeArray<MatchEvent>()

        state.EffectiveFullTimeSubTick <-
            fullTime clock + state.StoppageTime.AccumulatedSeconds * clock.SubTicksPerSecond * 1<subtick>

        let maxSeguridad = fullTime clock + clock.SubTicksPerSecond * 60 * 10 * 1<subtick> // +10 min extra

        while state.Flow <> MatchEnded && state.SubTick < maxSeguridad do
            let snapshotSubTick = state.SubTick
            let result = MatchStepper.updateOne ctx clock [||] state
            for e in result.Events do events.Add(e)
            takeSnapshot state (int snapshotSubTick)

        state, events |> Seq.toList

    let private isKeyEvent (e: MatchEvent) : bool =
        match e.Type with
        | MatchEventType.Goal
        | MatchEventType.Assist
        | MatchEventType.YellowCard
        | MatchEventType.RedCard
        | MatchEventType.Injury _
        | MatchEventType.Save -> true
        | _ -> false

    let runLoopFast (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : SimState * MatchEvent list =
        let finalState, allEvents = runLoop ctx state clock (fun _ _ -> ())
        let keyEvents = allEvents |> List.filter isKeyEvent
        finalState, keyEvents

    let runLoopFull (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : MatchReplay =
        let snapshots = System.Collections.Generic.List<SimSnapshot>()
        let snapshotInterval = clock.SubTicksPerSecond / 8 * 1<subtick>
        let mutable lastSnapshotAt = 0<subtick>

        let takeSnapshot st subTick =
            if subTick >= lastSnapshotAt + snapshotInterval then
                snapshots.Add(SnapshotBuilder.take st)
                lastSnapshotAt <- subTick + snapshotInterval

        let takeSnapshotInt st subTick =
            takeSnapshot st (subTick * 1<subtick>)

        let finalState, events = runLoop ctx state clock takeSnapshotInt

        { Context = ctx
          Final = finalState
          Events = events
          Snapshots = snapshots.ToArray() }

    open FsToolkit.ErrorHandling

    let private resolveCoach (club: Club) (staff: Map<StaffId, Staff>) : Result<Staff, SimulationError> =
        staff
        |> Map.values
        |> Seq.tryFind (fun s -> s.Role = HeadCoach && s.Contract |> Option.map _.ClubId = Some club.Id)
        |> Option.map Ok
        |> Option.defaultValue (Error(MissingLineup club.Name))

    let private toCoords slotX slotY (clubSide: ClubSide) : float<meter> * float<meter> =
        match clubSide with
        | HomeClub -> (1.0 - slotY) * PitchLength, slotX * PitchWidth
        | AwayClub -> slotY * PitchLength, slotX * PitchWidth

    let private kickOffPosition (x: float) (y: float) (clubSide: ClubSide) : float<meter> * float<meter> =
        let tacticalX, tacticalY = toCoords x y clubSide

        let clampedX =
            match clubSide with
            | HomeClub -> clamp tacticalX 2.0<meter> (HalfwayLineX - 1.0<meter>)
            | AwayClub -> clamp tacticalX (HalfwayLineX + 1.0<meter>) (PitchLength - 2.0<meter>)

        clampedX, tacticalY

    let private extractLineup
        (club: Club)
        (headCoach: Staff)
        (players: Map<PlayerId, Player>)
        (clubSide: ClubSide)
        : Result<(Player * float<meter> * float<meter>)[] * TeamTactics * TacticalInstructions option, SimulationError> =
        match headCoach.Attributes.Coaching.Lineup with
        | None -> Error(MissingLineup club.Name)
        | Some lu ->
            let slots =
                lu.Slots
                |> List.choose (fun s ->
                    s.PlayerId
                    |> Option.bind (fun pid ->
                        players
                        |> Map.tryFind pid
                        |> Option.map (fun p ->
                            let x, y = toCoords s.X s.Y clubSide
                            p, x, y)))
                |> Array.ofList

            Ok(slots, lu.Tactics, lu.Instructions)

    let private validateLineups (home: Club) homeData (away: Club) awayData : Result<unit, SimulationError> =
        if home.Id = away.Id then
            Error(SameClub home.Name)
        elif homeData |> Array.length <> 11 then
            Error(IncompleteLineup(home.Name, homeData |> Array.length))
        elif awayData |> Array.length <> 11 then
            Error(IncompleteLineup(away.Name, awayData |> Array.length))
        else
            Ok()

    let private positionArrayOf (players: Player[]) (posMap: Map<PlayerId, float<meter> * float<meter>>) : Spatial[] =
        players
        |> Array.map (fun p ->
            match Map.tryFind p.Id posMap with
            | Some(x, y) -> defaultSpatial x y
            | None -> failwithf $"Position missing for player %s{p.Name} (%A{p.Id})")

    let private initSimState
        home
        homeCoach
        hp
        (hPosMap: Map<PlayerId, float<meter> * float<meter>>)
        homeTactics
        homeInstructions
        away
        awayCoach
        ap
        (aPosMap: Map<PlayerId, float<meter> * float<meter>>)
        awayTactics
        awayInstructions
        isKnockout
        profileMap
        =

        let hPos =
            hp
            |> Array.mapi (fun i (p: Player) ->
                let tx, ty =
                    hPosMap
                    |> Map.tryFind p.Id
                    |> Option.defaultValue (HalfwayLineX, PitchWidth / 2.0)

                let prof = profileMap |> Map.tryFind p.Id |> Option.defaultValue (Player.profile p)

                let kx =
                    match p.Position with
                    | ST
                    | AMC ->
                        let allowedDepth = (float prof.AttackingDepth) * 5.0<meter>
                        let threshold = HalfwayLineX - 5.0<meter> + allowedDepth

                        if tx > threshold then
                            HalfwayLineX - 1.0<meter> + allowedDepth * 0.5
                        else
                            clamp tx 2.0<meter> (HalfwayLineX - 2.0<meter>)
                    | _ -> clamp tx 2.0<meter> (HalfwayLineX - 2.0<meter>)

                defaultSpatial kx ty)

        let aPos =
            ap
            |> Array.map (fun (p: Player) ->
                let tx, ty =
                    aPosMap
                    |> Map.tryFind p.Id
                    |> Option.defaultValue (HalfwayLineX, PitchWidth / 2.0)

                let kx = clamp tx (HalfwayLineX + 4.0<meter>) (PitchLength - 2.0<meter>)

                defaultSpatial kx ty)

        let defaultInstr = TacticalInstructions.defaultInstructions
        let hCount = hp |> Array.length
        let aCount = ap |> Array.length

        let homeRoster = PlayerRoster.build hp
        let awayRoster = PlayerRoster.build ap
        let homeFrame = TeamFrame.init homeRoster hPos
        let awayFrame = TeamFrame.init awayRoster aPos

        let ctx =
            { Home = home
              Away = away
              HomeCoach = homeCoach
              AwayCoach = awayCoach
              HomePlayers = hp
              AwayPlayers = ap
              HomeBasePositions = hPos
              AwayBasePositions = aPos
              HomeChemistry = ChemistryGraph.init hCount
              AwayChemistry = ChemistryGraph.init aCount
              IsKnockoutMatch = isKnockout
              Config = BalanceCalibrator.getConfig ()
              HomeRoster = homeRoster
              AwayRoster = awayRoster }

        let state = SimState()
        state.SubTick <- 0<subtick>
        state.HomeScore <- 0
        state.AwayScore <- 0
        state.Config <- BalanceCalibrator.getConfig ()

        state.Ball <- { defaultBall with Control = Free }

        state.Momentum <- 0.0
        state.HomeBasePositions <- ctx.HomeBasePositions
        state.AwayBasePositions <- ctx.AwayBasePositions
        state.HomeAttackDir <- LeftToRight
        state.BallXSmooth <- 52.5<meter>

        let homeTeam = TeamSimState.empty ()
        homeTeam.Frame <- homeFrame
        homeTeam.ShapeTargetX <- Array.zeroCreate homeFrame.SlotCount
        homeTeam.ShapeTargetY <- Array.zeroCreate homeFrame.SlotCount
        homeTeam.Tactics <- homeTactics
        homeTeam.Instructions <- homeInstructions |> Option.orElse (Some defaultInstr)
        state.Home <- homeTeam

        let awayTeam = TeamSimState.empty ()
        awayTeam.Frame <- awayFrame
        awayTeam.ShapeTargetX <- Array.zeroCreate awayFrame.SlotCount
        awayTeam.ShapeTargetY <- Array.zeroCreate awayFrame.SlotCount
        awayTeam.Tactics <- awayTactics
        awayTeam.Instructions <- awayInstructions |> Option.orElse (Some defaultInstr)
        state.Away <- awayTeam

        ctx, state

    let private simulatePenaltyShootout
        (ctx: MatchContext)
        (state: SimState)
        (players: Map<PlayerId, Player>)
        (clock: SimulationClock)
        =
        result {
            let homePlayers =
                ctx.Home.PlayerIds
                |> List.choose players.TryFind
                |> List.sortByDescending _.CurrentSkill

            let awayPlayers =
                ctx.Away.PlayerIds
                |> List.choose players.TryFind
                |> List.sortByDescending _.CurrentSkill

            let rec simulateKicks homeKicks awayKicks kickNum =
                let homeKicker = homePlayers |> List.item ((kickNum - 1) % homePlayers.Length)
                let awayKicker = awayPlayers |> List.item ((kickNum - 1) % awayPlayers.Length)

                let homeSubTick = int (fullTime clock) + kickNum * clock.SubTicksPerSecond * 2
                let awaySubTick = homeSubTick + clock.SubTicksPerSecond

                let homeScored =
                    SetPlayAction.resolvePenalty homeSubTick ctx state homeKicker HomeClub clock

                if homeScored then
                    RefereeApplicator.apply homeSubTick (ConfirmGoal(HomeClub, Some homeKicker.Id, false)) ctx state
                    |> ignore

                let awayScored =
                    SetPlayAction.resolvePenalty awaySubTick ctx state awayKicker AwayClub clock

                if awayScored then
                    RefereeApplicator.apply awaySubTick (ConfirmGoal(AwayClub, Some awayKicker.Id, false)) ctx state
                    |> ignore

                let newHomeKicks = (homeKicker.Id, homeScored, kickNum) :: homeKicks
                let newAwayKicks = (awayKicker.Id, awayScored, kickNum) :: awayKicks
                let homeGoals = newHomeKicks |> List.sumBy (fun (_, sc, _) -> if sc then 1 else 0)
                let awayGoals = newAwayKicks |> List.sumBy (fun (_, sc, _) -> if sc then 1 else 0)

                if kickNum > 5 && homeGoals <> awayGoals then
                    { HomeKicks = List.rev newHomeKicks
                      AwayKicks = List.rev newAwayKicks
                      CurrentKick = kickNum
                      IsComplete = true }
                elif kickNum > 5 then
                    simulateKicks newHomeKicks newAwayKicks (kickNum + 1)
                elif kickNum <= 5 then
                    let remaining = 5 - kickNum

                    if homeGoals > awayGoals + remaining || awayGoals > homeGoals + remaining then
                        { HomeKicks = List.rev newHomeKicks
                          AwayKicks = List.rev newAwayKicks
                          CurrentKick = kickNum
                          IsComplete = true }
                    elif kickNum = 5 then
                        { HomeKicks = List.rev newHomeKicks
                          AwayKicks = List.rev newAwayKicks
                          CurrentKick = kickNum
                          IsComplete = true }
                    else
                        simulateKicks newHomeKicks newAwayKicks (kickNum + 1)
                else
                    simulateKicks newHomeKicks newAwayKicks (kickNum + 1)

            let shootout = simulateKicks [] [] 1

            let baseSubTick = int (fullTime clock)

            let events =
                [ for pid, scored, k in shootout.HomeKicks ->
                      { SubTick = baseSubTick + k
                        PlayerId = pid
                        ClubId = ctx.Home.Id
                        Type = PenaltyAwarded scored
                        Context = EventContext.empty }
                  for pid, scored, k in shootout.AwayKicks ->
                      { SubTick = baseSubTick + k
                        PlayerId = pid
                        ClubId = ctx.Away.Id
                        Type = PenaltyAwarded scored
                        Context = EventContext.empty } ]

            return
                { Context = ctx
                  Final = state
                  Events = events
                  Snapshots = [||] }
        }

    let setup
        home
        away
        players
        staff
        isKnockout
        profileMap
        : Result<MatchContext * SimState * Player list * Player list, SimulationError> =
        result {
            let! homeCoach = resolveCoach home staff
            let! awayCoach = resolveCoach away staff
            let! homeData, homeTactics, homeInstructions = extractLineup home homeCoach players HomeClub
            let! awayData, awayTactics, awayInstructions = extractLineup away awayCoach players AwayClub
            do! validateLineups home homeData away awayData

            let hp = homeData |> Array.map (fun (p, _, _) -> p)
            let ap = awayData |> Array.map (fun (p, _, _) -> p)

            let hPosMap = homeData |> Array.map (fun (p, x, y) -> p.Id, (x, y)) |> Map.ofArray
            let aPosMap = awayData |> Array.map (fun (p, x, y) -> p.Id, (x, y)) |> Map.ofArray

            let ctx, state =
                initSimState
                    home
                    homeCoach
                    hp
                    hPosMap
                    homeTactics
                    homeInstructions
                    away
                    awayCoach
                    ap
                    aPosMap
                    awayTactics
                    awayInstructions
                    isKnockout
                    profileMap

            let homeSquad = home.PlayerIds |> List.choose players.TryFind
            let awaySquad = away.PlayerIds |> List.choose players.TryFind

            return ctx, state, homeSquad, awaySquad
        }

    let trySimulateMatch
        home
        away
        players
        staff
        profileMap
        : Result<int * int * MatchEvent list * SimState, SimulationError> =
        result {
            let validationErrors = ConfigValidation.validateAll (BalanceCalibrator.getConfig ())

            if not validationErrors.IsEmpty then
                let msg = validationErrors |> String.concat "\n"
                return failwithf $"BalanceConfig validation failed:\n%s{msg}"

            let! ctx, state, _, _ = setup home away players staff false profileMap
            let final, events = runLoopFast ctx state defaultClock
            return final.HomeScore, final.AwayScore, events, final
        }

    let trySimulateMatchFull home away players staff profileMap : Result<MatchReplay, SimulationError> =
        result {
            let validationErrors = ConfigValidation.validateAll (BalanceCalibrator.getConfig ())

            if not validationErrors.IsEmpty then
                let msg = validationErrors |> String.concat "\n"
                return failwithf $"BalanceConfig validation failed:\n%s{msg}"

            let! ctx, state, _, _ = setup home away players staff false profileMap
            return runLoopFull ctx state defaultClock
        }

    let trySimulateMatchKnockout home away players staff profileMap : Result<MatchReplay * bool, SimulationError> =
        result {
            let validationErrors = ConfigValidation.validateAll (BalanceCalibrator.getConfig ())

            if not validationErrors.IsEmpty then
                let msg = validationErrors |> String.concat "\n"
                return failwithf $"BalanceConfig validation failed:\n%s{msg}"

            let! ctx, state, _, _ = setup home away players staff true profileMap
            let replay = runLoopFull ctx state defaultClock

            if replay.Final.HomeScore = replay.Final.AwayScore then
                let! shootout = simulatePenaltyShootout ctx replay.Final players defaultClock
                return shootout, true
            else
                return replay, false
        }

    let buildInitState home away players staff profileMap =
        match setup home away players staff false profileMap with
        | Ok(ctx, state, homeSquad, awaySquad) -> Some(ctx, state, homeSquad, awaySquad)
        | Error _ -> None
