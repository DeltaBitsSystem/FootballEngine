namespace FootballEngine.Components

open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Domain
open FootballEngine.Icons
open FootballEngine.Routing
open Material.Icons

module Navigation =
    type NavItem =
        { Route: Route
          Label: TranslationKey
          Icon: MaterialIconKind }

    type NavSection =
        { Title: TranslationKey
          Items: NavItem list }

    let menuSections: NavSection list =
        [ { Title = TranslationKey.Today
            Items =
              [ { Route = Route.Home
                  Label = TranslationKey.Today
                  Icon = IconName.home }
                { Route = Route.Inbox(InboxRoute None)
                  Label = TranslationKey.Inbox
                  Icon = IconName.inbox } ] }
          { Title = TranslationKey.Squad
            Items =
              [ { Route = Route.Squad SquadRoute.All
                  Label = TranslationKey.Squad
                  Icon = IconName.squad }
                { Route = Route.Tactics TacticsRoute.Overview
                  Label = TranslationKey.Tactics
                  Icon = IconName.tactics }
                { Route = Route.Training TrainingRoute.Overview
                  Label = TranslationKey.Training
                  Icon = PlayerIcon.stamina }
                { Route = Route.Staff StaffRoute.AllStaff
                  Label = TranslationKey.Staff
                  Icon = ClubIcon.staff } ] }
          { Title = TranslationKey.Shortlist
            Items =
              [ { Route = Route.Scouting ScoutingRoute.Overview
                  Label = TranslationKey.Scouting
                  Icon = ClubIcon.scouting }
                { Route = Route.Transfers TransferRoute.Watchlist
                  Label = TranslationKey.Shortlist
                  Icon = IconName.filter }
                { Route = Route.Transfers TransferRoute.Negotiations
                  Label = TranslationKey.Negotiations
                  Icon = ClubIcon.transfer } ] }
          { Title = TranslationKey.Finances
            Items =
              [ { Route = Route.Finances FinanceRoute.Overview
                  Label = TranslationKey.Finances
                  Icon = ClubIcon.finances }
                { Route = Route.Board
                  Label = TranslationKey.Board
                  Icon = IconName.bullhorn }
                { Route = Route.Youth
                  Label = TranslationKey.Youth
                  Icon = IconName.school } ] }
          { Title = TranslationKey.Calendar
            Items =
              [ { Route = Route.Calendar
                  Label = TranslationKey.Calendar
                  Icon = IconName.calendar }
                { Route = Route.Competitions
                  Label = TranslationKey.Competitions
                  Icon = IconName.league }
                { Route = Route.News
                  Label = TranslationKey.News
                  Icon = IconName.bullhorn } ] }
          { Title = TranslationKey.Settings
            Items =
              [ { Route = Route.Settings SettingsRoute.Overview
                  Label = TranslationKey.Settings
                  Icon = IconName.settings } ] } ]

    let menuItems = menuSections |> List.collect _.Items

module Sidebar =
    let private sameDestination (left: Route) (right: Route) =
        match left, right with
        | Route.Home, Route.Home
        | Route.Calendar, Route.Calendar
        | Route.Competitions, Route.Competitions
        | Route.News, Route.News
        | Route.Board, Route.Board
        | Route.Youth, Route.Youth
        | Route.Inbox _, Route.Inbox _
        | Route.Squad _, Route.Squad _
        | Route.Staff _, Route.Staff _
        | Route.Tactics _, Route.Tactics _
        | Route.Training _, Route.Training _
        | Route.Scouting _, Route.Scouting _
        | Route.Transfers TransferRoute.Market, Route.Transfers(TransferRoute.Player _)
        | Route.Transfers TransferRoute.Watchlist, Route.Transfers TransferRoute.Watchlist
        | Route.Transfers TransferRoute.IncomingOffers, Route.Transfers TransferRoute.IncomingOffers
        | Route.Transfers TransferRoute.Negotiations, Route.Transfers TransferRoute.Negotiations
        | Route.Transfers TransferRoute.Negotiations, Route.Transfers(TransferRoute.Negotiation _)
        | Route.Transfers TransferRoute.Negotiations, Route.Transfers(TransferRoute.Offer _)
        | Route.Transfers TransferRoute.History, Route.Transfers TransferRoute.History
        | Route.Finances _, Route.Finances _
        | Route.Settings _, Route.Settings _ -> true
        | _ -> left = right

    let private navButton
        (item: Navigation.NavItem)
        (isActive: bool)
        (badgeCount: int option)
        (dispatch: Msg -> unit)
        : IView =
        let badge =
            match badgeCount with
            | Some count when count > 0 ->
                Border.create
                    [ Border.background Theme.Accent
                      Border.cornerRadius 8.0
                      Border.padding (4.0, 1.0)
                      Border.margin (4.0, 0.0)
                      Border.child (
                          TextBlock.create
                              [ TextBlock.text (string count)
                                TextBlock.fontSize 10.0
                                TextBlock.fontWeight FontWeight.Bold
                                TextBlock.foreground Theme.BgSidebar ]
                      ) ]
                :> IView
            | _ -> TextBlock.create [ TextBlock.text "" ] :> IView

        Button.create
            [ Button.background (
                  if isActive then
                      Theme.withAlpha "18" Theme.Accent
                  else
                      "Transparent"
              )
              Button.borderBrush (if isActive then Theme.Accent else "Transparent")
              Button.borderThickness (
                  if isActive then
                      Avalonia.Thickness(3.0, 0.0, 0.0, 0.0)
                  else
                      Avalonia.Thickness(0.0)
              )
              Button.cornerRadius 0.0
              Button.padding (18.0, 11.0)
              Button.horizontalAlignment HorizontalAlignment.Stretch
              Button.horizontalContentAlignment HorizontalAlignment.Left
              Button.onClick (fun _ -> dispatch (Navigate item.Route))
              Button.content (
                  DockPanel.create
                      [ DockPanel.lastChildFill true
                        DockPanel.children
                            [ badge
                              |> fun value -> Border.create [ Border.child value; Border.dock Dock.Right ]
                              StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 12.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ Icons.icon item.Icon 18.0 (if isActive then Theme.Accent else Theme.TextSub)
                                          TextBlock.create
                                              [ TextBlock.text (Localization.resolve item.Label [])
                                                TextBlock.fontSize 13.0
                                                TextBlock.fontWeight (
                                                    if isActive then FontWeight.SemiBold else FontWeight.Normal
                                                )
                                                TextBlock.foreground (
                                                    if isActive then Theme.TextMain else Theme.TextSub
                                                )
                                                TextBlock.verticalAlignment VerticalAlignment.Center ] ] ] ] ]
              ) ]

    let sidebar (state: State) (dispatch: Msg -> unit) =
        let unreadCount, visibleItems =
            match state.Mode with
            | InGame(gs, employment) ->
                let isEmployed =
                    match employment with
                    | Employed _ -> true
                    | NotEmployed -> false

                GameState.unreadInboxCount gs,
                Navigation.menuItems
                |> List.filter (fun item -> Route.canNavigate isEmployed item.Route)
            | _ -> 0, []

        Border.create
            [ Border.width 220.0
              Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 1.0, 0.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill false
                        DockPanel.children
                            [ Border.create
                                  [ DockPanel.dock Dock.Top
                                    Border.padding (20.0, 24.0, 20.0, 20.0)
                                    Border.borderBrush Theme.Border
                                    Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                    Border.child (
                                        StackPanel.create
                                            [ StackPanel.spacing 2.0
                                              StackPanel.children
                                                  [ TextBlock.create
                                                        [ TextBlock.text "FOOTBALL"
                                                          TextBlock.fontSize 16.0
                                                          TextBlock.fontWeight FontWeight.Black
                                                          TextBlock.foreground Theme.TextMain
                                                          TextBlock.lineSpacing 2.0 ]
                                                    TextBlock.create
                                                        [ TextBlock.text "ENGINE"
                                                          TextBlock.fontSize 16.0
                                                          TextBlock.fontWeight FontWeight.Black
                                                          TextBlock.foreground Theme.Accent
                                                          TextBlock.lineSpacing 2.0 ] ] ]
                                    ) ]
                              StackPanel.create
                                  [ DockPanel.dock Dock.Top
                                    StackPanel.margin (0.0, 4.0)
                                    StackPanel.children
                                        [ for section in Navigation.menuSections do
                                              let sectionItems =
                                                  section.Items
                                                  |> List.filter (fun item -> List.contains item visibleItems)

                                              if not sectionItems.IsEmpty then
                                                  Border.create
                                                      [ Border.padding (18.0, 12.0, 18.0, 4.0)
                                                        Border.child (
                                                            TextBlock.create
                                                                [ TextBlock.text (Localization.resolve section.Title [])
                                                                  TextBlock.fontSize 10.0
                                                                  TextBlock.fontWeight FontWeight.Bold
                                                                  TextBlock.foreground Theme.TextDim
                                                                  TextBlock.opacity 0.7 ]
                                                        ) ]
                                                  :> IView

                                              for item in sectionItems do
                                                  let badge =
                                                      match item.Route with
                                                      | Route.Inbox _ -> Some unreadCount
                                                      | _ -> None

                                                  navButton
                                                      item
                                                      (sameDestination item.Route state.Navigation.Current)
                                                      badge
                                                      dispatch ] ] ] ]
              ) ]
