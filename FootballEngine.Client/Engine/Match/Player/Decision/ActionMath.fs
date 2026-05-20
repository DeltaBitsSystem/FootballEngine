namespace FootballEngine.Player.Decision

open FootballEngine
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract


[<Measure>]
type intention

[<Measure>]
type execution

[<Measure>]
type outcome

[<Measure>]
type decisionScore

[<Measure>]
type executionScore

[<Struct>]
type IntentionInput =
    { Vision: int
      Composure: int
      Condition: int
      Pressure: float }

[<Struct>]
type ExecutionInput =
    { TechnicalA: int
      TechnicalB: int
      PhysicalA: int
      Condition: int }

module ActionMath =
    let private sigmoid x = 1.0 / (1.0 + System.Math.Exp(-x))

    // Probability helpers (using unique names to avoid collisions with Stats.fs)
    let engineBernoulli (p: Probability) : bool =
        let v = Probability.value p

        let clampedP =
            if System.Double.IsNaN v then
                0.0
            else
                System.Math.Clamp(v, 0.0, 1.0)

        let dist = FSharp.Stats.Distributions.Discrete.Bernoulli.Init clampedP
        dist.Sample() = 1

    let engineLogistic (x: float) : float = 1.0 / (1.0 + System.Math.Exp(-x))

    let engineLogisticBernoulli (score: float) (steepness: float) : bool =
        engineBernoulli (Probability.from (engineLogistic (score * steepness)))

    let engineBetaSample (mean: float) (concentration: float) : Probability =
        let clampedMean = System.Math.Clamp(mean, 0.01, 0.99)
        let alpha = System.Math.Max(0.01, clampedMean * concentration)
        let betaVal = System.Math.Max(0.01, (1.0 - clampedMean) * concentration)
        let dist = FSharp.Stats.Distributions.Continuous.Beta.Init alpha betaVal
        Probability.from (dist.Sample())

    let calcIntention (i: IntentionInput) : float<intention> =
        let base' = (MathPipelines.normStat i.Vision * 0.65) + (MathPipelines.normStat i.Composure * 0.35)
        let penalty = i.Pressure * (1.0 - MathPipelines.normStat i.Composure) * 0.30

        (base' * MathPipelines.normCondition i.Condition - penalty)
        |> clampFloat 0.05 1.0
        |> LanguagePrimitives.FloatWithMeasure<intention>

    let calcExecution (e: ExecutionInput) : float<execution> =
        let base' =
            (MathPipelines.normStat e.TechnicalA * 0.55)
            + (MathPipelines.normStat e.TechnicalB * 0.25)
            + (MathPipelines.normStat e.PhysicalA * 0.20)

        (base' * sqrt (MathPipelines.normCondition e.Condition))
        |> clampFloat 0.05 1.0
        |> LanguagePrimitives.FloatWithMeasure<execution>

    let calcMovementExecution (agility: int) (balance: int) (acceleration: int) (condition: int) : float<execution> =
        let base' =
            (MathPipelines.normStat agility * 0.55) + (MathPipelines.normStat balance * 0.25) + (MathPipelines.normStat acceleration * 0.20)

        (base' * sqrt (MathPipelines.normCondition condition))
        |> clampFloat 0.05 1.0
        |> LanguagePrimitives.FloatWithMeasure<execution>

    let playerMaxSpeed (pace: int) (condition: int) : float<meter / second> =
        let paceNorm = MathPipelines.normStat pace
        let condFactor = sqrt (MathPipelines.normCondition condition)
        (PlayerSpeedMin + (PlayerSpeedMax - PlayerSpeedMin) * paceNorm) * condFactor

    let playerAccel (acceleration: int) (condition: int) : float<meter / second^2> =
        let aNorm = MathPipelines.normStat acceleration
        let condFactor = MathPipelines.normCondition condition
        (PlayerAccelMin + (PlayerAccelMax - PlayerAccelMin) * aNorm) * condFactor

    let shotSpeed (finishing: int) : float<meter / second> =
        let fNorm = MathPipelines.normStat finishing
        ShotSpeedMin + (ShotSpeedMax - ShotSpeedMin) * fNorm

    let evalPerformance (config: PerformanceConfig) (stat: float) (condition: int) (morale: int) : float =
        let normCond = float condition / 100.0
        let normMorale = float morale / 100.0

        let rawValue =
            stat * config.StatWeight
            + normCond * config.ConditionWeight
            + normMorale * config.MoraleWeight

        let scaled = sigmoid (config.CurveSteepness * (rawValue - config.CurveInflection))
        clampFloat 0.01 1.0 scaled

    /// Softmax sampling with temperature for stochastic action selection.
    /// temperature -> 0: deterministic argmax
    /// temperature -> inf: uniform random
    /// Recommended: 0.10 - 0.25 for natural decision variability
    let softmaxSample (temperature: float) (items: (float * 'a) list) : 'a option =
        if items.IsEmpty then
            None
        elif items.Length = 1 then
            Some(snd items.Head)
        else
            let maxScore = items |> List.maxBy fst |> fst

            let shifted =
                items
                |> List.map (fun (s, v) ->
                    let expV = System.Math.Exp((s - maxScore) / temperature)
                    expV, v)

            let total = shifted |> List.sumBy fst
            let roll = Stats.rollProbability () * total

            let rec pick acc =
                function
                | [] -> snd (List.last shifted)
                | (w, v) :: rest ->
                    let acc' = acc + w
                    if roll <= acc' then v else pick acc' rest

            Some(pick 0.0 shifted)
