namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.AppTypes
open AppMsgs

module TrainingSlice =

    let update (msg: TrainingMsg) (state: TrainingState) : TrainingState =
        match msg with
        | TrainingMsg.SelectPlayer id -> { state with SelectedPlayer = Some id }
        | TrainingMsg.ClearSelection  -> { state with SelectedPlayer = None }
        | TrainingMsg.SetFocus _
        | TrainingMsg.SetIntensity _  -> state

    module Query =

        let sortedSquad (gs: GameState) : Player list =
            GameState.getUserSquad gs
            |> List.sortBy (fun p -> PlayerPresenter.positionSortKey p.Position, -p.CurrentSkill)

        let selectedPlayer (gs: GameState) (state: TrainingState) : Player option =
            state.SelectedPlayer |> Option.bind (fun id -> gs.Players |> Map.tryFind id)
