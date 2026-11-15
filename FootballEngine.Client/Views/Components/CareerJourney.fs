namespace FootballEngine.Components

open System.Globalization
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Layout
open Avalonia.Media
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.AppMsgs
open FootballEngine.Domain
open FootballEngine.Routing
open FootballEngine.Icons

module CareerJourney =
    let private tr key = Localization.resolve key []

    let private date (value: System.DateTime) =
        let culture =
            CultureInfo.GetCultureInfo(
                if Localization.active () = Locale.Es then
                    "es-AR"
                else
                    "en-GB"
            )

        value.ToString("ddd dd MMM", culture)

    let continueLabel state =
        match CareerFlow.continuation state with
        | CareerFlow.PrepareMatch _ -> tr TranslationKey.PrepareNextMatch
        | CareerFlow.ReturnToMatch(_, Postmatch) -> tr TranslationKey.MatchPostMatch
        | CareerFlow.ReturnToMatch _ -> tr TranslationKey.MatchResume
        | CareerFlow.AdvanceDay tomorrow -> Localization.resolve TranslationKey.ContinueToDate [ "date", date tomorrow ]
        | CareerFlow.Working -> tr TranslationKey.JourneyWorking
        | CareerFlow.Unavailable -> tr TranslationKey.Continue

    let canContinue state =
        match CareerFlow.continuation state with
        | CareerFlow.Working
        | CareerFlow.Unavailable -> false
        | _ -> true

    let private text size (color: string) value =
        TextBlock.create
            [ TextBlock.text value
              TextBlock.fontSize size
              TextBlock.foreground color
              TextBlock.textWrapping TextWrapping.Wrap ]
        :> IView

    let private actions children =
        WrapPanel.create [ WrapPanel.children children ] :> IView

    let private button label primary enabled action =
        let state =
            if enabled then
                UI.ControlState.Default
            else
                UI.ControlState.Disabled(tr TranslationKey.DisabledTitle)

        let view =
            if primary then
                UI.primaryButtonWithState label None state (fun _ -> action ())
            else
                UI.secondaryButton label None state (fun _ -> action ())

        Border.create [ Border.margin (0.0, 0.0, 8.0, 8.0); Border.child view ] :> IView

    let private panel title children =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.BorderLight
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding 20.0
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 12.0
                        StackPanel.children [ text 12.0 Theme.Accent title; yield! children ] ]
              ) ]
        :> IView

    let private lineupDetails gs =
        let readiness = CareerFlow.lineupReadiness gs
        let empty = max readiness.EmptySlots (11 - readiness.SlotCount)

        [ text
              16.0
              (if readiness.IsReady then Theme.TextMain else Theme.Warning)
              (tr (
                  if readiness.IsReady then
                      TranslationKey.LineupReady
                  else
                      TranslationKey.LineupNeedsAttention
              ))
          text
              13.0
              Theme.TextSub
              (Localization.resolve
                  TranslationKey.LineupCount
                  [ "count", string (readiness.SlotCount - readiness.EmptySlots) ])
          if empty > 0 then
              text 13.0 Theme.Warning (Localization.resolve TranslationKey.LineupEmpty [ "count", string empty ])
          if not readiness.UnavailableStarters.IsEmpty then
              text
                  13.0
                  Theme.Warning
                  (Localization.resolve
                      TranslationKey.LineupUnavailable
                      [ "names", String.concat ", " readiness.UnavailableStarters ])
          if readiness.HasDuplicates then
              text 13.0 Theme.Warning (tr TranslationKey.LineupDuplicates) ]

    let private fixtureTitle (gs: GameState) (fixture: MatchFixture) =
        let name id =
            gs.Clubs |> Map.tryFind id |> Option.map _.Name |> Option.defaultValue "—"

        $"{name fixture.HomeClubId} · {name fixture.AwayClubId} — {date fixture.ScheduledDate}"

    let private chip (label: string) (foreground: string) (background: string) : IView =
        Border.create
            [ Border.background background
              Border.cornerRadius Theme.RadiusPill
              Border.padding (12.0, 6.0)
              Border.horizontalAlignment HorizontalAlignment.Left
              Border.child (
                  TextBlock.create
                      [ TextBlock.text (label.ToUpperInvariant())
                        TextBlock.fontSize 13.0
                        TextBlock.fontWeight FontWeight.Bold
                        TextBlock.foreground foreground ]
              ) ]
        :> IView

    /// What the rival's staff actually learned about the user's side, from its persisted memory.
    let private rivalCard (gs: GameState) (rivalId: ClubId) dispatch : IView =
        let memory =
            gs.Clubs
            |> Map.tryFind rivalId
            |> Option.bind (fun rival -> Map.tryFind gs.UserClubId rival.ScoutingMemory)
            |> Option.filter (fun memory -> memory.MatchesSeen > 0.0)

        let tags (pattern: PatternVector) =
            [ let longest =
                  [ pattern.PassShort, TranslationKey.StyleShortPassing
                    pattern.PassMedium, TranslationKey.StyleMixedPassing
                    pattern.PassLong, TranslationKey.StyleLongBalls ]
                  |> List.maxBy fst

              yield tr (snd longest)

              if pattern.Directness > 0.55 then
                  yield tr TranslationKey.StyleDirect

              if pattern.CrossRate > 0.35 then
                  yield tr TranslationKey.StyleCrosses ]

        let title, body, chips =
            match memory with
            | Some memory ->
                let pattern = memory.Weighted

                let corridor =
                    [ pattern.CorridorLeft, TranslationKey.CorridorLeft
                      pattern.CorridorCenter, TranslationKey.CorridorCenter
                      pattern.CorridorRight, TranslationKey.CorridorRight ]
                    |> List.maxBy fst
                    |> snd
                    |> tr

                tr TranslationKey.RivalStudiedTitle,
                Localization.resolve
                    TranslationKey.RivalStudiedBody
                    [ "count", string (int (round memory.MatchesSeen)); "corridor", corridor ],
                tags pattern
            | None -> tr TranslationKey.RivalUnknownTitle, tr TranslationKey.RivalUnknownBody, []

        Border.create
            [ Border.background (Theme.withAlpha "8C" Theme.BgMain)
              Border.borderBrush (Theme.withAlpha "55" Theme.Hot)
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding (20.0, 18.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 12.0
                        StackPanel.children
                            [ StackPanel.create
                                  [ StackPanel.orientation Orientation.Horizontal
                                    StackPanel.spacing 10.0
                                    StackPanel.children
                                        [ Icons.icon Material.Icons.MaterialIconKind.History 20.0 Theme.Hot
                                          TextBlock.create
                                              [ TextBlock.text title
                                                TextBlock.fontSize 15.0
                                                TextBlock.fontWeight FontWeight.Bold
                                                TextBlock.foreground Theme.TextMain
                                                TextBlock.verticalAlignment VerticalAlignment.Center ] ] ]
                              text 14.0 Theme.TextSub body
                              if not chips.IsEmpty then
                                  WrapPanel.create
                                      [ WrapPanel.children
                                            [ for label in chips ->
                                                  Border.create
                                                      [ Border.background (Theme.withAlpha "26" Theme.Hot)
                                                        Border.cornerRadius Theme.RadiusSm
                                                        Border.padding (10.0, 5.0)
                                                        Border.margin (0.0, 0.0, 8.0, 0.0)
                                                        Border.child (
                                                            TextBlock.create
                                                                [ TextBlock.text label
                                                                  TextBlock.fontSize 13.0
                                                                  TextBlock.fontWeight FontWeight.SemiBold
                                                                  TextBlock.foreground "#FFB27A" ]
                                                        ) ] ] ]
                              Button.create
                                  [ Button.background "Transparent"
                                    Button.borderThickness 0.0
                                    Button.padding 0.0
                                    Button.horizontalAlignment HorizontalAlignment.Left
                                    Button.onClick (fun _ -> dispatch (Navigate(Route.Tactics TacticsRoute.Overview)))
                                    Button.content (
                                        TextBlock.create
                                            [ TextBlock.text (tr TranslationKey.AdjustTactics)
                                              TextBlock.fontSize 14.0
                                              TextBlock.fontWeight FontWeight.SemiBold
                                              TextBlock.foreground Theme.Accent ]
                                    ) ] ] ]
              ) ]
        :> IView

    /// The rival head coach before the match: who he is, how well he reads the game, what happened between the two
    /// coaches and how he will set up against the user.
    let private coachCard (es: bool) (vm: RivalCoachPresenter.RivalCoachVM) : IView =
        let label value =
            TextBlock.create
                [ TextBlock.text value
                  TextBlock.fontSize 11.0
                  TextBlock.fontWeight FontWeight.SemiBold
                  TextBlock.foreground Theme.TextMuted ]
            :> IView

        let stars =
            StackPanel.create
                [ StackPanel.orientation Orientation.Horizontal
                  StackPanel.spacing 2.0
                  StackPanel.children
                      [ for index in 1..5 ->
                            Icons.icon
                                Material.Icons.MaterialIconKind.Star
                                14.0
                                (if index <= vm.Stars then
                                     Theme.Warning
                                 else
                                     Theme.withAlpha "55" Theme.TextDim) ] ]
            :> IView

        let record =
            match vm.RecordVs with
            | RivalCoachPresenter.FirstMeeting -> if es then "Primer cruce" else "First meeting"
            | RivalCoachPresenter.Meetings(played, won, drawn, lost) ->
                if es then
                    $"PJ {played} · G {won} · E {drawn} · P {lost}"
                else
                    $"P {played} · W {won} · D {drawn} · L {lost}"

        let column (title: string) (body: IView) =
            StackPanel.create
                [ StackPanel.spacing 4.0
                  StackPanel.margin (0.0, 0.0, 28.0, 0.0)
                  StackPanel.children [ label (title.ToUpperInvariant()); body ] ]
            :> IView

        Border.create
            [ Border.background (Theme.withAlpha "8C" Theme.BgMain)
              Border.borderBrush (Theme.withAlpha "55" Theme.Accent)
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding (20.0, 16.0)
              Border.child (
                  StackPanel.create
                      [ StackPanel.spacing 10.0
                        StackPanel.children
                            [ WrapPanel.create
                                  [ WrapPanel.children
                                        [ Icons.icon Material.Icons.MaterialIconKind.Whistle 20.0 Theme.Accent
                                          Border.create
                                              [ Border.margin (10.0, 0.0, 12.0, 0.0)
                                                Border.child (
                                                    TextBlock.create
                                                        [ TextBlock.text vm.CoachName
                                                          TextBlock.fontSize 16.0
                                                          TextBlock.fontWeight FontWeight.Bold
                                                          TextBlock.foreground Theme.TextMain
                                                          TextBlock.verticalAlignment VerticalAlignment.Center ]
                                                ) ]
                                          chip vm.Archetype Theme.Accent (Theme.withAlpha "24" Theme.Accent) ] ]
                              text 13.0 Theme.TextSub vm.Behaviour
                              WrapPanel.create
                                  [ WrapPanel.children
                                        [ column (if es then "Lectura de juego" else "Game reading") stars
                                          column
                                              (if es then "Historial contra vos" else "Record against you")
                                              (text 13.0 Theme.TextMain record) ] ]
                              column
                                  (if es then "Cómo te va a jugar" else "How he'll play you")
                                  (text 13.0 "#FFB27A" $"{vm.Formation} · {vm.PlanReason}") ] ]
              ) ]
        :> IView

    /// The plan preview scores every formation for the rival's squad, which is too much to repeat on each render of
    /// the home page; the inputs are immutable values, so one entry keyed by reference is enough.
    let mutable private coachCache: (obj * obj * bool * RivalCoachPresenter.RivalCoachVM option) option =
        None

    let private coachFor (es: bool) (settings: FootballEngine.Settings.SaveSettings) (gs: GameState) =
        match coachCache with
        | Some(cachedGs, cachedSettings, cachedEs, vm) when
            obj.ReferenceEquals(cachedGs, gs)
            && obj.ReferenceEquals(cachedSettings, settings)
            && cachedEs = es
            ->
            vm
        | _ ->
            let cfg =
                (FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave (Some settings)).RivalManager

            let vm = RivalCoachPresenter.project es cfg gs
            coachCache <- Some(box gs, box settings, es, vm)
            vm

    let private teamName (gs: GameState) (clubId: ClubId) =
        gs.Clubs |> Map.tryFind clubId |> Option.map _.Name |> Option.defaultValue "—"

    /// Match-day hero: the fixture in scoreboard type, lit like the pitch, with the rival's read beside it.
    let home (state: State) dispatch =
        match state.Mode with
        | InGame(gs, _) ->
            let pending = Inbox.pendingActionCount gs.Inbox
            let next = GameState.getUserNextFixture gs

            let headline: IView list =
                match next with
                | Some(_, fixture) ->
                    let isToday = fixture.ScheduledDate.Date = gs.CurrentDate.Date
                    let isHome = fixture.HomeClubId = gs.UserClubId

                    let team (clubId: ClubId) (color: string) =
                        StackPanel.create
                            [ StackPanel.orientation Orientation.Horizontal
                              StackPanel.spacing 14.0
                              StackPanel.children
                                  [ UI.crest (ClubIdentity.kitOf gs clubId) 46.0
                                    UI.display (teamName gs clubId) 52.0 color ] ]
                        :> IView

                    [ (if isToday then
                           chip (tr TranslationKey.MatchdayBadge) Theme.Accent (Theme.withAlpha "24" Theme.Accent)
                       else
                           chip
                               (Localization.resolve
                                   TranslationKey.NextMatchBadge
                                   [ "date", date fixture.ScheduledDate ])
                               Theme.TextSub
                               (Theme.withAlpha "1F" Theme.TextMain))
                      WrapPanel.create
                          [ WrapPanel.children
                                [ Border.create
                                      [ Border.margin (0.0, 0.0, 22.0, 6.0)
                                        Border.child (team fixture.HomeClubId Theme.TextMain) ]
                                  Border.create
                                      [ Border.margin (0.0, 0.0, 22.0, 6.0)
                                        Border.verticalAlignment VerticalAlignment.Center
                                        Border.child (UI.display (tr TranslationKey.Versus) 28.0 Theme.TextDim) ]
                                  Border.create
                                      [ Border.margin (0.0, 0.0, 0.0, 6.0)
                                        Border.child (team fixture.AwayClubId "#CFE0D6") ] ] ]
                      text
                          15.0
                          "#B9C7BF"
                          ($"{date fixture.ScheduledDate} · "
                           + tr (
                               if isHome then
                                   TranslationKey.VenueHome
                               else
                                   TranslationKey.VenueAway
                           )) ]
                | None -> [ text 18.0 Theme.TextMain (tr TranslationKey.NoScheduledFixture) ]

            let hint =
                tr (
                    if state.Match.PendingResult.IsSome then
                        TranslationKey.ResultAwaitingConfirmation
                    elif state.Match.RuntimeId.IsSome then
                        TranslationKey.MatchSessionHint
                    elif (CareerFlow.todayFixture gs).IsSome then
                        TranslationKey.PreparationHint
                    else
                        TranslationKey.AdvanceNextEventHint
                )

            let coach = coachFor (Localization.active () = Locale.Es) state.SaveSettings gs

            let left =
                StackPanel.create
                    [ StackPanel.spacing 14.0
                      StackPanel.children
                          [ yield! headline
                            match coach with
                            | Some vm -> coachCard (Localization.active () = Locale.Es) vm
                            | None -> ()
                            yield! lineupDetails gs
                            actions
                                [ button (tr TranslationKey.ReviewLineup) false (not state.IsProcessing) (fun () ->
                                      dispatch (Navigate(Route.Tactics TacticsRoute.Overview)))
                                  if pending > 0 then
                                      button
                                          (Localization.resolve
                                              TranslationKey.PendingDecisions
                                              [ "count", string pending ])
                                          false
                                          true
                                          (fun () ->
                                              dispatch (InboxMsg(SetRequiresActionOnly true))
                                              dispatch (Navigate(Route.Inbox(InboxRoute None))))
                                  if state.AdvanceTarget.IsSome then
                                      button (tr TranslationKey.StopAfterDay) false true (fun () ->
                                          dispatch (SimMsg CancelAdvance))
                                  elif
                                      next
                                      |> Option.exists (fun (_, fixture) ->
                                          fixture.ScheduledDate.Date > gs.CurrentDate.Date)
                                  then
                                      button (tr TranslationKey.AdvanceNextEvent) false (canContinue state) (fun () ->
                                          dispatch (SimMsg AdvanceToNextEvent))
                                  match CareerFlow.lastResult gs with
                                  | Some(id, _) ->
                                      button (tr TranslationKey.OpenLastResult) false true (fun () ->
                                          dispatch (Navigate(Route.Match(id, Postmatch))))
                                  | None -> () ]
                            text 13.0 "#9FB3A8" hint ] ]

            let rival =
                next
                |> Option.map (fun (_, fixture) ->
                    let rivalId =
                        if fixture.HomeClubId = gs.UserClubId then
                            fixture.AwayClubId
                        else
                            fixture.HomeClubId

                    rivalCard gs rivalId dispatch)

            Border.create
                [ Border.background (Theme.spotlightBrush "#2E7A46" "#154A2C" "#0F2240")
                  Border.borderBrush "#3F8A57"
                  Border.borderThickness 1.0
                  Border.cornerRadius (Theme.RadiusXl + 2.0)
                  Border.boxShadow (BoxShadow.Parse "0 18 50 0 #402E7A46")
                  Border.padding (32.0, 28.0)
                  Border.child (
                      Grid.create
                          [ Grid.columnDefinitions (if rival.IsSome then "*, 28, 360" else "*")
                            Grid.children
                                [ Border.create [ Grid.column 0; Border.child left ]
                                  match rival with
                                  | Some card ->
                                      Border.create
                                          [ Grid.column 2
                                            Border.verticalAlignment VerticalAlignment.Center
                                            Border.child card ]
                                  | None -> () ] ]
                  ) ]
            :> IView
        | _ -> Border.create [] :> IView

    let preparation (state: State) matchId dispatch =
        match state.Mode with
        | InGame(gs, _) ->
            let fixture =
                gs.Competitions
                |> Map.toSeq
                |> Seq.tryPick (fun (_, competition) -> Map.tryFind matchId competition.Fixtures)

            let readiness = CareerFlow.lineupReadiness gs

            let canStart =
                not state.IsProcessing
                && readiness.IsReady
                && (fixture
                    |> Option.exists (fun fixture ->
                        not fixture.Played
                        && fixture.ScheduledDate.Date = gs.CurrentDate.Date
                        && MatchFixture.involves gs.UserClubId fixture))

            ScrollViewer.create
                [ ScrollViewer.content (
                      StackPanel.create
                          [ StackPanel.margin 24.0
                            StackPanel.spacing 16.0
                            StackPanel.children
                                [ panel
                                      (tr TranslationKey.BeforeKickoff)
                                      [ match fixture with
                                        | Some fixture -> text 24.0 Theme.TextMain (fixtureTitle gs fixture)
                                        | None -> text 16.0 Theme.Warning (tr TranslationKey.NoScheduledFixture)
                                        text 14.0 Theme.TextSub (tr TranslationKey.PreparationHint)
                                        yield! lineupDetails gs
                                        match TacticsSlice.Query.currentLineup gs with
                                        | Some lineup ->
                                            text
                                                14.0
                                                Theme.TextMain
                                                (Localization.resolve
                                                    TranslationKey.CurrentPlan
                                                    [ "formation", string lineup.Formation ])
                                        | None -> ()
                                        actions
                                            [ button (tr TranslationKey.StartPreparedMatch) true canStart (fun () ->
                                                  dispatch (Navigate(Route.Match(matchId, Live))))
                                              button (tr TranslationKey.ReviewLineup) false true (fun () ->
                                                  dispatch (Navigate(Route.Tactics TacticsRoute.Overview)))
                                              button (tr TranslationKey.ReturnHome) false true (fun () ->
                                                  dispatch (Navigate Route.Home)) ] ] ] ]
                  ) ]
            :> IView
        | _ -> Border.create [] :> IView

    let review (state: State) matchId dispatch =
        match state.Mode with
        | InGame(gs, _) ->
            let pending =
                state.Match.PendingResult
                |> Option.filter (fun result -> result.MatchId = matchId)

            let fixture =
                gs.Competitions
                |> Map.toSeq
                |> Seq.tryPick (fun (_, competition) -> Map.tryFind matchId competition.Fixtures)

            let score, events =
                match pending, fixture with
                | Some result, _ -> $"{result.HomeScore} – {result.AwayScore}", result.Events
                | None, Some fixture when fixture.Played ->
                    $"{fixture.HomeScore |> Option.defaultValue 0} – {fixture.AwayScore |> Option.defaultValue 0}",
                    fixture.Events
                | _ -> "—", []

            ScrollViewer.create
                [ ScrollViewer.content (
                      StackPanel.create
                          [ StackPanel.margin 24.0
                            StackPanel.spacing 16.0
                            StackPanel.children
                                [ panel
                                      (tr (
                                          if pending.IsSome then
                                              TranslationKey.ResultAwaitingConfirmation
                                          else
                                              TranslationKey.ResultRecorded
                                      ))
                                      [ match fixture with
                                        | Some fixture -> text 22.0 Theme.TextMain (fixtureTitle gs fixture)
                                        | None -> ()
                                        text 40.0 Theme.TextMain score
                                        text 14.0 Theme.TextSub (tr TranslationKey.MatchFacts)
                                        match CareerFlow.shotFacts state matchId events with
                                        | Some(homeShots, awayShots, homeXg, awayXg) ->
                                            text
                                                16.0
                                                Theme.TextMain
                                                ($"{tr TranslationKey.MatchShots}: {homeShots} – {awayShots}")

                                            text 16.0 Theme.TextMain ($"xG: {homeXg:F2} – {awayXg:F2}")
                                            text 12.0 Theme.TextMuted (tr TranslationKey.MatchEstimateHint)
                                        | None -> ()
                                        text
                                            13.0
                                            Theme.TextSub
                                            (Localization.resolve
                                                TranslationKey.RecordedEvents
                                                [ "count", string events.Length ])
                                        match state.Match.Error with
                                        | Some error -> text 13.0 Theme.Warning error
                                        | None -> ()
                                        actions
                                            [ if pending.IsSome then
                                                  button
                                                      (tr TranslationKey.ConfirmMatchResult)
                                                      true
                                                      (not state.IsProcessing)
                                                      (fun () -> dispatch (LiveMsg LiveMsg.ConfirmFinish))
                                              else
                                                  button (continueLabel state) true (canContinue state) (fun () ->
                                                      dispatch (SimMsg ContinueCareer))

                                                  button (tr TranslationKey.ReviewLineup) false true (fun () ->
                                                      dispatch (Navigate(Route.Tactics TacticsRoute.Overview)))
                                              button (tr TranslationKey.ReturnHome) false true (fun () ->
                                                  dispatch (Navigate Route.Home)) ] ] ] ]
                  ) ]
            :> IView
        | _ -> Border.create [] :> IView
