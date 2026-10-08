// Uses the existing archive codec headlessly; loading Client does not start Avalonia or a renderer.
#r "../../FootballEngine.Core/bin/Debug/net10.0/FootballEngine.Core.dll"
#r "../../FootballEngine.Client/bin/Debug/net10.0/FootballEngine.Client.dll"
#load "../../FootballEngine.Core.Tests/Infrastructure/TestBuilders.fs"

open System.IO
open FootballEngine
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

let unwrap =
  function
  | Ok value -> value
  | Error error -> failwithf "headless archive capture failed: %A" error

let path =
  fsi.CommandLineArgs
  |> Array.tryItem 1
  |> Option.defaultValue "/tmp/opencode/match-current.json"

let input =
  { MatchId = 11UL
    Context = TestBuilders.defaultCtx ()
    Seed = 11UL
    EffectiveConfig = EffectiveConfig.defaultConfig
    EngineVersion = "headless-capture-2026-10-05" }

let session =
  MatchSession.create Scheduler.systems input MatchRunMode.Fast |> unwrap

MatchSession.advanceTo 4000<subtick> session |> unwrap |> ignore

session
|> MatchObservationArchive.capture
|> MatchObservationArchive.encode
|> fun json -> File.WriteAllText(path, json)

printfn
  "Captured fixture match %d seed %d at frontier %d into %s (no GUI)."
  input.MatchId
  input.Seed
  (int session.World.State.Tick)
  path
