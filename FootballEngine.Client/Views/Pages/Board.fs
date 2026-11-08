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

module Board =
    let private tr key = Localization.resolve key []

    let private pulse = WorldPulseConfig.defaultWorldPulse

    /// Thresholds come from the same config the simulation uses, so the gauge never disagrees with the sacking logic.
    let confidenceZone (confidence: int) : TranslationKey * string =
        if confidence < pulse.UltimatumThreshold then
            TranslationKey.BoardZoneUltimatum, Theme.Danger
        elif confidence < pulse.BoardWarningThreshold then
            TranslationKey.BoardZoneWarning, Theme.Warning
        else
            TranslationKey.BoardZoneSafe, Theme.Success

    let fansBand (mood: int) : TranslationKey * string =
        if mood < pulse.FansFuriousBelow then
            TranslationKey.FansBandFurious, Theme.Danger
        elif mood < pulse.FansRestlessBelow then
            TranslationKey.FansBandRestless, Theme.Warning
        elif mood >= pulse.FansEuphoricFrom then
            TranslationKey.FansBandEuphoric, Theme.Accent
        else
            TranslationKey.FansBandContent, Theme.Success

    let objectiveKey (objective: BoardObjective) : TranslationKey =
        match objective with
        | LeagueObjective Survival -> TranslationKey.ObjectiveSurvival
        | LeagueObjective MidTable -> TranslationKey.ObjectiveMidTable
        | LeagueObjective TopHalf -> TranslationKey.ObjectiveTopHalf
        | LeagueObjective TopFour -> TranslationKey.ObjectiveTopFour
        | LeagueObjective WinLeague -> TranslationKey.ObjectiveWinLeague
        | CupObjective WinDomesticCup -> TranslationKey.ObjectiveDomesticCup
        | CupObjective WinContinentalCup -> TranslationKey.ObjectiveContinentalCup
        | CupObjective WinChampionsLeague -> TranslationKey.ObjectiveChampionsLeague
        | BoardObjective.Promotion -> TranslationKey.ObjectivePromotion
        | BoardObjective.Relegation -> TranslationKey.ObjectiveRelegation

    let private gauge (title: TranslationKey) (value: int) (verdict: TranslationKey, color: string) : IView =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding (22.0, 18.0)
              Border.margin (0.0, 0.0, 12.0, 12.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 10.0
                        StackPanel.children
                            [ UI.eyebrow (tr title) Theme.TextDim
                              StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 12.0
                                    StackPanel.children
                                        [ UI.display (string value) 56.0 color
                                          Border.create
                                              [ Border.verticalAlignment VerticalAlignment.Center
                                                Border.child (UI.badge ((tr verdict).ToUpperInvariant()) color) ] ] ]
                              ProgressBar.create
                                  [ ProgressBar.minimum 0.0
                                    ProgressBar.maximum 100.0
                                    ProgressBar.value (float value)
                                    ProgressBar.height 10.0
                                    ProgressBar.minHeight 10.0
                                    ProgressBar.cornerRadius 5.0
                                    ProgressBar.background Theme.Border
                                    ProgressBar.foreground color ] ] ]
              ) ]
        :> IView

    let private resultChip (outcome: MatchOutcomeTag) : IView =
        let letter, color =
            match outcome with
            | MatchOutcomeTag.Won -> tr TranslationKey.ResultLetterWin, Theme.Success
            | MatchOutcomeTag.Drawn -> tr TranslationKey.ResultLetterDraw, Theme.Warning
            | MatchOutcomeTag.Lost -> tr TranslationKey.ResultLetterLoss, Theme.Danger

        Border.create
            [ Border.width 34.0
              Border.height 34.0
              Border.cornerRadius 17.0
              Border.background (Theme.withAlpha "33" color)
              Border.borderBrush (Theme.withAlpha "88" color)
              Border.borderThickness 1.0
              Border.child (
                  TextBlock.create
                      [ TextBlock.text letter
                        TextBlock.fontSize 14.0
                        TextBlock.fontWeight FontWeight.Bold
                        TextBlock.foreground color
                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                        TextBlock.verticalAlignment VerticalAlignment.Center ]
              ) ]
        :> IView

    let private panel (title: string) (body: IView) : IView =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding (22.0, 18.0)
              Border.margin (0.0, 0.0, 12.0, 12.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 12.0
                        StackPanel.children [ UI.eyebrow title Theme.TextDim; body ] ]
              ) ]
        :> IView

    /// Pressure news about the user's club, newest first.
    let pressureNews (gs: GameState) : NewsItem list =
        gs.News
        |> List.filter (fun item ->
            item.Scope = NewsScope.UserClub
            && match item.Kind with
               | NewsKind.BoardUnrest(club, _)
               | NewsKind.FansMood(club, _)
               | NewsKind.ManagerSacked(club, _, _) -> club = gs.UserClubId
               | _ -> false)
        |> List.truncate 5

    let view (state: State) (_dispatch: Msg -> unit) : IView =
        match state.Mode with
        | InGame(gs, _) ->
            match gs.Clubs |> Map.tryFind gs.UserClubId with
            | None -> TextBlock.create [ TextBlock.text (tr TranslationKey.NoCareerLoaded) ] :> IView
            | Some club ->
                let recent = club.RecentResults |> List.truncate 8
                let news = pressureNews gs

                ScrollViewer.create
                    [ ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Disabled
                      ScrollViewer.content (
                          StackPanel.create
                              [ StackPanel.margin (32.0, 8.0, 32.0, 32.0)
                                StackPanel.maxWidth 1100.0
                                StackPanel.children
                                    [ UI.display (tr TranslationKey.Board) 44.0 Theme.TextMain
                                      TextBlock.create
                                          [ TextBlock.text (tr TranslationKey.BoardSubtitle)
                                            TextBlock.fontSize 14.0
                                            TextBlock.margin (0.0, 6.0, 0.0, 14.0)
                                            TextBlock.foreground Theme.TextMuted ]
                                      UniformGrid.create
                                          [ UniformGrid.columns 2
                                            UniformGrid.children
                                                [ gauge
                                                      TranslationKey.BoardConfidenceTitle
                                                      club.BoardConfidence
                                                      (confidenceZone club.BoardConfidence)
                                                  gauge
                                                      TranslationKey.BoardFansTitle
                                                      club.FansMood
                                                      (fansBand club.FansMood) ] ]
                                      panel
                                          (tr TranslationKey.BoardObjectiveTitle)
                                          (TextBlock.create
                                              [ TextBlock.text (tr (objectiveKey club.BoardObjective))
                                                TextBlock.fontSize 20.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.foreground Theme.TextMain ])
                                      panel
                                          (tr TranslationKey.BoardRecentForm)
                                          (if recent.IsEmpty then
                                               TextBlock.create
                                                   [ TextBlock.text (tr TranslationKey.BoardNoResults)
                                                     TextBlock.fontSize 14.0
                                                     TextBlock.foreground Theme.TextMuted ]
                                               :> IView
                                           else
                                               StackPanel.create
                                                   [ StackPanel.orientation Orientation.Horizontal
                                                     StackPanel.spacing 8.0
                                                     StackPanel.children (recent |> List.map resultChip) ]
                                               :> IView)
                                      panel
                                          (tr TranslationKey.BoardPressureNews)
                                          (if news.IsEmpty then
                                               TextBlock.create
                                                   [ TextBlock.text (tr TranslationKey.BoardNoPressure)
                                                     TextBlock.fontSize 14.0
                                                     TextBlock.foreground Theme.TextMuted ]
                                               :> IView
                                           else
                                               StackPanel.create
                                                   [ StackPanel.spacing 4.0
                                                     StackPanel.children (news |> List.map (News.card gs)) ]
                                               :> IView) ] ]
                      ) ]
                :> IView
        | _ -> TextBlock.create [ TextBlock.text (tr TranslationKey.NoCareerLoaded) ] :> IView
