namespace FootballEngine

open System
open FootballEngine.Domain
open FootballEngine.AppTypes
open AppMsgs

module InboxSlice =

    let update (msg: InboxMsg) (state: InboxState) : InboxState =
        match msg with
        | InboxMsg.SelectMessage id  -> { state with SelectedMessageId = Some id }
        | InboxMsg.MarkAsRead _
        | InboxMsg.MarkActionTaken _ -> state

    module Query =

        let messageList (gs: GameState) : InboxMessage list =
            gs.Inbox |> List.sortByDescending _.Date

        let selectedMessage (gs: GameState) (state: InboxState) : InboxMessage option =
            state.SelectedMessageId |> Option.bind (fun id -> gs.Inbox |> List.tryFind (fun m -> m.Id = id))

        let relativeDate (date: DateTime) (gs: GameState) : string =
            Formatters.relativeDate date gs.CurrentDate
