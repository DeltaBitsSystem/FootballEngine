namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.AppTypes

module PlayerPresenter =

    let positionSortKey (pos: Position) : int =
        match pos with
        | GK                        -> 0
        | DR | DC | DL | WBR | WBL -> 1
        | DM | MC | MR | ML        -> 2
        | AMR | AMC | AML          -> 3
        | ST                       -> 4

    let positionGroup (pos: Position) : int * string =
        match pos with
        | GK                        -> 0, "GOALKEEPERS"
        | DR | DC | DL | WBR | WBL -> 1, "DEFENDERS"
        | DM | MC | MR | ML        -> 2, "MIDFIELDERS"
        | AMR | AMC | AML          -> 3, "ATTACKING MID"
        | ST                       -> 4, "ATTACKERS"

    let fitnessColor (v: int) : string =
        if v >= 80  then Theme.Success
        elif v >= 55 then Theme.Warning
        else             Theme.Danger

    let sortBy (field: SortField) (currentDate: System.DateTime) (players: Player list) : Player list =
        match field with
        | ByName     -> players |> List.sortBy _.Name
        | ByCA       -> players |> List.sortByDescending _.CurrentSkill
        | ByAge      -> players |> List.sortBy (Formatters.age currentDate)
        | ByValue    -> players |> List.sortByDescending (fun p -> Player.playerValue p.CurrentSkill)
        | ByPosition -> players |> List.sortBy (fun p -> positionSortKey p.Position, -p.CurrentSkill)

    let groupBy (field: SortField) (currentDate: System.DateTime) (players: Player list)
        : (string option * Player list) list =
        match field with
        | ByPosition ->
            players
            |> List.groupBy (fun p -> positionGroup p.Position)
            |> List.sortBy  (fun ((key, _), _) -> key)
            |> List.map     (fun ((_, label), ps) ->
                Some label, ps |> List.sortByDescending _.CurrentSkill)
        | other ->
            [ None, sortBy other currentDate players ]
