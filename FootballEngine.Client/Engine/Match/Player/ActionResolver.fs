namespace FootballEngine

open FootballEngine.Domain

open FootballEngine.Player.Actions
open FootballEngine.Player.Decision
open FootballEngine.Referee
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Types.SchedulingTypes
open SimStateOps


module ActionResolver =

    let private bothSides = [| HomeClub; AwayClub |]

    let private findController
        (ctx: MatchContext)
        (state: SimState)
        : Player option * PlayerRoster option * ClubSide option =
        match state.Ball.Control with
        | Controlled(_, pid)
        | Receiving(_, pid, _) ->
            let mutable result: Player option * PlayerRoster option * ClubSide option =
                None, None, None

            let bx = state.Ball.Position.X
            let by = state.Ball.Position.Y

            let maxControlDistSq =
                ctx.Config.Physics.ContactRadius * ctx.Config.Physics.ContactRadius

            for clubSide in bothSides do
                let frame = getFrame state clubSide
                let roster = getRoster ctx clubSide

                match findIdxByPid pid frame roster with
                | ValueSome idx ->
                    let px = float frame.Physics.PosX[idx] * 1.0<meter>
                    let py = float frame.Physics.PosY[idx] * 1.0<meter>
                    let dx = px - bx
                    let dy = py - by
                    let distSq = dx * dx + dy * dy

                    if distSq <= maxControlDistSq then
                        result <- Some roster.Players[idx], Some roster, Some clubSide
                    else
                        losePossession state
                | ValueNone -> ()

            result
        | Airborne -> None, None, None
        | _ ->
            let bx = state.Ball.Position.X
            let by = state.Ball.Position.Y
            let mutable bestDist = System.Double.PositiveInfinity * 1.0<meter^2>
            let mutable bestPlayer: Player option = None
            let mutable bestRoster: PlayerRoster option = None
            let mutable bestClub: ClubSide option = None

            for clubSide in bothSides do
                let frame = getFrame state clubSide
                let roster = getRoster ctx clubSide

                for i = 0 to frame.SlotCount - 1 do
                    match frame.Physics.Occupancy[i] with
                    | OccupancyKind.Active _ ->
                        let px = float frame.Physics.PosX[i] * 1.0<meter>
                        let py = float frame.Physics.PosY[i] * 1.0<meter>
                        let dx = px - bx
                        let dy = py - by
                        let distSq = dx * dx + dy * dy

                        if distSq < bestDist then
                            bestDist <- distSq
                            bestPlayer <- Some roster.Players[i]
                            bestRoster <- Some roster
                            bestClub <- Some clubSide
                    | _ -> ()


            let maxControlDistSq =
                ctx.Config.Physics.ContactRadius * ctx.Config.Physics.ContactRadius

            if bestDist <= maxControlDistSq then
                bestPlayer, bestRoster, bestClub
            else
                None, None, None

    let private decideAction
        (controller: Player)
        (profile: BehavioralProfile)
        (meIdx: int)
        (clubSide: ClubSide)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        : OnBallIntent option =
        let team = buildTeamPerspective clubSide ctx state

        let influence =
            if clubSide = HomeClub then
                state.HomeInfluenceFrame
            else
                state.AwayInfluenceFrame

        let actx =
            AgentContext.build
                controller
                profile
                meIdx
                team
                ValueNone
                state
                clock
                ctx
                state.Config.Decision
                state.Config.BuildUp
                None
                ValueNone
                influence

        let scores = PlayerScorer.computeAll actx state.MatchMemory
        let mask = ActionEligibility.evaluate actx
        PlayerDecision.decide actx scores mask

    let private resolveIntent
        (subTick: int)
        (intent: OnBallIntent)
        (ctx: MatchContext)
        (state: SimState)
        (clock: SimulationClock)
        : ActionResult * RefereeAction list =
        match intent with
        | OnBallIntent.Shoot ->
            let events, outputs = ShotAction.resolve subTick ctx state clock
            ActionResult.withOutputs events outputs, []

        | OnBallIntent.Pass targetPid ->
            match findActivePlayer ctx state targetPid with
            | Some target ->
                let events, outputs = PassAction.resolve subTick ctx state clock target
                ActionResult.withOutputs events outputs, []
            | None -> ActionResult.empty, []

        | OnBallIntent.Dribble ->
            let events, actions, outputs = DuelAction.resolve subTick ctx state clock
            { ActionResult.ofEvents events with PendingRefereeActions = actions; Outputs = outputs }, []

        | OnBallIntent.Cross ->
            let result = CrossAction.resolve subTick ctx state clock
            result, []

        | OnBallIntent.LongBall _ ->
            let events, outputs = PassAction.resolveLong subTick ctx state clock
            ActionResult.withOutputs events outputs, []

        | OnBallIntent.PassIntoSpace targetCell ->
            let events, outputs = PassAction.resolveIntoSpace subTick ctx state clock targetCell
            ActionResult.withOutputs events outputs, []

        | OnBallIntent.Tackle oppPid ->
            match findActivePlayer ctx state oppPid with
            | Some opponent ->
                let events, actions, outputs = DuelAction.resolveTackle subTick ctx state opponent
                { ActionResult.ofEvents events with PendingRefereeActions = actions; Outputs = outputs }, []
            | None -> ActionResult.empty, []

    let run (subTick: int) (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : ActionResult * RefereeAction list =
        match state.Flow with
        | Live ->
            let controller, rosterOpt, controllerClubSide = findController ctx state

            let actionResult, actionRefereeActions =
                match controller, rosterOpt, controllerClubSide with
                | Some ctrl, Some roster, Some clubSide ->
                    if ctrl.Position = GK then
                        match state.Ball.Control with
                        | Controlled(side, gkId) when gkId = ctrl.Id ->
                            let events, outputs = GKAction.resolve subTick ctx state clock
                            ActionResult.withOutputs events outputs, []
                        | _ ->
                            let meIdx = roster.Players |> Array.findIndex (fun p -> p.Id = ctrl.Id)
                            let profile = roster.Profiles[meIdx]

                            match decideAction ctrl profile meIdx clubSide ctx state clock with
                            | Some action -> resolveIntent subTick action ctx state clock
                            | None -> ActionResult.empty, []
                    else
                        let meIdx = roster.Players |> Array.findIndex (fun p -> p.Id = ctrl.Id)
                        let profile = roster.Profiles[meIdx]

                        match decideAction ctrl profile meIdx clubSide ctx state clock with
                        | Some action -> resolveIntent subTick action ctx state clock
                        | None ->
                            match state.Ball.Control with
                            | Controlled _
                            | Receiving _ ->
                                let holdTimeout =
                                    match state.Ball.PlayerHoldSinceSubTick with
                                    | Some since -> subTick - since >= 12
                                    | None -> true

                                if holdTimeout then
                                    losePossession state
                                    ActionResult.empty, []
                                else
                                    ActionResult.empty, []
                            | _ -> ActionResult.empty, []
                | _ -> ActionResult.empty, []

            actionResult, actionRefereeActions
        | _ -> ActionResult.empty, []
