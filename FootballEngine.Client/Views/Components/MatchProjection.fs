namespace FootballEngine.Client.Views.Components

open FootballEngine
open FootballEngine.Core.Match
open FootballEngine.Domain

type Vector2 = { X: float; Y: float }

module Vector2 =
    let zero = { X = 0.0; Y = 0.0 }
    let inline create x y = { X = x; Y = y }
    let inline add a b = { X = a.X + b.X; Y = a.Y + b.Y }
    let inline scale s v = { X = v.X * s; Y = v.Y * s }
    let inline magnitude v = sqrt (v.X * v.X + v.Y * v.Y)

type PlayerVisualState =
    | Idle
    | Walking
    | Running
    | Sprinting
    | Kicking
    | Tackling

type RenderPlayer =
    { Id: PlayerId
      Position: Vector2
      Velocity: Vector2
      Acceleration: Vector2
      State: PlayerVisualState
      JerseyNumber: int
      ShortName: string
      Condition: int
      IsHome: bool
      HasBall: bool
      IsSidelined: bool }

type RenderBall =
    { Position: Vector2
      Velocity: Vector2
      VelocityZ: float
      Spin: Vector2
      Height: float }

type RenderFrame =
    { Players: RenderPlayer[]
      Ball: RenderBall
      TimeSeconds: float
      HomeScore: int
      AwayScore: int
      AttackingClubId: ClubId option
      Momentum: float
      HomeName: string
      AwayName: string }

module MatchProjection =

    let private shortName (name: string) =
        let parts = name.Split([| ' ' |])
        if parts.Length >= 2 then parts[parts.Length - 1] else name

    let private projectTeam (isHome: bool) (xs: float32[]) (ys: float32[]) (names: string[]) : RenderPlayer[] =
        xs
        |> Array.mapi (fun i x ->
            let name =
                if i < names.Length && not (System.String.IsNullOrEmpty names[i]) then
                    shortName names[i]
                else
                    $"{i + 1}"

            { Id = i + 1
              Position = { X = float x; Y = float ys[i] }
              Velocity = Vector2.zero
              Acceleration = Vector2.zero
              State = Idle
              JerseyNumber = i + 1
              ShortName = name
              Condition = 100
              IsHome = isHome
              HasBall = false
              IsSidelined = false })

    let project
        (homeNames: string[])
        (awayNames: string[])
        (homeName: string)
        (awayName: string)
        (snapshot: Replay.ReplaySnapshot)
        (timeSeconds: float)
        : RenderFrame =
        let homePlayers = projectTeam true snapshot.HomeX snapshot.HomeY homeNames
        let awayPlayers = projectTeam false snapshot.AwayX snapshot.AwayY awayNames

        let renderBall =
            { Position =
                { X = float snapshot.BallX
                  Y = float snapshot.BallY }
              Velocity = Vector2.zero
              VelocityZ = float snapshot.BallZ
              Spin = Vector2.zero
              Height = float snapshot.BallZ }

        { Players = Array.append homePlayers awayPlayers
          Ball = renderBall
          TimeSeconds = timeSeconds
          HomeScore = snapshot.HomeScore
          AwayScore = snapshot.AwayScore
          AttackingClubId = None
          Momentum = 0.0
          HomeName = homeName
          AwayName = awayName }

module MatchInterp =

    let private hermiteScalar (p0: float) (v0: float) (p1: float) (v1: float) (t: float) (dt: float) =
        let t2 = t * t
        let t3 = t2 * t
        let h00 = 2.0 * t3 - 3.0 * t2 + 1.0
        let h10 = t3 - 2.0 * t2 + t
        let h01 = -2.0 * t3 + 3.0 * t2
        let h11 = t3 - t2
        h00 * p0 + h10 * v0 * dt + h01 * p1 + h11 * v1 * dt

    let private hermiteVec2 (a: Vector2) (va: Vector2) (b: Vector2) (vb: Vector2) (t: float) (dt: float) =
        { X = hermiteScalar a.X va.X b.X vb.X t dt
          Y = hermiteScalar a.Y va.Y b.Y vb.Y t dt }

    let hermite (frame0: RenderFrame) (frame1: RenderFrame) (t: float) (dt: float) : RenderFrame =
        let players =
            if frame0.Players.Length <> frame1.Players.Length then
                frame0.Players
            else
                Array.map2
                    (fun (p0: RenderPlayer) (p1: RenderPlayer) ->
                        { p0 with
                            Position = hermiteVec2 p0.Position p0.Velocity p1.Position p1.Velocity t dt
                            Velocity =
                                let t2 = t * t
                                let dh00 = 6.0 * t2 - 6.0 * t
                                let dh10 = 3.0 * t2 - 4.0 * t + 1.0
                                let dh01 = -6.0 * t2 + 6.0 * t
                                let dh11 = 3.0 * t2 - 2.0 * t

                                { X =
                                    dh00 * p0.Position.X / dt
                                    + dh10 * p0.Velocity.X
                                    + dh01 * p1.Position.X / dt
                                    + dh11 * p1.Velocity.X
                                  Y =
                                    dh00 * p0.Position.Y / dt
                                    + dh10 * p0.Velocity.Y
                                    + dh01 * p1.Position.Y / dt
                                    + dh11 * p1.Velocity.Y }
                            Acceleration =
                                { X = (p1.Velocity.X - p0.Velocity.X) / dt
                                  Y = (p1.Velocity.Y - p0.Velocity.Y) / dt }
                            State = p0.State })
                    frame0.Players
                    frame1.Players

        let ball =
            let b0 = frame0.Ball
            let b1 = frame1.Ball

            let dx = b1.Position.X - b0.Position.X
            let dy = b1.Position.Y - b0.Position.Y
            let distSq = dx * dx + dy * dy

            if distSq > 25.0 then
                if t < 0.5 then
                    { b0 with
                        Spin = Vector2.scale (1.0 - t) b0.Spin |> Vector2.add (Vector2.scale t b1.Spin)
                        VelocityZ = b0.VelocityZ * (1.0 - t) + b1.VelocityZ * t }
                else
                    { b1 with
                        Spin = Vector2.scale (1.0 - t) b0.Spin |> Vector2.add (Vector2.scale t b1.Spin)
                        VelocityZ = b0.VelocityZ * (1.0 - t) + b1.VelocityZ * t }
            else
                { Position = hermiteVec2 b0.Position b0.Velocity b1.Position b1.Velocity t dt
                  Velocity =
                    let t2 = t * t
                    let dh00 = 6.0 * t2 - 6.0 * t
                    let dh10 = 3.0 * t2 - 4.0 * t + 1.0
                    let dh01 = -6.0 * t2 + 6.0 * t
                    let dh11 = 3.0 * t2 - 2.0 * t

                    { X =
                        dh00 * b0.Position.X / dt
                        + dh10 * b0.Velocity.X
                        + dh01 * b1.Position.X / dt
                        + dh11 * b1.Velocity.X
                      Y =
                        dh00 * b0.Position.Y / dt
                        + dh10 * b0.Velocity.Y
                        + dh01 * b1.Position.Y / dt
                        + dh11 * b1.Velocity.Y }
                  Spin = Vector2.scale (1.0 - t) b0.Spin |> Vector2.add (Vector2.scale t b1.Spin)
                  VelocityZ = b0.VelocityZ * (1.0 - t) + b1.VelocityZ * t
                  Height = hermiteScalar b0.Height 0.0 b1.Height 0.0 t dt }

        { frame0 with
            Players = players
            Ball = ball
            TimeSeconds = frame0.TimeSeconds + t * dt
            HomeScore = if t < 0.5 then frame0.HomeScore else frame1.HomeScore
            AwayScore = if t < 0.5 then frame0.AwayScore else frame1.AwayScore }
