namespace FootballEngine.Benchmarks

open System
open System.Diagnostics
open FootballEngine.Core.Match

module Program =

    [<EntryPoint>]
    let main argv =
        let ctx = BenchContext.create ()
        let systems = Scheduler.systems

        let matches =
            match argv with
            | [| m |] ->
                (try
                    int m
                 with _ ->
                     5)
            | _ -> 5

        // Warmup (JIT + tiered compilation).
        for _ in 1..1 do
            Simulator.runMatch systems ctx 1UL Simulator.RunMode.Fast None |> ignore

        let sw = Stopwatch.StartNew()
        let gcBefore = GC.GetTotalAllocatedBytes()
        let mutable goals = 0
        let mutable events = 0
        let mutable ticks = 0L

        for i in 0 .. matches - 1 do
            let seed = uint64 (i + 2)
            let out = Simulator.runMatch systems ctx seed Simulator.RunMode.Fast None
            goals <- goals + out.Final.State.HomeScore + out.Final.State.AwayScore
            events <- events + out.Events.Length
            ticks <- ticks + int64 out.Final.State.Tick

        sw.Stop()
        let allocMb = float (GC.GetTotalAllocatedBytes() - gcBefore) / (1024.0 * 1024.0)

        let perMatch = float sw.Elapsed.TotalMilliseconds / float matches

        printfn
            "matches=%d totalMs=%.1f perMatchMs=%.1f allocMb=%d allocMb/match=%.1f ticks/match=%.0f goals=%d events/match=%.0f"
            matches
            (float sw.Elapsed.TotalMilliseconds)
            perMatch
            (int allocMb)
            (allocMb / float matches)
            (float ticks / float matches)
            goals
            (float events / float matches)

        0
