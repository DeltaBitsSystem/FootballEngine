module FootballEngine.Benchmarks.Program

open System
open System.Diagnostics
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Generation
open FootballEngine.Simulation
open FootballEngine.Types

let sw = Stopwatch()

let loadData () =
    match Data.ModLoader.loadAll Data.ModPaths.builtinsDir Data.ModPaths.modsDir with
    | Ok data -> Data.DataRegistry.setLoadedData data
    | Error _ -> ()

let runOneSim (ctx: MatchContext) (state: SimState) =
    sw.Restart()

    let final, _events =
        MatchSimulator.runLoopFast ctx state SimulationClock.defaultClock

    sw.Stop()
    final.HomeScore, final.AwayScore, sw.ElapsedMilliseconds

[<EntryPoint>]
let main _args =
    loadData ()
    printfn "=== Generating world..."
    let gameState = WorldGen.generateNewGame "ENG" "Test Manager" [ "ESP" ]

    let clubs = gameState.Clubs |> Map.toArray |> Array.map snd
    printfn $"=== Generated %d{clubs.Length} clubs"

    if clubs.Length < 2 then
        failwith "Need at least 2 clubs"

    let homeClub = clubs.[0]
    let awayClub = clubs.[1]
    printfn $"=== Match: %s{homeClub.Name} vs %s{awayClub.Name}"

    printfn "=== Warm-up (1 iteration)..."

    let ctx, state, _, _ =
        MatchSimulator.setup homeClub awayClub gameState.Players gameState.Staff false gameState.ProfileCache
        |> function
            | Ok v -> v
            | Error e -> failwithf $"Setup failed: %A{e}"

    let hs, as_, ms = runOneSim ctx state
    printfn $"  Warm-up: %d{hs}-%d{as_} (%d{ms} ms)"

    printfn "=== Starting infinite loop..."
    printfn $"PID: %d{Process.GetCurrentProcess().Id}"
    printfn ""

    let mutable iter = 1

    while true do
        let ctx, state, _, _ =
            MatchSimulator.setup homeClub awayClub gameState.Players gameState.Staff false gameState.ProfileCache
            |> function
                | Ok v -> v
                | Error e -> failwithf $"Setup failed: %A{e}"

        let hs, as_, ms = runOneSim ctx state
        let allocMB = GC.GetTotalMemory(false) / 1048576L
        printfn $"Iter %4d{iter} | %d{hs}-%d{as_} | %5d{ms} ms | %6d{allocMB} MB"
        iter <- iter + 1

    0
