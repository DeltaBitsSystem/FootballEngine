namespace FootballEngine.Domain

open System

type InboxMessageCategory =
    | Development
    | Transfer
    | BoardUpdate
    | MatchReport
    | InjuryMessage
    | Contract

type InboxMessage =
    { Id: int
      Date: DateTime
      From: string
      Subject: string
      Body: string
      Category: InboxMessageCategory
      IsRead: bool
      RequiresAction: bool
      ActionTaken: bool option }

module Inbox =
    let create
        (date: DateTime)
        (from: string)
        (subject: string)
        (body: string)
        (category: InboxMessageCategory)
        (requiresAction: bool)
        =
        { Id = 0
          Date = date
          From = from
          Subject = subject
          Body = body
          Category = category
          IsRead = false
          RequiresAction = requiresAction
          ActionTaken = None }

    let markAsRead (messageId: int) (messages: InboxMessage list) : InboxMessage list =
        messages
        |> List.map (fun m -> if m.Id = messageId then { m with IsRead = true } else m)

    let markAsActionTaken (messageId: int) (messages: InboxMessage list) : InboxMessage list =
        messages
        |> List.map (fun m ->
            if m.Id = messageId then
                { m with ActionTaken = Some true }
            else
                m)

    let unreadCount (messages: InboxMessage list) : int =
        messages |> List.filter (fun m -> not m.IsRead) |> List.length

    let pendingActionCount (messages: InboxMessage list) : int =
        messages
        |> List.filter (fun m -> m.RequiresAction && (m.ActionTaken.IsNone || m.ActionTaken = Some false))
        |> List.length
