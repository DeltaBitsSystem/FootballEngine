#r "System.Diagnostics.Process"

open System
open System.Diagnostics

let run cmd args =
    let psi = ProcessStartInfo(FileName = cmd, Arguments = args, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false)
    let p = Process.Start(psi)
    p.WaitForExit()
    p.StandardOutput.ReadToEnd()

printfn "=== Building..."
run "dotnet" "build FootballEngine.Benchmarks/FootballEngine.Benchmarks.fsproj -c Release --verbosity quiet" |> ignore

printfn "=== Starting benchmark..."
let bench = Process.Start("dotnet", "exec FootballEngine.Benchmarks/bin/Release/net10.0/FootballEngine.Benchmarks.dll")
printfn "PID: %d" bench.Id

printfn "=== Waiting 10s..."
System.Threading.Thread.Sleep(10000)

printfn "=== Capturing trace (60s)..."
let trace = Process.Start("dotnet-trace", "collect --profile gc-verbose --process-id " + string bench.Id + " --duration 00:02:00 --output trace.nettrace")
trace.WaitForExit()

printfn "=== Report:"
let report = run "dotnet-trace" "report trace.nettrace topN -n 30"
printfn "%s" report

bench.Kill()
printfn "=== Done"
