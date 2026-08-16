namespace Training

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open FootballEngine.Core.Match

/// CLI:
///   validate [n]        — run n matches with defaultConfig, print vs targets
///   tune [sweeps] [n]   — coordinate-descent auto-calibration, then validate
///   dump <path> [n]     — tune then dump the best knobs as JSON for the game
module Program =

    let seedBase = 42UL

    let private defaultArgs (args: string list) (def: string list) = if args.IsEmpty then def else args

    let private validateCmd (n: int) (cfg: BalanceConfig) : int =
        let metrics = Validator.validate cfg seedBase n
        Validator.print "default config" metrics
        0

    let private tuneCmd (sweeps: int) (n: int) : BalanceConfig =
        let start = BalanceConfig.defaultConfig
        let startErr = SimulatorRunner.evalStable start seedBase n
        printfn "start error (n=%d): %.3f" n startErr

        let eval =
            if n <= 1 then
                fun cfg -> SimulatorRunner.evalQuick cfg seedBase
            else
                fun cfg -> SimulatorRunner.evalStable cfg seedBase n

        let sw = Diagnostics.Stopwatch.StartNew()
        let result = AutoTuner.tune eval start sweeps
        sw.Stop()

        printfn
            "tuned in %d evals, %.1fs (error %.3f -> %.3f)"
            result.Evaluations
            sw.Elapsed.TotalSeconds
            result.StartError
            result.Error

        for name, delta in AutoTuner.deltas start result.Best do
            printfn "  %-18s %+0.4f" name delta

        let metrics = Validator.validate result.Best seedBase n
        Validator.print "tuned config" metrics
        result.Best

    let private dumpCmd (path: string) (n: int) : unit =
        let best = tuneCmd 3 n

        let values = AutoTuner.knobs |> List.map (fun p -> p.Name, p.Get best) |> dict

        let json =
            JsonSerializer.Serialize(values, JsonSerializerOptions(WriteIndented = true))

        File.WriteAllText(path, json)
        printfn "dumped %d knobs to %s" values.Count path

    [<EntryPoint>]
    let main argv =
        let args = argv |> Array.toList

        match args with
        | "validate" :: rest ->
            let n = defaultArgs rest [ "2" ] |> List.head |> int
            validateCmd n BalanceConfig.defaultConfig
        | "tune" :: rest ->
            let parts = defaultArgs rest [ "6"; "2" ]
            let sweeps = parts.[0] |> int
            let n = if parts.Length > 1 then parts.[1] |> int else 1
            tuneCmd sweeps n |> ignore
            0
        | "dump" :: path :: rest ->
            let n = defaultArgs rest [ "1" ] |> List.head |> int
            dumpCmd path n
            0
        | _ ->
            printfn "usage: validate [n] | tune [sweeps] [n] | dump <path> [n]"
            0
