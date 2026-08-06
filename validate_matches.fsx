#r "FootballEngine.Tests/bin/Debug/net10.0/FootballEngine.Tests.dll"
#r "FootballEngine.Client/bin/Debug/net10.0/FootballEngine.Client.dll"

open System
open System.IO
open System.Text.Json
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Runners

let fullMatchTicks = 95 * 40 * 60

let homePos =
  [| 50.0, 34.0
     35.0, 10.0
     38.0, 34.0
     38.0, 54.0
     35.0, 68.0
     45.0, 34.0
     52.0, 20.0
     52.0, 54.0
     60.0, 10.0
     62.0, 34.0
     70.0, 34.0 |]

let awayPos =
  [| 100.0, 34.0
     91.0, 56.0
     91.0, 43.0
     91.0, 25.0
     91.0, 12.0
     75.0, 34.0
     65.0, 46.0
     65.0, 22.0
     45.0, 53.0
     40.0, 34.0
     37.0, 34.0 |]

let makeLineup gk out =
  [| makeGk 1 gk gk gk
     makePlayer 2 DL out
     makePlayer 3 DC out
     makePlayer 4 DC out
     makePlayer 5 DR out
     makePlayer 6 DM out
     makePlayer 7 MC out
     makePlayer 8 MC out
     makePlayer 9 AML out
     makePlayer 10 AMC out
     makePlayer 11 ST out |]

let runMatch idx =
  let hp = makeLineup 14 14
  let ap = makeLineup 14 14
  let ctx, state = buildSimState hp homePos ap awayPos
  state.Flow <- MatchFlow.Live
  let final, evs = advanceTicks fullMatchTicks ctx state
  // Use the engine's own JsonMatchDump
  JsonMatchDump.dumpMatch ctx.Home.Name ctx.Away.Name ctx.Home.Id ctx.Away.Id final.HomeScore final.AwayScore evs
  |> ignore

  evs

printfn "Running 10 matches (parallel)..."
let sw = System.Diagnostics.Stopwatch.StartNew()
let results = [| 1..10 |] |> Array.Parallel.map runMatch
sw.Stop()
printfn "Done in %ds\n" (sw.ElapsedMilliseconds / 1000L)

let allEvents = results |> Array.collect List.toArray

printfn "=== EVENTS BY TYPE ==="

allEvents
|> Array.groupBy (fun e -> string e.Type)
|> Array.map (fun (k, v) -> k, v.Length)
|> Array.sortByDescending snd
|> Array.iter (fun (name, count) -> printfn "  %-25s %d" name count)

printfn "\nTotal: %d events across 10 matches" allEvents.Length

printfn "\n=== SCORES ==="

results
|> Array.iteri (fun i evs ->
  let goals = evs |> List.filter (fun e -> e.Type = MatchEventType.Goal)
  printfn "  Match %d: %d goals" (i + 1) goals.Length)

printfn "\n=== JSON files in diag/ ==="

if Directory.Exists "diag" then
  for f in DirectoryInfo("diag").GetFiles("match_*.json") do
    let doc = JsonDocument.Parse(File.ReadAllText f.FullName)
    let r = doc.RootElement

    printfn
      "  %s: %s %d-%d (%d events)"
      f.Name
      (r.GetProperty("home").GetString())
      (r.GetProperty("homeScore").GetInt32())
      (r.GetProperty("awayScore").GetInt32())
      (r.GetProperty("events").GetArrayLength())
else
  printfn "  (no diag/ directory)"
