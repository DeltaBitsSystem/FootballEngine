namespace FootballEngine.Client.Animation

open System
open System.IO
open System.Numerics
open FootballEngine.Core.Match

module MotionCheckpointCodec =
    let private writeVector (writer: BinaryWriter) (v: Vector3) =
        writer.Write v.X
        writer.Write v.Y
        writer.Write v.Z

    let private readVector (reader: BinaryReader) =
        Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())

    let private writeFoot (writer: BinaryWriter) (foot: FootPlan) =
        writer.Write(int foot.Phase)
        writeVector writer foot.Anchor
        writer.Write foot.ReleaseAt
        let segment = foot.Segment
        writer.Write segment.Start
        writer.Write segment.Finish
        writeVector writer segment.From
        writeVector writer segment.Target
        writeVector writer segment.FromVelocity
        writeVector writer segment.TargetVelocity
        writer.Write segment.Clearance

    let private readFoot (reader: BinaryReader) =
        let phase = reader.ReadInt32()

        if phase < 0 || phase > 3 then
            raise (InvalidDataException "checkpoint support phase is invalid")

        let anchor = readVector reader
        let releaseAt = reader.ReadSingle()

        let segment =
            { Start = reader.ReadSingle()
              Finish = reader.ReadSingle()
              From = readVector reader
              Target = readVector reader
              FromVelocity = readVector reader
              TargetVelocity = readVector reader
              Clearance = reader.ReadSingle() }

        { Phase = enum<SupportPhase> phase
          Anchor = anchor
          ReleaseAt = releaseAt
          Segment = segment }

    let serialize (checkpoint: MotionCheckpoint) =
        use stream = new MemoryStream()
        use writer = new BinaryWriter(stream)
        writer.Write 0x46454D43
        writer.Write 1
        writer.Write checkpoint.Identity.PlannerVersion
        writer.Write checkpoint.Identity.ConfigHash
        writer.Write checkpoint.Identity.RigHash
        writer.Write checkpoint.Identity.ProfileHash
        writer.Write checkpoint.Identity.TrackId
        writer.Write checkpoint.Tick
        writer.Write checkpoint.Cursor
        writer.Write checkpoint.ActorCount
        let roots = checkpoint.Plans |> Array.map _.Root
        let evidence = checkpoint.Tasks |> Array.map _.Evidence

        MotionTrack.writeBoundary
            writer
            { Tick = checkpoint.Tick * 1<subtick>
              Roots = roots
              Evidence = evidence }

        for plan in checkpoint.Plans do
            writer.Write plan.Tick
            writeFoot writer plan.Left
            writeFoot writer plan.Right
            writeVector writer plan.Acceleration
            writeVector writer plan.Lean
            writer.Write plan.Gaze.X
            writer.Write plan.Gaze.Y
            writer.Write plan.Seed

        for task in checkpoint.Tasks do
            writer.Write task.PreparedAt
            writer.Write task.CancelledAt
            writer.Write task.Active

        writer.Flush()
        stream.ToArray()

    let deserialize (config: AnimationConfig) expectedIdentity (payload: byte[]) =
        if payload.Length > 1024 * 1024 then
            Error "checkpoint exceeds its payload budget"
        else
            try
                use stream = new MemoryStream(payload, false)
                use reader = new BinaryReader(stream)

                if reader.ReadInt32() <> 0x46454D43 || reader.ReadInt32() <> 1 then
                    Error "checkpoint format is incompatible"
                else
                    let identity =
                        { PlannerVersion = reader.ReadInt32()
                          ConfigHash = reader.ReadString()
                          RigHash = reader.ReadString()
                          ProfileHash = reader.ReadString()
                          TrackId = reader.ReadUInt64() }

                    if identity <> expectedIdentity then
                        Error "checkpoint planner/config/rig/profile/track identity is incompatible"
                    else
                        let tick = reader.ReadInt32()
                        let cursor = reader.ReadUInt64()
                        let count = reader.ReadInt32()

                        if tick < 0 || cursor < 1UL || count < 0 || count > config.MaxActors then
                            Error "checkpoint frontier/cursor/cardinality is invalid"
                        else
                            let boundary = MotionTrack.readBoundary reader

                            if
                                int boundary.Tick <> tick
                                || boundary.Roots.Length <> count
                                || boundary.Evidence.Length <> count * config.TasksPerActor
                            then
                                Error "checkpoint state cardinality does not match its header"
                            else
                                let plans =
                                    Array.init count (fun index ->
                                        { Root = boundary.Roots[index]
                                          Tick = reader.ReadInt32()
                                          Left = readFoot reader
                                          Right = readFoot reader
                                          Acceleration = readVector reader
                                          Lean = readVector reader
                                          Gaze = Vector2(reader.ReadSingle(), reader.ReadSingle())
                                          Seed = reader.ReadUInt64() })

                                let tasks =
                                    Array.init boundary.Evidence.Length (fun index ->
                                        { Evidence = boundary.Evidence[index]
                                          PreparedAt = reader.ReadInt32()
                                          CancelledAt = reader.ReadInt32()
                                          Active = reader.ReadBoolean() })

                                if stream.Position <> stream.Length then
                                    Error "checkpoint contains trailing data"
                                else
                                    let checkpoint =
                                        { Identity = identity
                                          Tick = tick
                                          Cursor = cursor
                                          ActorCount = count
                                          Plans = plans
                                          Tasks = tasks }

                                    if MotionCheckpoint.valid config expectedIdentity checkpoint then
                                        Ok checkpoint
                                    else
                                        Error "checkpoint contains invalid motion/participation/task state"
            with
            | :? InvalidDataException as error -> Error error.Message
            | :? IOException as error -> Error error.Message
            | :? ArgumentException as error -> Error error.Message
