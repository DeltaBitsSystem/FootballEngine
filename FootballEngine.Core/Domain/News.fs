namespace FootballEngine.Domain

open System

[<RequireQualifiedAccess>]
type NewsScope =
    | UserClub
    | UserLeague
    | World

/// Declaration order is the ranking: comparison must order Minor below Headline.
[<RequireQualifiedAccess>]
type NewsImportance =
    | Minor
    | Notable
    | Major
    | Headline

[<RequireQualifiedAccess>]
type StreakKind =
    | Wins
    | Unbeaten
    | Winless
    | Losses

[<RequireQualifiedAccess>]
type FansMoodBand =
    | Furious
    | Restless
    | Content
    | Euphoric

[<RequireQualifiedAccess>]
type UnrestReason =
    | LackOfMinutes
    | ContractStalled
    | BiggerClubInterest

[<RequireQualifiedAccess>]
type MilestoneKind =
    | Debut
    | Appearances
    | Goals

[<RequireQualifiedAccess>]
type AwardKind =
    | TopScorer
    | PlayerOfSeason
    | YoungPlayer
    | ManagerOfSeason

[<RequireQualifiedAccess>]
type MatchOutcomeTag =
    | Won
    | Drawn
    | Lost

/// Structured facts only; the client owns every word shown to the player.
[<RequireQualifiedAccess>]
type NewsKind =
    | BigWin of winner: ClubId * loser: ClubId * goalsFor: int * goalsAgainst: int
    | Upset of underdog: ClubId * favourite: ClubId * goalsUnderdog: int * goalsFavourite: int
    | Streak of club: ClubId * kind: StreakKind * length: int
    | NewLeader of club: ClubId * competition: CompetitionId
    | Clasico of home: ClubId * away: ClubId
    | HatTrick of player: PlayerId * club: ClubId * goals: int
    | ScoringRun of player: PlayerId * matches: int
    | BoardUnrest of club: ClubId * confidence: int
    | ManagerSacked of club: ClubId * manager: StaffId * matchesInCharge: int
    | ManagerAppointed of club: ClubId * manager: StaffId
    | FansMood of club: ClubId * mood: FansMoodBand
    | TransferRequest of player: PlayerId * club: ClubId * reason: UnrestReason
    | ContractRunningOut of player: PlayerId * club: ClubId * monthsLeft: int
    | Milestone of player: PlayerId * kind: MilestoneKind * value: int
    | Breakout of player: PlayerId * club: ClubId
    | Retirement of player: PlayerId * age: int
    | Rumour of player: PlayerId * from: ClubId * interested: ClubId
    | TransferDone of player: PlayerId * from: ClubId option * toClub: ClubId * fee: decimal
    | Award of kind: AwardKind * player: PlayerId option * staff: StaffId option * competition: CompetitionId
    | Promotion of club: ClubId * competition: CompetitionId
    | Relegation of club: ClubId * competition: CompetitionId
    | Champion of club: ClubId * competition: CompetitionId

type NewsItem =
    { Id: int
      Date: DateTime
      Kind: NewsKind
      Importance: NewsImportance
      Scope: NewsScope
      Clubs: ClubId list
      Players: PlayerId list }

/// One appearance, recorded where the experience counters change so milestones are exact rather than inferred.
type PlayerAppearance =
    {
        Player: PlayerId
        /// Career appearances after this match.
        Matches: int
        /// Career goals after this match.
        GoalsAfter: int
        GoalsInMatch: int
    }

/// A transfer that actually completed (money moved, affiliation changed); no other record of it survives.
type CompletedTransfer =
    { Player: PlayerId
      From: ClubId option
      To: ClubId
      Fee: decimal }

/// Facts produced by the simulation that the pulse consumes exactly once.
type PulseQueues =
    {
        /// Newest first.
        Appearances: PlayerAppearance list
        /// Newest first.
        Transfers: CompletedTransfer list
        /// Negotiation ids already considered for rumours.
        NegotiationsSeen: Set<int>
    }

module PulseQueues =

    /// Bounds memory when nothing drains the queues (headless runs without the pulse).
    let private cap = 5000

    let empty =
        { Appearances = []
          Transfers = []
          NegotiationsSeen = Set.empty }

    let recordAppearance (appearance: PlayerAppearance) (queues: PulseQueues) : PulseQueues =
        { queues with
            Appearances = appearance :: queues.Appearances |> List.truncate cap }

    let recordTransfer (transfer: CompletedTransfer) (queues: PulseQueues) : PulseQueues =
        { queues with
            Transfers = transfer :: queues.Transfers |> List.truncate cap }
