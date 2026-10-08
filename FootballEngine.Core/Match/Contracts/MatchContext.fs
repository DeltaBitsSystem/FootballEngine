namespace FootballEngine.Core.Match

[<Struct>]
type HomeAdvantageConfig =
    { CrowdSupport: float32
      Familiarity: float32 }

[<Struct>]
type ClubRef =
    { Id: uint64
      Name: string
      BaseTactics: TacticsConfig
      Chemistry: float
      Familiarity: float32
      HomeAdvantage: HomeAdvantageConfig }

/// Slot-indexed: the engine addresses players by fixed slot, so frame arrays
/// align 1:1 with the lineup.
type Lineup =
    { Profiles: PlayerProfile[]
      BasePositions: Spatial[]
      SlotCount: int
      StarterCount: int }

/// The World-to-match contract for one participant. The engine keeps the
/// profile slot-indexed for hot-path access, while this record keeps roster
/// eligibility and player identity explicit at the boundary.
[<Struct>]
type MatchParticipant =
    { Id: PlayerId
      Role: SlotRole
      Eligible: bool
      InitialCondition: float32 }

type MatchLineupBoundary =
    { Starters: MatchParticipant[]
      Substitutes: MatchParticipant[]
      Eligibility: Map<PlayerId, bool>
      Roles: Map<PlayerId, SlotRole> }

module MatchLineupBoundary =

    let fromLineup (lineup: Lineup) : MatchLineupBoundary =
        let participants =
            lineup.Profiles
            |> Array.map (fun profile ->
                { Id = profile.Id
                  Role = profile.Role
                  Eligible = true
                  InitialCondition = 1.0f })

        { Starters = participants |> Array.take lineup.StarterCount
          Substitutes = participants |> Array.skip lineup.StarterCount
          Eligibility = participants |> Array.map (fun p -> p.Id, p.Eligible) |> Map.ofArray
          Roles = participants |> Array.map (fun p -> p.Id, p.Role) |> Map.ofArray }

type CompetitionSubstitutionRules =
    { MaxSubstitutions: int
      WindowStarts: int<matchMin>[]
      AllowExtraTime: bool }

type MatchTieResolution =
    | AllowDraw
    | ExtraTimeThenPenalties
    | PenaltiesAfterRegulation

/// Everything fixed before kickoff — clubs, lineups, config, stage. With the
/// match seed, it fully determines the simulation.
type MatchContext =
    { HomeClub: ClubRef
      AwayClub: ClubRef
      HomeLineup: Lineup
      AwayLineup: Lineup
      HomeParticipants: MatchLineupBoundary
      AwayParticipants: MatchLineupBoundary
      SubstitutionRules: CompetitionSubstitutionRules
      Config: BalanceConfig
      TieResolution: MatchTieResolution }
