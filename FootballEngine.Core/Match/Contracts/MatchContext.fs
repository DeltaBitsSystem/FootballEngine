namespace FootballEngine.Core.Match

[<Struct>]
type HomeAdvantageConfig =
    { CrowdSupport: float32
      Familiarity: float32 }

/// What the match needs to know about the coach on the bench. `Autonomous` is true for every side the user does
/// not command: only those get the in-match brain, so the user's side keeps behaving exactly as it always did.
/// The four traits run 0..1.
[<Struct>]
type ManagerProfile =
    { Autonomous: bool
      Reading: float32
      Boldness: float32
      Temperament: float32
      Adaptability: float32 }

module ManagerProfile =

    /// A side commanded by a person: no brain, and neutral traits nothing reads.
    let human: ManagerProfile =
        { Autonomous = false
          Reading = 0.5f
          Boldness = 0.5f
          Temperament = 0.5f
          Adaptability = 0.5f }

[<Struct>]
type ClubRef =
    { Id: uint64
      Name: string
      BaseTactics: TacticsConfig
      Chemistry: float
      AttackPrior: Map<int, FootballEngine.Domain.AttackEstimate>
      Familiarity: float32
      HomeAdvantage: HomeAdvantageConfig
      Manager: ManagerProfile }

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
