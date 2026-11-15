namespace FootballEngine.Core.Match

open System.IO
open Microsoft.FSharp.Reflection

/// Replay restores the rest of the world; this payload proves it restored the actual commitment, not a phase guess.
module DefensiveCheckpoint =
    let snapshot (team: TeamWorld) =
        DefensiveTransitionState.snapshot team.Defense

    let private writeOption (writer: BinaryWriter) write =
        function
        | Some value ->
            writer.Write true
            write value
        | None -> writer.Write false

    let private writeValueOption (writer: BinaryWriter) write =
        function
        | ValueSome value ->
            writer.Write true
            write value
        | ValueNone -> writer.Write false

    let private readValueOption (reader: BinaryReader) read =
        if reader.ReadBoolean() then ValueSome(read ()) else ValueNone

    let private readOption (reader: BinaryReader) read =
        if reader.ReadBoolean() then Some(read ()) else None

    let private writeTag<'T> (writer: BinaryWriter) (value: 'T) =
        let case, _ = FSharpValue.GetUnionFields(box value, typeof<'T>)
        writer.Write(byte case.Tag)

    let private readTag<'T> (reader: BinaryReader) : 'T =
        let tag = int (reader.ReadByte())
        let cases = FSharpType.GetUnionCases typeof<'T>

        match
            cases
            |> Array.tryFind (fun case -> case.Tag = tag && case.GetFields().Length = 0)
        with
        | Some case -> FSharpValue.MakeUnion(case, [||]) :?> 'T
        | None -> raise (InvalidDataException "unknown defensive checkpoint tag")

    let private writeTactics (w: BinaryWriter) (t: TacticsConfig) =
        w.Write t.Formation

        for value in
            [ t.DefensiveLine
              t.PressIntensity
              t.Width
              t.Tempo
              t.Directness
              t.Mentality
              t.PressTriggerZone ] do
            w.Write value

    let private readTactics (r: BinaryReader) : TacticsConfig =
        { Formation = r.ReadString()
          DefensiveLine = r.ReadSingle()
          PressIntensity = r.ReadSingle()
          Width = r.ReadSingle()
          Tempo = r.ReadSingle()
          Directness = r.ReadSingle()
          Mentality = r.ReadSingle()
          PressTriggerZone = r.ReadSingle() }

    let write (w: BinaryWriter) (state: DefensiveTransitionState) =
        let tick (value: int<subtick>) = w.Write(int value)
        let slot (value: int) = w.Write value

        let point (value: Vec2) =
            w.Write(float value.X)
            w.Write(float value.Y)

        writeOption w tick state.ControlChangedAt

        writeOption
            w
            (fun (e: DefensiveEpisode) ->
                w.Write(EventId.value e.Id)
                tick e.LostAt
                point e.Origin
                writeTag w e.Mode
                writeTag w e.Reason
                tick e.ModeSince
                tick e.CommitUntil
                writeOption w slot e.Primary
                writeOption w slot e.Cover
                tick e.EvaluatedAt
                writeTactics w e.Tactics
                w.Write e.ParticipationRevision
                writeOption w tick e.StabilizingSince
                writeOption w tick e.EndedAt
                writeOption w (writeTag w) e.Outcome)
            state.Episode

        w.Write state.Tasks.Length

        for task in state.Tasks do
            writeValueOption
                w
                (fun (task: DefensiveTask) ->
                    writeTag w task.Role
                    point task.Target
                    writeValueOption w (fun (PlayerId id) -> w.Write id) task.Opponent)
                task

    let read (r: BinaryReader) =
        let tick () = r.ReadInt32() * 1<subtick>

        let point () : Vec2 =
            { X = r.ReadDouble() * 1.0<meter>
              Y = r.ReadDouble() * 1.0<meter> }

        let changed = readOption r tick

        let episode =
            readOption r (fun () ->
                { Id = EventId(r.ReadUInt64())
                  LostAt = tick ()
                  Origin = point ()
                  Mode = readTag r
                  Reason = readTag r
                  ModeSince = tick ()
                  CommitUntil = tick ()
                  Primary = readOption r r.ReadInt32
                  Cover = readOption r r.ReadInt32
                  EvaluatedAt = tick ()
                  Tactics = readTactics r
                  ParticipationRevision = r.ReadInt32()
                  StabilizingSince = readOption r tick
                  EndedAt = readOption r tick
                  Outcome = readOption r (fun () -> readTag r) })

        let count = r.ReadInt32()

        if count < 1 || int64 count > r.BaseStream.Length - r.BaseStream.Position then
            raise (InvalidDataException "defensive checkpoint slot count is outside the supported range")

        let tasks =
            Array.init count (fun _ ->
                readValueOption r (fun () ->
                    { Role = readTag r
                      Target = point ()
                      Opponent = readValueOption r (fun () -> PlayerId(r.ReadInt32())) }))

        { Episode = episode
          ControlChangedAt = changed
          Tasks = tasks }
