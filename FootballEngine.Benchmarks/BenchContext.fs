namespace FootballEngine.Benchmarks

/// Minimal realistic MatchContext builder (mirrors the tests' TestBuilders but
/// standalone so the bench project only references Core). 4-3-3 vs 4-4-2.
module BenchContext =

    open FootballEngine.Core.Match

    let private profile (id: int) (role: SlotRole) : PlayerProfile =
        let baseAttr =
            match role with
            | SlotRole.Goalkeeper -> 70.0f
            | SlotRole.Striker -> 75.0f
            | _ -> 65.0f

        { Id = PlayerId id
          Role = role
          IsGK = role = SlotRole.Goalkeeper
          Pace = baseAttr
          Strength = baseAttr
          Stamina = baseAttr
          Agility = baseAttr
          Vision = baseAttr
          Passing = baseAttr
          Finishing = baseAttr
          Crossing = baseAttr
          Dribbling = baseAttr
          Tackling = baseAttr
          Marking = baseAttr
          Positioning = baseAttr
          Anticipation = baseAttr
          Composure = baseAttr
          Aggression = baseAttr
          Flair = baseAttr
          Teamwork = baseAttr
          DecisionMaking = baseAttr
          Personality =
            { Composure = 0.5f
              Aggression = 0.5f
              RiskTaking = 0.5f
              Flair = 0.5f
              Leadership = 0.5f
              Consistency = 0.5f
              BigGameMentality = 0.5f }
          InjuryProne = 0.1f }

    let private roles4: SlotRole[] =
        [| SlotRole.Goalkeeper
           SlotRole.Defender
           SlotRole.Defender
           SlotRole.Defender
           SlotRole.Defender
           SlotRole.DefensiveMid
           SlotRole.CenterMid
           SlotRole.CenterMid
           SlotRole.Winger
           SlotRole.AttackingMid
           SlotRole.Striker |]

    let private roles4_4_2: SlotRole[] =
        [| SlotRole.Goalkeeper
           SlotRole.FullBack
           SlotRole.Defender
           SlotRole.Defender
           SlotRole.FullBack
           SlotRole.CenterMid
           SlotRole.CenterMid
           SlotRole.Winger
           SlotRole.Winger
           SlotRole.Striker
           SlotRole.Striker |]

    let private lineup (offset: int) (roles: SlotRole[]) : Lineup =
        let profiles =
            [| for i in 0 .. roles.Length - 1 -> profile (offset + i + 1) roles.[i] |]

        let basePositions =
            [| for i in 0 .. roles.Length - 1 ->
                   { X = -30.0<meter> + (float (i % 4) * 10.0<meter>)
                     Y = -20.0<meter> + (float (i % 3) * 10.0<meter>)
                     Z = 0.0<meter>
                     Vx = 0.0<meterPerSecond>
                     Vy = 0.0<meterPerSecond>
                     Vz = 0.0<meterPerSecond> } |]

        { Profiles = profiles
          BasePositions = basePositions
          SlotCount = roles.Length }

    let create () : MatchContext =
        let tactics (press: float32) (width: float32) (line: float32) : TacticsConfig =
            { Formation = "x"
              DefensiveLine = line
              PressIntensity = press
              Width = width
              Tempo = 0.5f
              Directness = 0.5f }

        { HomeClub =
            { Id = 1UL
              Name = "Home FC"
              BaseTactics = tactics 0.5f 0.5f 0.5f
              Chemistry = 0.6
              HomeAdvantage =
                { CrowdSupport = 0.5f
                  Familiarity = 0.5f } }
          AwayClub =
            { Id = 2UL
              Name = "Away FC"
              BaseTactics = tactics 0.4f 0.6f 0.4f
              Chemistry = 0.6
              HomeAdvantage =
                { CrowdSupport = 0.5f
                  Familiarity = 0.5f } }
          HomeLineup = lineup 0 roles4
          AwayLineup = lineup 100 roles4_4_2
          Config = BalanceConfig.defaultConfig
          IsKnockout = false }
