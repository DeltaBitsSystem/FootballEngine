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
open FootballEngine.World.Pulse

/// The unemployed manager's home: who wants him, on what terms, and the career that brought him here.
module Jobs =
    let private tr key = Localization.resolve key []

    let private clubName (gs: GameState) (clubId: ClubId) =
        gs.Clubs |> Map.tryFind clubId |> Option.map _.Name |> Option.defaultValue ""

    let private muted (text: string) : IView =
        TextBlock.create
            [ TextBlock.text text
              TextBlock.fontSize 14.0
              TextBlock.textWrapping TextWrapping.Wrap
              TextBlock.foreground Theme.TextMuted ]
        :> IView

    let private card (child: IView) : IView =
        Border.create
            [ Border.background Theme.BgCard
              Border.borderBrush Theme.Border
              Border.borderThickness 1.0
              Border.cornerRadius Theme.RadiusLg
              Border.padding (22.0, 18.0)
              Border.margin (0.0, 0.0, 0.0, 12.0)
              Border.child child ]
        :> IView

    let private offerCard (gs: GameState) (dispatch: Msg -> unit) (offer: JobOffer) : IView =
        let kit = ClubIdentity.kitOf gs offer.ClubId

        let league =
            match ClubIdentity.leagueStanding gs offer.ClubId with
            | Some(name, Some position) -> $"{position}.º · {name}"
            | Some(name, None) -> name
            | None -> ""

        let confidence =
            gs.Clubs
            |> Map.tryFind offer.ClubId
            |> Option.map _.BoardConfidence
            |> Option.defaultValue 0

        let daysLeft = max 0 (int (offer.Expires.Date - gs.CurrentDate.Date).TotalDays)

        card (
            DockPanel.create
                [ DockPanel.lastChildFill true
                  DockPanel.children
                      [ StackPanel.create
                            [ DockPanel.dock Dock.Right
                              StackPanel.spacing 8.0
                              StackPanel.verticalAlignment VerticalAlignment.Center
                              StackPanel.children
                                  [ UI.primaryButtonWithState (tr TranslationKey.JobsAccept) None UI.Default (fun _ ->
                                        dispatch (CareerMsg(AcceptJobOffer offer.Id)))
                                    UI.secondaryButton (tr TranslationKey.JobsDecline) None UI.Default (fun _ ->
                                        dispatch (CareerMsg(DeclineJobOffer offer.Id))) ] ]
                        StackPanel.create
                            [ StackPanel.orientation Orientation.Horizontal
                              StackPanel.spacing 16.0
                              StackPanel.children
                                  [ UI.crest kit 56.0
                                    StackPanel.create
                                        [ StackPanel.spacing 4.0
                                          StackPanel.children
                                              [ UI.display (clubName gs offer.ClubId) 26.0 Theme.TextMain
                                                TextBlock.create
                                                    [ TextBlock.text league
                                                      TextBlock.fontSize 13.0
                                                      TextBlock.foreground Theme.TextSub ]
                                                TextBlock.create
                                                    [ TextBlock.text (
                                                          Localization.resolve
                                                              TranslationKey.JobsTerms
                                                              [ "salary", Formatters.money offer.Salary
                                                                "years", string offer.ContractYears ]
                                                      )
                                                      TextBlock.fontSize 14.0
                                                      TextBlock.fontWeight FontWeight.SemiBold
                                                      TextBlock.foreground Theme.TextMain ]
                                                muted (
                                                    Localization.resolve
                                                        TranslationKey.JobsBoardMood
                                                        [ "value", string confidence ]
                                                )
                                                TextBlock.create
                                                    [ TextBlock.text (
                                                          Localization.resolve
                                                              TranslationKey.JobsExpires
                                                              [ "days", string daysLeft ]
                                                      )
                                                      TextBlock.fontSize 12.0
                                                      TextBlock.foreground Theme.Warning ] ] ] ] ] ] ]
        )

    let careerLine (gs: GameState) (event: ManagerCareerEvent) : System.DateTime * string =
        let resolve key club =
            Localization.resolve key [ "club", clubName gs club ]

        match event with
        | ManagerCareerEvent.ManagerHired(date, club) -> date, resolve TranslationKey.CareerHiredEvent club
        | ManagerCareerEvent.ManagerDismissed(date, club, _) -> date, resolve TranslationKey.CareerDismissedEvent club
        | ManagerCareerEvent.ManagerResigned(date, club) -> date, resolve TranslationKey.CareerResignedEvent club
        | ManagerCareerEvent.ObjectiveReviewed(date, club, met, _) ->
            date,
            resolve
                (if met then
                     TranslationKey.CareerObjectiveMet
                 else
                     TranslationKey.CareerObjectiveMissed)
                club

    let private careerPanel (gs: GameState) : IView =
        let reputation =
            GameState.userManager gs |> Option.map _.Reputation |> Option.defaultValue 0

        card (
            StackPanel.create
                [ StackPanel.spacing 10.0
                  StackPanel.children
                      [ UI.eyebrow (tr TranslationKey.JobsCareerTitle) Theme.TextDim
                        TextBlock.create
                            [ TextBlock.text (
                                  Localization.resolve TranslationKey.JobsReputation [ "value", string reputation ]
                              )
                              TextBlock.fontSize 15.0
                              TextBlock.fontWeight FontWeight.SemiBold
                              TextBlock.foreground Theme.TextMain ]
                        for event in gs.CareerHistory |> List.truncate 8 do
                            let date, text = careerLine gs event

                            StackPanel.create
                                [ StackPanel.orientation Orientation.Horizontal
                                  StackPanel.spacing 12.0
                                  StackPanel.children
                                      [ TextBlock.create
                                            [ TextBlock.text (date.ToString "dd/MM/yyyy")
                                              TextBlock.width 86.0
                                              TextBlock.fontSize 13.0
                                              TextBlock.foreground Theme.TextDim ]
                                        TextBlock.create
                                            [ TextBlock.text text
                                              TextBlock.fontSize 13.0
                                              TextBlock.foreground Theme.TextSub ] ] ] ] ]
        )

    let view (state: State) (dispatch: Msg -> unit) : IView =
        match state.Mode with
        | InGame(gs, _) ->
            let offers = JobMarket.activeOffers gs.CurrentDate gs

            ScrollViewer.create
                [ ScrollViewer.horizontalScrollBarVisibility ScrollBarVisibility.Disabled
                  ScrollViewer.content (
                      StackPanel.create
                          [ StackPanel.margin (32.0, 8.0, 32.0, 32.0)
                            StackPanel.maxWidth 1100.0
                            StackPanel.children
                                [ UI.display (tr TranslationKey.JobsTitle) 44.0 Theme.TextMain
                                  TextBlock.create
                                      [ TextBlock.text (
                                            Localization.resolve
                                                TranslationKey.JobsSubtitle
                                                [ "club", clubName gs gs.UserClubId ]
                                        )
                                        TextBlock.fontSize 14.0
                                        TextBlock.textWrapping TextWrapping.Wrap
                                        TextBlock.margin (0.0, 6.0, 0.0, 18.0)
                                        TextBlock.foreground Theme.TextMuted ]
                                  UI.eyebrow (tr TranslationKey.JobsOffersTitle) Theme.TextDim
                                  Border.create [ Border.height 10.0 ]
                                  if offers.IsEmpty then
                                      card (
                                          StackPanel.create
                                              [ StackPanel.spacing 6.0
                                                StackPanel.children
                                                    [ UI.display
                                                          (tr TranslationKey.JobsNoOffersTitle)
                                                          22.0
                                                          Theme.TextMain
                                                      muted (tr TranslationKey.JobsNoOffersSub) ] ]
                                      )
                                  else
                                      for offer in offers do
                                          offerCard gs dispatch offer
                                  careerPanel gs ] ]
                  ) ]
            :> IView
        | _ -> TextBlock.create [ TextBlock.text (tr TranslationKey.NoCareerLoaded) ] :> IView
