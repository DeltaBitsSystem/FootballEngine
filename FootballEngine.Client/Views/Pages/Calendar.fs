namespace FootballEngine.Pages

open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.AppMsgs
open FootballEngine.Components
open FootballEngine.Domain

module Calendar =
    let private tr key = Localization.resolve key []

    let private culture () =
        System.Globalization.CultureInfo.GetCultureInfo(
            if Localization.active () = Locale.Es then "es-AR" else "en-GB"
        )

    let private fixtureCard (gs: GameState) (competition: string) (fixture: MatchFixture) (isNext: bool) : IView =
        let name id =
            gs.Clubs |> Map.tryFind id |> Option.map _.Name |> Option.defaultValue "—"

        let isHome = fixture.HomeClubId = gs.UserClubId

        let team (clubId: ClubId) =
            StackPanel.create
                [ StackPanel.orientation Orientation.Horizontal
                  StackPanel.spacing 10.0
                  StackPanel.children
                      [ UI.crest (ClubIdentity.kitOf gs clubId) 28.0
                        TextBlock.create
                            [ TextBlock.text (name clubId)
                              TextBlock.fontSize 15.0
                              TextBlock.fontWeight (if clubId = gs.UserClubId then FontWeight.Bold else FontWeight.Medium)
                              TextBlock.foreground Theme.TextMain
                              TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
            :> IView

        let outcome: IView =
            match fixture.HomeScore, fixture.AwayScore with
            | Some home, Some away -> UI.display $"{home} - {away}" 28.0 Theme.TextMain
            | _ when isNext -> UI.badge ((tr TranslationKey.FixtureNext).ToUpperInvariant()) Theme.Accent
            | _ ->
                TextBlock.create
                    [ TextBlock.text (tr TranslationKey.FixturePending)
                      TextBlock.fontSize 13.0
                      TextBlock.foreground Theme.TextMuted
                      TextBlock.verticalAlignment VerticalAlignment.Center ]

        Border.create
            [ Border.background (if isNext then Theme.BgCardHover else Theme.BgCard)
              Border.borderBrush (if isNext then Theme.Accent else Theme.Border)
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding (18.0, 12.0)
              Border.margin (0.0, 0.0, 0.0, 8.0)
              Border.child (
                  Grid.create
                      [ Grid.columnDefinitions "64, 20, *, 20, Auto, 20, 110"
                        Grid.children
                            [ StackPanel.create
                                  [ Grid.column 0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children
                                        [ UI.display (fixture.ScheduledDate.ToString("dd")) 32.0 Theme.TextMain
                                          TextBlock.create
                                              [ TextBlock.text (fixture.ScheduledDate.ToString("ddd", culture ()))
                                                TextBlock.fontSize 12.0
                                                TextBlock.foreground Theme.TextMuted ] ] ]
                              StackPanel.create
                                  [ Grid.column 2
                                    StackPanel.spacing 8.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.children [ team fixture.HomeClubId; team fixture.AwayClubId ] ]
                              StackPanel.create
                                  [ Grid.column 4
                                    StackPanel.spacing 6.0
                                    StackPanel.verticalAlignment VerticalAlignment.Center
                                    StackPanel.horizontalAlignment HorizontalAlignment.Right
                                    StackPanel.children
                                        [ TextBlock.create
                                              [ TextBlock.text competition
                                                TextBlock.fontSize 13.0
                                                TextBlock.foreground Theme.TextSub
                                                TextBlock.horizontalAlignment HorizontalAlignment.Right ]
                                          TextBlock.create
                                              [ TextBlock.text (
                                                    tr (if isHome then TranslationKey.VenueHome else TranslationKey.VenueAway)
                                                )
                                                TextBlock.fontSize 12.0
                                                TextBlock.foreground (if isHome then Theme.AccentAlt else Theme.Warning)
                                                TextBlock.horizontalAlignment HorizontalAlignment.Right ] ] ]
                              Border.create
                                  [ Grid.column 6
                                    Border.horizontalAlignment HorizontalAlignment.Right
                                    Border.verticalAlignment VerticalAlignment.Center
                                    Border.child outcome ] ] ]
              ) ]
        :> IView

    let view (state: State) (_dispatch: Msg -> unit) =
        match state.Mode with
        | InGame(gs, _) ->
            let fixtures =
                gs.Competitions
                |> Map.toList
                |> List.collect (fun (_, comp) -> comp.Fixtures |> Map.toList |> List.map (fun (_, f) -> comp.Name, f))
                |> List.filter (fun (_, fixture) -> MatchFixture.involves gs.UserClubId fixture)
                |> List.sortBy (fun (_, fixture) -> fixture.ScheduledDate)

            let nextDate =
                fixtures
                |> List.tryFind (fun (_, fixture) -> MatchFixture.isPending fixture)
                |> Option.map (fun (_, fixture) -> fixture.ScheduledDate)

            let months =
                fixtures |> List.groupBy (fun (_, fixture) -> fixture.ScheduledDate.Year, fixture.ScheduledDate.Month)

            ScrollViewer.create
                [ ScrollViewer.horizontalScrollBarVisibility Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                  ScrollViewer.content (
                      StackPanel.create
                          [ StackPanel.margin (32.0, 8.0, 32.0, 32.0)
                            StackPanel.maxWidth 1100.0
                            StackPanel.children
                                [ UI.display (tr TranslationKey.CalendarTitle) 44.0 Theme.TextMain
                                  TextBlock.create
                                      [ TextBlock.text (
                                            Localization.resolve
                                                TranslationKey.CalendarSummary
                                                [ "season", string gs.Season; "count", string fixtures.Length ]
                                        )
                                        TextBlock.fontSize 14.0
                                        TextBlock.margin (0.0, 6.0, 0.0, 8.0)
                                        TextBlock.foreground Theme.TextMuted ]
                                  for (year, month), items in months do
                                      Border.create
                                          [ Border.margin (4.0, 18.0, 0.0, 10.0)
                                            Border.child (
                                                UI.eyebrow
                                                    (System.DateTime(year, month, 1).ToString("MMMM yyyy", culture ()))
                                                    Theme.TextDim
                                            ) ]

                                      for competition, fixture in items do
                                          fixtureCard gs competition fixture (Some fixture.ScheduledDate = nextDate) ] ]
                  ) ]
            :> IView
        | _ -> TextBlock.create [ TextBlock.text (tr TranslationKey.NoCareerLoaded) ] :> IView
