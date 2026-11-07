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
                  Icon = IconName.bullhorn } ] } ]

    /// Settings sits apart at the foot of the sidebar: it configures the game, it is not a place in the club.
    let settingsItem: NavItem =
        { Route = Route.Settings SettingsRoute.Overview
          Label = TranslationKey.Settings
          Icon = IconName.settings }

    let menuItems = (menuSections |> List.collect _.Items) @ [ settingsItem ]

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
        let badge: IView list =
            match badgeCount with
            | Some count when count > 0 -> [ UI.countBadge count |> fun view -> Border.create [ Border.dock Dock.Right; Border.verticalAlignment VerticalAlignment.Center; Border.child view ] ]
            | _ -> []

        Button.create
            [ Button.background (if isActive then Theme.BgCardHover else "Transparent")
              Button.borderThickness 0.0
              Button.cornerRadius Theme.RadiusMd
              Button.padding (14.0, 11.0)
              Button.margin (12.0, 1.0)
              Button.horizontalAlignment HorizontalAlignment.Stretch
              Button.horizontalContentAlignment HorizontalAlignment.Stretch
              Button.onClick (fun _ -> dispatch (Navigate item.Route))
              Button.content (
                  DockPanel.create
                      [ DockPanel.lastChildFill true
                        DockPanel.children
                            [ yield! badge
                              StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 12.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ Icons.icon item.Icon 20.0 (if isActive then Theme.Accent else Theme.TextMuted)
                                          TextBlock.create
                                              [ TextBlock.text (Localization.resolve item.Label [])
                                                TextBlock.fontSize 15.0
                                                TextBlock.fontWeight (
                                                    if isActive then FontWeight.SemiBold else FontWeight.Medium
                                                )
                                                TextBlock.foreground (if isActive then Theme.TextMain else Theme.TextSub)
                                                TextBlock.verticalAlignment VerticalAlignment.Center ] ] ] ] ]
              ) ]

    /// The club's own colours tint its card, so a manager always sees whose dugout they are in.
    let private clubCard (gs: GameState) (dispatch: Msg -> unit) : IView =
        let club = gs.Clubs |> Map.tryFind gs.UserClubId
        let name = club |> Option.map _.Name |> Option.defaultValue ""
        let kit = ClubIdentity.kitOf gs gs.UserClubId

        let tint =
            if UI.inkOn kit.Primary = Theme.night.OnAccent then
                kit.Secondary
            else
                kit.Primary

        let leagueLine =
            match ClubIdentity.leagueStanding gs gs.UserClubId with
            | Some(league, Some position) -> $"{position}.º · {league}"
            | Some(league, None) -> league
            | None -> ""

        let background =
            let gradient = LinearGradientBrush()
            gradient.StartPoint <- Avalonia.RelativePoint(0.0, 0.0, Avalonia.RelativeUnit.Relative)
            gradient.EndPoint <- Avalonia.RelativePoint(1.0, 1.0, Avalonia.RelativeUnit.Relative)
            gradient.GradientStops.Add(GradientStop(Color.Parse(Theme.withAlpha "40" tint), 0.0))
            gradient.GradientStops.Add(GradientStop(Color.Parse(Theme.withAlpha "0A" tint), 0.75))
            gradient

        Button.create
            [ Button.background background
              Button.borderBrush (Theme.withAlpha "66" tint)
              Button.borderThickness 1.0
              Button.cornerRadius Theme.RadiusLg
              Button.padding (12.0, 10.0)
              Button.horizontalAlignment HorizontalAlignment.Stretch
              Button.horizontalContentAlignment HorizontalAlignment.Left
              Button.onClick (fun _ -> dispatch (Navigate Route.Home))
              Button.content (
                  StackPanel.create
                      [ StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 12.0
                        StackPanel.children
                            [ UI.crest kit 44.0
                              StackPanel.create
                                  [ StackPanel.spacing 3.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ UI.display name 22.0 Theme.TextMain
                                          TextBlock.create
                                              [ TextBlock.text leagueLine
                                                TextBlock.fontSize 12.0
                                                TextBlock.foreground Theme.TextSub
                                                TextBlock.textTrimming TextTrimming.CharacterEllipsis
                                                TextBlock.maxWidth 140.0 ] ] ] ] ]
              ) ]

    let sidebar (state: State) (dispatch: Msg -> unit) =
        let unreadCount, visibleItems, gameState =
            match state.Mode with
            | InGame(gs, employment) ->
                let isEmployed =
                    match employment with
                    | Employed _ -> true
                    | NotEmployed -> false

                GameState.unreadInboxCount gs,
                Navigation.menuItems
                |> List.filter (fun item -> Route.canNavigate isEmployed item.Route),
                Some gs
            | _ -> 0, [], None

        let item (navItem: Navigation.NavItem) =
            let badge =
                match navItem.Route with
                | Route.Inbox _ -> Some unreadCount
                | _ -> None

            navButton navItem (sameDestination navItem.Route state.Navigation.Current) badge dispatch

        Border.create
            [ Border.width 240.0
              Border.background (Theme.withAlpha "E6" Theme.BgSidebar)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 1.0, 0.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill true
                        DockPanel.children
                            [ Border.create
                                  [ DockPanel.dock Dock.Top
                                    Border.padding (14.0, 20.0, 14.0, 12.0)
                                    Border.child (
                                        match gameState with
                                        | Some gs -> clubCard gs dispatch
                                        | None -> UI.display "Football Engine" 22.0 Theme.TextMain
                                    ) ]
                              if List.contains Navigation.settingsItem visibleItems then
                                  Border.create
                                      [ DockPanel.dock Dock.Bottom
                                        Border.padding (0.0, 8.0, 0.0, 16.0)
                                        Border.borderBrush Theme.Border
                                        Border.borderThickness (0.0, 1.0, 0.0, 0.0)
                                        Border.child (item Navigation.settingsItem) ]
                              ScrollViewer.create
                                  [ ScrollViewer.horizontalScrollBarVisibility Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                                    ScrollViewer.content (
                                        StackPanel.create
                                            [ StackPanel.margin (0.0, 0.0, 0.0, 12.0)
                                              StackPanel.children
                                                  [ for section in Navigation.menuSections do
                                                        let sectionItems =
                                                            section.Items
                                                            |> List.filter (fun navItem -> List.contains navItem visibleItems)

                                                        if not sectionItems.IsEmpty then
                                                            Border.create
                                                                [ Border.padding (26.0, 14.0, 18.0, 6.0)
                                                                  Border.child (
                                                                      UI.eyebrow (Localization.resolve section.Title []) Theme.TextDim
                                                                  ) ]
                                                            :> IView

                                                        for navItem in sectionItems do
                                                            item navItem ] ]
                                    ) ] ] ]
              ) ]
