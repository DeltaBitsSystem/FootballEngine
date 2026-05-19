namespace FootballEngine.Pages

open System
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.AppMsgs
open FootballEngine.Domain
open FootballEngine.Components
open FootballEngine.Icons

module InboxPresenter =
    let categoryIcon (category: InboxMessageCategory) =
        match category with
        | Development -> IconName.school
        | Transfer -> ClubIcon.transfer
        | BoardUpdate -> IconName.bullhorn
        | MatchReport -> MatchEvent.goal
        | InjuryMessage -> PlayerIcon.injured
        | Contract -> IconName.fileDocumentEdit

    let categoryColor (category: InboxMessageCategory) =
        match category with
        | Development -> Theme.Accent
        | Transfer -> Theme.Success
        | BoardUpdate -> Theme.Warning
        | MatchReport -> Theme.AccentAlt
        | InjuryMessage -> Theme.Danger
        | Contract -> Theme.TextMuted

    let categoryLabel (category: InboxMessageCategory) =
        match category with
        | Development -> "DEVELOPMENT"
        | Transfer -> "TRANSFER"
        | BoardUpdate -> "BOARD"
        | MatchReport -> "MATCH"
        | InjuryMessage -> "INJURY"
        | Contract -> "CONTRACT"

    let getMessageList (state: State) =
        match state.Mode with
        | InGame(gs, _) -> gs.Inbox |> List.sortByDescending (fun m -> m.Date)
        | _ -> []

    let getSelectedMessage (state: State) =
        match state.Mode with
        | InGame(gs, _) ->
            match state.Inbox.SelectedMessageId with
            | Some id -> gs.Inbox |> List.tryFind (fun m -> m.Id = id)
            | None -> None
        | _ -> None

module Inbox =

    let private messageListItem (message: InboxMessage) (isSelected: bool) (currentDate: DateTime) dispatch =
        let categoryColor = InboxPresenter.categoryColor message.Category
        let categoryIcon = InboxPresenter.categoryIcon message.Category
        let dateStr = Formatters.relativeDate message.Date currentDate

        let hasAction =
            message.RequiresAction
            && (message.ActionTaken.IsNone || message.ActionTaken = Some false)

        Border.create
            [ Border.background (SelectionStyle.rowBgColor isSelected categoryColor)
              Border.borderBrush (SelectionStyle.rowBorderColor isSelected (categoryColor + "60"))
              Border.borderThickness (2.0, 0.0, 0.0, 1.0)
              Border.cursor Avalonia.Input.Cursor.Default
              Border.onPointerReleased (fun _ -> dispatch (InboxMsg(InboxMsg.SelectMessage message.Id)))
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions (ColumnDefs.toGridString ColumnDefs.inboxListCols)
                        Grid.rowDefinitions "Auto, Auto"
                        Grid.margin (12.0, 10.0)
                        Grid.children
                            [ Border.create
                                  [ Grid.column 0
                                    Grid.rowSpan 2
                                    Border.background (categoryColor + "20")
                                    Border.cornerRadius 8.0
                                    Border.padding 8.0
                                    Border.margin (0.0, 0.0, 10.0, 0.0)
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child (Icons.iconSm categoryIcon categoryColor) ]

                              StackPanel.create
                                  [ Grid.column 1
                                    Grid.row 0
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text message.Subject
                                                TextBlock.fontSize 12.0
                                                TextBlock.fontWeight (
                                                    if not message.IsRead then
                                                        FontWeight.SemiBold
                                                    else
                                                        FontWeight.Normal
                                                )
                                                TextBlock.foreground (
                                                    if not message.IsRead then Theme.TextMain else Theme.TextSub
                                                )
                                                TextBlock.textTrimming TextTrimming.CharacterEllipsis ]
                                          if hasAction then
                                              Border.create
                                                  [ Border.background (Theme.Warning + "25")
                                                    Border.borderBrush (Theme.Warning + "60")
                                                    Border.borderThickness 1.0
                                                    Border.cornerRadius 3.0
                                                    Border.padding (5.0, 1.0)
                                                    Border.child (
                                                        TextBlock.create
                                                            [ TextBlock.text "ACTION"
                                                              TextBlock.fontSize 8.0
                                                              TextBlock.fontWeight FontWeight.Bold
                                                              TextBlock.foreground Theme.Warning ]
                                                    ) ]
                                              :> IView
                                          else
                                              TextBlock.create [ TextBlock.text "" ] :> IView ] ]

                              StackPanel.create
                                  [ Grid.column 2
                                    Grid.row 0
                                    StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ if not message.IsRead then
                                              Border.create
                                                  [ Border.width 7.0
                                                    Border.height 7.0
                                                    Border.cornerRadius 3.5
                                                    Border.background categoryColor
                                                    Border.verticalAlignment VerticalAlignment.Center ]
                                              :> IView
                                          else
                                              TextBlock.create [ TextBlock.text "" ] :> IView
                                          TextBlock.create
                                              [ TextBlock.text dateStr
                                                TextBlock.fontSize 10.0
                                                TextBlock.foreground Theme.TextMuted
                                                TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]

                              TextBlock.create
                                  [ Grid.column 1
                                    Grid.row 1
                                    Grid.columnSpan 2
                                    TextBlock.text message.Body
                                    TextBlock.fontSize 10.0
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                    TextBlock.margin (0.0, 3.0, 0.0, 0.0) ] ] ]
              ) ]
        |> View.withKey $"inbox-{message.Id}"
        :> IView

    let private messageList (messages: InboxMessage list) (selectedId: int option) (currentDate: DateTime) dispatch =
        let unreadCount = messages |> List.filter (fun m -> not m.IsRead) |> List.length

        let header =
            Border.create
                [ Border.padding (14.0, 11.0)
                  Border.borderBrush Theme.Border

                  Border.child (
                      Grid.create
                          [ Grid.columnDefinitions "*, Auto"
                            Grid.children
                                [ StackPanel.create
                                      [ StackPanel.orientation Orientation.Horizontal
                                        StackPanel.spacing 6.0
                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                        StackPanel.children
                                            [ Icons.iconSm IconName.inbox Theme.Accent
                                              TextBlock.create
                                                  [ TextBlock.text "INBOX"
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontWeight FontWeight.Black
                                                    TextBlock.foreground Theme.TextMuted
                                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                                  if unreadCount > 0 then
                                      Border.create
                                          [ Grid.column 1
                                            Border.background Theme.Accent


                                            Border.verticalAlignment VerticalAlignment.Center
                                            Border.child (
                                                TextBlock.create
                                                    [ TextBlock.text $"{unreadCount} unread"
                                                      TextBlock.fontSize 9.0
                                                      TextBlock.fontWeight FontWeight.Bold
                                                      TextBlock.foreground Theme.BgSidebar ]
                                            ) ]
                                      :> IView
                                  else
                                      TextBlock.create [ Grid.column 1; TextBlock.text "" ] :> IView ] ]
                  ) ]
            :> IView

        let body =
            if messages.IsEmpty then
                Border.create
                    [ Border.child (
                          StackPanel.create
                              [ StackPanel.horizontalAlignment HorizontalAlignment.Center
                                StackPanel.children
                                    [ Icons.icon IconName.inboxArrowDown 36.0 (Theme.TextMuted + "80")
                                      TextBlock.create
                                          [ TextBlock.text "No messages yet"
                                            TextBlock.fontSize 13.0
                                            TextBlock.foreground Theme.TextMuted
                                            TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                      TextBlock.create
                                          [ TextBlock.text "Messages will appear here as\nyour season progresses"
                                            TextBlock.fontSize 10.0
                                            TextBlock.foreground (Theme.TextMuted + "80")
                                            TextBlock.textAlignment TextAlignment.Center
                                            TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
                      ) ]
                :> IView
            else
                ScrollViewer.create
                    [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                      ScrollViewer.content (
                          StackPanel.create
                              [ StackPanel.children
                                    [ for msg in messages do
                                          messageListItem msg (selectedId = Some msg.Id) currentDate dispatch ] ]
                      ) ]
                :> IView

        UI.panelCard header body

    let private messageDetail (message: InboxMessage option) (currentDate: DateTime) dispatch =
        match message with
        | None ->
            Border.create
                [ Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.cornerRadius 10.0
                  Border.child (
                      StackPanel.create
                          [ StackPanel.spacing 12.0
                            StackPanel.horizontalAlignment HorizontalAlignment.Center
                            StackPanel.verticalAlignment VerticalAlignment.Center
                            StackPanel.children
                                [ Icons.icon IconName.mailOpen 44.0 (Theme.TextMuted + "60")
                                  TextBlock.create
                                      [ TextBlock.text "Select a message"
                                        TextBlock.fontSize 14.0
                                        TextBlock.fontWeight FontWeight.SemiBold
                                        TextBlock.foreground Theme.TextMuted
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center ]
                                  TextBlock.create
                                      [ TextBlock.text "Choose a message from the list to read it"
                                        TextBlock.fontSize 11.0
                                        TextBlock.foreground (Theme.TextMuted + "80")
                                        TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
                  ) ]
            :> IView

        | Some msg ->
            let categoryColor = InboxPresenter.categoryColor msg.Category
            let categoryIcon = InboxPresenter.categoryIcon msg.Category
            let categoryLabel = InboxPresenter.categoryLabel msg.Category
            let dateStr = Formatters.relativeDate msg.Date currentDate

            let hasAction =
                msg.RequiresAction && (msg.ActionTaken.IsNone || msg.ActionTaken = Some false)

            Border.create
                [ Border.background Theme.BgCard
                  Border.borderBrush Theme.Border
                  Border.borderThickness 1.0
                  Border.cornerRadius 10.0
                  Border.clipToBounds true
                  Border.child (
                      DockPanel.create
                          [ DockPanel.lastChildFill true
                            DockPanel.children
                                [ Border.create
                                      [ DockPanel.dock Dock.Top
                                        Border.padding (20.0, 16.0)
                                        Border.borderBrush Theme.Border
                                        Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                        Border.child (
                                            StackPanel.create
                                                [ StackPanel.spacing 10.0
                                                  StackPanel.children
                                                      [ Border.create
                                                            [ Border.background (categoryColor + "20")
                                                              Border.cornerRadius 3.0
                                                              Border.padding (6.0, 2.0)
                                                              Border.horizontalAlignment HorizontalAlignment.Left
                                                              Border.child (
                                                                  TextBlock.create
                                                                      [ TextBlock.text categoryLabel
                                                                        TextBlock.fontSize 9.0
                                                                        TextBlock.fontWeight FontWeight.Bold
                                                                        TextBlock.foreground categoryColor ]
                                                              ) ]
                                                        TextBlock.create
                                                            [ TextBlock.text msg.Subject
                                                              TextBlock.fontSize 16.0
                                                              TextBlock.fontWeight FontWeight.Bold
                                                              TextBlock.foreground Theme.TextMain
                                                              TextBlock.textWrapping TextWrapping.Wrap ]
                                                        Grid.create
                                                            [ Grid.columnDefinitions "*, Auto"
                                                              Grid.children
                                                                  [ StackPanel.create
                                                                        [ StackPanel.orientation Orientation.Horizontal
                                                                          StackPanel.spacing 10.0
                                                                          StackPanel.verticalAlignment
                                                                              VerticalAlignment.Center
                                                                          StackPanel.children
                                                                              [ Icons.iconSm categoryIcon categoryColor
                                                                                TextBlock.create
                                                                                    [ TextBlock.text msg.From
                                                                                      TextBlock.fontSize 11.0
                                                                                      TextBlock.fontWeight
                                                                                          FontWeight.SemiBold
                                                                                      TextBlock.foreground Theme.TextSub ]
                                                                                TextBlock.create
                                                                                    [ TextBlock.text "·"
                                                                                      TextBlock.fontSize 11.0
                                                                                      TextBlock.foreground
                                                                                          Theme.TextMuted ]
                                                                                TextBlock.create
                                                                                    [ TextBlock.text dateStr
                                                                                      TextBlock.fontSize 11.0
                                                                                      TextBlock.foreground
                                                                                          Theme.TextMuted ] ] ]
                                                                    if hasAction then
                                                                        Button.create
                                                                            [ Grid.column 1
                                                                              Button.background Theme.Success
                                                                              Button.foreground Theme.BgSidebar
                                                                              Button.borderThickness 0.0
                                                                              Button.padding (14.0, 7.0)
                                                                              Button.cornerRadius 6.0
                                                                              Button.fontSize 11.0
                                                                              Button.fontWeight FontWeight.SemiBold
                                                                              Button.content "Mark as Done"
                                                                              Button.onClick (fun _ ->
                                                                                  dispatch (
                                                                                      InboxMsg(MarkActionTaken msg.Id)
                                                                                  )) ]
                                                                        :> IView
                                                                    else
                                                                        TextBlock.create
                                                                            [ Grid.column 1; TextBlock.text "" ]
                                                                        :> IView ] ] ] ]
                                        ) ]

                                  ScrollViewer.create
                                      [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                                        ScrollViewer.content (
                                            Border.create
                                                [ Border.padding (20.0, 16.0)
                                                  Border.child (
                                                      TextBlock.create
                                                          [ TextBlock.text msg.Body
                                                            TextBlock.fontSize 12.0
                                                            TextBlock.lineHeight 20.0
                                                            TextBlock.foreground Theme.TextMain
                                                            TextBlock.textWrapping TextWrapping.Wrap ]
                                                  ) ]
                                        ) ] ] ]
                  ) ]
            :> IView

    let inboxView (state: State) dispatch : IView =
        let messages = InboxPresenter.getMessageList state
        let selectedMessage = InboxPresenter.getSelectedMessage state

        let currentDate =
            match state.Mode with
            | InGame(gs, _) -> gs.CurrentDate
            | _ -> DateTime.Now

        Grid.create
            [ Grid.columnDefinitions "320, *"
              Grid.children
                  [ Border.create
                        [ Grid.column 0
                          Border.borderBrush Theme.Border
                          Border.borderThickness (0.0, 0.0, 1.0, 0.0)
                          Border.child (messageList messages state.Inbox.SelectedMessageId currentDate dispatch) ]

                    Border.create
                        [ Grid.column 1
                          Border.padding (16.0, 16.0)
                          Border.child (messageDetail selectedMessage currentDate dispatch) ] ] ]
