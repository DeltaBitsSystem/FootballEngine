namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.AppTypes
open AppMsgs

module TacticsSlice =

    let update (msg: TacticsMsg) (state: TacticsState) : TacticsState =
        match msg with
        | TacticsMsg.StartDrag id   -> { state with DraggedPlayer = Some id }
        | TacticsMsg.EndDrag        -> { state with DraggedPlayer = None }
        | TacticsMsg.SetFormation _
        | TacticsMsg.SetTeamTactics _
        | TacticsMsg.SetInstruction _
        | TacticsMsg.DropPlayerInSlot _ -> state

    module Query =

        let currentLineup (gs: GameState) : Lineup option =
            GameState.headCoach gs.UserClubId gs
            |> Option.bind (fun coach -> coach.Attributes.Coaching.Lineup)

        let benchPlayers (gs: GameState) : Player list =
            let lineup = currentLineup gs
            let starterIds =
                lineup
                |> Option.map (fun l -> l.Slots |> List.choose _.PlayerId |> Set.ofList)
                |> Option.defaultValue Set.empty
            GameState.getUserSquad gs
            |> List.filter (fun p -> not (starterIds.Contains p.Id))
            |> List.sortBy (fun p -> PlayerPresenter.positionSortKey p.Position, -p.CurrentSkill)

        let starterAverages (gs: GameState) : {| Fitness: int; Skill: int; Morale: int |} =
            let lineup = currentLineup gs
            let starters =
                lineup
                |> Option.map (fun l -> l.Slots |> List.choose _.PlayerId)
                |> Option.defaultValue []
                |> List.choose gs.Players.TryFind
            let avg f =
                if starters.IsEmpty then 0
                else starters |> List.averageBy (fun p -> float (f p)) |> int
            {| Fitness = avg _.MatchFitness
               Skill   = avg _.CurrentSkill
               Morale  = avg _.Morale |}
