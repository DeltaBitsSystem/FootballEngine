namespace Training

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Simulation
open FootballEngine.Types

type SimResult =
    { HomeScore: int
      AwayScore: int
      Events: MatchEvent list
      FinalState: SimState
      HomeId: ClubId
      AwayId: ClubId }

module SimulatorRunner =

    let runOneMatch
        (config: BalanceConfig)
        (home: Club)
        (away: Club)
        (players: Map<PlayerId, Player>)
        (staff: Map<StaffId, Staff>)
        (profileMap: Map<PlayerId, BehavioralProfile>)
        : Result<SimResult, string> =
        match MatchSimulator.trySimulateMatchFull config home away players staff profileMap with
        | Ok replay ->
            Ok
                { HomeScore = replay.Final.HomeScore
                  AwayScore = replay.Final.AwayScore
                  Events = replay.Events
                  FinalState = replay.Final
                  HomeId = home.Id
                  AwayId = away.Id }
        | Error err -> Error(sprintf "Simulation error: %A" err)

    let runBatch
        (config: BalanceConfig)
        (n: int)
        (home: Club)
        (away: Club)
        (players: Map<PlayerId, Player>)
        (staff: Map<StaffId, Staff>)
        (profileMap: Map<PlayerId, BehavioralProfile>)
        : SimResult list =
        Array.Parallel.init n (fun _ -> runOneMatch config home away players staff profileMap)
        |> Array.choose (function
            | Ok r -> Some r
            | Error _ -> None)
        |> Array.toList
