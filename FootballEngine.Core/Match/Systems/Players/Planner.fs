namespace FootballEngine.Core.Match

/// Chain state used by the planner and tests. Defined at namespace level
/// so tests can reference it bare after `open FootballEngine.Core.Match`.
type ChainState =
    { BallX: float<meter>
      BallY: float<meter>
      CarrierSlot: int
      Side: ClubSide
      OurPos: (int * float<meter> * float<meter>)[]
      OppPos: (int * float<meter> * float<meter>)[]
      OffsideLine: float<meter>
      Dir: float }

/// Action-chain planner: given the level-1 options from Decision, simulate
/// multi-step continuations (pass → pass → shoot, etc.) and score each chain
/// so the carrier picks the option that opens the best future. Pure and
/// deterministic — no mutable state, no RNG — same ChainState always produces
/// the same evaluations.
module Planner =

    type ChainAction =
        | Pass of receiverSlot: int * x: float<meter> * y: float<meter>
        | Shoot
        | Dribble
        | Cross of receiverSlot: int * x: float<meter> * y: float<meter>
        | Clear

    type ChainEvaluation =
        { ActionCode: int
          TargetSlot: int
          Chain: ChainAction list
          Value: float }

    /// Value a chain state: progression + shoot bonus + danger penalty +
    /// possession security, all on a 0–1-ish scale matching single-step
    /// utilities. The shooting belief reuses the engine-wide xG formula with a
    /// neutral finishing assumption — the plan evaluates where the ball should
    /// go; the option utility already carries the shooter's actual ability.
    let value (w: WeightSectionsPlanner) (s: ChainState) : float =
        let pitchHalf = 52.5

        let progression =
            w.ProgressionWeight * (float s.BallX + pitchHalf) / (2.0 * pitchHalf)

        let shootBonus =
            let d = MatchMath.distanceToGoal s.Side s.BallX s.BallY

            if d < 32.0 then
                w.ShootValueWeight * MatchMath.xGFrom d w.ShootFinishingAssumption 1.0
            else
                0.0

        let mutable dangerPenalty = 0.0

        for _, ox, oy in s.OppPos do
            let distRival = MatchMath.dist s.BallX s.BallY ox oy

            if distRival < 30.0 then
                let idx = min (int (distRival / 5.0)) (w.DangerBase.Length - 1) |> max 0

                let zone =
                    if float s.BallX * s.Dir < -20.0 then 0
                    elif float s.BallX * s.Dir < 20.0 then 1
                    else 2

                let zoneIdx = min zone (w.DangerZoneWeights.Length - 1) |> max 0
                dangerPenalty <- dangerPenalty - w.DangerBase.[idx] * w.DangerZoneWeights.[zoneIdx]

        let mutable hasTeammateNear = false

        for _, tx, ty in s.OurPos do
            if not hasTeammateNear && MatchMath.dist s.BallX s.BallY tx ty < 10.0 then
                hasTeammateNear <- true

        let possessionSecurity =
            w.PossessionSecurityWeight * (if hasTeammateNear then 1.0 else 0.0)

        progression + shootBonus + dangerPenalty + possessionSecurity

    /// Simulate a pass from (bx,by) to receiver at (rx,ry) and return the
    /// resulting ChainState (ball at receiver, receiver becomes carrier).
    let private simulatePass (s: ChainState) (receiverSlot: int) (rx: float<meter>) (ry: float<meter>) : ChainState =
        { s with
            BallX = rx
            BallY = ry
            CarrierSlot = receiverSlot }

    /// Check if a receiver is offside given the current state.
    let private isOffside (s: ChainState) (rx: float<meter>) : bool =
        if s.Dir > 0.0 then
            float rx > float s.OffsideLine
        else
            float rx < float s.OffsideLine

    /// Find the receiver slot and position from the current state.
    let private findReceiver (s: ChainState) (slot: int) : (int * float<meter> * float<meter>) option =
        s.OurPos
        |> Array.tryFind (fun (s', _, _) -> s' = slot)
        |> Option.map (fun (_, x, y) -> slot, x, y)

    /// Check if a pass from the current carrier to (rx,ry) is feasible:
    /// the ball must arrive before any opponent. The opponent GK is not an
    /// interceptor (a keeper never leaves his line for a pass) — this must
    /// match PassIntercept.interceptorStep or the plan and the real pass
    /// model would disagree about what is playable.
    let private isPassFeasible
        (pparams: InterceptTable.InterceptParams)
        (s: ChainState)
        (rx: float<meter>)
        (ry: float<meter>)
        : bool =
        let d = MatchMath.dist s.BallX s.BallY rx ry

        if d < 0.5 || d > 60.0 then
            false
        else
            let ball =
                { X = s.BallX
                  Y = s.BallY
                  Z = 0.0<meter>
                  Vx = (float (rx - s.BallX) / max (float d) 0.5) * 22.0<meterPerSecond>
                  Vy = (float (ry - s.BallY) / max (float d) 0.5) * 22.0<meterPerSecond>
                  Vz = 0.0<meterPerSecond> }

            let receiverSteps = InterceptTable.stepsToReach pparams ball rx ry

            let mutable bestOpponent = pparams.MaxSteps

            for oslot, ox, oy in s.OppPos do
                if oslot <> 0 then
                    let steps = InterceptTable.stepsToReach pparams ball ox oy

                    if steps < bestOpponent then
                        bestOpponent <- steps

            receiverSteps < bestOpponent

    /// Search: given level-1 options, simulate the first action, then
    /// expand the best continuation recursively up to MaxChainLength.
    /// Each evaluation is anchored on the option's own calibrated utility
    /// (which carries skill, memory, pressure, phase, personality) and adds
    /// the continuation delta (value after - value before), so the chain
    /// ranks options without discarding why the carrier wanted them.
    let search
        (cfg: PlannerConfig)
        (w: WeightSectionsPlanner)
        (pparams: InterceptTable.InterceptParams)
        (s: ChainState)
        (options: (int * int * float) list)
        : ChainEvaluation list =
        let baseValue = value w s
        let evaluations = ResizeArray<ChainEvaluation>()
        let mutable budgetUsed = 0

        for (code, target, utility) in options do
            let chainActions = ResizeArray<ChainAction>()
            let mutable currentState = s
            let mutable finalValue = utility
            let mutable depth = 0

            while depth < cfg.MaxChainLength && budgetUsed < cfg.MaxEvaluateLimit do
                if depth = 0 then
                    // Level 1: simulate the first action.
                    match code with
                    | 1 when target >= 0 ->
                        match findReceiver currentState target with
                        | Some(_, rx, ry) ->
                            if isPassFeasible pparams currentState rx ry then
                                chainActions.Add(Pass(target, rx, ry))
                                currentState <- simulatePass currentState target rx ry
                                budgetUsed <- budgetUsed + 1
                                finalValue <- utility + (value w currentState - baseValue)
                                depth <- depth + 1
                            else
                                depth <- cfg.MaxChainLength
                        | None -> depth <- cfg.MaxChainLength
                    | 2 ->
                        chainActions.Add(Shoot)
                        finalValue <- utility + w.ShootValueWeight * 0.5
                        depth <- cfg.MaxChainLength
                    | 3 ->
                        chainActions.Add(Dribble)
                        budgetUsed <- budgetUsed + 1
                        depth <- depth + 1
                    | 4 ->
                        chainActions.Add(Clear)
                        depth <- cfg.MaxChainLength
                    | 5 ->
                        chainActions.Add(Cross(target, currentState.BallX, currentState.BallY))
                        depth <- cfg.MaxChainLength
                    | _ -> depth <- cfg.MaxChainLength
                else
                    // Levels 2+: expand best continuation greedily.
                    let lastAction = chainActions.[chainActions.Count - 1]

                    match lastAction with
                    | Pass(_, _, _) ->
                        let mutable bestNext = None
                        let mutable bestVal = -1e9

                        for slot, tx, ty in currentState.OurPos do
                            if
                                slot <> currentState.CarrierSlot
                                && slot <> 0
                                && not (isOffside currentState tx)
                                && isPassFeasible pparams currentState tx ty
                            then
                                let next = simulatePass currentState slot tx ty
                                let v = value w next

                                if v > bestVal then
                                    bestVal <- v
                                    bestNext <- Some(slot, tx, ty)

                        match bestNext with
                        | Some(slot, tx, ty) ->
                            chainActions.Add(Pass(slot, tx, ty))
                            currentState <- simulatePass currentState slot tx ty
                            budgetUsed <- budgetUsed + 1
                            finalValue <- utility + (value w currentState - baseValue)
                            depth <- depth + 1
                        | None ->
                            let goalDist = abs (float currentState.BallX - 52.5 * currentState.Dir)

                            if goalDist < 32.0 then
                                chainActions.Add(Shoot)
                                finalValue <- finalValue + w.ShootValueWeight * 0.5

                            depth <- cfg.MaxChainLength
                    | Shoot -> depth <- cfg.MaxChainLength
                    | Dribble ->
                        let dir = currentState.Dir
                        let dribbleDist = 8.0<meter>
                        let nextX = currentState.BallX + dribbleDist * dir

                        let next = { currentState with BallX = nextX }

                        chainActions.Add(Dribble)
                        currentState <- next
                        budgetUsed <- budgetUsed + 1
                        finalValue <- utility + (value w currentState - baseValue)
                        depth <- depth + 1
                    | _ -> depth <- cfg.MaxChainLength

            evaluations.Add(
                { ActionCode = code
                  TargetSlot = target
                  Chain = chainActions |> Seq.toList
                  Value = finalValue }
            )

        evaluations |> Seq.toList

    /// Build a ChainState from the live world.
    let project (ctx: MatchContext) (world: World) (side: ClubSide) (slot: int) : ChainState =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let oppSide = MatchWorld.oppOf side
        let oppTeam = MatchWorld.team world oppSide
        let oppFrame = oppTeam.Frame

        let ballX = world.State.Ball.Position.X
        let ballY = world.State.Ball.Position.Y
        let dir = MatchMath.goalDirection side
        let offsideLine = MatchWorld.offsideLine ctx world side

        let ourPos =
            [| for s in MatchWorld.activeSlots frame do
                   (s, MatchWorld.posX frame s, MatchWorld.posY frame s) |]

        let oppPos =
            [| for s in MatchWorld.activeSlots oppFrame do
                   (s, MatchWorld.posX oppFrame s, MatchWorld.posY oppFrame s) |]

        { BallX = ballX
          BallY = ballY
          CarrierSlot = slot
          Side = side
          OurPos = ourPos
          OppPos = oppPos
          OffsideLine = offsideLine
          Dir = dir }
