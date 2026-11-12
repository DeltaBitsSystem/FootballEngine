namespace FootballEngine.Pages

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
open FootballEngine.Routing


module HomePresenter =

    type NextMatchVM =
        { LocalName: string
          VisitName: string
          Date: System.DateTime
          LocationTag: string }

    type RecentResultVM =
        { HomeName: string
          AwayName: string
          HomeScore: int
          AwayScore: int
          UserWon: bool option }

    let getNextMatch (state: GameState) =
        match GameState.getUserNextFixture state with
        | None -> None
        | Some(_, fixture) ->
            let userTeam = state.Clubs[state.UserClubId]
            let isHome = fixture.HomeClubId = state.UserClubId
            let rivalId = if isHome then fixture.AwayClubId else fixture.HomeClubId
            let rival = state.Clubs[rivalId]

            Some
                { LocalName = if isHome then userTeam.Name else rival.Name
                  VisitName = if isHome then rival.Name else userTeam.Name
                  Date = fixture.ScheduledDate
                  LocationTag = if isHome then (Localization.resolve TranslationKey.HomeTag []) else (Localization.resolve TranslationKey.AwayTag []) }

    let getUpcomingFixtures (state: GameState) (count: int) =
        state.Competitions
        |> Map.toSeq
        |> Seq.collect (fun (_, comp) -> comp.Fixtures |> Map.toSeq)
        |> Seq.filter (fun (_, f) -> MatchFixture.isPending f && MatchFixture.involves state.UserClubId f)
        |> Seq.sortBy (fun (_, f) -> f.ScheduledDate)
        |> Seq.truncate count
        |> List.ofSeq

    let getRecentResults (state: GameState) (count: int) =
        state.Competitions
        |> Map.toSeq
        |> Seq.collect (fun (_, comp) -> comp.Fixtures |> Map.toSeq)
        |> Seq.filter (fun (_, f) -> MatchFixture.isPlayed f && MatchFixture.involves state.UserClubId f)
        |> Seq.sortByDescending (fun (_, f) -> f.ScheduledDate)
        |> Seq.truncate count
        |> Seq.choose (fun (_, f) ->
            match f.HomeScore, f.AwayScore with
            | Some hScore, Some aScore ->
                let isHome = f.HomeClubId = state.UserClubId

                let homeName =
                    state.Clubs
                    |> Map.tryFind f.HomeClubId
                    |> Option.map _.Name
                    |> Option.defaultValue "?"

                let awayName =
                    state.Clubs
                    |> Map.tryFind f.AwayClubId
                    |> Option.map _.Name
                    |> Option.defaultValue "?"

                let userWon =
                    if isHome then
                        if hScore > aScore then Some true
                        elif hScore < aScore then Some false
                        else None
                    else if aScore > hScore then
                        Some true
                    elif aScore < hScore then
                        Some false
                    else
                        None

                Some
                    { HomeScore = hScore
                      AwayScore = aScore
                      HomeName = homeName
                      AwayName = awayName
                      UserWon = userWon }
            | _ -> None)
        |> List.ofSeq

    let private emptyStanding clubId =
        { ClubId = clubId
          Played = 0
          Won = 0
          Drawn = 0
          Lost = 0
          GoalsFor = 0
          GoalsAgainst = 0
          Points = 0 }

    let private getPosColor (leagueId: int) (pos: int) =
        match leagueId, pos with
        | 1, p when p <= 4 -> Theme.Success
        | 1, p when p >= 18 -> Theme.Danger
        | 2, p when p <= 2 -> Theme.Success
        | 2, p when p <= 6 -> Theme.Warning
        | _ -> Theme.TextMuted

    let getUserStanding (state: GameState) (leagueId: CompetitionId) =
        state.Competitions.TryFind leagueId
        |> Option.bind (fun comp ->
            let sorted = Competition.rankedStandings comp |> List.indexed

            sorted
            |> List.tryFind (fun (_, (clubId, _)) -> clubId = state.UserClubId)
            |> Option.map (fun (i, (_, s)) -> i + 1, sorted.Length, s))

    let getTableData (state: GameState) (leagueId: CompetitionId) =
        match state.Competitions.TryFind leagueId with
        | None -> []
        | Some league ->
            Competition.rankedStandings league
            |> List.indexed
            |> List.map (fun (i, (clubId, s)) ->
                let club = state.Clubs[clubId]

                { Display.Tables.Pos = i + 1
                  Display.Tables.TeamName = club.Name
                  Display.Tables.Points = s.Points
                  Display.Tables.Stats = $"{s.Won}-{s.Drawn}-{s.Lost}"
                  Display.Tables.IsUser = (club.Id = state.UserClubId)
                  Display.Tables.PosColor = getPosColor leagueId (i + 1) })

    let getGroupedCompetitions (state: GameState) =
        let all =
            state.Competitions
            |> Map.toList
            |> List.map (fun (id, comp) -> id, comp.Name, comp.Type)
            |> List.sortBy (fun (_, name, _) -> name)

        let leagues =
            all
            |> List.choose (fun (id, name, t) ->
                match t with
                | NationalLeague _ -> Some(id, name)
                | _ -> None)

        let cups =
            all
            |> List.choose (fun (id, name, t) ->
                match t with
                | NationalCup _ -> Some(id, name)
                | _ -> None)

        let international =
            all
            |> List.choose (fun (id, name, t) ->
                match t with
                | InternationalCup _ -> Some(id, name)
                | _ -> None)

        [ if not leagues.IsEmpty then
              ((Localization.resolve TranslationKey.HomeLeagues []).ToUpperInvariant()), IconName.league, leagues
          if not cups.IsEmpty then
              ((Localization.resolve TranslationKey.HomeCups []).ToUpperInvariant()), ClubIcon.stadium, cups
          if not international.IsEmpty then
              ((Localization.resolve TranslationKey.HomeInternational []).ToUpperInvariant()), IconName.league, international ]


module Home =

    let private compactViewport () =
        match Avalonia.Application.Current with
        | null -> false
        | app ->
            match app.ApplicationLifetime with
            | :? Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime as desktop when
                not (isNull desktop.MainWindow)
                ->
                desktop.MainWindow.ClientSize.Width < 800.0
            | _ -> false

    let private panelHeader (iconKind: Material.Icons.MaterialIconKind) (label: string) (extra: IView option) =
        Border.create
            [ Border.padding (14.0, 10.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill false
                        DockPanel.children
                            [ match extra with
                              | Some v -> Border.create [ DockPanel.dock Dock.Right; Border.child v ]
                              | None -> ()
                              StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 6.0
                                    StackPanel.children
                                        [ Icons.icon iconKind 18.0 Theme.Accent
                                          UI.display label 22.0 Theme.TextMain ] ] ] ]
              ) ]
        :> IView

    /// Messages the game surfaces on Home so the manager never has to read the whole inbox:
    /// decisions waiting on them first, then the categories that change a squad or a job.
    let private inboxPreview (gs: GameState) dispatch : IView =
        let weight (message: InboxMessage) =
            let pending = if message.RequiresAction && message.ActionTaken.IsNone then 0 else 10

            let category =
                match message.Category with
                | InjuryMessage -> 0
                | Contract -> 1
                | Transfer -> 2
                | BoardUpdate -> 3
                | Development -> 4
                | MatchReport -> 5

            pending + category

        let unread = gs.Inbox |> List.filter (fun message -> not message.IsRead)

        let important =
            unread
            |> List.sortBy (fun message -> weight message, -message.Date.Ticks)
            |> List.truncate 3

        let categoryLabel, categoryColor =
            fun (category: InboxMessageCategory) ->
                match category with
                | InjuryMessage -> Localization.resolve TranslationKey.CategoryInjury [], Theme.Danger
                | Contract -> Localization.resolve TranslationKey.CategoryContract [], Theme.Hot
                | Transfer -> Localization.resolve TranslationKey.CategoryTransfer [], Theme.AccentAlt
                | BoardUpdate -> Localization.resolve TranslationKey.CategoryBoard [], Theme.Warning
                | Development -> Localization.resolve TranslationKey.CategoryDevelopment [], Theme.Success
                | MatchReport -> Localization.resolve TranslationKey.CategoryMatch [], Theme.TextMuted
            |> fun f -> (fun c -> fst (f c)), (fun c -> snd (f c))

        let row (message: InboxMessage) : IView =
            let color = categoryColor message.Category

            Border.create
                [ Border.background Theme.BgElevated
                  Border.borderBrush Theme.BorderLight
                  Border.borderThickness 1.0
                  Border.cornerRadius Theme.RadiusMd
                  Border.padding (14.0, 12.0)
                  Border.child (
                      Grid.create
                          [ Grid.columnDefinitions "Auto, 14, *, 14, Auto"
                            Grid.children
                                [ Border.create
                                      [ Grid.column 0
                                        Border.verticalAlignment VerticalAlignment.Center
                                        Border.child (UI.badge ((categoryLabel message.Category).ToUpperInvariant()) color) ]
                                  StackPanel.create
                                      [ Grid.column 2
                                        StackPanel.spacing 2.0
                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                        StackPanel.children
                                            [ TextBlock.create
                                                  [ TextBlock.text message.Subject
                                                    TextBlock.fontSize 15.0
                                                    TextBlock.fontWeight FontWeight.Bold
                                                    TextBlock.foreground Theme.TextMain
                                                    TextBlock.textTrimming TextTrimming.CharacterEllipsis ]
                                              TextBlock.create
                                                  [ TextBlock.text $"{message.From} · {message.Body}"
                                                    TextBlock.fontSize 13.0
                                                    TextBlock.foreground Theme.TextMuted
                                                    TextBlock.textTrimming TextTrimming.CharacterEllipsis ] ] ]
                                  StackPanel.create
                                      [ Grid.column 4
                                        StackPanel.orientation Orientation.Horizontal
                                        StackPanel.spacing 8.0
                                        StackPanel.verticalAlignment VerticalAlignment.Center
                                        StackPanel.children
                                            [ UI.primaryButton (Localization.resolve TranslationKey.InboxOpen []) None (fun _ ->
                                                  dispatch (InboxMsg(SelectMessage message.Id))
                                                  dispatch (Navigate(Route.Inbox(InboxRoute(Some message.Id)))))
                                              UI.ghostButton (Localization.resolve TranslationKey.InboxLater []) None (fun _ ->
                                                  dispatch (InboxMsg(MarkAsRead message.Id))) ] ] ] ]
                  ) ]
            :> IView

        let routine = unread.Length - important.Length

        let seeAll =
            Button.create
                [ Button.background "Transparent"
                  Button.borderThickness 0.0
                  Button.padding 0.0
                  Button.onClick (fun _ -> dispatch (Navigate(Route.Inbox(InboxRoute None))))
                  Button.content (
                      TextBlock.create
                          [ TextBlock.text (Localization.resolve TranslationKey.InboxSeeAll [ "count", string gs.Inbox.Length ])
                            TextBlock.fontSize 14.0
                            TextBlock.fontWeight FontWeight.SemiBold
                            TextBlock.foreground Theme.Accent ]
                  ) ]

        let body =
            StackPanel.create
                [ StackPanel.margin (20.0, 4.0, 20.0, 18.0)
                  StackPanel.spacing 10.0
                  StackPanel.children
                      [ if important.IsEmpty then
                            TextBlock.create
                                [ TextBlock.text (Localization.resolve TranslationKey.InboxAllClear [])
                                  TextBlock.fontSize 14.0
                                  TextBlock.foreground Theme.TextMuted ]
                            :> IView
                        else
                            yield! important |> List.map row
                        if routine > 0 then
                            TextBlock.create
                                [ TextBlock.text (Localization.resolve TranslationKey.InboxRoutineHint [ "count", string routine ])
                                  TextBlock.fontSize 13.0
                                  TextBlock.foreground Theme.TextMuted ] ] ]

        UI.panelCard (panelHeader IconName.inbox (Localization.resolve TranslationKey.InboxImportantTitle []) (Some seeAll)) body

    /// The day's headlines from the wider world, so a quiet inbox never means a quiet game.
    let private worldPanel (gs: GameState) dispatch : IView =
        let top = News.topItems 3 gs.News

        let goNews = fun _ -> dispatch (Navigate Route.News)

        let seeAll =
            Button.create
                [ Button.background "Transparent"
                  Button.borderThickness 0.0
                  Button.padding 0.0
                  Button.onClick goNews
                  Button.content (
                      TextBlock.create
                          [ TextBlock.text (Localization.resolve TranslationKey.WorldAroundSeeAll [])
                            TextBlock.fontSize 14.0
                            TextBlock.fontWeight FontWeight.SemiBold
                            TextBlock.foreground Theme.Accent ]
                  ) ]

        let row (item: NewsItem) : IView =
            let headline, _ = NewsText.render gs item

            Button.create
                [ Button.background "Transparent"
                  Button.borderThickness 0.0
                  Button.padding (0.0, 4.0)
                  Button.horizontalAlignment HorizontalAlignment.Stretch
                  Button.horizontalContentAlignment HorizontalAlignment.Left
                  Button.onClick goNews
                  Button.content (
                      StackPanel.create
                          [ StackPanel.orientation Orientation.Horizontal
                            StackPanel.spacing 12.0
                            StackPanel.children
                                [ (match item.Clubs with
                                   | clubId :: _ -> UI.crest (ClubIdentity.kitOf gs clubId) 28.0
                                   | [] -> UI.crest (ClubIdentity.kitOf gs gs.UserClubId) 28.0)
                                  TextBlock.create
                                      [ TextBlock.text headline
                                        TextBlock.fontSize 15.0
                                        TextBlock.fontWeight (
                                            if item.Importance >= NewsImportance.Major then
                                                FontWeight.Bold
                                            else
                                                FontWeight.Medium
                                        )
                                        TextBlock.foreground Theme.TextMain
                                        TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                  ) ]
            :> IView

        let body =
            StackPanel.create
                [ StackPanel.margin (20.0, 4.0, 20.0, 18.0)
                  StackPanel.spacing 6.0
                  StackPanel.children
                      [ if top.IsEmpty then
                            TextBlock.create
                                [ TextBlock.text (Localization.resolve TranslationKey.WorldAroundQuiet [])
                                  TextBlock.fontSize 14.0
                                  TextBlock.foreground Theme.TextMuted ]
                            :> IView
                        else
                            yield! top |> List.map row ] ]

        UI.panelCard (panelHeader IconName.bullhorn (Localization.resolve TranslationKey.WorldAroundTitle []) (Some seeAll)) body

    let private resultDot (userWon: bool option) =
        let color =
            match userWon with
            | Some true -> Theme.Success
            | Some false -> Theme.Danger
            | None -> Theme.Warning

        let letter =
            match userWon with
            | Some true -> "W"
            | Some false -> "L"
            | None -> "D"

        Border.create
            [ Border.width 22.0
              Border.height 22.0
              Border.cornerRadius 11.0
              Border.background (Theme.withAlpha "33" color)
              Border.borderBrush (Theme.withAlpha "88" color)
              Border.borderThickness 1.0
              Border.child (
                  TextBlock.create
                      [ TextBlock.text letter
                        TextBlock.fontSize 9.0
                        TextBlock.fontWeight FontWeight.Black
                        TextBlock.foreground color
                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                        TextBlock.verticalAlignment VerticalAlignment.Center ]
              ) ]

    let private summaryBar
        (compact: bool)
        (userTeam: Club)
        (pos: int option)
        (total: int option)
        (standing: LeagueStanding option)
        =
        let statCard label compactLabel value icon sub =
            UI.iconStatCard (if compact then compactLabel else label) value icon (if compact then "" else sub)

        let cards: IView list =
            [ statCard (Localization.resolve TranslationKey.StatCash []) (Localization.resolve TranslationKey.StatCash []) $"€{int (userTeam.Finance.Cash / 1_000_000m)}M" ClubIcon.finances (Localization.resolve TranslationKey.StatCashSub [])
              statCard
                  (Localization.resolve TranslationKey.StatPosition [])
                  (Localization.resolve TranslationKey.StatPositionShort [])
                  (match pos, total with
                   | Some p, Some t -> $"{p} / {t}"
                   | _ -> "—")
                  IconName.league
                  (match standing with
                   | Some s ->
                       let gd = s.GoalsFor - s.GoalsAgainst
                       let gdStr = if gd >= 0 then $"+{gd}" else $"{gd}"
                       $"{s.Points} pts • {gdStr} GD"
                   | _ -> "")
              statCard
                  (Localization.resolve TranslationKey.StatForm [])
                  (Localization.resolve TranslationKey.StatForm [])
                  (match standing with
                   | Some s -> $"{s.Won}W {s.Drawn}D {s.Lost}L"
                   | None -> "—")
                  MatchEvent.goal
                  (match standing with
                   | Some s -> $"Played {s.Played}"
                   | None -> "")
              statCard
                  (Localization.resolve TranslationKey.StatMorale [])
                  (Localization.resolve TranslationKey.StatMorale [])
                  $"{userTeam.Morale}%%"
                  PlayerIcon.morale
                  (if userTeam.Morale > 70 then (Localization.resolve TranslationKey.MoraleHigh [])
                   elif userTeam.Morale > 45 then (Localization.resolve TranslationKey.MoraleOkay [])
                   else (Localization.resolve TranslationKey.MoraleLow [])) ]

        if compact then
            Grid.create
                [ Grid.columnDefinitions "*, *"
                  Grid.rowDefinitions "*, *"
                  Grid.children
                      [ for index, card in List.indexed cards do
                            Border.create
                                [ Grid.column (index % 2)
                                  Grid.row (index / 2)
                                  Border.margin 4.0
                                  Border.child card ] ] ]
        else
            Grid.create
                [ Grid.columnDefinitions "*, 10, *, 10, *, 10, *"
                  Grid.children
                      [ Border.create [ Grid.column 0; Border.child cards[0] ]
                        Border.create [ Grid.column 2; Border.child cards[1] ]
                        Border.create [ Grid.column 4; Border.child cards[2] ]
                        Border.create [ Grid.column 6; Border.child cards[3] ] ] ]

    let private notificationRow (note: Notification) dispatch =
        let icon = note.Icon

        let color =
            match icon with
            | Material.Icons.MaterialIconKind.Trophy
            | Material.Icons.MaterialIconKind.CheckCircleOutline -> Theme.Accent
            | Material.Icons.MaterialIconKind.ArrowUpBoldCircleOutline -> Theme.Success
            | Material.Icons.MaterialIconKind.ArrowDownBoldCircleOutline
            | Material.Icons.MaterialIconKind.AlertCircleOutline -> Theme.Danger
            | Material.Icons.MaterialIconKind.FlagCheckered -> Theme.Warning
            | _ -> Theme.TextMuted

        let copyText =
            if note.Body = "" then
                note.Title
            else
                note.Title + "\n" + note.Body

        let copyNotification (event: Avalonia.Interactivity.RoutedEventArgs) =
            event.Handled <- true

            match event.Source with
            | :? Control as source ->
                match TopLevel.GetTopLevel(source) with
                | null -> ()
                | topLevel ->
                    match topLevel.Clipboard with
                    | null -> ()
                    | clipboard ->
                        Async.StartImmediate(
                            async {
                                try
                                    let data = new Avalonia.Input.DataTransfer()
                                    data.Add(Avalonia.Input.DataTransferItem.CreateText(copyText))
                                    do! clipboard.SetDataAsync(data) |> Async.AwaitTask
                                with _ ->
                                    ()
                            }
                        )
            | _ -> ()

        let actions =
            StackPanel.create
                [ Grid.column 2
                  StackPanel.orientation Orientation.Horizontal
                  StackPanel.spacing 2.0
                  StackPanel.children
                      [ UI.iconOnlyButton
                            (Localization.resolve TranslationKey.CopyNotification [])
                            Material.Icons.MaterialIconKind.ContentCopy
                            UI.ButtonVariant.Ghost
                            UI.ControlState.Default
                            copyNotification
                        Button.create
                            [ Button.background "Transparent"
                              Button.borderThickness 0.0
                              Button.padding (4.0, 0.0)
                              Button.verticalAlignment VerticalAlignment.Top
                              Button.cursor Avalonia.Input.Cursor.Default
                              Button.onClick (fun _ -> dispatch (NotificationMsg(DismissNotification note.Id)))
                              Button.content (
                                  TextBlock.create
                                      [ TextBlock.text "✕"
                                        TextBlock.fontSize 10.0
                                        TextBlock.foreground Theme.TextMuted ]
                              ) ] ] ]

        Border.create
            [ Border.padding (12.0, 10.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.background (Theme.withAlpha "0A" color)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "Auto, *, Auto"
                        Grid.children
                            [ Icons.iconMd icon color
                              StackPanel.create
                                  [ Grid.column 1
                                    StackPanel.spacing 2.0
                                    StackPanel.margin (8.0, 0.0)
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text note.Title
                                                TextBlock.fontSize 12.0
                                                TextBlock.fontWeight FontWeight.SemiBold
                                                TextBlock.foreground Theme.TextMain
                                                TextBlock.textWrapping TextWrapping.Wrap ]
                                          if note.Body <> "" then
                                              TextBlock.create
                                                  [ TextBlock.text note.Body
                                                    TextBlock.fontSize 11.0
                                                    TextBlock.foreground Theme.TextMuted
                                                    TextBlock.textWrapping TextWrapping.Wrap ] ] ]

                              actions ] ]
              ) ]

    let private notificationsPanel (notes: Notification list) dispatch =
        let clearAllButton =
            Button.create
                [ Button.background "Transparent"
                  Button.borderThickness 0.0
                  Button.padding (4.0, 2.0)
                  Button.cursor Avalonia.Input.Cursor.Default
                  Button.onClick (fun _ -> dispatch (NotificationMsg DismissAll))
                  Button.content (
                      TextBlock.create
                          [ TextBlock.text (Localization.resolve TranslationKey.ClearAll [])
                            TextBlock.fontSize 10.0
                            TextBlock.foreground Theme.TextMuted ]
                  ) ]
            :> IView

        let header =
            Border.create
                [ Border.padding (14.0, 10.0)
                  Border.borderBrush Theme.Border
                  Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                  Border.child (
                      DockPanel.create
                          [ DockPanel.lastChildFill false
                            DockPanel.children
                                [ if not notes.IsEmpty then
                                      Border.create [ DockPanel.dock Dock.Right; Border.child clearAllButton ]
                                  StackPanel.create
                                      [ StackPanel.orientation Orientation.Horizontal
                                        StackPanel.spacing 6.0
                                        StackPanel.children
                                            [ Icons.iconSm IconName.info Theme.Accent
                                              TextBlock.create
                                                  [ TextBlock.text ((Localization.resolve TranslationKey.Notifications []).ToUpperInvariant())
                                                    TextBlock.fontSize 10.0
                                                    TextBlock.fontWeight FontWeight.Black
                                                    TextBlock.foreground Theme.TextMuted
                                                    TextBlock.verticalAlignment VerticalAlignment.Center ]
                                              if not notes.IsEmpty then
                                                  UI.countBadge notes.Length ] ] ] ]
                  ) ]
            :> IView

        let body =
            if notes.IsEmpty then
                Border.create
                    [ Border.padding (14.0, 16.0)
                      Border.child (
                          TextBlock.create
                              [ TextBlock.text (Localization.resolve TranslationKey.NoNotifications [])
                                TextBlock.fontSize 12.0
                                TextBlock.foreground Theme.TextMuted ]
                      ) ]
                :> IView
            else
                ScrollViewer.create
                    [ ScrollViewer.maxHeight 260.0
                      ScrollViewer.content (
                          StackPanel.create
                              [ StackPanel.spacing 0.0
                                StackPanel.children
                                    [ for note in notes do
                                          notificationRow note dispatch ] ]
                      ) ]
                :> IView

        UI.panelCard header body

    let private upcomingFixtureRow (fixture: MatchId * MatchFixture) (gs: GameState) =
        let _, f = fixture
        let isHome = f.HomeClubId = gs.UserClubId
        let rivalId = if isHome then f.AwayClubId else f.HomeClubId

        let rival =
            gs.Clubs |> Map.tryFind rivalId |> Option.map _.Name |> Option.defaultValue "?"

        let locationColor = if isHome then Theme.AccentAlt else Theme.Warning

        Border.create
            [ Border.padding (14.0, 10.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "Auto, *, Auto"
                        Grid.children
                            [ Border.create
                                  [ Grid.column 0
                                    Border.background (Theme.withAlpha "22" locationColor)
                                    Border.borderBrush (Theme.withAlpha "55" locationColor)
                                    Border.borderThickness 1.0
                                    Border.cornerRadius 4.0
                                    Border.padding (5.0, 2.0)
                                    Border.margin (0.0, 0.0, 10.0, 0.0)
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child (
                                        TextBlock.create
                                            [ TextBlock.text (if isHome then "H" else "A")
                                              TextBlock.fontSize 9.0
                                              TextBlock.fontWeight FontWeight.Black
                                              TextBlock.foreground locationColor ]
                                    ) ]
                              TextBlock.create
                                  [ Grid.column 1
                                    TextBlock.text rival
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground Theme.TextMain
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]
                              TextBlock.create
                                  [ Grid.column 2
                                    TextBlock.text (f.ScheduledDate.ToString("dd MMM"))
                                    TextBlock.fontSize 11.0
                                    TextBlock.foreground Theme.TextMuted
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    let private upcomingPanel (fixtures: (MatchId * MatchFixture) list) (gs: GameState) =
        let header = panelHeader IconName.calendar (Localization.resolve TranslationKey.UpcomingFixtures []) None

        let body =
            if fixtures.IsEmpty then
                Border.create
                    [ Border.padding (14.0, 16.0)
                      Border.child (
                          TextBlock.create
                              [ TextBlock.text (Localization.resolve TranslationKey.NoUpcomingFixtures [])
                                TextBlock.fontSize 12.0
                                TextBlock.foreground Theme.TextMuted ]
                      ) ]
                :> IView
            else
                StackPanel.create
                    [ StackPanel.spacing 0.0
                      StackPanel.children
                          [ for fx in fixtures do
                                upcomingFixtureRow fx gs ] ]
                :> IView

        UI.panelCard header body

    let private recentResultRow (r: HomePresenter.RecentResultVM) =
        let resultColor =
            match r.UserWon with
            | Some true -> Theme.Success
            | Some false -> Theme.Danger
            | None -> Theme.Warning

        Border.create
            [ Border.padding (14.0, 9.0)
              Border.borderBrush Theme.Border
              Border.borderThickness (0.0, 0.0, 0.0, 1.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "*, Auto, *"
                        Grid.children
                            [ TextBlock.create
                                  [ Grid.column 0
                                    TextBlock.text r.HomeName
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground Theme.TextMain
                                    TextBlock.textAlignment TextAlignment.Right
                                    TextBlock.verticalAlignment VerticalAlignment.Center ]
                              Border.create
                                  [ Grid.column 1
                                    Border.background (Theme.withAlpha "22" resultColor)
                                    Border.borderBrush (Theme.withAlpha "55" resultColor)
                                    Border.borderThickness 1.0
                                    Border.cornerRadius 6.0
                                    Border.padding (10.0, 4.0)
                                    Border.margin (10.0, 0.0)
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child (
                                        TextBlock.create
                                            [ TextBlock.text $"{r.HomeScore} - {r.AwayScore}"
                                              TextBlock.fontSize 12.0
                                              TextBlock.fontWeight FontWeight.Black
                                              TextBlock.foreground resultColor ]
                                    ) ]
                              TextBlock.create
                                  [ Grid.column 2
                                    TextBlock.text r.AwayName
                                    TextBlock.fontSize 12.0
                                    TextBlock.fontWeight FontWeight.SemiBold
                                    TextBlock.foreground Theme.TextMain
                                    TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
              ) ]

    let private recentResultsPanel (results: HomePresenter.RecentResultVM list) =
        let header = panelHeader MatchEvent.goal (Localization.resolve TranslationKey.RecentResults []) None

        let body =
            if results.IsEmpty then
                Border.create
                    [ Border.padding (14.0, 16.0)
                      Border.child (
                          TextBlock.create
                              [ TextBlock.text (Localization.resolve TranslationKey.NoResultsYet [])
                                TextBlock.fontSize 12.0
                                TextBlock.foreground Theme.TextMuted ]
                      ) ]
                :> IView
            else
                StackPanel.create
                    [ StackPanel.spacing 0.0
                      StackPanel.children
                          [ for r in results do
                                recentResultRow r ] ]
                :> IView

        UI.panelCard header body

    let private competitionPicker
        (groups: (string * Material.Icons.MaterialIconKind * (CompetitionId * string) list) list)
        (selectedId: CompetitionId)
        (onSelect: CompetitionId -> unit)
        =
        let body =
            ScrollViewer.create
                [ ScrollViewer.verticalScrollBarVisibility ScrollBarVisibility.Auto
                  ScrollViewer.content (
                      StackPanel.create
                          [ StackPanel.spacing 0.0
                            StackPanel.children
                                [ for label, icon, items in groups do
                                      Border.create
                                          [ Border.padding (14.0, 9.0)
                                            Border.background Theme.BgElevated
                                            Border.borderBrush Theme.Border
                                            Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                                            Border.child (
                                                StackPanel.create
                                                    [ StackPanel.orientation Orientation.Horizontal
                                                      StackPanel.spacing 6.0
                                                      StackPanel.children
                                                          [ Icons.iconSm icon Theme.TextDim
                                                            TextBlock.create
                                                                [ TextBlock.text label
                                                                  TextBlock.fontSize 9.0
                                                                  TextBlock.fontWeight FontWeight.Bold
                                                                  TextBlock.foreground Theme.TextDim
                                                                  TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                                            ) ]

                                      for id, name in items do
                                          let isActive = selectedId = id

                                          Border.create
                                              [ Border.background (
                                                    if isActive then Theme.AccentLight else "Transparent"
                                                )
                                                Border.borderBrush (if isActive then Theme.Accent else "Transparent")
                                                Border.borderThickness (3.0, 0.0, 0.0, 0.0)
                                                Border.child (
                                                    Button.create
                                                        [ Button.horizontalAlignment HorizontalAlignment.Stretch
                                                          Button.padding (14.0, 9.0)
                                                          Button.background "Transparent"
                                                          Button.borderThickness 0.0
                                                          Button.cornerRadius 0.0
                                                          Button.cursor Avalonia.Input.Cursor.Default
                                                          Button.onClick (fun _ -> onSelect id)
                                                          Button.content (
                                                              TextBlock.create
                                                                  [ TextBlock.text name
                                                                    TextBlock.fontSize 12.0
                                                                    TextBlock.fontWeight (
                                                                        if isActive then
                                                                            FontWeight.SemiBold
                                                                        else
                                                                            FontWeight.Medium
                                                                    )
                                                                    TextBlock.foreground (
                                                                        if isActive then Theme.Accent else Theme.TextSub
                                                                    ) ]
                                                          ) ]
                                                ) ] ] ]
                  ) ]
            :> IView

        Border.create
            [ Border.background Theme.BgCard
              Border.cornerRadius Theme.RadiusLg
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.clipToBounds true
              Border.child body ]

    let private standingsPanel (gs: GameState) (selectedId: CompetitionId) =
        let rows = HomePresenter.getTableData gs selectedId

        let header =
            Border.create
                [ Border.padding (14.0, 10.0)
                  Border.background Theme.BgElevated
                  Border.borderBrush Theme.Border
                  Border.borderThickness (0.0, 0.0, 0.0, 1.0)
                  Border.child (
                      Grid.create
                          [ Grid.columnDefinitions "32, *, 64, 40, 40"
                            Grid.children
                                [ TextBlock.create
                                      [ Grid.column 0
                                        TextBlock.text "#"
                                        TextBlock.fontSize 9.0
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.foreground Theme.TextDim
                                        TextBlock.textAlignment TextAlignment.Center ]
                                  TextBlock.create
                                      [ Grid.column 1
                                        TextBlock.text ((Localization.resolve TranslationKey.TableClub []).ToUpperInvariant())
                                        TextBlock.fontSize 9.0
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.foreground Theme.TextDim ]
                                  TextBlock.create
                                      [ Grid.column 2
                                        TextBlock.text (Localization.resolve TranslationKey.TableRecord [])
                                        TextBlock.fontSize 9.0
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.foreground Theme.TextDim
                                        TextBlock.textAlignment TextAlignment.Center ]
                                  TextBlock.create
                                      [ Grid.column 3
                                        TextBlock.text "GD"
                                        TextBlock.fontSize 9.0
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.foreground Theme.TextDim
                                        TextBlock.textAlignment TextAlignment.Center ]
                                  TextBlock.create
                                      [ Grid.column 4
                                        TextBlock.text ((Localization.resolve TranslationKey.TablePoints []).ToUpperInvariant())
                                        TextBlock.fontSize 9.0
                                        TextBlock.fontWeight FontWeight.Bold
                                        TextBlock.foreground Theme.TextDim
                                        TextBlock.textAlignment TextAlignment.Right ] ] ]
                  ) ]
            :> IView

        let body =
            if rows.IsEmpty then
                Border.create
                    [ Border.padding (20.0, 24.0)
                      Border.child (
                          StackPanel.create
                              [ StackPanel.spacing 8.0
                                StackPanel.horizontalAlignment HorizontalAlignment.Center
                                StackPanel.children
                                    [ Icons.iconSm IconName.league Theme.TextDim
                                      TextBlock.create
                                          [ TextBlock.text (Localization.resolve TranslationKey.NoStandings [])
                                            TextBlock.fontSize 12.0
                                            TextBlock.foreground Theme.TextDim
                                            TextBlock.horizontalAlignment HorizontalAlignment.Center ] ] ]
                      ) ]
                :> IView
            else
                ScrollViewer.create
                    [ ScrollViewer.maxHeight 420.0
                      ScrollViewer.content (
                          StackPanel.create
                              [ StackPanel.spacing 0.0
                                StackPanel.children
                                    [ for row in rows do
                                          let isTop4 = row.Pos <= 4
                                          let isRelegation = row.Pos >= 18
                                          // Row tint only for user — top/relegation get only badge, not full bg
                                          let rowBg =
                                              (if row.IsUser then
                                                   Theme.withAlpha "14" Theme.Accent
                                               else
                                                   "Transparent")

                                          let badgeBg, badgeFg =
                                              if row.IsUser then Theme.Accent, Theme.BgSidebar
                                              elif isTop4 then Theme.AccentAlt, "#ffffff"
                                              elif isRelegation then Theme.Danger, "#ffffff"
                                              else Theme.BgElevated, Theme.TextMuted

                                          Border.create
                                              [ Border.background rowBg
                                                Border.borderBrush (if row.IsUser then Theme.Accent else "Transparent")
                                                Border.borderThickness (3.0, 0.0, 0.0, 0.0)
                                                // left accent only for user; top/relegation badge does the job
                                                Border.padding (8.0, 7.0)
                                                Border.child (
                                                    Grid.create
                                                        [ Grid.columnDefinitions "32, *, 64, 40, 40"
                                                          Grid.children
                                                              [ // ── Pos badge — solid, high contrast (fixes Screenshot_20260822_005723)
                                                                Border.create
                                                                    [ Grid.column 0
                                                                      Border.background badgeBg
                                                                      Border.cornerRadius 6.0
                                                                      Border.width 22.0
                                                                      Border.height 20.0
                                                                      Border.verticalAlignment VerticalAlignment.Center
                                                                      Border.horizontalAlignment
                                                                          HorizontalAlignment.Center
                                                                      Border.child (
                                                                          TextBlock.create
                                                                              [ TextBlock.text (string row.Pos)
                                                                                TextBlock.fontSize 10.0
                                                                                TextBlock.fontWeight FontWeight.Black
                                                                                TextBlock.foreground badgeFg
                                                                                TextBlock.horizontalAlignment
                                                                                    HorizontalAlignment.Center
                                                                                TextBlock.verticalAlignment
                                                                                    VerticalAlignment.Center ]
                                                                      ) ]
                                                                TextBlock.create
                                                                    [ Grid.column 1
                                                                      TextBlock.text row.TeamName
                                                                      TextBlock.fontSize 12.0
                                                                      TextBlock.fontWeight (
                                                                          if row.IsUser then
                                                                              FontWeight.Bold
                                                                          else
                                                                              FontWeight.Medium
                                                                      )
                                                                      TextBlock.foreground (
                                                                          if row.IsUser then
                                                                              Theme.TextMain
                                                                          else
                                                                              Theme.TextSub
                                                                      )
                                                                      TextBlock.verticalAlignment
                                                                          VerticalAlignment.Center
                                                                      TextBlock.textTrimming
                                                                          TextTrimming.CharacterEllipsis ]
                                                                TextBlock.create
                                                                    [ Grid.column 2
                                                                      TextBlock.text row.Stats
                                                                      TextBlock.fontSize 11.0
                                                                      TextBlock.foreground Theme.TextMuted
                                                                      TextBlock.textAlignment TextAlignment.Center
                                                                      TextBlock.verticalAlignment
                                                                          VerticalAlignment.Center ]
                                                                TextBlock.create
                                                                    [ Grid.column 3
                                                                      TextBlock.text "—"
                                                                      TextBlock.fontSize 11.0
                                                                      TextBlock.foreground Theme.TextMuted
                                                                      TextBlock.textAlignment TextAlignment.Center
                                                                      TextBlock.verticalAlignment
                                                                          VerticalAlignment.Center ]
                                                                TextBlock.create
                                                                    [ Grid.column 4
                                                                      TextBlock.text (string row.Points)
                                                                      TextBlock.fontSize 12.5
                                                                      TextBlock.fontWeight FontWeight.Black
                                                                      TextBlock.foreground (
                                                                          if row.IsUser then
                                                                              Theme.Accent
                                                                          else
                                                                              Theme.TextMain
                                                                      )
                                                                      TextBlock.textAlignment TextAlignment.Right
                                                                      TextBlock.verticalAlignment
                                                                          VerticalAlignment.Center ] ] ]
                                                ) ] ] ]
                      ) ]
                :> IView

        UI.panelCard header body

    let homeView
        (state: State)
        (selectedLeagueId: CompetitionId)
        (onLeagueChange: CompetitionId -> unit)
        dispatch
        : IView =
        match state.Mode with
        | InGame(gs, _) ->
            let compact = compactViewport ()
            let userTeam = gs.Clubs[gs.UserClubId]

            let upcomingFixtures = HomePresenter.getUpcomingFixtures gs 5
            let recentResults = HomePresenter.getRecentResults gs 5
            let userStanding = HomePresenter.getUserStanding gs selectedLeagueId
            let groups = HomePresenter.getGroupedCompetitions gs

            let pos, total, standing =
                match userStanding with
                | Some(p, t, s) -> Some p, Some t, Some s
                | None -> None, None, None

            let bodyColumns: IView =
                if compact then
                    StackPanel.create
                        [ StackPanel.spacing 16.0
                          StackPanel.children
                              [ competitionPicker groups selectedLeagueId onLeagueChange
                                standingsPanel gs selectedLeagueId
                                recentResultsPanel recentResults
                                upcomingPanel upcomingFixtures gs
                                notificationsPanel state.Notifications dispatch ] ]
                    :> IView
                else
                    Grid.create
                        [ Grid.columnDefinitions "1.6*, 20, 1*"
                          Grid.children
                              [ Grid.create
                                    [ Grid.column 0
                                      Grid.columnDefinitions "190, 12, *"
                                      Grid.children
                                          [ Border.create
                                                [ Grid.column 0
                                                  Border.verticalAlignment VerticalAlignment.Top
                                                  Border.child (
                                                      competitionPicker groups selectedLeagueId onLeagueChange
                                                  ) ]
                                            StackPanel.create
                                                [ Grid.column 2
                                                  StackPanel.spacing 16.0
                                                  StackPanel.children
                                                      [ standingsPanel gs selectedLeagueId
                                                        recentResultsPanel recentResults ] ] ] ]
                                StackPanel.create
                                    [ Grid.column 2
                                      StackPanel.spacing 16.0
                                      StackPanel.children
                                          [ upcomingPanel upcomingFixtures gs
                                            notificationsPanel state.Notifications dispatch ] ] ] ]
                    :> IView

            ScrollViewer.create
                [ ScrollViewer.content (
                      StackPanel.create
                          [ StackPanel.margin (32.0, 8.0, 32.0, 32.0)
                            StackPanel.spacing 20.0
                            StackPanel.children
                                [ CareerJourney.home state dispatch
                                  inboxPreview gs dispatch
                                  worldPanel gs dispatch
                                  summaryBar compact userTeam pos total standing
                                  bodyColumns ] ]
                  ) ]
        | _ -> Border.create [] :> IView
