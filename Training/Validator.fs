namespace Training

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Simulation

module Validator =

    let validate
        (validationMatches: int)
        (home: Club)
        (away: Club)
        (players: Map<PlayerId, Player>)
        (staff: Map<StaffId, Staff>)
        (profileMap: Map<PlayerId, BehavioralProfile>)
        (targets: Training.CalibrationTargets)
        (originalWeights: BalanceConfig)
        (candidateWeights: BalanceConfig)
        (originalError: float)
        : Result<BalanceConfig, string> =
        let results =
            SimulatorRunner.runBatch candidateWeights validationMatches home away players staff profileMap

        if List.isEmpty results then
            Error "Validator: no matches simulated"
        else
            let matchMetrics, _ = MetricsAggregator.aggregate results
            let newError, _ = ErrorCalculator.computeError matchMetrics targets

            if newError < originalError then
                Ok candidateWeights
            else
                Error(sprintf "Validation failed: original error %.4f, new error %.4f" originalError newError)
