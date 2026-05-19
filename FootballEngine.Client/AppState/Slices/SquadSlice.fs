namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.AppTypes
open AppMsgs

module SquadSlice =

    let update (msg: SquadMsg) (state: SquadState) : SquadState =
        match msg with
        | SquadMsg.SetSort f       -> { state with SortBy = f }
        | SquadMsg.SelectPlayer id -> { state with SelectedPlayer = Some id }
        | SquadMsg.ClearSelection  -> { state with SelectedPlayer = None }
        | SquadMsg.StartDrag id    -> { state with DraggedPlayer = Some id }
        | SquadMsg.EndDrag         -> { state with DraggedPlayer = None }

    type TeamStatsVM = {
        TotalValue: string
        AvgSkill:   string
        TotalWages: string
        SquadSize:  string
    }

    module Query =

        let teamStats (gs: GameState) : TeamStatsVM =
            let players = GameState.getUserSquad gs
            let totalValue = players |> List.sumBy (fun p -> Player.playerValue p.CurrentSkill)
            let totalWages =
                players |> List.sumBy (fun p ->
                    Player.contractOf p |> Option.map _.Salary |> Option.defaultValue 0m)
            let avgSkill =
                if players.IsEmpty then 0.0
                else players |> List.averageBy (fun p -> float p.CurrentSkill)
            { TotalValue = Formatters.money totalValue
              AvgSkill   = $"%.0f{avgSkill}"
              TotalWages = Formatters.salary totalWages
              SquadSize  = string players.Length }

        let groupedPlayers (gs: GameState) (state: SquadState) =
            GameState.getUserSquad gs
            |> PlayerPresenter.groupBy state.SortBy gs.CurrentDate

        let selectedPlayer (gs: GameState) (state: SquadState) : Player option =
            state.SelectedPlayer |> Option.bind (fun id -> gs.Players |> Map.tryFind id)
