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

[<Struct>]
type MatchPhase =
    | BuildUp
    | Midfield
    | Attack

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
      Events: FootballEngine.Core.Match.MatchEvent list }

type AttackDir =
    | LeftToRight
    | RightToLeft


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

    /// True for knockout rounds (a draw must be resolved by extra time/shootout).
    let isKnockout (f: MatchFixture) =
        match f.Round with
        | Some(KnockoutRound _)
        | Some Final
        | Some ThirdPlace -> true
        | _ -> false
