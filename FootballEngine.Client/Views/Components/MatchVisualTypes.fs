namespace FootballEngine.Client.Views.Components

open FootballEngine.Domain
open FootballEngine.AppTypes
open FootballEngine.LiveMatch

[<Struct>]
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

type RestartVisual =
    | KickOff
    | ThrowIn
    | Corner
    | GoalKick
    | FreeKick
    | Penalty

type MatchSituation =
    | Live
    | Goal
    | VarReview
    | Injury
    | Restart of RestartVisual option
    | HalfTime
    | ExtraTimeBreak
    | FullTimeReview
    | Shootout
    | Ended

type OfficialSignal =
    | NoSignal
    | Whistle
    | RestartDirection of RestartVisual

type RenderPlayer =
    { Id: PlayerId
      Position: Vector2
      Velocity: Vector2
      Acceleration: Vector2
      State: PlayerVisualState
      VisualSlot: int
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

type RenderOfficial =
    { Position: Vector2
      Signal: OfficialSignal
      IsAssistant: bool }

type RenderFrame =
    { Players: RenderPlayer[]
      Ball: RenderBall
      Officials: RenderOfficial[]
      TimeSeconds: float
      HomeScore: int
      AwayScore: int
      AttackingClubId: ClubId option
      Momentum: float
      Situation: MatchSituation
      HomeName: string
      AwayName: string
      CompetitionName: string option }

type MatchPitchBinding =
    { RuntimeId: MatchRuntimeId
      Fixture: FixtureContext }
