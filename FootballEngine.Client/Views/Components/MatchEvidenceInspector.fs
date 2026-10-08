namespace FootballEngine.Components

open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open System
open System.IO
open System.Threading.Tasks
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.Core.Match
open FootballEngine.LiveMatch

module MatchEvidenceInspector =
    let view runtimeId (live: LiveMatchState) dispatch : IView =
        let notify title body =
            dispatch (
                Msg.NotificationMsg(
                    NotificationMsg.PushNotification
                        { Id = 0
                          Icon = Material.Icons.MaterialIconKind.InformationOutline
                          Title = title
                          Body = body
                          IsRead = false }
                )
            )

        let players =
            [| yield "All players", None
               for KeyValue(PlayerId id, name) in live.Fixture.PlayerNames do
                   yield sprintf "%s (#%d)" name id, Some id |]

        let selected =
            players
            |> Array.tryFind (fun (_, id) -> id = live.Ui.SelectedPlayerId)
            |> Option.map fst
            |> Option.defaultValue "All players"

        let filter = live.Ui.EvidenceQuery

        let setFilter query =
            dispatch (Msg.LiveMsg(LiveMsg.SetEvidenceQuery query))

        let captured = live.Evidence |> Option.map _.Entries |> Option.defaultValue [||]

        let choiceControl (choices: (string * 'a option)[]) selected decode onSelected : IView =
            let label =
                choices
                |> Array.tryFind (fun (_, value) -> value = selected)
                |> Option.map fst
                |> Option.defaultValue (fst choices[0])

            ComboBox.create
                [ ComboBox.dataItems (choices |> Array.map fst)
                  ComboBox.selectedItem label
                  ComboBox.horizontalAlignment HorizontalAlignment.Stretch
                  ComboBox.onSelectedItemChanged (fun value ->
                      if not (isNull value) then
                          decode (string value)
                          |> Option.iter (fun choice ->
                              if choice <> selected then
                                  onSelected choice)) ]
        // Live publications may replace dropdown items before the selection callback runs.
        let decodeChoice allLabel prefix wrap (label: string) =
            if label = allLabel then
                Some None
            elif label.StartsWith(prefix, StringComparison.Ordinal) then
                match UInt64.TryParse(label.Substring(prefix.Length)) with
                | true, id -> Some(Some(wrap id))
                | _ -> None
            else
                None

        let actions =
            [| yield "All actions", None
               for ActionId id in
                   Array.append (captured |> Array.choose Observation.actionId) (filter.Action |> Option.toArray)
                   |> Array.distinct do
                   yield sprintf "Action #%d" id, Some(ActionId id) |]

        let events =
            [| yield "All events", None
               for EventId id in
                   Array.append
                       (captured
                        |> Array.choose (fun entry ->
                            match entry.Data with
                            | EventCommitted event -> Some(MatchEvent.id event)
                            | _ -> None))
                       (filter.Event |> Option.toArray)
                   |> Array.distinct do
                   yield sprintf "Event #%d" id, Some(EventId id) |]

        let tickControl tick onChanged : IView =
            NumericUpDown.create
                [ NumericUpDown.value (decimal (int tick))
                  NumericUpDown.minimum 0m
                  NumericUpDown.maximum (decimal Int32.MaxValue)
                  NumericUpDown.increment 1m
                  NumericUpDown.onValueChanged (fun value ->
                      value
                      |> Option.ofNullable
                      |> Option.iter (fun value ->
                          let next = int value * 1<subtick>

                          if next <> tick then
                              onChanged next)) ]

        let controls: IView =
            StackPanel.create
                [ StackPanel.spacing 6.0
                  StackPanel.children
                      [ ComboBox.create
                            [ ComboBox.dataItems (players |> Array.map fst)
                              ComboBox.selectedItem selected
                              ComboBox.horizontalAlignment HorizontalAlignment.Stretch
                              ComboBox.onSelectedItemChanged (fun value ->
                                  if not (isNull value) then
                                      players
                                      |> Array.tryFind (fun (label, _) -> label = string value)
                                      |> Option.iter (fun (_, id) ->
                                          if id <> live.Ui.SelectedPlayerId then
                                              dispatch (Msg.LiveMsg(LiveMsg.SelectPlayer id)))) ]
                        choiceControl
                            actions
                            filter.Action
                            (decodeChoice "All actions" "Action #" ActionId)
                            (fun action -> setFilter { filter with Action = action })
                        choiceControl events filter.Event (decodeChoice "All events" "Event #" EventId) (fun event ->
                            setFilter { filter with Event = event })
                        TextBlock.create
                            [ TextBlock.text "From tick (inclusive)"; TextBlock.foreground Theme.TextMuted ]
                        tickControl filter.FromTick (fun tick -> setFilter { filter with FromTick = tick })
                        TextBlock.create
                            [ TextBlock.text "Until tick (inclusive)"
                              TextBlock.foreground Theme.TextMuted ]
                        tickControl filter.UntilTick (fun tick -> setFilter { filter with UntilTick = tick })
                        Button.create
                            [ Button.content "Reset action, event and interval"
                              Button.onClick (fun _ ->
                                  setFilter
                                      { filter with
                                          Action = None
                                          Event = None
                                          FromTick = 0<subtick>
                                          UntilTick = Int32.MaxValue * 1<subtick> }) ]
                        Button.create
                            [ Button.content "Capture selected player · next 60s"
                              Button.isEnabled live.Ui.SelectedPlayerId.IsSome
                              Button.onClick (fun _ ->
                                  let capture =
                                      match live.Ui.SelectedPlayerId with
                                      | Some id ->
                                          MatchRuntime.selectUpcoming
                                              runtimeId
                                              (PlayerId id)
                                              (SimulationClock.defaultClock.SubTicksPerSecond * 60 * 1<tickDelta>)
                                      | None -> Error "Select a player first."

                                  match capture with
                                  | Ok() ->
                                      notify
                                          "Selective capture"
                                          "Evaluated alternatives will be retained for the next 60 simulation seconds."
                                  | Error error -> notify "Capture unavailable" error) ]
                        Button.create
                            [ Button.content "Stop detailed capture"
                              Button.onClick (fun _ ->
                                  match MatchRuntime.selectObservation runtimeId None with
                                  | Ok() -> notify "Capture stopped" "Minimum causal evidence remains active."
                                  | Error error -> notify "Capture unavailable" error) ]
                        Button.create
                            [ Button.content "Export reproducible match archive"
                              Button.onClick (fun _ ->
                                  async {
                                      let! captured = MatchRuntime.captureObservation runtimeId |> Async.AwaitTask

                                      match captured with
                                      | Error error -> notify "Export unavailable" error
                                      | Ok archive ->
                                          let! saved =
                                              Task.Run(fun () ->
                                                  try
                                                      let folder =
                                                          Path.Combine(
                                                              Environment.GetFolderPath(
                                                                  Environment.SpecialFolder.LocalApplicationData
                                                              ),
                                                              "FootballEngine",
                                                              "Diagnostics"
                                                          )

                                                      Directory.CreateDirectory folder |> ignore

                                                      let path =
                                                          Path.Combine(
                                                              folder,
                                                              sprintf
                                                                  "match-%d-%s.json"
                                                                  archive.Input.MatchId
                                                                  (DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"))
                                                          )

                                                      File.WriteAllText(path, MatchObservationArchive.encode archive)
                                                      Ok path
                                                  with error ->
                                                      Error error.Message)
                                              |> Async.AwaitTask

                                          match saved with
                                          | Ok path -> notify "Match archive exported" path
                                          | Error error -> notify "Export failed" error
                                  }
                                  |> Async.StartImmediate) ] ] ]

        let text message (color: string) : IView =
            TextBlock.create
                [ TextBlock.text message
                  TextBlock.fontSize 12.0
                  TextBlock.foreground color
                  TextBlock.textWrapping TextWrapping.Wrap ]

        let rows =
            match live.Evidence with
            | None -> [ text "Waiting for captured match evidence." Theme.TextMuted ]
            | Some snapshot ->
                let describe entry =
                    let explanation = Observation.explain entry

                    match Observation.actor entry with
                    | Some actor ->
                        match live.Fixture.PlayerNames |> Map.tryFind actor with
                        | Some name -> explanation.Replace(sprintf "%A" actor, name)
                        | None -> explanation
                    | None -> explanation

                let rec chain depth entry =
                    [ yield text (describe entry) Theme.TextMain
                      if depth > 0 then
                          let causes = Observation.causes snapshot entry

                          if entry.Cause.IsSome && causes.Length = 0 then
                              yield
                                  text
                                      "Referenced cause is outside the retained window; no reconstruction is invented."
                                      Theme.Warning

                          for cause in causes do
                              let (ObservationId id) = cause.Id
                              yield text (sprintf "Because #%d at tick %d:" id (int cause.Tick)) Theme.Info
                              yield! chain (depth - 1) cause ]

                let retained = Observation.query filter snapshot |> Result.defaultValue [||]

                let matchedCount = retained.Length
                let retained = retained |> Array.rev |> Array.truncate 24

                [ yield
                      text
                          (sprintf "%d captured · %d older records evicted" snapshot.Total snapshot.Evicted)
                          Theme.TextMuted
                  yield
                      text
                          (sprintf
                              "%d matches · ticks %d–%d (inclusive) · newest 24 shown"
                              matchedCount
                              (int filter.FromTick)
                              (int filter.UntilTick))
                          Theme.TextMuted
                  if snapshot.Unlinked > 0UL then
                      yield
                          text
                              (sprintf
                                  "%d causal links dropped at capture capacity; history is incomplete."
                                  snapshot.Unlinked)
                              Theme.Warning
                  yield
                      text
                          "Recorded model evidence, not inferred motives. Newest first; expand to inspect explicit causes."
                          Theme.TextSub
                  if retained.Length = 0 then
                      yield
                          text
                              "No retained evidence for this selection yet. Capture starts at the worker frontier; it does not reconstruct earlier decisions."
                              Theme.TextMuted
                  for index, entry in retained |> Array.indexed do
                      let (ObservationId id) = entry.Id

                      yield
                          Expander.create
                              [ Expander.header (sprintf "#%d · tick %d" id (int entry.Tick))
                                Expander.isExpanded (index < 1)
                                Expander.horizontalAlignment HorizontalAlignment.Stretch
                                Expander.content (
                                    StackPanel.create [ StackPanel.spacing 6.0; StackPanel.children (chain 3 entry) ]
                                ) ] ]

        Border.create
            [ Grid.column 1
              Border.background Theme.BgSidebar
              Border.borderBrush Theme.Border
              Border.borderThickness (1.0, 0.0, 0.0, 0.0)
              Border.padding 14.0
              Border.child (
                  ScrollViewer.create
                      [ ScrollViewer.content (
                            StackPanel.create
                                [ StackPanel.spacing 10.0
                                  StackPanel.children
                                      [ text "MATCH EVIDENCE · WHY?" Theme.Accent; controls; yield! rows ] ]
                        ) ]
              ) ]
