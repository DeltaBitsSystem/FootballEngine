namespace FootballEngine.Core.Match

open System

type MatchAction =
    | ChangeTactics of TacticsConfig
    | RequestSubstitution of outgoing: PlayerId * incoming: PlayerId

type MatchCommand =
    { EffectiveTick: int<subtick>
      Sequence: uint64
      Revision: uint64
      Manager: uint64
      Action: MatchAction }

type MatchInput =
    { MatchId: uint64
      Context: MatchContext
      Seed: uint64
      EffectiveConfig: EffectiveConfig
      EngineVersion: string }

module MatchInput =
    let validate (input: MatchInput) =
        let errors = ResizeArray<string>()

        let validateLineup label (lineup: Lineup) =
            if
                lineup.SlotCount < 1
                || lineup.StarterCount < 1
                || lineup.StarterCount > lineup.SlotCount
            then
                errors.Add(sprintf "%s lineup must have positive, consistent slot and starter counts." label)

            if isNull lineup.Profiles || isNull lineup.BasePositions then
                errors.Add(sprintf "%s lineup must supply profile and position arrays." label)
            elif
                lineup.Profiles.Length <> lineup.SlotCount
                || lineup.BasePositions.Length <> lineup.SlotCount
            then
                errors.Add(sprintf "%s profile and position arrays must align with SlotCount." label)
            else
                if lineup.Profiles |> Array.distinctBy _.Id |> Array.length <> lineup.SlotCount then
                    errors.Add(sprintf "%s lineup contains duplicate sporting player identities." label)
                if lineup.Profiles |> Array.exists (fun p -> let (PlayerId id) = p.Id in id <= 0) then
                    errors.Add(sprintf "%s lineup contains nonpositive sporting player identities." label)

                for position in lineup.BasePositions do
                    if
                        [| float position.X
                           float position.Y
                           float position.Z
                           float position.Vx
                           float position.Vy
                           float position.Vz |]
                        |> Array.exists (System.Double.IsFinite >> not)
                    then
                        errors.Add(sprintf "%s lineup contains non-finite position or velocity." label)

        let context = input.Context

        if context.Config <> input.EffectiveConfig.Balance then
            errors.Add "Match context and effective configuration must describe the same resolved balance."

        if context.HomeClub.Id = context.AwayClub.Id then
            errors.Add "Home and away clubs must have distinct identities."

        for club in [ context.HomeClub; context.AwayClub ] do
            if club.AttackPrior.Count > 24 then errors.Add "Attack priors exceed the fixed contextual bound."
            for KeyValue(key, estimate) in club.AttackPrior do
                if key < 0 || key >= 24 || not (Double.IsFinite estimate.Samples && Double.IsFinite estimate.RewardSum)
                   || estimate.Samples < 0.0 || estimate.Samples > context.Config.CollectiveAttack.EvidenceCap || abs estimate.RewardSum > estimate.Samples then
                    errors.Add "Attack priors contain an invalid contextual estimate."

        validateLineup "Home" context.HomeLineup
        validateLineup "Away" context.AwayLineup

        if
            not (isNull context.HomeLineup.Profiles)
            && not (isNull context.AwayLineup.Profiles)
        then
            let homeIds = context.HomeLineup.Profiles |> Array.map _.Id |> Set.ofArray

            if
                context.AwayLineup.Profiles
                |> Array.exists (fun player -> Set.contains player.Id homeIds)
            then
                errors.Add "A player cannot occupy both teams in one match."

        if String.IsNullOrWhiteSpace input.EngineVersion then
            errors.Add "Match input must identify its engine build."

        if errors.Count = 0 then Ok() else Error(List.ofSeq errors)

    let private copyLineup (lineup: Lineup) =
        { lineup with
            Profiles = Array.copy lineup.Profiles
            BasePositions = Array.copy lineup.BasePositions }

    let private copyParticipants (participants: MatchLineupBoundary) =
        { participants with
            Starters = Array.copy participants.Starters
            Substitutes = Array.copy participants.Substitutes }

    // Records and maps are immutable, but slot arrays must not remain owned by a caller or UI worker.
    let snapshot (input: MatchInput) =
        let context = input.Context

        { input with
            Context =
                { context with
                    HomeLineup = copyLineup context.HomeLineup
                    AwayLineup = copyLineup context.AwayLineup
                    HomeParticipants = copyParticipants context.HomeParticipants
                    AwayParticipants = copyParticipants context.AwayParticipants
                    SubstitutionRules =
                        { context.SubstitutionRules with
                            WindowStarts = Array.copy context.SubstitutionRules.WindowStarts } } }

type MatchFacts =
    { HomeScore: int
      AwayScore: int
      EventsHash: uint64 }

type MatchLoad =
    { HomeEvents: int
      AwayEvents: int
      TotalTicks: int<subtick> }

type MatchDiscipline =
    { YellowCards: int
      RedCards: int
      Fouls: int }

type MatchTraceRef = { Version: int; Events: int }

type MatchResult =
    { MatchId: uint64
      EngineVersion: string
      Facts: MatchFacts
      Load: MatchLoad
      InjuredPlayers: PlayerId list
      Discipline: MatchDiscipline
      Metrics: TeamStats * TeamStats
      Trace: MatchTraceRef option }

type MatchPlayerStats =
    { PlayerId: FootballEngine.Domain.PlayerId
      GoalsScored: int
      AssistsGiven: int
      MinutesPlayed: int
      YellowCards: int
      RedCards: int
      FoulsCommitted: int }

/// A stale worker cannot overwrite the live world through this result.
type MatchJobResult =
    { Input: MatchInput
      Commands: MatchCommand list
      MatchId: int
      SourceRevision: uint64
      InputHash: uint64
      ResultHash: uint64
      HomeScore: int
      AwayScore: int
      EventsHash: uint64
      Events: MatchEvent list
      InjuredPlayers: FootballEngine.Domain.PlayerId list
      HomePlayerStats: MatchPlayerStats[]
      AwayPlayerStats: MatchPlayerStats[]
      Metrics: TeamStats * TeamStats
      Replay: Replay.ReplayData option }

type MatchContractError =
    | InvalidCommandOrder of string
    | InvalidCommandRevision of string
    | InvalidCommandManager of string
    | InvalidCommandTiming of string
    | InvalidCommandPlayers of string
    | InvalidCommandRules of string
    | CommandLimitExceeded of int
    | InvalidAdvanceBoundary of string
    | TickLimitExceeded of int<subtick>
    | SessionAlreadyFinished
    | InvalidRunMode of string
    | InvalidInputConfig of string list
    | InvalidMatchInput of string list
    | InvalidCheckpoint of string

type MatchCheckpoint =
    { Version: int
      MatchId: uint64
      Seed: uint64
      EngineVersion: string
      Revision: uint64
      InputHash: uint64
      Mode: MatchRunMode
      Frontier: int<subtick>
      Rng: Rng
      Commands: MatchCommand list
      Pending: MatchCommand list
      EventsHash: uint64
      TraceCount: int
      HomeDefense: DefensiveTransitionState
      AwayDefense: DefensiveTransitionState
      HomeAttack: CollectiveAttackState
      AwayAttack: CollectiveAttackState }

type SessionAdvance =
    { ReachedTick: int<subtick>
      Flow: MatchFlow
      Events: MatchEvent[]
      Poses: Replay.ReplaySnapshot[]
      Motion: MotionBoundary[] }
