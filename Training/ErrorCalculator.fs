namespace Training

/// Error surface: each metric is scored against a band; the total is the sum
/// of per-metric squared errors. Zero error = inside every band.
module ErrorCalculator =

    let targets: MetricTarget list =
        [ { Name = "goals"
            Target = 2.7
            Lo = 1.7
            Hi = 4.5 }
          { Name = "shots"
            Target = 28.0
            Lo = 20.0
            Hi = 36.0 }
          { Name = "passPct"
            Target = 81.0
            Lo = 72.0
            Hi = 90.0 }
          { Name = "fouls"
            Target = 23.0
            Lo = 14.0
            Hi = 32.0 }
          { Name = "yellows"
            Target = 5.0
            Lo = 2.0
            Hi = 8.0 }
          { Name = "corners"
            Target = 10.5
            Lo = 7.0
            Hi = 14.0 }
          { Name = "offsides"
            Target = 3.5
            Lo = 1.0
            Hi = 6.0 }
          { Name = "possession"
            Target = 50.0
            Lo = 30.0
            Hi = 70.0 } ]

    let metricError (t: MetricTarget) (v: float) : float =
        if v < t.Lo then ((t.Lo - v) / (t.Lo * 0.4)) ** 2.0
        elif v > t.Hi then ((v - t.Hi) / (t.Hi * 0.4)) ** 2.0
        else 0.0

    let error (metrics: MatchMetrics list) : float =
        if metrics.IsEmpty then
            failwith "error: no metrics"
        else
            let values = metrics |> List.map Metrics.toPairs

            targets
            |> List.sumBy (fun t ->
                values
                |> List.averageBy (fun pairs ->
                    let v = pairs |> List.find (fun (name, _) -> name = t.Name) |> snd
                    metricError t v))

    /// Per-metric table for validation output.
    let deltas (aggregate: MatchMetrics) : (MetricTarget * float) list =
        let pairs = Metrics.toPairs aggregate

        targets
        |> List.map (fun t -> t, (pairs |> List.find (fun (name, _) -> name = t.Name) |> snd))
