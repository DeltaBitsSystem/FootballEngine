namespace FootballEngine

open FootballEngine.Domain


module MovementConstants =

    let movementCoefficients (profile: BehavioralProfile) =
        let offensive =
            profile.AttackingDepth * 0.30
            + profile.Directness * 0.25
            + profile.PositionalFreedom * 0.20

        let defensive =
            profile.DefensiveHeight * 0.35
            + profile.PressingIntensity * 0.25
            + (1.0 - profile.PositionalFreedom) * 0.25

        let lateral =
            abs (profile.LateralTendency - 0.5) * 0.50 + profile.PositionalFreedom * 0.25

        (offensive, defensive, lateral)
