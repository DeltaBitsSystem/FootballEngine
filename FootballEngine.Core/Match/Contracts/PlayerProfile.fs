namespace FootballEngine.Core.Match

[<Struct>]
type BehaviorProfile =
    { Composure: float32
      Aggression: float32
      RiskTaking: float32
      Flair: float32
      Leadership: float32
      Consistency: float32
      BigGameMentality: float32 }

/// Engine-side player attributes — decoupled from Domain. Slot-indexed by team lineup.
[<Struct>]
type PlayerProfile =
    { Id: PlayerId
      Role: SlotRole
      IsGK: bool
      Pace: float32
      Strength: float32
      Stamina: float32
      Agility: float32
      Vision: float32
      Passing: float32
      Finishing: float32
      Crossing: float32
      Dribbling: float32
      Tackling: float32
      Marking: float32
      Positioning: float32
      Anticipation: float32
      Composure: float32
      Aggression: float32
      Flair: float32
      Teamwork: float32
      DecisionMaking: float32
      Personality: BehaviorProfile
      InjuryProne: float32 }

module Projection =

    /// Domain Player -> engine PlayerProfile. The ONLY place Domain types appear in the match engine.
    /// Placeholder mapping — the real projection lands in P9.
    let toProfile (_player: FootballEngine.Domain.Player) : PlayerProfile =
        { Id = PlayerId 0
          Role = SlotRole.CenterMid
          IsGK = false
          Pace = 60.0f
          Strength = 60.0f
          Stamina = 60.0f
          Agility = 60.0f
          Vision = 60.0f
          Passing = 60.0f
          Finishing = 60.0f
          Crossing = 60.0f
          Dribbling = 60.0f
          Tackling = 60.0f
          Marking = 60.0f
          Positioning = 60.0f
          Anticipation = 60.0f
          Composure = 60.0f
          Aggression = 60.0f
          Flair = 60.0f
          Teamwork = 60.0f
          DecisionMaking = 60.0f
          Personality =
            { Composure = 0.5f
              Aggression = 0.5f
              RiskTaking = 0.5f
              Flair = 0.5f
              Leadership = 0.5f
              Consistency = 0.5f
              BigGameMentality = 0.5f }
          InjuryProne = 0.1f }
