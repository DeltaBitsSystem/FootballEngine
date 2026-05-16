namespace FootballEngine.Referee

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types

type ActionResult =
    { Events: DomainEvent[] }

module ActionResult =
    let empty = { Events = [||] }

    let ofEvents (evs: DomainEvent list) =
        { Events = Array.ofList evs }

    let combine (results: ActionResult list) =
        { Events = results |> List.toArray |> Array.collect _.Events }
