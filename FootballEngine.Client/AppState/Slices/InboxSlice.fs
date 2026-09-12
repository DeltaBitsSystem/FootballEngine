namespace FootballEngine

open System
open FootballEngine.Domain
open FootballEngine.AppTypes
open AppMsgs

module InboxSlice =

    let update (msg: InboxMsg) (state: InboxState) : InboxState =
        match msg with
        | InboxMsg.SelectMessage id ->
            { state with
                SelectedMessageId = Some id }
        | InboxMsg.MarkAsRead _
        | InboxMsg.MarkActionTaken _ -> state
        | InboxMsg.SetRequiresActionOnly value ->
            { state with
                RequiresActionOnly = value }
        | InboxMsg.SetUnreadOnly value -> { state with UnreadOnly = value }
        | InboxMsg.SetCategoryFilter category -> { state with CategoryFilter = category }
        | InboxMsg.ClearSelection -> { state with SelectedMessageId = None }

    module Query =

        let private matchesCategory category message =
            match category with
            | AllInboxCategories -> true
            | InboxCategory selected -> message.Category = selected

        let filteredMessages (gs: GameState) (state: InboxState) : InboxMessage list =
            gs.Inbox
            |> List.filter (fun message ->
                (not state.RequiresActionOnly
                 || (message.RequiresAction && message.ActionTaken <> Some true))
                && (not state.UnreadOnly || not message.IsRead)
                && matchesCategory state.CategoryFilter message)
            |> List.sortByDescending (fun message -> message.Date, message.Id)

        let messageList (gs: GameState) : InboxMessage list =
            gs.Inbox |> List.sortByDescending (fun message -> message.Date, message.Id)

        type MessageGroup =
            { Representative: InboxMessage
              Messages: InboxMessage list }

        let groupRoutineMessages (messages: InboxMessage list) : MessageGroup list =
            messages
            |> List.groupBy (fun message ->
                if message.RequiresAction then
                    Choice1Of2 message.Id
                else
                    Choice2Of2(message.Date.Date, message.From, message.Subject, message.Category))
            |> List.map (fun (_, grouped) ->
                { Representative = grouped |> List.maxBy (fun message -> message.Date, message.Id)
                  Messages = grouped |> List.sortByDescending (fun message -> message.Date, message.Id) })
            |> List.sortByDescending (fun group -> group.Representative.Date, group.Representative.Id)

        let selectedMessage (gs: GameState) (state: InboxState) : InboxMessage option =
            state.SelectedMessageId
            |> Option.bind (fun id -> gs.Inbox |> List.tryFind (fun m -> m.Id = id))

        let relativeDate (date: DateTime) (gs: GameState) : string =
            Formatters.relativeDate date gs.CurrentDate
