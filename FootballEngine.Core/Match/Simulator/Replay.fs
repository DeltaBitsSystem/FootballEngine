namespace FootballEngine.Core.Match

/// Lightweight per-snapshot record (positions, ball, score, flow) kept in
/// compact binary form so matches can be viewed and analyzed without
/// re-running the engine.
module Replay =

    type ReplayMeta = { Version: int }

    type ReplaySnapshot =
        { Tick: int<subtick>
          HomeScore: int
          AwayScore: int
          Flow: byte
          BallX: float32
          BallY: float32
          BallZ: float32
          HomeX: float32[]
          HomeY: float32[]
          AwayX: float32[]
          AwayY: float32[] }

    let private flowByte (flow: MatchFlow) : byte =
        match flow with
        | MatchFlow.Live -> 0uy
        | MatchFlow.GoalPause _ -> 1uy
        | MatchFlow.VARReview _ -> 2uy
        | MatchFlow.InjuryPause _ -> 3uy
        | MatchFlow.RestartDelay _ -> 4uy
        | MatchFlow.HalfTime _ -> 5uy
        | MatchFlow.ExtraTimeBreak _ -> 6uy
        | MatchFlow.FullTimeReview _ -> 7uy
        | MatchFlow.Shootout -> 8uy
        | MatchFlow.MatchEnded -> 9uy

    let capture (world: World) : ReplaySnapshot =
        let n = world.Home.Frame.SlotCount

        { Tick = world.State.Tick
          HomeScore = world.State.HomeScore
          AwayScore = world.State.AwayScore
          Flow = flowByte world.State.Flow
          BallX = float32 world.State.Ball.Position.X
          BallY = float32 world.State.Ball.Position.Y
          BallZ = float32 world.State.Ball.Position.Z
          HomeX = Array.copy world.Home.Frame.PosX
          HomeY = Array.copy world.Home.Frame.PosY
          AwayX = Array.copy world.Away.Frame.PosX
          AwayY = Array.copy world.Away.Frame.PosY }

    let serialize (snaps: ReplaySnapshot list) : byte[] =
        use ms = new System.IO.MemoryStream()

        use w = new System.IO.BinaryWriter(ms)
        w.Write(List.length snaps)

        for s in snaps do
            w.Write(int s.Tick)
            w.Write s.HomeScore
            w.Write s.AwayScore
            w.Write(int s.Flow)
            w.Write s.BallX
            w.Write s.BallY
            w.Write s.BallZ
            w.Write s.HomeX.Length

            for i in 0 .. s.HomeX.Length - 1 do
                w.Write s.HomeX.[i]
                w.Write s.HomeY.[i]
                w.Write s.AwayX.[i]
                w.Write s.AwayY.[i]

        ms.ToArray()

    let deserialize (data: byte[]) : ReplaySnapshot list =
        use ms = new System.IO.MemoryStream(data)

        use r = new System.IO.BinaryReader(ms)
        let count = r.ReadInt32()

        [ for _ in 1..count ->
              let tick = r.ReadInt32() * 1<subtick>
              let home = r.ReadInt32()
              let away = r.ReadInt32()
              let flow = byte (r.ReadInt32())
              let bx = r.ReadSingle()
              let by = r.ReadSingle()
              let bz = r.ReadSingle()
              let n = r.ReadInt32()

              let hx = Array.zeroCreate<float32> n
              let hy = Array.zeroCreate<float32> n
              let ax = Array.zeroCreate<float32> n
              let ay = Array.zeroCreate<float32> n

              for i in 0 .. n - 1 do
                  hx.[i] <- r.ReadSingle()
                  hy.[i] <- r.ReadSingle()
                  ax.[i] <- r.ReadSingle()
                  ay.[i] <- r.ReadSingle()

              { Tick = tick
                HomeScore = home
                AwayScore = away
                Flow = flow
                BallX = bx
                BallY = by
                BallZ = bz
                HomeX = hx
                HomeY = hy
                AwayX = ax
                AwayY = ay } ]

    type ReplayData =
        { Snapshots: byte[]
          Count: int
          Meta: ReplayMeta }

    let write (snaps: ReplaySnapshot list) : ReplayData =
        { Snapshots = serialize snaps
          Count = List.length snaps
          Meta = { Version = 1 } }

    let read (data: ReplayData) : seq<ReplaySnapshot> =
        deserialize data.Snapshots |> List.toSeq
