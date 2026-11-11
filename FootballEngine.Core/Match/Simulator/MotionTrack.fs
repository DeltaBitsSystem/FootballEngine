namespace FootballEngine.Core.Match

open System
open System.IO
open System.IO.Compression
open System.Numerics

/// Independently indexed sections keep Core unaware of any visual planner payload.
module MotionTrack =
    let checkpointInterval = 128
    let private version = 1

    let tickDuration (data: byte[]) =
        try
            use stream = new MemoryStream(data, false)
            use reader = new BinaryReader(stream)

            if reader.ReadInt32() <> version then
                Error "motion track version is incompatible"
            else
                let seconds = reader.ReadSingle()

                if Single.IsFinite seconds && seconds > 0.0f then
                    Ok seconds
                else
                    Error "motion tick duration is invalid"
        with
        | :? IOException as error -> Error error.Message
        | :? ArgumentException as error -> Error error.Message

    let private writeVector (writer: BinaryWriter) (v: Vector3) =
        writer.Write v.X
        writer.Write v.Y
        writer.Write v.Z

    let private readVector (reader: BinaryReader) =
        Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())

    let private writeOptional (writer: BinaryWriter) value =
        match value with
        | ValueNone -> writer.Write false
        | ValueSome v ->
            writer.Write true
            writeVector writer v

    let private readOptional (reader: BinaryReader) =
        if reader.ReadBoolean() then
            ValueSome(readVector reader)
        else
            ValueNone

    let writeBoundary (writer: BinaryWriter) (boundary: MotionBoundary) =
        writer.Write(int boundary.Tick)
        writer.Write boundary.Roots.Length

        for root in boundary.Roots do
            let (PlayerId actor) = root.Actor
            writer.Write actor
            writer.Write root.Generation
            writeVector writer root.Position
            writeVector writer root.Velocity
            writer.Write root.Facing
            writer.Write root.AngularVelocity
            writer.Write root.Condition
            writer.Write root.GaitPhase
            writeOptional writer root.AttentionTarget
            writer.Write root.Morphology.Height
            writer.Write root.Morphology.Mass
            writer.Write root.Morphology.ThighLength
            writer.Write root.Morphology.ShinLength
            writer.Write root.Morphology.FootLength
            writer.Write root.Morphology.ShoulderWidth

            writer.Write(
                if root.Morphology.PreferredFoot = ContactFoot.Left then
                    0uy
                else
                    1uy
            )

            writer.Write root.Active

        writer.Write boundary.Evidence.Length

        for record in boundary.Evidence do
            let (PlayerId actor) = record.Actor
            writer.Write record.Sequence
            writer.Write record.TaskId
            writer.Write actor
            writer.Write record.Generation
            writer.Write(int record.Family)
            writer.Write(int record.Effector)
            writer.Write(int record.Phase)
            writer.Write(int record.OccurredAt)
            writer.Write(int record.AvailableAt)
            writer.Write(int record.StartedAt)
            writer.Write(int record.CommitAt)
            writer.Write(int record.EndsAt)
            writeVector writer record.PredictedPoint
            writeOptional writer record.ConfirmedPoint
            writeVector writer record.BallCenter
            writeOptional writer record.IncomingVelocity
            writeOptional writer record.OutgoingVelocity

    let private cardinality (reader: BinaryReader) limit =
        let count = reader.ReadInt32()

        if count < 0 || count > limit then
            raise (InvalidDataException "motion section cardinality exceeds its contract")

        count

    let private readPreferredFoot (reader: BinaryReader) =
        match reader.ReadByte() with
        | 0uy -> ContactFoot.Left
        | 1uy -> ContactFoot.Right
        | _ -> raise (InvalidDataException "motion preferred-foot discriminator is invalid")

    let readBoundary (reader: BinaryReader) =
        let tick = reader.ReadInt32() * 1<subtick>

        let roots =
            Array.init (cardinality reader 64) (fun _ ->
                { Actor = PlayerId(reader.ReadInt32())
                  Generation = reader.ReadInt32()
                  Position = readVector reader
                  Velocity = readVector reader
                  Facing = reader.ReadSingle()
                  AngularVelocity = reader.ReadSingle()
                  Condition = reader.ReadSingle()
                  GaitPhase = reader.ReadSingle()
                  AttentionTarget = readOptional reader
                  Morphology =
                    { Height = reader.ReadSingle()
                      Mass = reader.ReadSingle()
                      ThighLength = reader.ReadSingle()
                      ShinLength = reader.ReadSingle()
                      FootLength = reader.ReadSingle()
                      ShoulderWidth = reader.ReadSingle()
                      PreferredFoot = readPreferredFoot reader }
                  Active = reader.ReadBoolean() })

        let evidence =
            Array.init (cardinality reader 256) (fun _ ->
                { Sequence = reader.ReadUInt64()
                  TaskId = reader.ReadUInt64()
                  Actor = PlayerId(reader.ReadInt32())
                  Generation = reader.ReadInt32()
                  Family = enum<MotionFamily> (reader.ReadInt32())
                  Effector = enum<MotionEffector> (reader.ReadInt32())
                  Phase = enum<MotionEvidencePhase> (reader.ReadInt32())
                  OccurredAt = reader.ReadInt32() * 1<subtick>
                  AvailableAt = reader.ReadInt32() * 1<subtick>
                  StartedAt = reader.ReadInt32() * 1<subtick>
                  CommitAt = reader.ReadInt32() * 1<subtick>
                  EndsAt = reader.ReadInt32() * 1<subtick>
                  PredictedPoint = readVector reader
                  ConfirmedPoint = readOptional reader
                  BallCenter = readVector reader
                  IncomingVelocity = readOptional reader
                  OutgoingVelocity = readOptional reader })

        { Tick = tick
          Roots = roots
          Evidence = evidence }

    type Writer(?tickSeconds: float32) =
        let tickSeconds =
            defaultArg tickSeconds (1.0f / float32 SimulationClock.defaultClock.SubTicksPerSecond)

        let blocks = ResizeArray<struct (int * int * byte[])>()
        let pending = new MemoryStream()
        let binary = new BinaryWriter(pending, Text.Encoding.UTF8, true)
        let mutable first = 0
        let mutable count = 0
        let mutable lastTick = -1
        let mutable closed = false

        let flush () =
            if count > 0 then
                binary.Flush()
                use compressed = new MemoryStream()

                do
                    use codec = new BrotliStream(compressed, CompressionLevel.Fastest, true)
                    pending.Position <- 0L
                    pending.CopyTo codec

                blocks.Add(struct (first, count, compressed.ToArray()))
                pending.SetLength 0L
                pending.Position <- 0L
                count <- 0

        member _.Append(boundary: MotionBoundary) =
            if closed || int boundary.Tick <> lastTick + 1 then
                Error "motion boundaries must be contiguous and writer must be open"
            else
                if count = 0 then
                    first <- int boundary.Tick

                writeBoundary binary boundary
                count <- count + 1
                lastTick <- int boundary.Tick

                if count = checkpointInterval then
                    flush ()

                Ok()

        member _.Complete() =
            flush ()
            closed <- true
            use output = new MemoryStream()
            use writer = new BinaryWriter(output)
            writer.Write version
            writer.Write tickSeconds
            writer.Write blocks.Count
            let mutable offset = 12 + blocks.Count * 16

            for struct (tick, size, bytes) in blocks do
                writer.Write tick
                writer.Write size
                writer.Write offset
                writer.Write bytes.Length
                offset <- offset + bytes.Length

            for struct (_, _, bytes) in blocks do
                writer.Write bytes

            writer.Flush()
            output.ToArray()

        member _.ReadBlock(tick: int<subtick>) =
            try
                let blockIndex = int tick / checkpointInterval

                if tick < 0<subtick> || int tick > lastTick then
                    Error "motion tick is outside the captured frontier"
                elif blockIndex < blocks.Count then
                    let struct (_, length, payload) = blocks[blockIndex]
                    use stream = new MemoryStream(payload, false)
                    use codec = new BrotliStream(stream, CompressionMode.Decompress)
                    use reader = new BinaryReader(codec)
                    Ok(Array.init length (fun _ -> readBoundary reader))
                else
                    binary.Flush()
                    use stream = new MemoryStream(pending.ToArray(), false)
                    use reader = new BinaryReader(stream)
                    Ok(Array.init count (fun _ -> readBoundary reader))
            with
            | :? InvalidDataException as error -> Error error.Message
            | :? IOException as error -> Error error.Message

        interface IDisposable with
            member _.Dispose() =
                binary.Dispose()
                pending.Dispose()

    let readBlock (data: byte[]) (tick: int<subtick>) =
        try
            use stream = new MemoryStream(data, false)
            use reader = new BinaryReader(stream)

            if reader.ReadInt32() <> version then
                Error "motion track version is incompatible"
            else
                let tickSeconds = reader.ReadSingle()

                if not (Single.IsFinite tickSeconds) || tickSeconds <= 0.0f then
                    raise (InvalidDataException "motion tick duration is invalid")

                let count = cardinality reader 4000
                let mutable found = ValueNone
                let mutable expectedOffset = 12 + count * 16

                for index = 0 to count - 1 do
                    let first, length, offset, bytes =
                        reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()

                    if
                        first <> index * checkpointInterval
                        || length < 1
                        || length > checkpointInterval
                        || (index < count - 1 && length <> checkpointInterval)
                        || offset <> expectedOffset
                        || bytes < 1
                        || int64 offset + int64 bytes > int64 data.Length
                    then
                        raise (InvalidDataException "motion index is invalid")

                    expectedOffset <- offset + bytes

                    if int tick >= first && int tick < first + length then
                        found <- ValueSome(struct (first, length, offset, bytes))

                if int64 expectedOffset <> stream.Length then
                    raise (InvalidDataException "motion track contains trailing data")

                match found with
                | ValueNone -> Error "motion tick is outside the archive"
                | ValueSome(struct (first, length, offset, bytes)) ->
                    use block = new MemoryStream(data, offset, bytes, false)
                    use codec = new BrotliStream(block, CompressionMode.Decompress)
                    use decoded = new BinaryReader(codec)
                    let result = Array.init length (fun _ -> readBoundary decoded)

                    if
                        result
                        |> Array.mapi (fun index boundary -> int boundary.Tick <> first + index)
                        |> Array.exists id
                    then
                        Error "motion block boundaries do not match the canonical index"
                    elif codec.ReadByte() <> -1 then
                        Error "motion block has trailing data"
                    else
                        Ok result
        with
        | :? InvalidDataException as error -> Error error.Message
        | :? IOException as error -> Error error.Message
        | :? ArgumentException as error -> Error error.Message
