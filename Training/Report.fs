namespace Training

open System.Collections.Generic
open FootballEngine.Core.Match

/// One pass per match yields both the banded metrics and the causal funnels behind them, so an out-of-band number
/// comes with the stage where it goes wrong.
module Report =

    type MatchRun =
        { Metrics: MatchMetrics
          Counts: Dictionary<string, float> }

    let private runMatch (cfg: BalanceConfig) (seed: uint64) : MatchRun =
        let counts = Dictionary<string, float>()
        let add key = counts[key] <- (match counts.TryGetValue key with | true, v -> v | _ -> 0.0) + 1.0
        let mutable lastBall = "open play"

        let world =
            SimulatorRunner.runObserved cfg seed (fun _ events ->
                for event in events do
                    match event.Data with
                    | EventData.Shot _ ->
                        add "shots"
                        lastBall <- "shot"
                    | EventData.KeeperDive _ -> add "keeper dives"
                    | EventData.Save _ ->
                        add "saves"
                        lastBall <- "save"
                    | EventData.Woodwork _ -> add "woodwork"
                    | EventData.Goal _ -> add "goals"
                    | EventData.PhysicalContact record when record.BallTouched ->
                        match record.ActionKind with
                        | Some ContactActionKind.ContactTrap ->
                            add "traps"

                            if record.ControlAfter.Kind = ContactControlKind.UncontrolledBall then
                                add "traps lost"
                        | _ -> ()

                        lastBall <- "loose contact"
                    | EventData.FootContact(_, _, kind, _, _, _) -> lastBall <- $"touch {kind}"
                    | EventData.CrossAttempt _ -> lastBall <- "cross"
                    | EventData.Interception _ -> lastBall <- "interception"
                    | EventData.Foul(_, _, severity, _, _) ->
                        add "fouls"
                        add $"fouls {severity}"
                    | EventData.Card(_, CardColor.Yellow) -> add "yellows"
                    | EventData.Card(_, CardColor.Red) -> add "reds"
                    | EventData.SetPiece(SetPieceKind.Corner, _) -> add $"corner after {lastBall}"
                    | _ -> ())

        let metrics = Metrics.ofWorld world
        counts["on target"] <- metrics.OnTarget

        { Metrics = metrics; Counts = counts }

    let run (cfg: BalanceConfig) (seeds: uint64[]) : MatchRun list =
        seeds |> Array.Parallel.map (runMatch cfg) |> Array.toList

    let private perMatch (runs: MatchRun list) key =
        runs
        |> List.averageBy (fun r -> match r.Counts.TryGetValue key with | true, v -> v | _ -> 0.0)

    /// Mean and 95 % half-width of the mean over matches; a difference smaller than the half-widths is noise.
    let private meanAndInterval (values: float list) =
        let n = float values.Length
        let mean = List.average values

        let sd =
            if values.Length < 2 then 0.0
            else sqrt (values |> List.sumBy (fun v -> (v - mean) ** 2.0) |> fun s -> s / (n - 1.0))

        mean, 1.96 * sd / sqrt n

    let print (label: string) (runs: MatchRun list) =
        let n = runs.Length
        printfn "== %s (%d matches) ==" label n

        for t in ErrorCalculator.targets do
            let values =
                runs |> List.map (fun r -> Metrics.toPairs r.Metrics |> List.find (fun (name, _) -> name = t.Name) |> snd)

            let mean, half = meanAndInterval values
            let marker = if mean >= t.Lo && mean <= t.Hi then "OK " else "BAD"
            printfn "  %-11s %6.1f ±%-5.1f (target %.1f, band [%.1f..%.1f]) %s" t.Name mean half t.Target t.Lo t.Hi marker

        printfn "  total error: %.3f" (ErrorCalculator.error (runs |> List.map _.Metrics))

        let rate numerator denominator =
            let d = perMatch runs denominator
            if d > 0.0 then 100.0 * perMatch runs numerator / d else nan

        // Top-flight references (Opta-era league averages) for the stage rates.
        let stage label numerator denominator reference =
            printfn "  %-34s %5.1f %%  (real ~%s)" label (rate numerator denominator) reference

        printfn "-- funnels (per match) --"

        printfn
            "  shots %.1f -> on target %.1f -> keeper dives %.1f -> saves %.1f -> goals %.1f (woodwork %.1f)"
            (perMatch runs "shots")
            (perMatch runs "on target")
            (perMatch runs "keeper dives")
            (perMatch runs "saves")
            (perMatch runs "goals")
            (perMatch runs "woodwork")

        stage "shots on target" "on target" "shots" "33"
        stage "conversion (goals / shots)" "goals" "shots" "11"
        stage "saves / on target" "saves" "on target" "70"
        stage "first touch lost (traps)" "traps lost" "traps" "?"
        stage "yellows / fouls" "yellows" "fouls" "20"

        printfn
            "  traps %.1f, lost %.1f | fouls %.1f: tactical %.1f, yellow-worthy %.1f, red-worthy %.1f | reds %.2f"
            (perMatch runs "traps")
            (perMatch runs "traps lost")
            (perMatch runs "fouls")
            (perMatch runs "fouls Tactical")
            (perMatch runs "fouls YellowWorthy")
            (perMatch runs "fouls RedWorthy")
            (perMatch runs "reds")

        let corners =
            runs
            |> Seq.collect (fun r -> r.Counts.Keys)
            |> Seq.filter (fun k -> k.StartsWith "corner after")
            |> Seq.distinct
            |> Seq.map (fun k -> k, perMatch runs k)
            |> Seq.sortByDescending snd
            |> Seq.truncate 4
            |> Seq.map (fun (k, v) -> $"{k.Substring 13} {v:F1}")
            |> String.concat ", "

        printfn "  corners by last touch: %s" corners
