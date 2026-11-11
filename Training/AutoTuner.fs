namespace Training

open FootballEngine.Core.Match

/// Coordinate descent over the decision/ball/referee knobs. Each evaluation is
/// a deterministic headless match, so improvements are comparable. This is the
/// self-learning loop: play → measure → adjust → repeat.
module AutoTuner =

    let private dec (cfg: BalanceConfig) : WeightSectionsDecision = cfg.Weights.Decision
    let private ballCfg (cfg: BalanceConfig) : BallConfig = cfg.Ball
    let private acts (cfg: BalanceConfig) : ActionsConfig = cfg.Actions
    let private refCfg (cfg: BalanceConfig) : RefereeConfig = cfg.Referee

    let private withDec (cfg: BalanceConfig) (f: WeightSectionsDecision -> WeightSectionsDecision) : BalanceConfig =
        { cfg with
            Weights =
                { cfg.Weights with
                    Decision = f (dec cfg) } }

    let private withBallCfg (cfg: BalanceConfig) (f: BallConfig -> BallConfig) : BalanceConfig =
        { cfg with Ball = f (ballCfg cfg) }

    let private withActs (cfg: BalanceConfig) (f: ActionsConfig -> ActionsConfig) : BalanceConfig =
        { cfg with Actions = f (acts cfg) }

    let private withRefCfg (cfg: BalanceConfig) (f: RefereeConfig -> RefereeConfig) : BalanceConfig =
        { cfg with Referee = f (refCfg cfg) }

    /// Knob = (name, getter, setter, min, max). Add a line to tune a new constant.
    let knobs: Param list =
        [ { Name = "passBase"
            Get = fun c -> (dec c).PassBaseUtility
            Set = fun c v -> withDec c (fun d -> { d with PassBaseUtility = v })
            Min = 0.2
            Max = 0.8 }
          { Name = "shotBase"
            Get = fun c -> (dec c).ShotBaseUtility
            Set = fun c v -> withDec c (fun d -> { d with ShotBaseUtility = v })
            Min = 0.05
            Max = 0.6 }
          { Name = "dribbleBase"
            Get = fun c -> (dec c).DribbleBaseUtility
            Set = fun c v -> withDec c (fun d -> { d with DribbleBaseUtility = v })
            Min = 0.0
            Max = 0.5 }
          { Name = "crossBase"
            Get = fun c -> (dec c).CrossBaseUtility
            Set = fun c v -> withDec c (fun d -> { d with CrossBaseUtility = v })
            Min = 0.0
            Max = 0.3 }
          { Name = "shotXGMult"
            Get = fun c -> (dec c).ShotXGMultiplier
            Set = fun c v -> withDec c (fun d -> { d with ShotXGMultiplier = v })
            Min = 0.3
            Max = 3.0 }
          { Name = "shotDepthAtt"
            Get = fun c -> (dec c).ShotDepthBonusAttacking
            Set = fun c v -> withDec c (fun d -> { d with ShotDepthBonusAttacking = v })
            Min = 0.0
            Max = 1.0 }
          { Name = "shotDepthDef"
            Get = fun c -> (dec c).ShotDepthBonusDefending
            Set = fun c v -> withDec c (fun d -> { d with ShotDepthBonusDefending = v })
            Min = 0.0
            Max = 1.0 }
          { Name = "dribbleZoneMult"
            Get = fun c -> (dec c).DribbleZoneAttackingMult
            Set = fun c v -> withDec c (fun d -> { d with DribbleZoneAttackingMult = v })
            Min = 0.0
            Max = 1.0 }
          { Name = "dribblePhaseMult"
            Get = fun c -> (dec c).DribblePhaseBonusMult
            Set = fun c v -> withDec c (fun d -> { d with DribblePhaseBonusMult = v })
            Min = 0.0
            Max = 1.0 }
          { Name = "pressFactor"
            Get = fun c -> (dec c).PassHighPressFactor
            Set = fun c v -> withDec c (fun d -> { d with PassHighPressFactor = v })
            Min = 0.0
            Max = 1.0 }
          { Name = "temperature"
            Get = fun c -> (dec c).DecisionTemperature
            Set = fun c v -> withDec c (fun d -> { d with DecisionTemperature = v })
            Min = 0.05
            Max = 0.5 }
          { Name = "passRollingSpinRatio"
            Get = fun c -> (ballCfg c).PassRollingSpinRatio
            Set = fun c v -> withBallCfg c (fun b -> { b with PassRollingSpinRatio = v })
            Min = 0.3
            Max = 1.0 }
          { Name = "foulYellowChance"
            Get = fun c -> (refCfg c).FoulTacticalChance
            Set = fun c v -> withRefCfg c (fun r -> { r with FoulTacticalChance = v })
            Min = 0.02
            Max = 0.4 }
          { Name = "dribbleSuccess"
            Get = fun c -> (acts c).DribbleBaseSuccess
            Set = fun c v -> withActs c (fun a -> { a with DribbleBaseSuccess = v })
            Min = 0.2
            Max = 0.9 }
          { Name = "receptionBase"
            Get = fun c -> (ballCfg c).ReceptionBase
            Set = fun c v -> withBallCfg c (fun b -> { b with ReceptionBase = v })
            Min = 0.3
            Max = 0.9 }
          { Name = "receptionSpacePenalty"
            Get = fun c -> (ballCfg c).ReceptionSpacePenalty
            Set = fun c v -> withBallCfg c (fun b -> { b with ReceptionSpacePenalty = v })
            Min = 0.0
            Max = 0.1 } ]

    let private clamp (v: float) (lo: float) (hi: float) = max lo (min hi v)

    let private initialSteps (p: Param) = (p.Max - p.Min) / 4.0

    let private minStep (p: Param) = (p.Max - p.Min) / 200.0

    let private tryRandomRestart
        (eval: BalanceConfig -> float)
        (rngState: System.Random)
        (best: BalanceConfig)
        (bestErr: float)
        : BalanceConfig * float * int =
        // Poor man's annealing: sample every knob uniformly and keep the best.
        let mutable b = best
        let mutable e = bestErr
        let mutable evals = 0

        for _ in 1..4 do
            let mutable cfg = b

            for p in knobs do
                let v = p.Min + rngState.NextDouble() * (p.Max - p.Min)
                cfg <- p.Set cfg v

            let ce = eval cfg
            evals <- evals + 1

            if ce < e then
                e <- ce
                b <- cfg

        b, e, evals

    let tune (eval: BalanceConfig -> float) (start: BalanceConfig) (sweeps: int) : TuneResult =
        let mutable best = start
        let mutable bestErr = eval start
        let mutable evals = 1
        let history = ResizeArray<float>()
        history.Add bestErr
        let mutable steps = knobs |> List.map (fun p -> p, initialSteps p)
        let rngState = System.Random 12345

        for _ in 1..sweeps do
            let mutable improvedThisSweep = false

            for p, step in steps do
                if step >= minStep p then
                    let up = clamp (p.Get best + step) p.Min p.Max
                    let down = clamp (p.Get best - step) p.Min p.Max

                    let tryDir (v: float) =
                        if v <> p.Get best then
                            let cfg = p.Set best v
                            let e = eval cfg
                            evals <- evals + 1

                            if e < bestErr then
                                bestErr <- e
                                best <- cfg
                                true
                            else
                                false
                        else
                            false

                    let upOk = tryDir up
                    let downOk = if not upOk then tryDir down else false

                    if upOk || downOk then
                        improvedThisSweep <- true
                        history.Add bestErr
                    else
                        // Halve the step for the next sweep.
                        steps <-
                            steps
                            |> List.map (fun (pp, s) -> if pp.Name = p.Name then pp, s / 2.0 else pp, s)

            if not improvedThisSweep then
                // Stalled: restart exploration with full steps (multi-start descent).
                steps <- knobs |> List.map (fun p -> p, initialSteps p)
                let nb, ne, nev = tryRandomRestart eval rngState best bestErr
                evals <- evals + nev

                if ne < bestErr then
                    bestErr <- ne
                    best <- nb
                    history.Add bestErr

        { Best = best
          Error = bestErr
          StartError = history.[0]
          Evaluations = evals
          History = List.ofSeq history }

    /// Deltas of the best config vs the start config, for reporting.
    let deltas (start: BalanceConfig) (best: BalanceConfig) : (string * float) list =
        knobs |> List.map (fun p -> p.Name, p.Get best - p.Get start)
