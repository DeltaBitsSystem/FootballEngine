namespace FootballEngine.Core.Match

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.FSharp.Reflection

/// Machine-readable record of a match (events + stats) for the calibration
/// pipeline (R11): training consumes the same deterministic data the engine ran.
module MatchDump =

    type private FinalScore = { Home: int; Away: int }

    let private options =
        let options =
            JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

        options.Converters.Add(JsonFSharpConverter())
        options

    // DU.ToString uses the general-purpose F# formatter; event kinds have fixed names known once per process.
    let private kindNames =
        let names = System.Collections.Generic.Dictionary<EventKind, string>()

        for case in FSharpType.GetUnionCases typeof<EventKind> do
            names.Add(FSharpValue.MakeUnion(case, [||]) :?> EventKind, case.Name)

        names

    let private finalScore (events: MatchEvent list) : FinalScore =
        let home, away, _ =
            List.fold
                (fun (home: int, away: int, lastScoring: ClubSide option) (ev: MatchEvent) ->
                    match ev.Data with
                    | EventData.Goal(side, _, _, _) ->
                        let home' = if side = ClubSide.Home then home + 1 else home
                        let away' = if side = ClubSide.Away then away + 1 else away
                        home', away', Some side
                    | EventData.VARDecision VARResult.GoalOverturned ->
                        match lastScoring with
                        | Some ClubSide.Home -> max 0 (home - 1), away, None
                        | Some ClubSide.Away -> home, max 0 (away - 1), None
                        | None -> home, away, None
                    | _ -> home, away, lastScoring)
                (0, 0, None)
                events

        { Home = home; Away = away }

    /// Streaming keeps diagnostic output independent of match execution and avoids an intermediate event model.
    let write
        (destination: Stream)
        (ctx: MatchContext)
        (events: MatchEvent list)
        (homeStats: TeamStats)
        (awayStats: TeamStats)
        : Result<unit, string> =
        try
            use writer = new Utf8JsonWriter(destination)
            writer.WriteStartObject()
            writer.WriteNumber("schemaVersion", 2)
            writer.WritePropertyName("context")
            JsonSerializer.Serialize(writer, ctx, options)
            let score = finalScore events
            writer.WriteStartObject("finalScore")
            writer.WriteNumber("home", score.Home)
            writer.WriteNumber("away", score.Away)
            writer.WriteEndObject()
            writer.WritePropertyName("homeStats")
            JsonSerializer.Serialize(writer, homeStats, options)
            writer.WritePropertyName("awayStats")
            JsonSerializer.Serialize(writer, awayStats, options)
            writer.WriteStartArray("events")

            for event in events do
                writer.WriteStartObject()
                writer.WriteNumber("tick", int event.Tick)
                writer.WriteNumber("id", MatchEvent.id event |> EventId.value)
                writer.WriteString("kind", kindNames[MatchEvent.kind event])
                writer.WritePropertyName("data")
                JsonSerializer.Serialize(writer, event.Data, options)
                writer.WriteEndObject()

            writer.WriteEndArray()
            writer.WriteEndObject()
            writer.Flush()
            Ok()
        with
        | :? IOException as error -> Error error.Message
        | :? JsonException as error -> Error error.Message
        | :? NotSupportedException as error -> Error error.Message
        | :? ArgumentException as error -> Error error.Message
        | :? ObjectDisposedException as error -> Error error.Message

    let export ctx events homeStats awayStats : Result<string, string> =
        use destination = new MemoryStream()

        write destination ctx events homeStats awayStats
        |> Result.map (fun () -> Encoding.UTF8.GetString(destination.GetBuffer(), 0, int destination.Length))
