namespace FootballEngine.Domain

open System

type ClubSide =
    | HomeClub
    | AwayClub

module ClubSide =
    let flip =
        function
        | HomeClub -> AwayClub
        | AwayClub -> HomeClub

type MatchPhase =
    | BuildUp
    | Midfield
    | Attack

type MatchEventType =
    | Goal
    | GoalKick
    | OwnGoal
    | Assist
    | YellowCard
    | RedCard
    | Injury of description: string
    | SubstitutionIn
    | SubstitutionOut
    | PenaltyAwarded of isScored: bool
    | FreeKick of isScored: bool
    | Corner
    | PassCompleted of fromPlayer: PlayerId * toPlayer: PlayerId
    | PassLaunched of fromPlayer: PlayerId * toPlayer: PlayerId
    | PassIncomplete of fromPlayer: PlayerId
    | PassDeflected of fromPlayer: PlayerId * deflectedById: PlayerId
    | PassIntercepted of fromPlayer: PlayerId * interceptorId: PlayerId
    | PassMisplaced of fromPlayer: PlayerId * toPlayer: PlayerId
    | DribbleSuccess
    | DribbleFail
    | DribbleKeep
    | TackleSuccess
    | TackleFail
    | CrossAttempt of isSuccessful: bool
    | CrossLaunched of fromPlayer: PlayerId * toPlayer: PlayerId
    | LongBall of isSuccessful: bool
    | ShotBlocked
    | ShotLaunched
    | ShotOnTarget
    | ShotOffTarget
    | Save
    | SaveCaught of shooterId: PlayerId * gkId: PlayerId
    | SaveParried of shooterId: PlayerId * gkId: PlayerId
    | GKPunch of gkId: PlayerId
    | GKDistribution of gkId: PlayerId * targetId: PlayerId
    | IndirectFreeKickAwarded of team: ClubSide
    | FoulCommitted
    | KickOff


type PitchPos = { X: float; Y: float }

type EventSecondary =
    | Deflected
    | Rebound
    | BlockedByWall
    | DeflectedOffPost
    | KeeperParried

type EventContext =
    { Position: PitchPos option
      Quality: float option
      ExpectedGoal: float option
      AssistId: PlayerId option
      SecondaryResult: EventSecondary option }

module EventContext =
    let empty =
        { Position = None
          Quality = None
          ExpectedGoal = None
          AssistId = None
          SecondaryResult = None }

    let at x y =
        { empty with
            Position = Some { X = x; Y = y } }


type MatchEvent =
    { SubTick: int
      PlayerId: PlayerId
      ClubId: ClubId
      Type: MatchEventType
      Context: EventContext }

type MatchFixture =
    { Id: MatchId
      CompetitionId: CompetitionId
      Round: Round option
      HomeClubId: ClubId
      AwayClubId: ClubId
      ScheduledDate: DateTime
      Played: bool
      HomeScore: int option
      AwayScore: int option
      Events: MatchEvent list }

type AttackDir =
    | LeftToRight
    | RightToLeft


type PitchZone =
    | AttackingZone
    | MidfieldZone
    | DefensiveZone

type FoulSeverity =
    | Trivial
    | TacticalFoul
    | ProfessionalFoul
    | DOGSO
    | SeriousFoulPlay
    | ViolentConduct

type PassType =
    | GroundPass
    | LoftedPass
    | ThroughBall
    | Chip
    | DrivenPass
    | CutBack
    | LayOff
    | OneTwo

type ShotType =
    | DrivenShot
    | PlacedShot
    | ChipShot
    | Volley
    | HalfVolley
    | Header
    | FirstTimeShot
    | Curler

type TackleType =
    | StandingTackle
    | SlideTackle
    | Block
    | Jockey

type DribbleType =
    | SpeedDribble
    | SkillDribble
    | ShieldDribble
    | KnockAndRun




module MatchFixture =

    let involves (clubId: ClubId) (f: MatchFixture) =
        f.HomeClubId = clubId || f.AwayClubId = clubId

    let isPlayed (f: MatchFixture) = f.Played

    let isPending (f: MatchFixture) = not f.Played

    let scoreFor (clubId: ClubId) (f: MatchFixture) =
        match f.HomeScore, f.AwayScore with
        | Some h, Some a ->
            if f.HomeClubId = clubId then Some(h, a)
            elif f.AwayClubId = clubId then Some(a, h)
            else None
        | _ -> None
