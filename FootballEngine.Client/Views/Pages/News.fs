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

module News =
    let private tr key = Localization.resolve key []

    let private culture () =
        System.Globalization.CultureInfo.GetCultureInfo(
            if Localization.active () = Locale.Es then
                "es-AR"
            else
                "en-GB"
        )

    /// Pure so the filter semantics are testable without a view.
    let visible (filter: NewsFilter) (items: NewsItem list) : NewsItem list =
        match filter with
        | NewsEverything -> items
        | NewsMyClub -> items |> List.filter (fun item -> item.Scope = NewsScope.UserClub)
        | NewsMyLeague -> items |> List.filter (fun item -> item.Scope = NewsScope.UserLeague)
        | NewsWorld -> items |> List.filter (fun item -> item.Scope = NewsScope.World)

    /// Newest day first, then the most important item of a day first, so the feed reads like a front page.
    let byDay (items: NewsItem list) : (System.DateTime * NewsItem list) list =
        items
        |> List.groupBy (fun item -> item.Date.Date)
        |> List.sortByDescending fst
        |> List.map (fun (day, dayItems) ->
            day, dayItems |> List.sortByDescending (fun item -> item.Importance, item.Id))

    /// Best items first across every scope; used by Home to pick the day's headlines.
    let topItems (count: int) (items: NewsItem list) : NewsItem list =
        match items with
        | [] -> []
        | _ ->
            let latestDay = items |> List.map (fun item -> item.Date.Date) |> List.max

            items
            |> List.filter (fun item -> item.Date.Date = latestDay)
            |> List.sortByDescending (fun item -> item.Importance, item.Id)
            |> List.truncate count

    let kindChip (kind: NewsKind) : TranslationKey * string =
        match kind with
        | NewsKind.BigWin _
        | NewsKind.Upset _
        | NewsKind.Streak _
        | NewsKind.NewLeader _
        | NewsKind.Clasico _ -> TranslationKey.CategoryMatch, Theme.AccentAlt
        | NewsKind.BoardUnrest _
        | NewsKind.ManagerSacked _
        | NewsKind.ManagerAppointed _
        | NewsKind.FansMood _ -> TranslationKey.CategoryBoard, Theme.Warning
        | NewsKind.TransferDone _
        | NewsKind.Rumour _
        | NewsKind.TransferRequest _
        | NewsKind.ContractRunningOut _ -> TranslationKey.CategoryTransfer, Theme.Success
        | NewsKind.HatTrick _
        | NewsKind.ScoringRun _
        | NewsKind.Milestone _
        | NewsKind.Breakout _
        | NewsKind.Retirement _ -> TranslationKey.NewsChipPlayer, Theme.Accent
        | NewsKind.Award _
        | NewsKind.Promotion _
        | NewsKind.Relegation _
        | NewsKind.Champion _ -> TranslationKey.NewsChipSeason, Theme.Hot

    let private crests (gs: GameState) (item: NewsItem) : IView =
        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.spacing 6.0
              StackPanel.verticalAlignment VerticalAlignment.Center
              StackPanel.children (
                  item.Clubs
                  |> List.truncate 2
                  |> List.map (fun clubId -> UI.crest (ClubIdentity.kitOf gs clubId) 40.0)
              ) ]
        :> IView

    let private chips (item: NewsItem) : IView =
        let label, color = kindChip item.Kind

        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.spacing 6.0
              StackPanel.children
                  [ if item.Importance >= NewsImportance.Major then
                        UI.badge ((tr TranslationKey.NewsMajorChip).ToUpperInvariant()) Theme.Hot
                    UI.badge ((tr label).ToUpperInvariant()) color ] ]
        :> IView

    let card (gs: GameState) (item: NewsItem) : IView =
        let headline, body = NewsText.render gs item
        let isHeadline = item.Importance = NewsImportance.Headline

        Border.create
            [ Border.background (if isHeadline then Theme.BgCardHover else Theme.BgCard)
              Border.borderBrush (if isHeadline then Theme.Accent else Theme.Border)
              Border.borderThickness (if isHeadline then 2.0 else 1.0)
              Border.cornerRadius Theme.RadiusLg
              Border.padding (18.0, 14.0)
              Border.margin (0.0, 0.0, 0.0, 8.0)
              Border.child (
                  DockPanel.create
                      [ DockPanel.lastChildFill true
                        DockPanel.children
                            [ Border.create
                                  [ DockPanel.dock Dock.Left
                                    Border.margin (0.0, 0.0, 16.0, 0.0)
                                    Border.child (crests gs item) ]
                              StackPanel.create
                                  [ StackPanel.spacing 6.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ chips item
                                          TextBlock.create
                                              [ TextBlock.text headline
                                                TextBlock.fontSize (if isHeadline then 20.0 else 16.0)
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.textWrapping TextWrapping.Wrap
                                                TextBlock.foreground Theme.TextMain ]
                                          TextBlock.create
                                              [ TextBlock.text body
                                                TextBlock.fontSize 13.0
                                                TextBlock.textWrapping TextWrapping.Wrap
                                                TextBlock.foreground Theme.TextMuted ] ] ] ] ]
              ) ]
        :> IView

    let private filterPills (state: State) dispatch : IView =
        let pill (filter: NewsFilter) (label: TranslationKey) =
            UI.tabButton (tr label) (state.News.Filter = filter) (fun _ -> dispatch (NewsMsg(SetNewsFilter filter)))

        StackPanel.create
            [ StackPanel.orientation Orientation.Horizontal
              StackPanel.margin (0.0, 8.0, 0.0, 6.0)
              StackPanel.children
                  [ pill NewsMyClub TranslationKey.NewsFilterClub
                    pill NewsMyLeague TranslationKey.NewsFilterLeague
                    pill NewsWorld TranslationKey.NewsFilterWorld
                    pill NewsEverything TranslationKey.NewsFilterAll ] ]
        :> IView

    let view (state: State) (dispatch: Msg -> unit) : IView =
        match state.Mode with
        | InGame(gs, _) ->
            let days = gs.News |> visible state.News.Filter |> List.truncate 120 |> byDay

            ScrollViewer.create
                [ ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Disabled
                  ScrollViewer.content (
                      StackPanel.create
                          [ StackPanel.margin (32.0, 8.0, 32.0, 32.0)
                            StackPanel.maxWidth 1100.0
                            StackPanel.children
                                [ UI.display (tr TranslationKey.News) 44.0 Theme.TextMain
                                  TextBlock.create
                                      [ TextBlock.text (tr TranslationKey.NewsSubtitle)
                                        TextBlock.fontSize 14.0
                                        TextBlock.margin (0.0, 6.0, 0.0, 8.0)
                                        TextBlock.foreground Theme.TextMuted ]
                                  filterPills state dispatch
                                  if days.IsEmpty then
                                      UI.emptyState
                                          IconName.bullhorn
                                          (tr TranslationKey.NewsEmptyTitle)
                                          (tr TranslationKey.NewsEmptySub)
                                  else
                                      for day, items in days do
                                          Border.create
                                              [ Border.margin (4.0, 18.0, 0.0, 10.0)
                                                Border.child (
                                                    UI.eyebrow (day.ToString("dddd d MMMM", culture ())) Theme.TextDim
                                                ) ]

                                          for item in items do
                                              card gs item ] ]
                  ) ]
            :> IView
        | _ -> TextBlock.create [ TextBlock.text (tr TranslationKey.NoCareerLoaded) ] :> IView
