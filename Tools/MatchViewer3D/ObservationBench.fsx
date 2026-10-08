// Run after a Release Core build; capture and text formatting are measured separately.
#r "../../FootballEngine.Core/bin/Release/net10.0/FootballEngine.Core.dll"
#load "../../FootballEngine.Core.Tests/Infrastructure/TestBuilders.fs"

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

let unwrap =
  function
  | Ok value -> value
  | Error error -> failwithf "benchmark failed: %A" error

let tickLimit =
  match Int32.TryParse(Environment.GetEnvironmentVariable "FOOTBALL_OBSERVATION_BENCH_TICKS") with
  | true, value when value > 0 && value <= int MatchSession.maxTicks -> value
  | _ -> 12000

let repetitions =
  match Int32.TryParse(Environment.GetEnvironmentVariable "FOOTBALL_OBSERVATION_BENCH_REPETITIONS") with
  | true, value when value >= 3 -> value
  | _ -> 12

let run seed enabled publish selective =
  let input =
    { MatchId = seed
      Context = TestBuilders.defaultCtx ()
      Seed = seed
      EffectiveConfig = EffectiveConfig.defaultConfig
      EngineVersion = "observation-bench" }

  let session =
    MatchSession.create Scheduler.systems input MatchRunMode.Fast |> unwrap

  session.World.Observation.Enabled <- enabled

  if selective then
    session.World.Observation.SelectDetail(
      Some
        { Actor = input.Context.HomeLineup.Profiles[9].Id
          FromTick = 0<subtick>
          UntilTick = tickLimit * 1<subtick> }
    )
    |> unwrap

  let ticks = tickLimit
  let samples = Array.zeroCreate<double> ticks
  let bytes = GC.GetAllocatedBytesForCurrentThread()

  let collections =
    [| for generation = 0 to 2 do
         GC.CollectionCount generation |]

  let totalStart = Stopwatch.GetTimestamp()
  let mutable published = 0UL
  let mutable tick = 0

  while tick < ticks && session.World.State.Flow <> MatchFlow.MatchEnded do
    tick <- tick + 1
    let start = Stopwatch.GetTimestamp()
    MatchSession.advanceTo (tick * 1<subtick>) session |> unwrap |> ignore

    if publish && tick % 10 = 0 && published < session.World.Observation.Total then
      let delta = session.World.Observation.SnapshotSince published
      published <- delta.Total

    samples[tick - 1] <- float (Stopwatch.GetTimestamp() - start) / float Stopwatch.Frequency * 1000000.0

  let elapsed =
    float (Stopwatch.GetTimestamp() - totalStart) / float Stopwatch.Frequency
    * 1000.0

  let allocated = GC.GetAllocatedBytesForCurrentThread() - bytes
  let measured = samples[.. tick - 1]
  Array.sortInPlace measured
  let snapshot = MatchSession.observations session

  if selective then
    File.WriteAllText(
      Path.Combine("/tmp/opencode", sprintf "observation-seed-%d.json" seed),
      Observation.exportJson snapshot
    )

  let percentile fraction =
    measured[min (tick - 1) (int (float tick * fraction))]

  {| Seed = seed
     Enabled = enabled
     Publish = publish
     Selective = selective
     Ticks = tick
     Milliseconds = elapsed
     P50Microseconds = percentile 0.5
     P95Microseconds = percentile 0.95
     P99Microseconds = percentile 0.99
     Collections =
      [| for generation = 0 to 2 do
           GC.CollectionCount generation - collections[generation] |]
     AllocatedBytes = allocated
     Retained = snapshot.Entries.Length
     Evicted = snapshot.Evicted
     Unlinked = snapshot.Unlinked
     Total = snapshot.Total
     HomeScore = session.World.State.HomeScore
     AwayScore = session.World.State.AwayScore
     HomeShots = session.World.Home.Stats.Shots
     AwayShots = session.World.Away.Stats.Shots
     FinalFlow = sprintf "%A" session.World.State.Flow
     EventsHash = MatchOutcomeHash.eventsHash (List.ofSeq session.Events) |}

run 8UL false false false |> ignore
run 8UL true true true |> ignore

let rows =
  [| for repetition = 0 to repetitions - 1 do
       let seed = uint64 (11 + repetition % 3)

       let modes =
         [ false, false, false; true, false, false; true, true, false; true, true, true ]

       for enabled, publish, selective in (if repetition % 2 = 0 then modes else List.rev modes) do
         yield run seed enabled publish selective |]

for seed in [ 11UL; 12UL; 13UL ] do
  let hashes =
    rows
    |> Array.filter (fun row -> row.Seed = seed)
    |> Array.map _.EventsHash
    |> Array.distinct

  if hashes.Length <> 1 then
    failwithf "observation changed simulation for seed %d" seed

let median values =
  let sorted = Array.sort values
  let middle = sorted.Length / 2

  if sorted.Length % 2 = 0 then
    (sorted[middle - 1] + sorted[middle]) / 2.0
  else
    sorted[middle]

let summary enabled publish selective =
  let selected =
    rows
    |> Array.filter (fun row -> row.Enabled = enabled && row.Publish = publish && row.Selective = selective)

  {| Enabled = enabled
     Publish = publish
     Selective = selective
     MedianMilliseconds = selected |> Array.map _.Milliseconds |> median
     MedianP99Microseconds = selected |> Array.map _.P99Microseconds |> median
     MedianAllocatedBytes = selected |> Array.map (fun row -> float row.AllocatedBytes) |> median |}

let paired =
  rows
  |> Array.chunkBySize 4
  |> Array.map (fun group ->
    let baseline = group |> Array.find (fun row -> not row.Enabled)
    let capture = group |> Array.find (fun row -> row.Enabled && not row.Publish)
    let publication = group |> Array.find (fun row -> row.Publish && not row.Selective)
    let selective = group |> Array.find _.Selective
    let percent before after = (after / before - 1.0) * 100.0

    {| Seed = baseline.Seed
       CapturePercent = percent baseline.Milliseconds capture.Milliseconds
       PublicationPercent = percent baseline.Milliseconds publication.Milliseconds
       SelectivePercent = percent baseline.Milliseconds selective.Milliseconds
       CaptureP99Percent = percent baseline.P99Microseconds capture.P99Microseconds
       PublicationP99Percent = percent baseline.P99Microseconds publication.P99Microseconds |})

let pairedSummary =
  {| MedianCapturePercent = paired |> Array.map _.CapturePercent |> median
     MedianPublicationPercent = paired |> Array.map _.PublicationPercent |> median
     MedianSelectivePercent = paired |> Array.map _.SelectivePercent |> median
     MedianCaptureP99Percent = paired |> Array.map _.CaptureP99Percent |> median
     MedianPublicationP99Percent = paired |> Array.map _.PublicationP99Percent |> median |}

let report =
  {| Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
     OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription
     ProcessorCount = Environment.ProcessorCount
     TickLimit = tickLimit
     Summary =
      [| summary false false false
         summary true false false
         summary true true false
         summary true true true |]
     PairedSummary = pairedSummary
     PairedRows = paired
     Rows = rows |}

let destination = Path.Combine("/tmp/opencode", "observation-benchmark.json")
File.WriteAllText(destination, JsonSerializer.Serialize(report, JsonSerializerOptions(WriteIndented = true)))
printfn "%s" (JsonSerializer.Serialize(report.Summary))
printfn "Same-seed paired overhead: %s" (JsonSerializer.Serialize(pairedSummary))
printfn "Report: %s" destination
