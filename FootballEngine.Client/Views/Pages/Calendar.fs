namespace FootballEngine.Pages

open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Avalonia.Media
open FootballEngine
open FootballEngine.AppTypes
open FootballEngine.AppMsgs
open FootballEngine.Domain

module Calendar =
    let private fixtureLine (names: Map<ClubId, string>) (_, fixture: MatchFixture) =
        let home =
            names
            |> Map.tryFind fixture.HomeClubId
            |> Option.defaultValue (string fixture.HomeClubId)

        let away =
            names
            |> Map.tryFind fixture.AwayClubId
            |> Option.defaultValue (string fixture.AwayClubId)

        let result =
            match fixture.HomeScore, fixture.AwayScore with
            | Some h, Some a -> $"  {h}-{a}"
            | _ -> "  pendiente"

        let dateText = fixture.ScheduledDate.ToString("dd/MM")

        TextBlock.create [ TextBlock.text $"{dateText}  {home} - {away}{result}" ] :> IView

    let view (state: State) (_dispatch: Msg -> unit) =
        match state.Mode with
        | InGame(gs, _) ->
            let names = gs.Clubs |> Map.map (fun _ club -> club.Name)

            let fixtures =
                gs.Competitions
                |> Map.toList
                |> List.collect (fun (_, comp) -> comp.Fixtures |> Map.toList |> List.map (fun (_, f) -> comp.Name, f))
                |> List.filter (fun (_, fixture) -> MatchFixture.involves gs.UserClubId fixture)
                |> List.sortBy (fun (_, fixture) -> fixture.ScheduledDate)
                |> List.truncate 20

            ScrollViewer.create
                [ ScrollViewer.padding 28.0
                  ScrollViewer.content (
                      StackPanel.create
                          [ StackPanel.spacing 12.0
                            StackPanel.children
                                [ TextBlock.create
                                      [ TextBlock.text "CALENDARIO"
                                        TextBlock.fontSize 24.0
                                        TextBlock.foreground Theme.TextMain ]
                                  TextBlock.create
                                      [ TextBlock.text $"Temporada {gs.Season} · {fixtures.Length} próximos compromisos"
                                        TextBlock.foreground Theme.TextMuted ]
                                  yield!
                                      fixtures
                                      |> List.map (fun (name, fixture) ->
                                          TextBlock.create [ TextBlock.text name; TextBlock.foreground Theme.Accent ]
                                          :> IView)
                                  yield! fixtures |> List.map (fixtureLine names) ] ]
                  ) ]
            :> IView
        | _ -> TextBlock.create [ TextBlock.text "No hay una carrera cargada" ] :> IView
