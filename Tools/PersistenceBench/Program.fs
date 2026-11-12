module FootballEngine.Tools.PersistenceBench

open System
open System.Diagnostics
open System.IO
open FootballEngine
open FootballEngine.Data
open FootballEngine.Domain
open FootballEngine.Generation
open FootballEngine.Settings
open FootballEngine.World

let country =
    """{"id":"bench:country/tst","kind":"country","schema":1,"code":"TST","confederation":"UEFA","nameKey":"bench:country/tst.name","domain":{"Country":{"Code":"TST","Name":"Testland","Confederation":"UEFA"},"Names":{"FirstNames":["Alex","Sam"],"LastNames":["Stone","River"]},"LeagueNames":["Test League"],"LeagueRules":[{"PointsForWin":3,"PointsForDraw":1,"Tiebreakers":["GD","GS"],"Promotion":[],"Relegation":[]}],"Clubs":[{"Name":"North FC","LeagueLevel":0,"Reputation":7000},{"Name":"South FC","LeagueLevel":0,"Reputation":6500},{"Name":"East FC","LeagueLevel":0,"Reputation":6000},{"Name":"West FC","LeagueLevel":0,"Reputation":6000}],"Cups":[]}}"""

let sampleWorld () =
    let package: ModPackage =
        { Manifest =
            { Id = ModId "bench"
              Version = "1.0.0"
              Dependencies = []
              Priority = 0
              Before = []
              After = [] }
          PackageSha256 =
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([| 42uy |])).ToLowerInvariant()
          SportsContent = Map.ofList [ ContentId "bench:country/tst", country ]
          Translations = Map.empty
          Assets = Map.empty
          Patches = [] }

    let resolved =
        match
            ModResolution.resolve
                [ { RelativePath = "benchmark.femod"
                    Package = package } ]
        with
        | Ok value -> value
        | Error error -> invalidOp $"Benchmark data is invalid: {error}"

    match WorldGen.generateNewGame resolved 0xC02UL (DateTime(2026, 7, 1)) "TST" "Bench Manager" [] with
    | Ok value -> value
    | Error error -> invalidOp $"Benchmark world is invalid: {error}"

let measure action =
    let timer = Stopwatch.StartNew()
    action ()
    timer.Stop()
    timer.Elapsed.TotalMilliseconds

[<EntryPoint>]
let main args =
    let root =
        Path.Combine(Path.GetTempPath(), "opencode", "football-engine-persistence-bench", Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory root |> ignore
    Db.configureStorageDirectoryForTests root

    let baseWorld = sampleWorld ()
    let basePlayers = baseWorld.Players |> Map.values |> Seq.toArray
    let playerCount = if args.Length > 0 then int args[0] else 10000

    let players =
        [ 1..playerCount ]
        |> Seq.map (fun id ->
            let player = basePlayers[(id - 1) % basePlayers.Length]
            id, { player with Id = id })
        |> Map.ofSeq

    let world = { baseWorld with Players = players }
    let clock = WorldClockOps.init world.Season
    let settings = SaveSettings.defaults
    (Db.initTablesForSlot "bench").GetAwaiter().GetResult()

    let runSave state =
        Db.saveGameInSlotAsync "bench" state clock settings
        |> fun task -> task.GetAwaiter().GetResult()

    let first = measure (fun () -> runSave world)
    let unchanged = measure (fun () -> runSave world)

    let changed =
        { world with
            Players =
                world.Players
                |> Map.change
                    1
                    (Option.map (fun player ->
                        { player with
                            Morale = player.Morale + 1 })) }

    let smallChange = measure (fun () -> runSave changed)

    let load =
        measure (fun () ->
            match (Db.loadGameInSlot "bench").GetAwaiter().GetResult() with
            | Ok(restored, _, _, _) when restored = changed -> ()
            | result -> invalidOp $"Benchmark round-trip failed: {result}")

    let listing =
        measure (fun () -> Db.listSaveSlotsAsync().GetAwaiter().GetResult() |> ignore)

    let path = Path.Combine(root, "career-bench.db3")

    printfn
        "players=%d save-initial=%.1fms save-unchanged=%.1fms save-one-player=%.1fms load=%.1fms list=%.1fms db=%.1fMiB"
        playerCount
        first
        unchanged
        smallChange
        load
        listing
        (float (FileInfo(path).Length) / 1048576.0)

    printfn "Benchmark database: %s" root
    0
