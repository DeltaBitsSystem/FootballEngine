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

    let private norm (v: int) = float32 v

    let private norm01 (v: int) = float32 v / 100.0f

    let private roleOf (pos: FootballEngine.Domain.Position) : SlotRole =
        match pos with
        | FootballEngine.Domain.Position.GK -> SlotRole.Goalkeeper
        | FootballEngine.Domain.Position.DC -> SlotRole.Defender
        | FootballEngine.Domain.Position.DL
        | FootballEngine.Domain.Position.DR
        | FootballEngine.Domain.Position.WBL
        | FootballEngine.Domain.Position.WBR -> SlotRole.FullBack
        | FootballEngine.Domain.Position.DM -> SlotRole.DefensiveMid
        | FootballEngine.Domain.Position.MC -> SlotRole.CenterMid
        | FootballEngine.Domain.Position.AMC -> SlotRole.AttackingMid
        | FootballEngine.Domain.Position.ML
        | FootballEngine.Domain.Position.MR
        | FootballEngine.Domain.Position.AML
        | FootballEngine.Domain.Position.AMR -> SlotRole.Winger
        | FootballEngine.Domain.Position.ST -> SlotRole.Striker

    /// Match fitness made real: scales engine physicals by readiness in [floor, 1].
    /// Applied once at context build so the per-tick engine stays allocation-free.
    /// Only physicals scale: a tired genius still sees the pass, he just arrives late.
    let withReadiness (readiness: float32) (profile: PlayerProfile) : PlayerProfile =
        let r = max 0.0f (min 1.0f readiness)

        { profile with
            Pace = profile.Pace * r
            Strength = profile.Strength * r
            Stamina = profile.Stamina * r
            Agility = profile.Agility * r }

    /// Domain Player -> engine PlayerProfile. The ONLY place Domain types appear in the match engine.
    let toProfile (p: FootballEngine.Domain.Player) : PlayerProfile =
        let phys = p.Physical
        let tech = p.Technical
        let mental = p.Mental
        let gk = p.Goalkeeping

        { Id = PlayerId p.Id
          Role = roleOf p.Position
          IsGK = p.Position = FootballEngine.Domain.Position.GK
          Pace = norm phys.Pace
          Strength = norm phys.Strength
          Stamina = norm phys.Stamina
          Agility = norm phys.Agility
          Vision = norm mental.Vision
          Passing = norm tech.Passing
          Finishing = norm tech.Finishing
          Crossing = norm tech.Crossing
          Dribbling = norm tech.Dribbling
          Tackling = norm tech.Tackling
          Marking = norm tech.Marking
          Positioning = norm mental.Positioning
          Anticipation = norm mental.Concentration
          Composure = norm mental.Composure
          Aggression = norm mental.Aggression
          Flair = norm tech.BallControl
          Teamwork = norm mental.WorkRate
          DecisionMaking = norm mental.Concentration
          Personality =
            { Composure = norm01 mental.Composure
              Aggression = norm01 mental.Aggression
              RiskTaking = norm01 mental.Bravery
              Flair = norm01 tech.BallControl
              Leadership = norm01 mental.Leadership
              Consistency = norm01 mental.Concentration
              BigGameMentality = norm01 p.Morale }
          InjuryProne = 0.1f }
