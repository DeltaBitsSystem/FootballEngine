#r "../../FootballEngine.Core/bin/Release/net10.0/FootballEngine.Core.dll"
#load "../../FootballEngine.Core.Tests/Infrastructure/TestBuilders.fs"

open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

let unwrap =
  function
  | Ok value -> value
  | Error error -> failwithf "%A" error

let input =
  { MatchId = 11UL
    Context = TestBuilders.defaultCtx ()
    Seed = 11UL
    EffectiveConfig = EffectiveConfig.defaultConfig
    EngineVersion = "restart-readiness-probe" }

let session =
  MatchSession.create Scheduler.systems input MatchRunMode.Fast |> unwrap

printfn "Resolved input identity: %d" (MatchJob.inputHash input)
MatchSession.advanceTo 160000<subtick> session |> unwrap |> ignore

match session.World.State.Flow with
| MatchFlow.Restart restart ->
  printfn "Restart %A phase %A award tick %d" restart.Id restart.Phase (int restart.AwardTick)

  match restart.Plan with
  | Some plan ->
    printfn "Validation: %A" (RestartRules.validate input.Context session.World plan)

    for target in plan.Targets do
      match MatchWorld.slotOf input.Context target.Side target.Player with
      | Some slot ->
        let frame = (MatchWorld.team session.World target.Side).Frame

        printfn
          "%A %A actual=(%.3f,%.3f) target=(%.3f,%.3f) velocity=(%.3f,%.3f)"
          target.Player
          target.Role
          (float frame.PosX[slot])
          (float frame.PosY[slot])
          (float target.Position.X)
          (float target.Position.Y)
          (float frame.VelX[slot])
          (float frame.VelY[slot])
      | None -> ()
  | None -> printfn "No plan"
| flow -> printfn "Flow: %A" flow
