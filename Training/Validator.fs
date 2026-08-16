namespace Training

open FootballEngine.Core.Match

/// Validation: run matches with a config and print a table against targets.
module Validator =

    let validate (cfg: BalanceConfig) (seedBase: uint64) (n: int) : MatchMetrics list =
        SimulatorRunner.runMatches cfg seedBase n

    let print (label: string) (metrics: MatchMetrics list) : unit =
        let agg = Metrics.aggregate metrics
        let pairs = Metrics.toPairs agg
        let n = metrics.Length

        printfn "== %s (%d matches) ==" label n

        for t in ErrorCalculator.targets do
            let v = pairs |> List.find (fun (name, _) -> name = t.Name) |> snd
            let ok = v >= t.Lo && v <= t.Hi
            let marker = if ok then "OK " else "BAD"

            printfn "  %-12s %6.1f  (target %.1f, band [%.1f..%.1f]) %s" t.Name v t.Target t.Lo t.Hi marker

        let total = ErrorCalculator.error metrics
        printfn "  total error: %.3f" total
