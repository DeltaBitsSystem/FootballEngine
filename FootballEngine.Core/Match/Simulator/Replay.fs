namespace FootballEngine.Core.Match

open System
open System.IO

/// Indexed visual replay. The engine keeps authoritative values in World; this
/// format is only a presentation projection and therefore may quantize positions.
module Replay =

    [<Literal>]
    let private magic = 0x33525046

    [<Literal>]
    let private formatVersion = 6

    [<Literal>]
    let blockSize = 32

    type ReplayMeta = { Version: int }

    type ReplaySnapshot =
        { Tick: int<subtick>
          HomePlayerIds: PlayerId[]
          AwayPlayerIds: PlayerId[]
          HomeScore: int
          AwayScore: int
          Flow: byte
          BallX: float32
          BallY: float32
          BallZ: float32
          BallVx: float32
          BallVy: float32
          BallVz: float32
          BallSpinTop: float32
          BallSpinSide: float32
          BallControlSide: sbyte
          BallController: int
          RefereeX: float32
          RefereeY: float32
          NearAssistantX: float32
          NearAssistantY: float32
          FarAssistantX: float32
          FarAssistantY: float32
          RefereeSignal: byte
          HomeX: float32[]
          HomeY: float32[]
          HomeVx: float32[]
          HomeVy: float32[]
          HomeCondition: float32[]
          HomeIntent: byte[]
          HomeActive: bool[]
          AwayX: float32[]
          AwayY: float32[]
          AwayVx: float32[]
          AwayVy: float32[]
          AwayCondition: float32[]
          AwayIntent: byte[]
          AwayActive: bool[] }

    type ReplayData =
        { Snapshots: byte[]
          Count: int
          Meta: ReplayMeta
          HomeSlotCount: int
          AwaySlotCount: int
          HomePlayerIds: PlayerId[]
          AwayPlayerIds: PlayerId[]
          BlockOffsets: int[] }

    module Quantization =
        [<Literal>]
        let CentimetersPerMeter = 100.0f

        let quantizeCentimeters (meters: float32) : int16 =
            let rounded =
                Math.Round(float meters * float CentimetersPerMeter, MidpointRounding.AwayFromZero)

            if rounded < float Int16.MinValue || rounded > float Int16.MaxValue then
                raise (ArgumentOutOfRangeException(nameof meters, "visual position exceeds int16 centimetre range"))

            int16 rounded

        let dequantizeCentimeters (centimeters: int16) : float32 =
            float32 centimeters / CentimetersPerMeter

    module RefereeSignalCode =
        [<Literal>]
        let None = 0uy

        [<Literal>]
        let Whistle = 1uy

        let ofSetPieceKind kind =
            match kind with
            | SetPieceKind.KickOff -> 10uy
            | SetPieceKind.ThrowIn -> 11uy
            | SetPieceKind.Corner -> 12uy
            | SetPieceKind.GoalKick -> 13uy
            | SetPieceKind.FreeKick -> 14uy
            | SetPieceKind.Penalty -> 15uy

    let private flowByte (flow: MatchFlow) : byte =
        match flow with
        | MatchFlow.PreMatch -> 0uy
        | MatchFlow.Live -> 0uy
        | MatchFlow.GoalPause _ -> 1uy
        | MatchFlow.VARReview _ -> 2uy
        | MatchFlow.InjuryPause _ -> 3uy
        | MatchFlow.Restart _ -> 4uy
        | MatchFlow.HalfTime _ -> 5uy
        | MatchFlow.ExtraTimeBreak _ -> 6uy
        | MatchFlow.FullTimeReview _ -> 7uy
        | MatchFlow.Shootout -> 8uy
        | MatchFlow.MatchEnded -> 9uy

    let capture (world: World) : ReplaySnapshot =
        let signal =
            match world.Officials.Signal with
            | RefereeSignal.NoSignal -> RefereeSignalCode.None
            | RefereeSignal.Whistle -> RefereeSignalCode.Whistle
            | RefereeSignal.RestartDirection(kind, _) -> RefereeSignalCode.ofSetPieceKind kind

        let controlSide, controller =
            match world.State.Ball.Control with
            | BallControl.Controlled(ClubSide.Home, player)
            | BallControl.Receiving(ClubSide.Home, player, _) ->
                let (PlayerId value) = player
                0y, value
            | BallControl.Controlled(ClubSide.Away, player)
            | BallControl.Receiving(ClubSide.Away, player, _) ->
                let (PlayerId value) = player
                1y, value
            | _ -> -1y, -1

        { Tick = world.State.Tick
          HomePlayerIds = Array.copy world.Home.RosterIds
          AwayPlayerIds = Array.copy world.Away.RosterIds
          HomeScore = world.State.HomeScore
          AwayScore = world.State.AwayScore
          Flow = flowByte world.State.Flow
          BallX = float32 world.State.Ball.Position.X
          BallY = float32 world.State.Ball.Position.Y
          BallZ = float32 world.State.Ball.Position.Z
          BallVx = float32 world.State.Ball.Position.Vx
          BallVy = float32 world.State.Ball.Position.Vy
          BallVz = float32 world.State.Ball.Position.Vz
          BallSpinTop = float32 world.State.Ball.Spin.Top
          BallSpinSide = float32 world.State.Ball.Spin.Side
          BallControlSide = controlSide
          BallController = controller
          RefereeX = float32 world.Officials.Referee.X
          RefereeY = float32 world.Officials.Referee.Y
          NearAssistantX = float32 world.Officials.NearAssistant.X
          NearAssistantY = float32 world.Officials.NearAssistant.Y
          FarAssistantX = float32 world.Officials.FarAssistant.X
          FarAssistantY = float32 world.Officials.FarAssistant.Y
          RefereeSignal = signal
          HomeX = Array.copy world.Home.Frame.PosX
          HomeY = Array.copy world.Home.Frame.PosY
          HomeVx = Array.copy world.Home.Frame.VelX
          HomeVy = Array.copy world.Home.Frame.VelY
          HomeCondition = Array.copy world.Home.Frame.Condition
          HomeIntent = world.Home.Frame.Intent.Kind |> Array.map byte
          HomeActive = world.Home.Frame.Occupancy |> Array.map MatchWorld.isActiveSlot
          AwayX = Array.copy world.Away.Frame.PosX
          AwayY = Array.copy world.Away.Frame.PosY
          AwayVx = Array.copy world.Away.Frame.VelX
          AwayVy = Array.copy world.Away.Frame.VelY
          AwayCondition = Array.copy world.Away.Frame.Condition
          AwayIntent = world.Away.Frame.Intent.Kind |> Array.map byte
          AwayActive = world.Away.Frame.Occupancy |> Array.map MatchWorld.isActiveSlot }

    let private bitsetLength count = (count + 7) / 8

    let private writeBitset (writer: BinaryWriter) (values: bool[]) (count: int) =
        for byteIndex in 0 .. bitsetLength count - 1 do
            let mutable bits = 0uy

            for bit in 0..7 do
                let index = byteIndex * 8 + bit

                if index < count && values[index] then
                    bits <- bits ||| (1uy <<< bit)

            writer.Write bits

    let private readBitset (reader: BinaryReader) count =
        let values = Array.zeroCreate<bool> count

        for byteIndex in 0 .. bitsetLength count - 1 do
            let bits = reader.ReadByte()

            for bit in 0..7 do
                let index = byteIndex * 8 + bit

                if index < count then
                    values[index] <- (bits &&& (1uy <<< bit)) <> 0uy

        values

    let private writePosition (writer: BinaryWriter) (x: float32) (y: float32) =
        writer.Write(Quantization.quantizeCentimeters x)
        writer.Write(Quantization.quantizeCentimeters y)

    let private readPosition (reader: BinaryReader) =
        Quantization.dequantizeCentimeters (reader.ReadInt16()), Quantization.dequantizeCentimeters (reader.ReadInt16())

    let private writeVelocity (writer: BinaryWriter) (value: float32) =
        writer.Write(int16 (Math.Clamp(Math.Round(float value * 100.0), float Int16.MinValue, float Int16.MaxValue)))

    let private readVelocity (reader: BinaryReader) = float32 (reader.ReadInt16()) / 100.0f

    let private writeSnapshot
        (writer: BinaryWriter)
        (homeSlotCount: int)
        (awaySlotCount: int)
        (snapshot: ReplaySnapshot)
        =
        if
            snapshot.HomeX.Length <> homeSlotCount
            || snapshot.HomePlayerIds.Length <> homeSlotCount
            || snapshot.HomeY.Length <> homeSlotCount
            || snapshot.HomeVx.Length <> homeSlotCount
            || snapshot.HomeVy.Length <> homeSlotCount
            || snapshot.HomeCondition.Length <> homeSlotCount
            || snapshot.HomeIntent.Length <> homeSlotCount
            || snapshot.HomeActive.Length <> homeSlotCount
            || snapshot.AwayX.Length <> awaySlotCount
            || snapshot.AwayPlayerIds.Length <> awaySlotCount
            || snapshot.AwayY.Length <> awaySlotCount
            || snapshot.AwayVx.Length <> awaySlotCount
            || snapshot.AwayVy.Length <> awaySlotCount
            || snapshot.AwayCondition.Length <> awaySlotCount
            || snapshot.AwayIntent.Length <> awaySlotCount
            || snapshot.AwayActive.Length <> awaySlotCount
        then
            invalidArg "snapshot" "replay snapshot cardinality differs from its header"

        writer.Write(int snapshot.Tick)
        writer.Write snapshot.HomeScore
        writer.Write snapshot.AwayScore
        writer.Write snapshot.Flow
        writer.Write(Quantization.quantizeCentimeters snapshot.BallX)
        writer.Write(Quantization.quantizeCentimeters snapshot.BallY)
        writer.Write(Quantization.quantizeCentimeters snapshot.BallZ)
        writeVelocity writer snapshot.BallVx
        writeVelocity writer snapshot.BallVy
        writeVelocity writer snapshot.BallVz
        writeVelocity writer snapshot.BallSpinTop
        writeVelocity writer snapshot.BallSpinSide
        writer.Write snapshot.BallControlSide
        writer.Write snapshot.BallController
        writePosition writer snapshot.RefereeX snapshot.RefereeY
        writePosition writer snapshot.NearAssistantX snapshot.NearAssistantY
        writePosition writer snapshot.FarAssistantX snapshot.FarAssistantY
        writer.Write snapshot.RefereeSignal

        for i in 0 .. homeSlotCount - 1 do
            writePosition writer snapshot.HomeX[i] snapshot.HomeY[i]
            writeVelocity writer snapshot.HomeVx[i]
            writeVelocity writer snapshot.HomeVy[i]
            writer.Write snapshot.HomeCondition[i]
            writer.Write snapshot.HomeIntent[i]

        for i in 0 .. awaySlotCount - 1 do
            writePosition writer snapshot.AwayX[i] snapshot.AwayY[i]
            writeVelocity writer snapshot.AwayVx[i]
            writeVelocity writer snapshot.AwayVy[i]
            writer.Write snapshot.AwayCondition[i]
            writer.Write snapshot.AwayIntent[i]

        writeBitset writer snapshot.HomeActive homeSlotCount
        writeBitset writer snapshot.AwayActive awaySlotCount

    type ReplayWriter(homeRoster: PlayerId array, awayRoster: PlayerId array) =
        let homePlayerIds = Array.copy homeRoster
        let awayPlayerIds = Array.copy awayRoster
        let homeSlotCount = homePlayerIds.Length
        let awaySlotCount = awayPlayerIds.Length
        let frames = new MemoryStream()
        let blockOffsets = ResizeArray<int>()
        let mutable count = 0
        let mutable completed: ReplayData option = None

        do
            if Array.distinct homePlayerIds |> Array.length <> homeSlotCount then
                invalidArg "homeRoster" "replay home roster player IDs must be unique"

            if Array.distinct awayPlayerIds |> Array.length <> awaySlotCount then
                invalidArg "awayRoster" "replay away roster player IDs must be unique"

        member _.Append(snapshot: ReplaySnapshot) =
            if completed.IsSome then
                invalidOp "replay writer is already complete"

            if
                snapshot.HomePlayerIds <> homePlayerIds
                || snapshot.AwayPlayerIds <> awayPlayerIds
            then
                invalidArg "snapshot" "replay roster identity changed within a match"

            if count % blockSize = 0 then
                blockOffsets.Add(int frames.Position)

            use writer = new BinaryWriter(frames, Text.Encoding.UTF8, true)
            writeSnapshot writer homeSlotCount awaySlotCount snapshot
            writer.Flush()
            count <- count + 1

        member _.Complete() : ReplayData =
            match completed with
            | Some data -> data
            | None ->
                let frameBytes = frames.ToArray()

                let headerSize =
                    7 * sizeof<int>
                    + (homeSlotCount + awaySlotCount) * sizeof<int>
                    + blockOffsets.Count * sizeof<int>

                use output = new MemoryStream(headerSize + frameBytes.Length)
                use writer = new BinaryWriter(output)
                writer.Write magic
                writer.Write formatVersion
                writer.Write count
                writer.Write homeSlotCount
                writer.Write awaySlotCount
                writer.Write blockSize
                writer.Write blockOffsets.Count

                for PlayerId playerId in homePlayerIds do
                    writer.Write playerId

                for PlayerId playerId in awayPlayerIds do
                    writer.Write playerId

                let absoluteOffsets =
                    blockOffsets |> Seq.map (fun offset -> headerSize + offset) |> Array.ofSeq

                for offset in absoluteOffsets do
                    writer.Write offset

                writer.Write frameBytes
                writer.Flush()
                let bytes = output.ToArray()
                frames.Dispose()

                let data =
                    { Snapshots = bytes
                      Count = count
                      Meta = { Version = formatVersion }
                      HomeSlotCount = homeSlotCount
                      AwaySlotCount = awaySlotCount
                      HomePlayerIds = homePlayerIds
                      AwayPlayerIds = awayPlayerIds
                      BlockOffsets = absoluteOffsets }

                completed <- Some data
                data

        interface IDisposable with
            member _.Dispose() = frames.Dispose()

    let write (snapshots: ReplaySnapshot list) : ReplayData =
        let homeCount =
            snapshots |> List.tryHead |> Option.map _.HomeX.Length |> Option.defaultValue 0

        let awayCount =
            snapshots |> List.tryHead |> Option.map _.AwayX.Length |> Option.defaultValue 0

        let homeRoster =
            snapshots
            |> List.tryHead
            |> Option.map _.HomePlayerIds
            |> Option.defaultValue [||]

        let awayRoster =
            snapshots
            |> List.tryHead
            |> Option.map _.AwayPlayerIds
            |> Option.defaultValue [||]

        let writer = new ReplayWriter(homeRoster, awayRoster)

        for snapshot in snapshots do
            writer.Append snapshot

        writer.Complete()

    let private readHeader (data: ReplayData) =
        use stream = new MemoryStream(data.Snapshots, false)
        use reader = new BinaryReader(stream)

        if reader.ReadInt32() <> magic then
            invalidArg "data" "replay magic does not match"

        if reader.ReadInt32() <> formatVersion then
            invalidArg "data" "replay format version is not supported"

        let count = reader.ReadInt32()
        let homeCount = reader.ReadInt32()
        let awayCount = reader.ReadInt32()
        let storedBlockSize = reader.ReadInt32()
        let blockCount = reader.ReadInt32()

        if
            count < 0
            || homeCount < 0
            || awayCount < 0
            || storedBlockSize <> blockSize
            || blockCount < 0
            || (int64 homeCount + int64 awayCount + int64 blockCount) * int64 sizeof<int> > reader.BaseStream.Length
                                                                                            - reader.BaseStream.Position
        then
            invalidArg "data" "replay header contains invalid cardinalities"

        let homePlayerIds = Array.init homeCount (fun _ -> PlayerId(reader.ReadInt32()))
        let awayPlayerIds = Array.init awayCount (fun _ -> PlayerId(reader.ReadInt32()))

        if
            data.Meta.Version <> formatVersion
            || count <> data.Count
            || homeCount <> data.HomeSlotCount
            || awayCount <> data.AwaySlotCount
            || homePlayerIds <> data.HomePlayerIds
            || awayPlayerIds <> data.AwayPlayerIds
            || Array.distinct homePlayerIds |> Array.length <> homeCount
            || Array.distinct awayPlayerIds |> Array.length <> awayCount
            || blockCount <> data.BlockOffsets.Length
        then
            invalidArg "data" "replay metadata does not match its header"

        let offsets = Array.init blockCount (fun _ -> reader.ReadInt32())

        let expectedBlockCount =
            count / blockSize + (if count % blockSize = 0 then 0 else 1)

        if
            blockCount <> expectedBlockCount
            || offsets
               |> Array.mapi (fun index offset -> index = 0 || offset > offsets.[index - 1])
               |> Array.exists not
            || offsets
               |> Array.exists (fun offset ->
                   int64 offset < reader.BaseStream.Position
                   || int64 offset >= int64 data.Snapshots.Length)
        then
            invalidArg "data" "replay frame index is invalid"

        if offsets <> data.BlockOffsets then
            invalidArg "data" "replay index does not match its header"

        count, homePlayerIds, awayPlayerIds, offsets

    let private readFrameFrom
        (reader: BinaryReader)
        (homePlayerIds: PlayerId[])
        (awayPlayerIds: PlayerId[])
        : ReplaySnapshot =
        let tick = reader.ReadInt32() * 1<subtick>
        let homeScore = reader.ReadInt32()
        let awayScore = reader.ReadInt32()
        let flow = reader.ReadByte()
        let ballX = Quantization.dequantizeCentimeters (reader.ReadInt16())
        let ballY = Quantization.dequantizeCentimeters (reader.ReadInt16())
        let ballZ = Quantization.dequantizeCentimeters (reader.ReadInt16())
        let ballVx = readVelocity reader
        let ballVy = readVelocity reader
        let ballVz = readVelocity reader
        let ballSpinTop = readVelocity reader
        let ballSpinSide = readVelocity reader
        let ballControlSide = reader.ReadSByte()
        let ballController = reader.ReadInt32()
        let refereeX, refereeY = readPosition reader
        let nearAssistantX, nearAssistantY = readPosition reader
        let farAssistantX, farAssistantY = readPosition reader
        let refereeSignal = reader.ReadByte()
        let homeCount = homePlayerIds.Length
        let awayCount = awayPlayerIds.Length
        let homeX = Array.zeroCreate<float32> homeCount
        let homeY = Array.zeroCreate<float32> homeCount
        let homeVx = Array.zeroCreate<float32> homeCount
        let homeVy = Array.zeroCreate<float32> homeCount
        let homeCondition = Array.zeroCreate<float32> homeCount
        let homeIntent = Array.zeroCreate<byte> homeCount
        let awayX = Array.zeroCreate<float32> awayCount
        let awayY = Array.zeroCreate<float32> awayCount
        let awayVx = Array.zeroCreate<float32> awayCount
        let awayVy = Array.zeroCreate<float32> awayCount
        let awayCondition = Array.zeroCreate<float32> awayCount
        let awayIntent = Array.zeroCreate<byte> awayCount

        for i in 0 .. homeCount - 1 do
            let x, y = readPosition reader
            homeX[i] <- x
            homeY[i] <- y
            homeVx[i] <- readVelocity reader
            homeVy[i] <- readVelocity reader
            homeCondition[i] <- reader.ReadSingle()
            homeIntent[i] <- reader.ReadByte()

        for i in 0 .. awayCount - 1 do
            let x, y = readPosition reader
            awayX[i] <- x
            awayY[i] <- y
            awayVx[i] <- readVelocity reader
            awayVy[i] <- readVelocity reader
            awayCondition[i] <- reader.ReadSingle()
            awayIntent[i] <- reader.ReadByte()

        let homeActive = readBitset reader homeCount
        let awayActive = readBitset reader awayCount

        { Tick = tick
          HomePlayerIds = Array.copy homePlayerIds
          AwayPlayerIds = Array.copy awayPlayerIds
          HomeScore = homeScore
          AwayScore = awayScore
          Flow = flow
          BallX = ballX
          BallY = ballY
          BallZ = ballZ
          BallVx = ballVx
          BallVy = ballVy
          BallVz = ballVz
          BallSpinTop = ballSpinTop
          BallSpinSide = ballSpinSide
          BallControlSide = ballControlSide
          BallController = ballController
          RefereeX = refereeX
          RefereeY = refereeY
          NearAssistantX = nearAssistantX
          NearAssistantY = nearAssistantY
          FarAssistantX = farAssistantX
          FarAssistantY = farAssistantY
          RefereeSignal = refereeSignal
          HomeX = homeX
          HomeY = homeY
          HomeVx = homeVx
          HomeVy = homeVy
          HomeCondition = homeCondition
          HomeIntent = homeIntent
          HomeActive = homeActive
          AwayX = awayX
          AwayY = awayY
          AwayVx = awayVx
          AwayVy = awayVy
          AwayCondition = awayCondition
          AwayIntent = awayIntent
          AwayActive = awayActive }

    let readFrame (data: ReplayData) (index: int) : ReplaySnapshot =
        let count, homePlayerIds, awayPlayerIds, offsets = readHeader data

        if index < 0 || index >= count then
            invalidArg "index" "replay frame index is outside the replay"

        let block = index / blockSize
        use stream = new MemoryStream(data.Snapshots, false)
        use reader = new BinaryReader(stream)
        stream.Position <- int64 offsets[block]

        let mutable frame: ReplaySnapshot option = None

        for _ in block * blockSize .. index do
            frame <- Some(readFrameFrom reader homePlayerIds awayPlayerIds)

        frame.Value

    let readPair (data: ReplayData) (index: int) : ReplaySnapshot * ReplaySnapshot option =
        let current = readFrame data index

        let next =
            if index + 1 < data.Count then
                Some(readFrame data (index + 1))
            else
                None

        current, next

    /// Lazy indexed access. Consumers that need a single frame should use
    /// readFrame/readPair; this function never materializes the complete replay.
    let read (data: ReplayData) : seq<ReplaySnapshot> =
        seq {
            for index in 0 .. data.Count - 1 do
                yield readFrame data index
        }
