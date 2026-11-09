namespace FootballEngine.Core.Match

/// A keeper reads a strike after his reaction time, predicts where the ball will cross his plane with the same
/// integrator that then flies it, and commits his gloves there if a dive can get them there in time. Contact with
/// the gloves is resolved by ContactDynamics like any other surface, so saves, parries and goals all come from
/// geometry and timing rather than a save roll.
module KeeperSave =

    [<Struct>]
    type private Crossing = { Y: float; Z: float; Seconds: float }

    let private maximumPredictionSeconds = 3.0

    /// First crossing of the plane x = planeX by the ball centre, interpolated within the step.
    let private crossingAt (cfg: BallConfig) dt tick (ball: BallState) planeX : Crossing voption =
        let radius = float ball.Radius
        let mutable state = ball
        let mutable elapsed = 0.0
        let mutable result = ValueNone

        while result.IsNone && elapsed < maximumPredictionSeconds do
            let next = BallDynamics.integrate cfg dt tick state
            let before = float state.Position.X - planeX
            let after = float next.Position.X - planeX

            if before * after <= 0.0 && before <> after then
                let f = before / (before - after)

                result <-
                    ValueSome
                        { Y = float state.Position.Y + (float next.Position.Y - float state.Position.Y) * f
                          Z = float state.Position.Z + (float next.Position.Z - float state.Position.Z) * f + radius
                          Seconds = elapsed + f * dt }

            state <- next
            elapsed <- elapsed + dt

        result

    let private inOwnBox side x y =
        abs (x - float (MatchMath.ownGoalX side)) <= float Pitch.PenaltyAreaDepth
        && abs y <= float Pitch.PenaltyAreaHalfWidth

    let private react (ctx: SystemContext) (rng: Rng) (world: World) (buffer: EventBuffer) (side: ClubSide) : Rng =
        let team = MatchWorld.team world side
        let tick = world.State.Tick
        let cfg = ctx.Ctx.Config.Keeper

        match team.Dive with
        | ValueSome dive when tick > dive.ContactAt + cfg.HandWindow -> team.Dive <- ValueNone
        | _ -> ()

        let ball = world.State.Ball

        match team.Dive, team.GoalkeeperSlot, ball.Trajectory, ball.Impulse with
        | ValueNone, Some slot, Some(TrajectoryIntent.Struck(shooter, _, _, _)), Some impulse when
            MatchWorld.isActive team.Frame slot
            && MatchWorld.teamOfPlayer ctx.Ctx shooter = Some(MatchWorld.oppOf side)
            ->
            let frame = team.Frame
            let profile = MatchWorld.profileOf ctx.Ctx side slot
            let kx = float (MatchWorld.posX frame slot)
            let ky = float (MatchWorld.posY frame slot)
            let perSecond = float ctx.Clock.SubTicksPerSecond
            let reaction = float cfg.ReactionBase - float cfg.ReactionSkill * float profile.Anticipation / 100.0
            let sinceStrike = float (tick - SimTick.value impulse.ContactTick) / perSecond

            // Hands are only legal inside his own area; elsewhere he defends a shot with his body like anyone.
            if sinceStrike < reaction || not (inOwnBox side kx ky) then
                rng
            else
                match crossingAt ctx.Ctx.Config.Ball (float (SimulationClock.dt ctx.Clock)) tick ball kx with
                | ValueNone -> rng
                | ValueSome crossing ->
                    let stature =
                        if profile.Morphology.Height > 0.0f then float profile.Morphology.Height else 1.88

                    let shoulderZ = float frame.PosZ[slot] + stature * cfg.ShoulderHeightRatio
                    let lateral = crossing.Y - ky
                    let vertical = crossing.Z - shoulderZ
                    let required = sqrt (lateral * lateral + vertical * vertical)
                    let agility = float profile.Agility / 100.0

                    let dive =
                        min
                            (float cfg.MaxDive)
                            (float cfg.DiveSpeed
                             * (0.75 + 0.5 * agility)
                             * max 0.0 (crossing.Seconds - float cfg.DiveSetup))

                    if required > float cfg.ArmReach + dive then
                        rng
                    else
                        let stretch =
                            if cfg.MaxDive > 0.0<meter> then
                                MatchMath.clamp01 ((required - float cfg.ArmReach) / float cfg.MaxDive)
                            else
                                0.0

                        // Full-stretch saves are less precise, and a well-positioned keeper places his hands better.
                        let sigma =
                            float cfg.HandError
                            * (0.3 + 0.7 * stretch)
                            * (1.0 - 0.5 * float profile.Positioning / 100.0)

                        let errorY, rng1 = Rng.normal 0.0 sigma rng
                        let errorZ, rng2 = Rng.normal 0.0 sigma rng1
                        let contactAt = tick + int (round (crossing.Seconds * perSecond)) * 1<subtick>

                        let hand: Vec3 =
                            { X = kx * 1.0<meter>
                              Y = (crossing.Y + errorY) * 1.0<meter>
                              Z = max (float cfg.HandRadius) (crossing.Z + errorZ) * 1.0<meter> }

                        team.Dive <-
                            ValueSome
                                { KeeperSlot = slot
                                  CommittedAt = tick
                                  ContactAt = contactAt
                                  Hand = hand
                                  Stretch = stretch }

                        buffer.Events.Add
                            { Tick = tick
                              Data = EventData.KeeperDive(profile.Id, hand.X, hand.Y, hand.Z, contactAt) }

                        rng2
        | _ -> rng

    let step: SystemStep =
        fun ctx rng world buffer ->
            let rng = react ctx rng world buffer ClubSide.Home
            react ctx rng world buffer ClubSide.Away
