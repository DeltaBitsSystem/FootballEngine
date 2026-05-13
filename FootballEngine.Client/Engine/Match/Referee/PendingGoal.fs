namespace FootballEngine.Referee

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types

type ActionResult =
    { Events: MatchEvent list
      PendingRefereeActions: RefereeAction list
      Outputs: SystemOutput list }

module ActionResult =
    let empty =
        { Events = []
          PendingRefereeActions = []
          Outputs = [] }

    let ofEvents events =
        { Events = events
          PendingRefereeActions = []
          Outputs = [] }

    let combine (results: ActionResult list) =
        { Events = results |> List.collect _.Events
          PendingRefereeActions = results |> List.collect _.PendingRefereeActions
          Outputs = results |> List.collect _.Outputs }

    let withOutputs events outputs =
        { Events = events
          PendingRefereeActions = []
          Outputs = outputs }
