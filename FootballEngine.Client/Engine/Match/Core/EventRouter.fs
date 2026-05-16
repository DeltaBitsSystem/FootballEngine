namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types

[<Struct>]
type ActivationSet =
    { RunCognition: bool
      RunAction: bool
      RunReferee: bool
      RunTeam: bool
      RunManager: bool }

module EventRouter =

    let route (events: ResizeArray<SemanticEvent>) (subTick: int) (clock: SimulationClock) : ActivationSet =

        let mutable cognitionTriggered = false
        let mutable actionTriggered = false
        let mutable runReferee = false
        let mutable runTeam = false
        let mutable runManager = false

        for i = 0 to events.Count - 1 do
            match events[i] with
            | BallSecured _ ->
                cognitionTriggered <- true
                actionTriggered <- true
                runTeam <- true
            | BallLost _ ->
                cognitionTriggered <- true
                runTeam <- true
            | BallLoose ->
                cognitionTriggered <- true
                runReferee <- true
                runTeam <- true
            | FoulOccurred _ -> runReferee <- true
            | GoalScored _ -> runReferee <- true
            | SetPieceAwarded _ -> runTeam <- true
            | RedCardIssued _ -> runManager <- true
            | MomentumShifted _ -> runManager <- true
            | PlayerConditionCritical _ -> runManager <- true
            | _ -> ()

        let periodic = subTick % (clock.SubTicksPerSecond / 2) = 0

        { RunCognition = cognitionTriggered || periodic
          RunAction = actionTriggered || periodic
          RunReferee = runReferee
          RunTeam = runTeam
          RunManager = runManager }
