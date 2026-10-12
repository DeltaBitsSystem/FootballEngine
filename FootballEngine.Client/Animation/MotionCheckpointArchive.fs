namespace FootballEngine.Client.Animation

open System
open System.IO
open FootballEngine.Core.Match

module MotionCheckpointArchive =
    type Writer() =
        let entries = ResizeArray<struct (int * byte[])>()
        let mutable completed: byte[] option = None

        member _.Append(checkpoint: MotionCheckpoint) =
            if
                completed.IsSome
                || checkpoint.Tick <> entries.Count * MotionTrack.checkpointInterval
            then
                Error "checkpoint archive requires contiguous indexed intervals and an open writer"
            else
                entries.Add(struct (checkpoint.Tick, MotionCheckpointCodec.serialize checkpoint))
                Ok()

        member _.Complete() =
            match completed with
            | Some payload -> payload
            | None ->
                use stream = new MemoryStream()
                use writer = new BinaryWriter(stream)
                writer.Write 0x46454D41
                writer.Write 1
                writer.Write entries.Count
                let mutable offset = 12 + entries.Count * 12

                for struct (tick, payload) in entries do
                    writer.Write tick
                    writer.Write offset
                    writer.Write payload.Length
                    offset <- offset + payload.Length

                for struct (_, payload) in entries do
                    writer.Write payload

                writer.Flush()
                let payload = stream.ToArray()
                completed <- Some payload
                payload

        member _.Preceding(config, identity, tick: int) =
            let index = tick / MotionTrack.checkpointInterval

            if tick < 0 || index >= entries.Count then
                Error "checkpoint is outside the captured archive"
            else
                let struct (_, payload) = entries[index]
                MotionCheckpointCodec.deserialize config identity payload

    let preceding config identity (payload: byte[]) tick =
        try
            use stream = new MemoryStream(payload, false)
            use reader = new BinaryReader(stream)

            if reader.ReadInt32() <> 0x46454D41 || reader.ReadInt32() <> 1 then
                Error "checkpoint archive format is incompatible"
            else
                let count = reader.ReadInt32()

                if count < 1 || count > 4000 || 12L + int64 count * 12L > stream.Length then
                    Error "checkpoint index cardinality is invalid"
                else
                    let mutable selected = ValueNone
                    let mutable expectedOffset = 12 + count * 12

                    for index = 0 to count - 1 do
                        let at = reader.ReadInt32()
                        let offset = reader.ReadInt32()
                        let length = reader.ReadInt32()

                        if
                            at <> index * MotionTrack.checkpointInterval
                            || offset <> expectedOffset
                            || length < 1
                            || length > 1024 * 1024
                            || int64 offset + int64 length > stream.Length
                        then
                            raise (InvalidDataException "checkpoint interval/offset/length is invalid")

                        expectedOffset <- offset + length

                        if at <= tick then
                            selected <- ValueSome(struct (at, offset, length))

                    if int64 expectedOffset <> stream.Length then
                        Error "checkpoint archive contains trailing data"
                    elif tick < 0 || tick / MotionTrack.checkpointInterval >= count then
                        Error "seek is outside the checkpoint index"
                    else
                        match selected with
                        | ValueNone -> Error "seek precedes the first checkpoint"
                        | ValueSome(struct (at, offset, length)) ->
                            stream.Position <- int64 offset

                            MotionCheckpointCodec.deserialize config identity (reader.ReadBytes length)
                            |> Result.bind (fun checkpoint ->
                                if checkpoint.Tick = at then
                                    Ok checkpoint
                                else
                                    Error "checkpoint frontier does not match its index")
        with
        | :? InvalidDataException as error -> Error error.Message
        | :? IOException as error -> Error error.Message
        | :? ArgumentException as error -> Error error.Message
