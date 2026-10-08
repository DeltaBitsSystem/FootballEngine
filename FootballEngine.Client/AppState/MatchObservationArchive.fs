namespace FootballEngine

open System
open System.Text.Json
open FootballEngine.Core.Match
open FootballEngine.Database

type MatchObservationArchive =
    { FormatVersion: int
      Input: MatchInput
      Checkpoint: byte[]
      Evidence: ObservationSnapshot }

type MatchObservationCapture =
    { Input: MatchInput
      Checkpoint: MatchCheckpoint
      Evidence: ObservationSnapshot }

module MatchObservationArchive =
    let snapshot (session: MatchSession) : MatchObservationCapture =
        { Input = session.Input
          Checkpoint = MatchSession.checkpoint session
          Evidence = MatchSession.observations session }

    let materialize (capture: MatchObservationCapture) : MatchObservationArchive =
        { FormatVersion = 1
          Input = capture.Input
          Checkpoint = capture.Checkpoint |> MatchSession.serializeCheckpoint
          Evidence = capture.Evidence }

    let capture session = snapshot session |> materialize

    let encode archive = JsonCodec.serialize archive

    let decode json =
        try
            let archive = JsonCodec.deserialize<MatchObservationArchive> json

            if obj.ReferenceEquals(archive, null) || archive.FormatVersion <> 1 then
                Error "Unsupported match observation archive format."
            elif
                obj.ReferenceEquals(archive.Input, null)
                || isNull archive.Checkpoint
                || obj.ReferenceEquals(archive.Evidence, null)
            then
                Error "Archive must contain input, checkpoint and evidence."
            elif
                isNull archive.Evidence.Entries
                || archive.Evidence.Capacity < 1
                || archive.Evidence.Entries.Length > archive.Evidence.Capacity
            then
                Error "Archive evidence must have bounded retention."
            else
                Ok archive
        with
        | :? JsonException as error -> Error error.Message
        | :? ArgumentException as error -> Error error.Message
        | :? Collections.Generic.KeyNotFoundException as error -> Error error.Message

    let restore archive =
        if archive.FormatVersion <> 1 then
            Error(InvalidCheckpoint "Unsupported match observation archive format.")
        else
            MatchSession.deserializeCheckpoint archive.Checkpoint
            |> Result.bind (MatchSession.restore Scheduler.systems archive.Input)
