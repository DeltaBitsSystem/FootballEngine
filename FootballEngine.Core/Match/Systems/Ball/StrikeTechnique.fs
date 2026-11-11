namespace FootballEngine.Core.Match

/// The part of the boot that meets a kicked ball and what it does to the strike. The surface is chosen when the
/// kick is requested, so the viewer can prepare the matching swing; the strike is shaped at contact, from the
/// kicker's technique, pressure and balance at that instant.
module StrikeTechnique =

    let private surfaceConfig (cfg: StrikeTechniqueConfig) surface =
        match surface with
        | StrikeSurface.Inside -> cfg.Inside
        | StrikeSurface.Instep -> cfg.Instep
        | StrikeSurface.Outside -> cfg.Outside

    /// Positive for the right boot, whose inside faces left and curls the ball right to left (counter-clockwise
    /// seen from above, positive side spin).
    let footSign foot =
        match foot with
        | ContactFoot.Right -> 1.0
        | ContactFoot.Left -> -1.0

    let choose (cfg: StrikeTechniqueConfig) (impulse: BallImpulse) (foot: ContactFoot) (facing: float) =
        let vx, vy = float impulse.LinearVelocity.X, float impulse.LinearVelocity.Y
        let forward = cos facing * vx + sin facing * vy
        let leftward = -sin facing * vx + cos facing * vy
        // Measured toward the kicking foot's outer side: right of the body for the right boot.
        let outward = -footSign foot * atan2 leftward forward
        let speed = sqrt (vx * vx + vy * vy)

        // A curled strike is bent with the inside of the boot, which is what gives it its side spin.
        if abs (float impulse.AngularVelocity.Side) > 1e-6 then StrikeSurface.Inside
        elif outward > cfg.OutsideAngle then StrikeSurface.Outside
        elif speed >= float cfg.InstepSpeed then StrikeSurface.Instep
        else StrikeSurface.Inside

    /// Technique that governs a strike kind, in [0, 1].
    let technique (profile: PlayerProfile) kind =
        match kind with
        | StrikeKind.Cross -> float profile.Crossing / 100.0
        | StrikeKind.Shot -> float profile.Finishing / 100.0
        | _ -> float profile.Passing / 100.0

    /// Direction error (radians, one sigma) of a strike from this surface.
    let spread (cfg: StrikeTechniqueConfig) surface technique opponents composure offBalance =
        let s = surfaceConfig cfg surface
        let pressure = float opponents * cfg.PressureSpreadPerOpponent * (1.0 - cfg.ComposureShield * composure)

        s.Spread
        * (1.0 - cfg.TechniqueShare * MatchMath.clamp01 technique)
        * (1.0 + pressure)
        * (if offBalance then cfg.OffBalanceSpread else 1.0)

    let offBalance (frame: TeamFrame) slot =
        let state = frame.ContactState[slot]
        state.Status <> BodyContactStatus.BodyStable || state.Support = BodySupport.NoBodySupport

    let shape
        (ctx: MatchContext)
        (world: World)
        (side: ClubSide)
        (slot: int)
        (foot: ContactFoot)
        (surface: StrikeSurface)
        (impulse: BallImpulse)
        (rng: Rng)
        : struct (BallImpulse * Rng) =
        // A shot carries its own aim error and curl from the finishing model; adding a surface error and spin
        // would count the same miss twice.
        if impulse.StrikeKind = StrikeKind.Shot then
            struct (impulse, rng)
        else
            let cfg = ctx.Config.StrikeTechnique
            let frame = (MatchWorld.team world side).Frame
            let profile = MatchWorld.profileOf ctx side slot

            let opponents =
                MatchWorld.opponentsNear
                    ctx
                    world
                    side
                    (MatchWorld.posX frame slot)
                    (MatchWorld.posY frame slot)
                    (float cfg.PressureRadius)

            let sigma =
                spread
                    cfg
                    surface
                    (technique profile impulse.StrikeKind)
                    opponents
                    (float profile.Composure / 100.0)
                    (offBalance frame slot)

            let angle, rng = Rng.normal 0.0 sigma rng
            let vx, vy = float impulse.LinearVelocity.X, float impulse.LinearVelocity.Y
            let c, s = cos angle, sin angle
            let speed = sqrt (vx * vx + vy * vy)
            let sense = if surface = StrikeSurface.Outside then -1.0 else 1.0
            let sideSpin = sense * footSign foot * (surfaceConfig cfg surface).SideSpinPerSpeed * speed

            let shaped =
                { impulse with
                    LinearVelocity =
                        { impulse.LinearVelocity with
                            X = (vx * c - vy * s) * 1.0<meterPerSecond>
                            Y = (vx * s + vy * c) * 1.0<meterPerSecond> }
                    AngularVelocity =
                        { impulse.AngularVelocity with
                            Side = impulse.AngularVelocity.Side + sideSpin * 1.0<radianPerSecond> } }

            struct (shaped, rng)
