namespace Training

open FootballEngine.Domain

module ErrorCalculator =

    let private mse (deltas: MetricDelta list) : float =
        if List.isEmpty deltas then
            0.0
        else
            deltas |> List.averageBy (fun d -> d.DeltaPct * d.DeltaPct)

    let computeError (matchMetrics: MatchMetrics) (targets: Training.CalibrationTargets) : float * MetricDelta list =
        let deltas = Calibrator.compare matchMetrics targets
        let error = mse deltas
        error, deltas

    let computeErrorWithExperience
        (matchMetrics: MatchMetrics)
        (exp: ExperienceMetrics)
        (targets: Training.CalibrationTargets)
        : float * MetricDelta list =
        let baseError, deltas = computeError matchMetrics targets

        let expPenalty =
            exp.MomentumVariance * 0.01
            + abs (exp.LateGoalFrequency - 0.2) * 0.5
            + abs (exp.ComebackRate - 0.1) * 0.5

        baseError + expPenalty, deltas
