namespace FootballEngine.Core.Match

open System
open System.Numerics

[<Struct>]
type private ImpactCandidate =
    { Fraction: float
      PlayerA: int
      PlayerB: int
      IsBall: bool
      Surface: ContactSurface
      FootFraction: float
      /// The surface is a diving keeper's gloves rather than a body-fixed hand.
      Gloves: bool }

/// The sole 40 Hz integrator for player roots and the ball. Candidate scans use
/// fixed-capacity per-match scratch; rendering and event capture never feed back.
module ContactDynamics =
    let private wrapAngle angle = if angle > System.Math.PI || angle <= -System.Math.PI then angle - 2.0 * System.Math.PI * System.Math.Round(angle / (2.0 * System.Math.PI)) else angle

    let private playerValue (PlayerId value) = value

    let private surfaceTag surface =
        match surface with
        | ContactSurface.ContactTorso -> 0
        | ContactSurface.ContactHead -> 1
        | ContactSurface.ContactLeg -> 2
        | ContactSurface.ContactFoot -> 3
        | ContactSurface.ContactHand -> 4
        | ContactSurface.ContactBall -> 5

    let private profileAt (ctx: MatchContext) (world: World) (scratch: ContactScratch) index =
        MatchWorld.profileOf ctx scratch.Sides[index] scratch.Slots[index]

    let private playerIdAt ctx world scratch index = (profileAt ctx world scratch index).Id

    let private actionAt (frame: TeamFrame) slot =
        frame.ContactActions[slot]
        |> Option.filter (fun action -> action.Phase = ContactActionPhase.ContactActionCommitted)

    /// A boot challenge whose leg is still out: committed, or completed on the ball but within the committed window,
    /// when a late follow-through can still catch the opponent's legs.
    let private bootChallenge (config: ContactActionConfig) (tick: int<subtick>) (frame: TeamFrame) slot =
        frame.ContactActions[slot]
        |> Option.filter (fun action ->
            (action.Kind = ContactActionKind.ContactStandingTackle || action.Kind = ContactActionKind.ContactSlidingTackle)
            && action.Generation = frame.ContactGeneration[slot]
            && (action.Phase = ContactActionPhase.ContactActionCommitted
                || action.Phase = ContactActionPhase.ContactActionCompleted
                   && tick < action.CommitAt + config.CommittedDuration))

    let private footPositionFor
        (ctx: MatchContext)
        (frame: TeamFrame)
        slot
        (action: ContactAction option)
        (morphology: PhysicalMorphology)
        rootX
        rootY
        forwardFraction
        : float * float * float =
        let baseFacing = float frame.BodyFacing[slot]
        let facing =
            match action with
            | Some committed ->
                let targetAngle = atan2 (float committed.PredictedY - rootY) (float committed.PredictedX - rootX)
                let change = wrapAngle (targetAngle - baseFacing)
                baseFacing
                + MatchMath.clamp
                    (-float ctx.Config.ContactAction.MaximumReorientation)
                    (float ctx.Config.ContactAction.MaximumReorientation)
                    change
            | None -> baseFacing

        let footLength =
            if morphology.FootLength > 0.0f then
                float morphology.FootLength
            else
                float ctx.Config.Contact.FootRadius * 2.0

        let errorX, errorY, errorZ =
            match action with
            | Some committed -> float committed.ErrorX, float committed.ErrorY, float committed.ErrorZ
            | None -> 0.0, 0.0, 0.0

        let extends =
            match action with
            | Some committed ->
                match committed.Kind with
                | ContactActionKind.ContactTrap
                | ContactActionKind.ContactStrike
                | ContactActionKind.ContactStandingTackle
                | ContactActionKind.ContactSlidingTackle -> true
                | _ -> false
            | None -> false

        match action with
        | Some committed when extends ->
            let dx = float committed.PredictedX - rootX
            let dy = float committed.PredictedY - rootY
            let distance = sqrt (dx * dx + dy * dy)
            let change = wrapAngle (atan2 dy dx - baseFacing)

            // A committed boot is planted where the ball will be, not under the hips: a player meets a ball beside him
            // by stretching the leg sideways. Behind him he cannot, so that falls back to the reoriented stance.
            if distance > 1e-6 && abs change <= System.Math.PI / 2.0 then
                let limit =
                    match committed.Kind with
                    | ContactActionKind.ContactSlidingTackle -> ctx.Config.ContactAction.SlidingReach
                    | _ -> ctx.Config.ContactAction.FootReach

                let reach = min distance (float limit)
                let along = max 0.0 (reach - footLength * (0.8 - forwardFraction))

                rootX + dx / distance * along + errorX, rootY + dy / distance * along + errorY, errorZ
            else
                rootX + cos facing * footLength * forwardFraction + errorX,
                rootY + sin facing * footLength * forwardFraction + errorY,
                errorZ
        | _ ->
            rootX + cos facing * footLength * forwardFraction + errorX,
            rootY + sin facing * footLength * forwardFraction + errorY,
            errorZ

    let private footPosition ctx (frame: TeamFrame) slot morphology rootX rootY forwardFraction =
        footPositionFor ctx frame slot (actionAt frame slot) morphology rootX rootY forwardFraction

    let private footContactRadius (contact: ContactConfig) =
        let radius = float contact.FootRadius
        // The shared centerline envelope covers lateral reach of two planted boots without another collision scan.
        sqrt (2.0 * radius * radius)

    let private contactControl (ball: BallState) : ContactControlSnapshot =
        match ball.Control with
        | BallControl.Controlled(side, player) when ball.GKHoldSince.IsSome ->
            { Kind = ContactControlKind.HeldBall
              Side = Some side
              Player = Some player }
        | BallControl.Controlled(side, player) ->
            { Kind = ContactControlKind.ControlledBall
              Side = Some side
              Player = Some player }
        | BallControl.Receiving(side, player, _) ->
            { Kind = ContactControlKind.ReceivingBall
              Side = Some side
              Player = Some player }
        | BallControl.Contesting side ->
            { Kind = ContactControlKind.ContestedBall
              Side = Some side
              Player = None }
        | BallControl.Airborne ->
            { Kind = ContactControlKind.UncontrolledBall
              Side = None
              Player = None }
        | BallControl.Free ->
            { Kind = ContactControlKind.UncontrolledBall
              Side = None
              Player = None }

    let private ballContactAllowed (world: World) (ball: BallState) =
        match world.State.Flow with
        | MatchFlow.Restart restart ->
            let pendingRestartStrike =
                ball.PendingStrike
                |> Option.exists (fun pending -> pending.Impulse.RestartId = Some restart.Id)

            let committedRestartStrike =
                ball.Impulse
                |> Option.exists (fun impulse -> impulse.RestartId = Some restart.Id)

            restart.Phase = RestartPhase.AwaitingContact
            && restart.CommandIssued
            && (pendingRestartStrike || committedRestartStrike)
        | _ -> true

    let private playerAt (ctx: MatchContext) (world: World) (scratch: ContactScratch) index =
        MatchWorld.team world scratch.Sides[index], scratch.Slots[index]

    let private predictedAxis position velocity acceleration duration =
        position + velocity * duration + 0.5 * acceleration * duration * duration

    let private sweptTime x0 y0 z0 x1 y1 z1 radius =
        let dx, dy, dz = x1 - x0, y1 - y0, z1 - z0
        let c = x0 * x0 + y0 * y0 + z0 * z0 - radius * radius
        let a = dx * dx + dy * dy + dz * dz
        let b = 2.0 * (x0 * dx + y0 * dy + z0 * dz)

        if c <= 0.0 then
            Some 0.0
        elif a <= 1e-18 then
            None
        else
            let discriminant = b * b - 4.0 * a * c

            if discriminant < 0.0 then
                None
            else
                let time = (-b - sqrt discriminant) / (2.0 * a)
                if time >= 0.0 && time <= 1.0 then Some time else None

    let private betterCandidate tolerance ctx world scratch candidate current =
        let key impact =
            let first = playerIdAt ctx world scratch impact.PlayerA |> playerValue

            if impact.IsBall then
                first, Int32.MaxValue, surfaceTag impact.Surface
            else
                let second = playerIdAt ctx world scratch impact.PlayerB |> playerValue
                min first second, max first second, surfaceTag impact.Surface

        match current with
        | ValueNone -> ValueSome candidate
        | ValueSome previous when candidate.Fraction < previous.Fraction - tolerance -> ValueSome candidate
        | ValueSome previous when abs (candidate.Fraction - previous.Fraction) <= tolerance && key candidate < key previous ->
            ValueSome candidate
        | _ -> current

    /// Broad-phase bounds, once per tick: actions and morphology do not change within it. Every surface stays
    /// within Reach of its root (head at the full height, a committed boot at most the longer leg reach plus the
    /// foot and its execution error); BodyReach is the horizontal part a body or boot can meet another body with.
    /// Bodies, or a body and the ball, further apart than their bounds plus the step's travel cannot touch, so
    /// skipping them changes no result: every narrow test would miss.
    let private computeReach (ctx: MatchContext) (world: World) (scratch: ContactScratch) =
        let cfg = ctx.Config.Contact

        let legReach =
            max (float ctx.Config.ContactAction.FootReach) (float ctx.Config.ContactAction.SlidingReach)
            + float cfg.LegRadius
            + footContactRadius cfg

        for a = 0 to scratch.Count - 1 do
            let frame = (MatchWorld.team world scratch.Sides[a]).Frame
            let slot = scratch.Slots[a]
            let foot = float (profileAt ctx world scratch a).Morphology.FootLength + float cfg.FootRadius * 2.0

            let error =
                match frame.ContactActions[slot] with
                | Some action ->
                    sqrt (float action.ErrorX ** 2.0 + float action.ErrorY ** 2.0 + float action.ErrorZ ** 2.0)
                | None -> 0.0

            scratch.BodyReach[a] <- scratch.Radius[a] + legReach + foot + error
            scratch.Reach[a] <- scratch.Height[a] + scratch.BodyReach[a]

    /// Marks the bodies that can still meet the ball before the tick ends: a necessary condition for findEarliest's
    /// per-step bound, so skipping the rest changes no result. Within the remaining time a body stays within its
    /// constant-acceleration travel of where it is now, and the ball within its speed plus what gravity and spin
    /// can add (drag, turf, bounces and the frame only slow it; Magnus does no work), so a body outside
    /// reach + both travels fails every step's test. Velocities change at impacts, so it is refreshed after each.
    let private markBallCandidates (ctx: MatchContext) (world: World) (scratch: ContactScratch) (ball: BallState) remaining =
        let gravity = float ctx.Config.Ball.Gravity
        let p = ball.Position
        let spin = ball.Spin
        let spinSpeed = sqrt (float spin.X * float spin.X + float spin.Y * float spin.Y + float spin.Z * float spin.Z)
        let ballSpeed =
            sqrt (float p.Vx * float p.Vx + float p.Vy * float p.Vy + float p.Vz * float p.Vz)
            + spinSpeed * float ball.Radius

        let ballTravel = ballSpeed * remaining + 0.5 * gravity * remaining * remaining
        let bx, by, bz = float p.X, float p.Y, float (p.Z + ball.Radius)
        // Rounding headroom only; the bound is already conservative.
        let margin = 0.05

        for a = 0 to scratch.Count - 1 do
            let team = MatchWorld.team world scratch.Sides[a]
            let vx, vy, vz = scratch.VelocityX[a], scratch.VelocityY[a], scratch.VelocityZ[a]
            let ax, ay = scratch.AccelerationX[a], scratch.AccelerationY[a]
            let horizontal = sqrt (vx * vx + vy * vy) * remaining + 0.5 * sqrt (ax * ax + ay * ay) * remaining * remaining
            let vertical = abs vz * remaining + 0.5 * gravity * remaining * remaining
            let dx, dy, dz = bx - scratch.StartX[a], by - scratch.StartY[a], bz - scratch.StartZ[a]
            let bound = scratch.Reach[a] + float ball.Radius + ballTravel + horizontal + vertical + margin

            scratch.BallCandidate[a] <- team.Dive.IsSome || dx * dx + dy * dy + dz * dz <= bound * bound

    let private findEarliest
        (ctx: MatchContext)
        (world: World)
        (scratch: ContactScratch)
        (ball: BallState)
        (duration: float)
        : ImpactCandidate voption * BallState =
        let cfg = ctx.Config.Contact
        let mutable best = ValueNone
        let mutable ballEnd = ball

        if ball.GKHoldSince.IsNone then
            ballEnd <- BallDynamics.integrate ctx.Config.Ball duration world.State.Tick ball

        let ballTravel =
            let p0, p1 = ball.Position, ballEnd.Position
            let dx, dy, dz = float (p1.X - p0.X), float (p1.Y - p0.Y), float (p1.Z - p0.Z)
            sqrt (dx * dx + dy * dy + dz * dz)

        for a = 0 to scratch.Count - 1 do
            let x1 = predictedAxis scratch.StartX[a] scratch.VelocityX[a] scratch.AccelerationX[a] duration
            let y1 = predictedAxis scratch.StartY[a] scratch.VelocityY[a] scratch.AccelerationY[a] duration
            let dx, dy = x1 - scratch.StartX[a], y1 - scratch.StartY[a]
            scratch.EndX[a] <- x1
            scratch.EndY[a] <- y1
            scratch.Travel[a] <- sqrt (dx * dx + dy * dy)

        let ballStart = ball.Position
        let ballFinish = ballEnd.Position
        let b0x, b0y, b0z = float ballStart.X, float ballStart.Y, float (ballStart.Z + ball.Radius)
        let b1x, b1y, b1z = float ballFinish.X, float ballFinish.Y, float (ballFinish.Z + ball.Radius)
        let ballAllowed = ballContactAllowed world ball

        for a = 0 to scratch.Count - 1 do
            let ax0, ay0, az0 = scratch.StartX[a], scratch.StartY[a], scratch.StartZ[a]
            let ax1 = predictedAxis ax0 scratch.VelocityX[a] scratch.AccelerationX[a] duration
            let ay1 = predictedAxis ay0 scratch.VelocityY[a] scratch.AccelerationY[a] duration

            for b = a + 1 to scratch.Count - 1 do
                let dx, dy = scratch.StartX[b] - ax0, scratch.StartY[b] - ay0
                let bound = scratch.BodyReach[a] + scratch.BodyReach[b] + scratch.Travel[a] + scratch.Travel[b]

                if dx * dx + dy * dy <= bound * bound then
                    let pairIndex = a * scratch.Sides.Length + b

                    if not scratch.ResolvedBodyPairs[pairIndex] then
                        let bx0, by0, bz0 = scratch.StartX[b], scratch.StartY[b], scratch.StartZ[b]
                        let bx1 = predictedAxis bx0 scratch.VelocityX[b] scratch.AccelerationX[b] duration
                        let by1 = predictedAxis by0 scratch.VelocityY[b] scratch.AccelerationY[b] duration
                        let relativeX0, relativeY0 = bx0 - ax0, by0 - ay0
                        let relativeX1, relativeY1 = bx1 - ax1, by1 - ay1
                        let radius = scratch.Radius[a] + scratch.Radius[b]

                        match sweptTime relativeX0 relativeY0 0.0 relativeX1 relativeY1 0.0 radius with
                        | Some fraction ->
                            let za = predictedAxis az0 scratch.VelocityZ[a] 0.0 (duration * fraction)
                            let zb = predictedAxis bz0 scratch.VelocityZ[b] 0.0 (duration * fraction)
                            let aLow = za + scratch.Height[a] * cfg.CapsuleLowerHeightRatio
                            let aHigh = za + scratch.Height[a] * cfg.CapsuleUpperHeightRatio
                            let bLow = zb + scratch.Height[b] * cfg.CapsuleLowerHeightRatio
                            let bHigh = zb + scratch.Height[b] * cfg.CapsuleUpperHeightRatio

                            if aLow <= bHigh && bLow <= aHigh then
                                best <-
                                    betterCandidate
                                        cfg.SimultaneousTolerance
                                        ctx
                                        world
                                        scratch
                                        { Fraction = fraction
                                          PlayerA = a
                                          PlayerB = b
                                          IsBall = false
                                          Surface = ContactSurface.ContactTorso
                                          FootFraction = 0.0
                                          Gloves = false }
                                        best
                        | None -> ()

                    // A tackle that misses the ball meets the legs: tripping and kicking are the boot striking them (Law 12).
                    // Only opponents are scanned; a boot among teammates is an accident the torso solver already separates.
                    if scratch.Sides[a] <> scratch.Sides[b] then
                        let bx0, by0 = scratch.StartX[b], scratch.StartY[b]
                        let bx1 = predictedAxis bx0 scratch.VelocityX[b] scratch.AccelerationX[b] duration
                        let by1 = predictedAxis by0 scratch.VelocityY[b] scratch.AccelerationY[b] duration

                        let considerBoot tackler victim tx0 ty0 tx1 ty1 vx0 vy0 vx1 vy1 =
                            let tacklerFrame = (MatchWorld.team world scratch.Sides[tackler]).Frame
                            let tacklerSlot = scratch.Slots[tackler]
                            let victimFrame = (MatchWorld.team world scratch.Sides[victim]).Frame

                            match bootChallenge ctx.Config.ContactAction world.State.Tick tacklerFrame tacklerSlot with
                            | Some action when
                                victimFrame.ContactState[scratch.Slots[victim]].Status <> BodyContactStatus.BodyFalling
                                ->
                                let morphology = (profileAt ctx world scratch tackler).Morphology
                                let footX0, footY0, _ = footPositionFor ctx tacklerFrame tacklerSlot (Some action) morphology tx0 ty0 0.8
                                let footX1, footY1, _ = footPositionFor ctx tacklerFrame tacklerSlot (Some action) morphology tx1 ty1 0.8

                                match
                                    sweptTime
                                        (vx0 - footX0)
                                        (vy0 - footY0)
                                        0.0
                                        (vx1 - footX1)
                                        (vy1 - footY1)
                                        0.0
                                        (footContactRadius cfg + float cfg.LegRadius)
                                with
                                | Some fraction ->
                                    best <-
                                        betterCandidate
                                            cfg.SimultaneousTolerance
                                            ctx
                                            world
                                            scratch
                                            { Fraction = fraction
                                              PlayerA = tackler
                                              PlayerB = victim
                                              IsBall = false
                                              Surface = ContactSurface.ContactFoot
                                              FootFraction = 0.8
                                              Gloves = false }
                                            best
                                | None -> ()
                            | _ -> ()

                        considerBoot a b ax0 ay0 ax1 ay1 bx0 by0 bx1 by1
                        considerBoot b a bx0 by0 bx1 by1 ax0 ay0 ax1 ay1

            if scratch.BallCandidate[a] then
                let side, slot = scratch.Sides[a], scratch.Slots[a]
                let team = MatchWorld.team world side
                let px0, py0, pz0 = scratch.StartX[a], scratch.StartY[a], scratch.StartZ[a]
                let pz1 = predictedAxis pz0 scratch.VelocityZ[a] 0.0 duration

                // Most players are nowhere near the ball; the cheap bound runs before any per-player lookup.
                let ballNear =
                    let dx, dy, dz = b0x - px0, b0y - py0, b0z - pz0
                    let bound = scratch.Reach[a] + float ball.Radius + ballTravel + scratch.Travel[a] + abs (pz1 - pz0)
                    dx * dx + dy * dy + dz * dz <= bound * bound
                    // A dive's gloves wait at a fixed point in the world, not relative to the keeper's root.
                    || team.Dive.IsSome

                if ballNear && ballAllowed && not scratch.ResolvedBallPlayers[a] then
                    let frame = team.Frame
                    let profile = profileAt ctx world scratch a
                    let morphology = profile.Morphology
                    let facing = float frame.BodyFacing[slot]
                    let shoulderWidth = if morphology.ShoulderWidth > 0.0f then float morphology.ShoulderWidth else scratch.Radius[a] * 2.0
                    let px1 = predictedAxis px0 scratch.VelocityX[a] scratch.AccelerationX[a] duration
                    let py1 = predictedAxis py0 scratch.VelocityY[a] scratch.AccelerationY[a] duration

                    let ownerIsActor =
                        match ball.Control with
                        | BallControl.Controlled(ownerSide, owner)
                        | BallControl.Receiving(ownerSide, owner, _) -> ownerSide = side && owner = profile.Id
                        | _ -> false

                    let isPendingStrikeActor =
                        match ball.PendingStrike with
                        | Some pending -> pending.Side = side && pending.Slot = slot
                        | None -> false

                    let isCommittingStrikeActor =
                        match ball.Impulse with
                        | Some impulse ->
                            impulse.ActorId = profile.Id
                            && world.State.Tick - SimTick.value impulse.ContactTick <= cfg.StrikeSeparation
                        | None -> false

                    if not ownerIsActor && not isPendingStrikeActor && not isCommittingStrikeActor then
                        let considerSurfaceWith gloves surface cx0 cy0 cz0 cx1 cy1 cz1 surfaceRadius footFraction =
                            match
                                sweptTime
                                    (b0x - cx0)
                                    (b0y - cy0)
                                    (b0z - cz0)
                                    (b1x - cx1)
                                    (b1y - cy1)
                                    (b1z - cz1)
                                    (surfaceRadius + float ball.Radius)
                            with
                            | Some fraction ->
                                best <-
                                    betterCandidate
                                        cfg.SimultaneousTolerance
                                        ctx
                                        world
                                        scratch
                                        { Fraction = fraction
                                          PlayerA = a
                                          PlayerB = -1
                                          IsBall = true
                                          Surface = surface
                                          FootFraction = footFraction
                                          Gloves = gloves }
                                        best
                            | None -> ()

                        considerSurfaceWith
                            false
                            ContactSurface.ContactTorso
                            px0
                            py0
                            (pz0 + scratch.Height[a] * 0.52)
                            px1
                            py1
                            (pz1 + scratch.Height[a] * 0.52)
                            scratch.Radius[a]
                            0.0

                        considerSurfaceWith
                            false
                            ContactSurface.ContactHead
                            px0
                            py0
                            (pz0 + scratch.Height[a] * 0.91)
                            px1
                            py1
                            (pz1 + scratch.Height[a] * 0.91)
                            (float ctx.Config.Contact.HeadRadius)
                            0.0

                        considerSurfaceWith
                            false
                            ContactSurface.ContactLeg
                            px0
                            py0
                            (pz0 + scratch.Height[a] * 0.20)
                            px1
                            py1
                            (pz1 + scratch.Height[a] * 0.20)
                            (float ctx.Config.Contact.LegRadius)
                            0.0

                        for feature = 0 to 2 do
                            let footFraction = float feature * 0.4
                            let footX0, footY0, footZ0 = footPosition ctx frame slot morphology px0 py0 footFraction
                            let footX1, footY1, footZ1 = footPosition ctx frame slot morphology px1 py1 footFraction

                            considerSurfaceWith
                                false
                                ContactSurface.ContactFoot
                                footX0
                                footY0
                                (pz0 + float ctx.Config.Contact.FootRadius * 0.65 + footZ0)
                                footX1
                                footY1
                                (pz1 + float ctx.Config.Contact.FootRadius * 0.65 + footZ1)
                                (footContactRadius ctx.Config.Contact)
                                footFraction

                        let lateralX, lateralY = -sin facing * shoulderWidth * 0.55, cos facing * shoulderWidth * 0.55

                        for hand = 0 to 1 do
                            let sign = float (2 * hand - 1)

                            considerSurfaceWith
                                false
                                ContactSurface.ContactHand
                                (px0 + lateralX * sign)
                                (py0 + lateralY * sign)
                                (pz0 + scratch.Height[a] * 0.68)
                                (px1 + lateralX * sign)
                                (py1 + lateralY * sign)
                                (pz1 + scratch.Height[a] * 0.68)
                                (float ctx.Config.Contact.HandRadius)
                                0.0

                        // The gloves wait where the dive put them; the swept test against the ball's path decides the save.
                        match team.Dive with
                        | ValueSome dive when
                            dive.KeeperSlot = slot
                            && abs (int (world.State.Tick - dive.ContactAt)) <= int ctx.Config.Keeper.HandWindow
                            ->
                            let hx, hy, hz = float dive.Hand.X, float dive.Hand.Y, float dive.Hand.Z

                            considerSurfaceWith
                                true
                                ContactSurface.ContactHand
                                hx
                                hy
                                hz
                                hx
                                hy
                                hz
                                (float ctx.Config.Keeper.HandRadius)
                                0.0
                        | _ -> ()

        best, ballEnd

    let private advancePlayers (ctx: SystemContext) (world: World) (scratch: ContactScratch) duration =
        let movement = ctx.Ctx.Config.Movement
        let gravity = float ctx.Ctx.Config.Ball.Gravity

        for index = 0 to scratch.Count - 1 do
            let side, slot = scratch.Sides[index], scratch.Slots[index]
            let frame = (MatchWorld.team world side).Frame
            let x0, y0, z0 = scratch.StartX[index], scratch.StartY[index], scratch.StartZ[index]
            let vx0, vy0, vz0 = scratch.VelocityX[index], scratch.VelocityY[index], scratch.VelocityZ[index]
            let x = predictedAxis x0 vx0 scratch.AccelerationX[index] duration
            let y = predictedAxis y0 vy0 scratch.AccelerationY[index] duration
            let vx = vx0 + scratch.AccelerationX[index] * duration
            let vy = vy0 + scratch.AccelerationY[index] * duration
            let airborne = z0 > 0.0 || vz0 > 0.0
            let zAcceleration = if airborne then -gravity else 0.0
            let z = predictedAxis z0 vz0 zAcceleration duration
            let vz = vz0 + zAcceleration * duration
            let grounded = z <= 0.0 && vz <= 0.0

            scratch.StartX[index] <- MatchMath.clamp (-(float Pitch.HalfLength)) (float Pitch.HalfLength) x
            scratch.StartY[index] <- MatchMath.clamp (-(float Pitch.HalfWidth)) (float Pitch.HalfWidth) y

            if grounded then
                scratch.StartZ[index] <- 0.0
                scratch.VelocityZ[index] <- 0.0
            else
                scratch.StartZ[index] <- max 0.0 z
                scratch.VelocityZ[index] <- vz

            scratch.VelocityX[index] <- vx
            scratch.VelocityY[index] <- vy

            let target = float frame.TargetFacing[slot]
            let current = float frame.BodyFacing[slot]
            let angleError = wrapAngle (target - current)
            let maxRate = float movement.TurningRate
            let stoppingRate = sqrt (2.0 * float movement.TurningAcceleration * abs angleError)
            let rateLimit = min maxRate stoppingRate
            let targetRate = MatchMath.clamp (-rateLimit) rateLimit (angleError / max duration 1e-9)
            let oldRate = float frame.BodyAngularVelocity[slot]
            let maxRateDelta = float movement.TurningAcceleration * duration
            let rate = oldRate + MatchMath.clamp (-maxRateDelta) maxRateDelta (targetRate - oldRate)
            frame.BodyAngularVelocity[slot] <- float32 rate
            frame.BodyAngularAcceleration[slot] <- float32 ((rate - oldRate) / max duration 1e-9)
            frame.BodyFacing[slot] <- float32 (wrapAngle (current + rate * duration))

    let private supportOf (frame: TeamFrame) slot =
        if frame.PosZ[slot] > 0.0f || frame.VelZ[slot] > 0.0f then
            BodySupport.NoBodySupport
        else
            BodySupport.DoubleFootSupport

    let private pinnedRestartExecutor (world: World) side slot =
        match world.State.Flow with
        | MatchFlow.Restart restart
            when restart.Phase = RestartPhase.Positioning
                 || restart.Phase = RestartPhase.Authorized
                 || restart.Phase = RestartPhase.AwaitingContact ->
            restart.Plan
            |> Option.exists (fun plan ->
                plan.Side = side
                && plan.Executor = (MatchWorld.team world side).RosterIds[slot])
        | _ -> false

    let private effectiveInverseMass (ctx: MatchContext) (world: World) (scratch: ContactScratch) index =
        let frame = (MatchWorld.team world scratch.Sides[index]).Frame
        let slot = scratch.Slots[index]
        let profile = profileAt ctx world scratch index
        let mass =
            if System.Double.IsFinite(float profile.Morphology.Mass) && profile.Morphology.Mass >= 40.0f then
                float profile.Morphology.Mass
            else
                float ctx.Config.Contact.FallbackMass

        let state = frame.ContactState[slot]
        let scale =
            if scratch.StartZ[index] > 0.0 || scratch.VelocityZ[index] > 0.0 || state.Support = BodySupport.NoBodySupport then
                ctx.Config.Contact.AirborneInverseMassScale
            else
                ctx.Config.Contact.SupportedInverseMassScale

        if pinnedRestartExecutor world scratch.Sides[index] slot then
            0.0
        else
            float scale / max 1.0 mass

    let private actionFor (ctx: MatchContext) (world: World) (scratch: ContactScratch) index =
        let team, slot = playerAt ctx world scratch index
        actionAt team.Frame slot

    let private updateBodyResponse
        (ctx: MatchContext)
        (world: World)
        (scratch: ContactScratch)
        index
        impulseX
        impulseY
        angularImpulse
        : unit =
        let team, slot = playerAt ctx world scratch index
        let frame = team.Frame
        let deltaVelocityX = impulseX * effectiveInverseMass ctx world scratch index
        let deltaVelocityY = impulseY * effectiveInverseMass ctx world scratch index
        let deltaSpeed = sqrt (deltaVelocityX * deltaVelocityX + deltaVelocityY * deltaVelocityY)
        let old = frame.ContactState[slot]
        let stabilityLoss = min 0.5 (deltaSpeed / float ctx.Config.Contact.FallImpulseThreshold * 0.35)
        let stability = float old.Stability |> fun value -> MatchMath.clamp01 (value - stabilityLoss)

        let nextStatus =
            if deltaSpeed >= ctx.Config.Contact.FallImpulseThreshold then
                BodyContactStatus.BodyFalling
            elif deltaSpeed >= ctx.Config.Contact.StaggerImpulseThreshold then
                BodyContactStatus.BodyStaggering
            else
                old.Status

        let changed = nextStatus <> old.Status
        let exitsAt =
            if nextStatus = BodyContactStatus.BodyFalling then
                world.State.Tick + ctx.Config.ContactAction.RecoveryDuration
            elif nextStatus = BodyContactStatus.BodyStaggering then
                world.State.Tick + ctx.Config.ContactAction.RecoveryDuration / 2
            else
                old.ExitsAt

        frame.ContactState[slot] <-
            { old with
                Stability = float32 stability
                PerturbationX = old.PerturbationX + float32 deltaVelocityX
                PerturbationY = old.PerturbationY + float32 deltaVelocityY
                AngularPerturbation = old.AngularPerturbation + float32 angularImpulse
                Status = nextStatus
                EnteredAt = if changed then world.State.Tick else old.EnteredAt
                ExitsAt = exitsAt
                LastContactAt = world.State.Tick
                Support =
                    if nextStatus = BodyContactStatus.BodyFalling
                       || scratch.StartZ[index] > 0.0
                       || scratch.VelocityZ[index] > 0.0 then
                        BodySupport.NoBodySupport
                    else
                        BodySupport.DoubleFootSupport }

    let private resolveBodyContact
        (ctx: SystemContext)
        (world: World)
        (scratch: ContactScratch)
        (candidate: ImpactCandidate)
        (fraction: float)
        : ContactRecord =
        let a, b = candidate.PlayerA, candidate.PlayerB
        let ax, ay = scratch.StartX[a], scratch.StartY[a]
        let bx, by = scratch.StartX[b], scratch.StartY[b]
        let dx, dy = bx - ax, by - ay
        let distance = sqrt (dx * dx + dy * dy)
        let idA, idB = playerIdAt ctx.Ctx world scratch a, playerIdAt ctx.Ctx world scratch b
        let nx, ny =
            if distance > 1e-9 then
                dx / distance, dy / distance
            elif idA < idB then
                0.0, 1.0
            else
                0.0, -1.0

        let relativeX = scratch.VelocityX[b] - scratch.VelocityX[a]
        let relativeY = scratch.VelocityY[b] - scratch.VelocityY[a]
        let relativeZ = scratch.VelocityZ[b] - scratch.VelocityZ[a]
        let normalSpeed = relativeX * nx + relativeY * ny
        let inverseA = effectiveInverseMass ctx.Ctx world scratch a
        let inverseB = effectiveInverseMass ctx.Ctx world scratch b
        let inverseSum = max 1e-12 (inverseA + inverseB)
        let contactCfg = ctx.Ctx.Config.Contact
        let normalImpulse =
            if normalSpeed < -float contactCfg.VelocitySlop then
                -(1.0 + contactCfg.BodyRestitution) * normalSpeed / inverseSum
            else
                0.0

        let tangentX, tangentY = -ny, nx
        let tangentSpeed = relativeX * tangentX + relativeY * tangentY
        let tangentImpulse =
            MatchMath.clamp
                (-contactCfg.BodyFriction * normalImpulse)
                (contactCfg.BodyFriction * normalImpulse)
                (-tangentSpeed / inverseSum)

        let impulseX = normalImpulse * nx + tangentImpulse * tangentX
        let impulseY = normalImpulse * ny + tangentImpulse * tangentY
        let supportBeforeA = (MatchWorld.team world scratch.Sides[a]).Frame.ContactState[scratch.Slots[a]].Support
        let supportBeforeB = (MatchWorld.team world scratch.Sides[b]).Frame.ContactState[scratch.Slots[b]].Support
        scratch.VelocityX[a] <- scratch.VelocityX[a] - impulseX * inverseA
        scratch.VelocityY[a] <- scratch.VelocityY[a] - impulseY * inverseA
        scratch.VelocityX[b] <- scratch.VelocityX[b] + impulseX * inverseB
        scratch.VelocityY[b] <- scratch.VelocityY[b] + impulseY * inverseB

        let penetration = max 0.0 (scratch.Radius[a] + scratch.Radius[b] - distance)
        let correction = min (float contactCfg.MaximumPositionCorrection) (penetration * contactCfg.PositionCorrectionPercent)
        let correctionA = correction * inverseA / inverseSum
        let correctionB = correction * inverseB / inverseSum
        scratch.StartX[a] <- scratch.StartX[a] - nx * correctionA
        scratch.StartY[a] <- scratch.StartY[a] - ny * correctionA
        scratch.StartX[b] <- scratch.StartX[b] + nx * correctionB
        scratch.StartY[b] <- scratch.StartY[b] + ny * correctionB

        updateBodyResponse ctx.Ctx world scratch a (-impulseX) (-impulseY) (tangentImpulse * 0.02)
        updateBodyResponse ctx.Ctx world scratch b impulseX impulseY (tangentImpulse * 0.02)

        let actionA, actionB = actionFor ctx.Ctx world scratch a, actionFor ctx.Ctx world scratch b
        let actorIndex, otherIndex, action =
            match actionA, actionB with
            | Some action, _ -> a, b, Some action
            | _, Some action -> b, a, Some action
            | _ when idA <= idB -> a, b, None
            | _ -> b, a, None

        let actorSide, actorSlot = scratch.Sides[actorIndex], scratch.Slots[actorIndex]
        let otherSide, otherSlot = scratch.Sides[otherIndex], scratch.Slots[otherIndex]
        let actorId, otherId = playerIdAt ctx.Ctx world scratch actorIndex, playerIdAt ctx.Ctx world scratch otherIndex
        let actorIsA = actorIndex = a
        let actorNormalX, actorNormalY = if actorIsA then nx, ny else -nx, -ny
        let actorRelativeX, actorRelativeY, actorRelativeZ =
            if actorIsA then relativeX, relativeY, relativeZ else -relativeX, -relativeY, -relativeZ
        let actorFrame = (MatchWorld.team world actorSide).Frame
        let otherFrame = (MatchWorld.team world otherSide).Frame
        let actorSupportBefore, otherSupportBefore =
            if actorIsA then supportBeforeA, supportBeforeB else supportBeforeB, supportBeforeA
        let actorSupportAfter = actorFrame.ContactState[actorSlot].Support
        let otherSupportAfter = otherFrame.ContactState[otherSlot].Support
        let recordId = ContactId world.NextContactId
        world.NextContactId <- world.NextContactId + 1UL
        let pointX = (ax + nx * scratch.Radius[a] + bx - nx * scratch.Radius[b]) * 0.5
        let pointY = (ay + ny * scratch.Radius[a] + by - ny * scratch.Radius[b]) * 0.5

        { Id = recordId
          Actor = actorId
          ActorGeneration = actorFrame.ContactGeneration[actorSlot]
          Other = Some otherId
          OtherGeneration = Some otherFrame.ContactGeneration[otherSlot]
          Action = action |> Option.map _.Id
          ActionKind = action |> Option.map _.Kind
          Tick = world.State.Tick
          Fraction = fraction
          PointX = pointX * 1.0<meter>
          PointY = pointY * 1.0<meter>
          PointZ = ((scratch.StartZ[a] + scratch.StartZ[b]) * 0.5) * 1.0<meter>
          NormalX = actorNormalX
          NormalY = actorNormalY
          NormalZ = 0.0
          SurfaceA = ContactSurface.ContactTorso
          SurfaceB = ContactSurface.ContactTorso
          RelativeVelocityX = actorRelativeX * 1.0<meterPerSecond>
          RelativeVelocityY = actorRelativeY * 1.0<meterPerSecond>
          RelativeVelocityZ = actorRelativeZ * 1.0<meterPerSecond>
          NormalImpulse = normalImpulse
          TangentialImpulse = abs tangentImpulse
          SupportBefore = actorSupportBefore
          SupportAfter = actorSupportAfter
          BallTouched = false
          BallTouchOrder = None
          ControlBefore = contactControl world.State.Ball
          ControlAfter = contactControl world.State.Ball }

    /// The boot meets the opponent's leg. The leg is swept rather than bounced (no restitution) and the hit lands
    /// below the centre of mass, so the victim takes a destabilising torque on top of the linear push.
    let private resolveBootContact
        (ctx: SystemContext)
        (world: World)
        (scratch: ContactScratch)
        (candidate: ImpactCandidate)
        (fraction: float)
        (action: ContactAction)
        : ContactRecord =
        let tackler, victim = candidate.PlayerA, candidate.PlayerB
        let tacklerSide, tacklerSlot = scratch.Sides[tackler], scratch.Slots[tackler]
        let victimSide, victimSlot = scratch.Sides[victim], scratch.Slots[victim]
        let tacklerFrame = (MatchWorld.team world tacklerSide).Frame
        let victimFrame = (MatchWorld.team world victimSide).Frame
        let morphology = (profileAt ctx.Ctx world scratch tackler).Morphology
        let footX, footY, footZ =
            footPositionFor
                ctx.Ctx
                tacklerFrame
                tacklerSlot
                (Some action)
                morphology
                scratch.StartX[tackler]
                scratch.StartY[tackler]
                candidate.FootFraction
        let legX, legY = scratch.StartX[victim], scratch.StartY[victim]
        let dx, dy = legX - footX, legY - footY
        let distance = sqrt (dx * dx + dy * dy)
        let nx, ny =
            if distance > 1e-9 then
                dx / distance, dy / distance
            else
                let facing = float tacklerFrame.BodyFacing[tacklerSlot]
                cos facing, sin facing

        let relativeX = scratch.VelocityX[victim] - scratch.VelocityX[tackler]
        let relativeY = scratch.VelocityY[victim] - scratch.VelocityY[tackler]
        let relativeZ = scratch.VelocityZ[victim] - scratch.VelocityZ[tackler]
        let normalSpeed = relativeX * nx + relativeY * ny
        let inverseTackler = effectiveInverseMass ctx.Ctx world scratch tackler
        let inverseVictim = effectiveInverseMass ctx.Ctx world scratch victim
        let contactCfg = ctx.Ctx.Config.Contact
        let normalImpulse =
            if normalSpeed < -float contactCfg.VelocitySlop then
                -normalSpeed / max 1e-12 (inverseTackler + inverseVictim)
            else
                0.0

        let impulseX, impulseY = normalImpulse * nx, normalImpulse * ny
        let supportBefore = tacklerFrame.ContactState[tacklerSlot].Support
        scratch.VelocityX[tackler] <- scratch.VelocityX[tackler] - impulseX * inverseTackler
        scratch.VelocityY[tackler] <- scratch.VelocityY[tackler] - impulseY * inverseTackler
        scratch.VelocityX[victim] <- scratch.VelocityX[victim] + impulseX * inverseVictim
        scratch.VelocityY[victim] <- scratch.VelocityY[victim] + impulseY * inverseVictim
        updateBodyResponse ctx.Ctx world scratch tackler (-impulseX) (-impulseY) 0.0
        updateBodyResponse ctx.Ctx world scratch victim impulseX impulseY normalImpulse
        let recordId = ContactId world.NextContactId
        world.NextContactId <- world.NextContactId + 1UL

        { Id = recordId
          Actor = action.Actor
          ActorGeneration = tacklerFrame.ContactGeneration[tacklerSlot]
          Other = Some(playerIdAt ctx.Ctx world scratch victim)
          OtherGeneration = Some victimFrame.ContactGeneration[victimSlot]
          Action = Some action.Id
          ActionKind = Some action.Kind
          Tick = world.State.Tick
          Fraction = fraction
          PointX = (footX + legX) * 0.5 * 1.0<meter>
          PointY = (footY + legY) * 0.5 * 1.0<meter>
          PointZ = (scratch.StartZ[tackler] + footZ + float contactCfg.FootRadius) * 1.0<meter>
          NormalX = nx
          NormalY = ny
          NormalZ = 0.0
          SurfaceA = ContactSurface.ContactFoot
          SurfaceB = ContactSurface.ContactLeg
          RelativeVelocityX = relativeX * 1.0<meterPerSecond>
          RelativeVelocityY = relativeY * 1.0<meterPerSecond>
          RelativeVelocityZ = relativeZ * 1.0<meterPerSecond>
          NormalImpulse = normalImpulse
          TangentialImpulse = 0.0
          SupportBefore = supportBefore
          SupportAfter = tacklerFrame.ContactState[tacklerSlot].Support
          BallTouched = false
          BallTouchOrder = None
          ControlBefore = contactControl world.State.Ball
          ControlAfter = contactControl world.State.Ball }

    let private resolveBallContact
        (ctx: SystemContext)
        (world: World)
        (scratch: ContactScratch)
        (candidate: ImpactCandidate)
        (fraction: float)
        (ball: BallState)
        (contactOrder: int)
        : BallState * ContactRecord * bool =
        let index = candidate.PlayerA
        let side, slot = scratch.Sides[index], scratch.Slots[index]
        let frame = (MatchWorld.team world side).Frame
        let profile = profileAt ctx.Ctx world scratch index
        let facing = float frame.BodyFacing[slot]
        let morphology = profile.Morphology
        let height = scratch.Height[index]
        let surfaceX, surfaceY, surfaceZ, surfaceRadius =
            match candidate.Surface with
            | ContactSurface.ContactHead ->
                scratch.StartX[index], scratch.StartY[index], scratch.StartZ[index] + height * 0.91, float ctx.Ctx.Config.Contact.HeadRadius
            | ContactSurface.ContactLeg ->
                scratch.StartX[index], scratch.StartY[index], scratch.StartZ[index] + height * 0.20, float ctx.Ctx.Config.Contact.LegRadius
            | ContactSurface.ContactFoot ->
                let footX, footY, footZ =
                    footPosition
                        ctx.Ctx
                        frame
                        slot
                        morphology
                        scratch.StartX[index]
                        scratch.StartY[index]
                        candidate.FootFraction
                footX,
                footY,
                scratch.StartZ[index] + float ctx.Ctx.Config.Contact.FootRadius * 0.65 + footZ,
                footContactRadius ctx.Ctx.Config.Contact
            | ContactSurface.ContactHand when candidate.Gloves ->
                match (MatchWorld.team world side).Dive with
                | ValueSome dive ->
                    float dive.Hand.X, float dive.Hand.Y, float dive.Hand.Z, float ctx.Ctx.Config.Keeper.HandRadius
                | ValueNone ->
                    scratch.StartX[index], scratch.StartY[index], scratch.StartZ[index] + height * 0.68, float ctx.Ctx.Config.Keeper.HandRadius
            | ContactSurface.ContactHand ->
                let lateralX, lateralY = -sin facing, cos facing
                let ballX, ballY = float ball.Position.X, float ball.Position.Y
                let sign = if (ballX - scratch.StartX[index]) * lateralX + (ballY - scratch.StartY[index]) * lateralY >= 0.0 then 1.0 else -1.0
                let width = if morphology.ShoulderWidth > 0.0f then float morphology.ShoulderWidth else scratch.Radius[index] * 2.0
                scratch.StartX[index] + lateralX * width * 0.55 * sign,
                scratch.StartY[index] + lateralY * width * 0.55 * sign,
                scratch.StartZ[index] + height * 0.68,
                float ctx.Ctx.Config.Contact.HandRadius
            | _ ->
                scratch.StartX[index], scratch.StartY[index], scratch.StartZ[index] + height * 0.52, scratch.Radius[index]

        let ballCenterZ = float (ball.Position.Z + ball.Radius)
        let dx, dy, dz = float ball.Position.X - surfaceX, float ball.Position.Y - surfaceY, ballCenterZ - surfaceZ
        let distance = sqrt (dx * dx + dy * dy + dz * dz)
        let nx, ny, nz =
            if distance > 1e-9 then
                dx / distance, dy / distance, dz / distance
            else
                let sideSign = if (playerValue profile.Id &&& 1) = 0 then 1.0 else -1.0
                cos facing * sideSign, sin facing * sideSign, 0.0

        let relativeX = float ball.Position.Vx - scratch.VelocityX[index]
        let relativeY = float ball.Position.Vy - scratch.VelocityY[index]
        let relativeZ = float ball.Position.Vz - scratch.VelocityZ[index]
        let normalSpeed = relativeX * nx + relativeY * ny + relativeZ * nz
        let inversePlayer = effectiveInverseMass ctx.Ctx world scratch index
        let inverseBall = 1.0 / float ctx.Ctx.Config.Contact.BallMass
        let inverseSum = inversePlayer + inverseBall
        let contactCfg = ctx.Ctx.Config.Contact
        let trapping =
            candidate.Surface = ContactSurface.ContactFoot
            && actionAt frame slot |> Option.exists (fun prepared -> prepared.Kind = ContactActionKind.ContactTrap)
        // First touch is ball control: the feel of the boot (dribbling) and the weight given to the ball (passing).
        // What a receiver lacks in it shows more while he is off balance.
        let missingTouch =
            let touch = (float profile.Dribbling + float profile.Passing) / 200.0
            let missing = 1.0 - MatchMath.clamp01 touch

            if StrikeTechnique.offBalance frame slot then
                min 1.0 (missing * contactCfg.OffBalanceTrapPenalty)
            else
                missing
        let restitution =
            if candidate.Gloves then
                ctx.Ctx.Config.Keeper.ParryRestitution
            elif trapping then
                contactCfg.BallFootRestitution + contactCfg.TrapPoorRestitution * missingTouch * missingTouch
            elif candidate.Surface = ContactSurface.ContactFoot then
                contactCfg.BallFootRestitution
            else
                contactCfg.BallBodyRestitution

        let normalImpulse =
            if normalSpeed < -float contactCfg.VelocitySlop then
                -(1.0 + restitution) * normalSpeed / inverseSum
            else
                0.0

        let tangentX0 = relativeX - normalSpeed * nx
        let tangentY0 = relativeY - normalSpeed * ny
        let tangentZ0 = relativeZ - normalSpeed * nz
        let tangentLength = sqrt (tangentX0 * tangentX0 + tangentY0 * tangentY0 + tangentZ0 * tangentZ0)
        let coulomb = min (contactCfg.BallBodyFriction * normalImpulse) (tangentLength / inverseSum)
        // An intentional trap moves the foot with the ball; only passive contacts are bounded by Coulomb friction.
        let tangentImpulse =
            if trapping then
                let absorption = contactCfg.TrapAbsorptionBase + contactCfg.TrapAbsorptionSkill * (1.0 - missingTouch)
                max coulomb (absorption * tangentLength / inverseSum)
            else
                coulomb
        let tangentX, tangentY, tangentZ =
            if tangentLength > 1e-9 then
                -tangentX0 / tangentLength * tangentImpulse,
                -tangentY0 / tangentLength * tangentImpulse,
                -tangentZ0 / tangentLength * tangentImpulse
            else
                0.0, 0.0, 0.0

        let impulseX, impulseY, impulseZ =
            normalImpulse * nx + tangentX,
            normalImpulse * ny + tangentY,
            normalImpulse * nz + tangentZ

        scratch.VelocityX[index] <- scratch.VelocityX[index] - impulseX * inversePlayer
        scratch.VelocityY[index] <- scratch.VelocityY[index] - impulseY * inversePlayer
        // The pitch supports a standing body: the ground takes the vertical share of a ball impact,
        // otherwise a rising ball would lift the player into a flight that no jump started.
        let standing = scratch.StartZ[index] <= 0.0 && scratch.VelocityZ[index] <= 0.0

        if not standing then
            scratch.VelocityZ[index] <- scratch.VelocityZ[index] - impulseZ * inversePlayer

        let penetration = max 0.0 (surfaceRadius + float ball.Radius - distance)
        // An initially overlapping ball already leaving the boot clears naturally; pushing it again invents travel.
        let correction =
            if normalSpeed > float contactCfg.VelocitySlop then
                0.0
            else
                min (float contactCfg.MaximumPositionCorrection) (penetration * contactCfg.PositionCorrectionPercent)
        let correctedBall =
            { ball with
                Position =
                    { ball.Position with
                        X = ball.Position.X + nx * correction * 1.0<meter>
                        Y = ball.Position.Y + ny * correction * 1.0<meter>
                        Z = max 0.0<meter> (ball.Position.Z + nz * correction * 1.0<meter>)
                        Vx = (float ball.Position.Vx + impulseX * inverseBall) * 1.0<meterPerSecond>
                        Vy = (float ball.Position.Vy + impulseY * inverseBall) * 1.0<meterPerSecond>
                        Vz = (float ball.Position.Vz + impulseZ * inverseBall) * 1.0<meterPerSecond> }
                Trajectory = ball.Trajectory
                FootMotion = if ball.PendingStrike.IsSome then None else ball.FootMotion
                PendingStrike = None
                StationarySince = None
                GKHoldSince = None
                Control = BallControl.Free
                LastTouch = Some profile.Id }

        let oldState = frame.ContactState[slot]
        updateBodyResponse ctx.Ctx world scratch index (-impulseX) (-impulseY) 0.0
        let currentState = frame.ContactState[slot]
        let action = actionAt frame slot
        let controlBefore = contactControl ball
        let relativeAfterX = float correctedBall.Position.Vx - scratch.VelocityX[index]
        let relativeAfterY = float correctedBall.Position.Vy - scratch.VelocityY[index]
        let relativeAfterZ = float correctedBall.Position.Vz - scratch.VelocityZ[index]
        let relativeAfterSpeed =
            sqrt (relativeAfterX * relativeAfterX + relativeAfterY * relativeAfterY + relativeAfterZ * relativeAfterZ)
        let incomingSpeed = sqrt (relativeX * relativeX + relativeY * relativeY + relativeZ * relativeZ)
        let controlledContact =
            candidate.Surface = ContactSurface.ContactFoot
            && currentState.Support <> BodySupport.NoBodySupport
            && currentState.Status <> BodyContactStatus.BodyFalling
            && relativeAfterSpeed <= float contactCfg.BallControlSpeed
            // Low restitution alone must not turn an uncontrollable impact into a controlled reception.
            && incomingSpeed <= float contactCfg.MaximumTrapSpeed
            && (incomingSpeed <= float contactCfg.BallControlSpeed
                || (action |> Option.exists (fun prepared -> prepared.Kind = ContactActionKind.ContactTrap)))
            && world.State.Flow = MatchFlow.Live
        // A set keeper holds what he can; at full stretch only softer shots stick, the rest are parried.
        let caught =
            candidate.Gloves
            && world.State.Flow = MatchFlow.Live
            && (let stretch =
                    match (MatchWorld.team world side).Dive with
                    | ValueSome dive -> dive.Stretch
                    | ValueNone -> 0.0

                incomingSpeed
                <= float ctx.Ctx.Config.Keeper.CatchSpeed
                   * (0.55 + 0.45 * float profile.Positioning / 100.0)
                   * (1.0 - 0.5 * stretch))

        let controlAfter =
            if controlledContact || caught then
                { Kind = ContactControlKind.ReceivingBall
                  Side = Some side
                  Player = Some profile.Id }
            else
                { Kind = ContactControlKind.UncontrolledBall
                  Side = None
                  Player = None }

        let correctedBall =
            if caught then
                { correctedBall with
                    Position =
                        { correctedBall.Position with
                            Vx = scratch.VelocityX[index] * 1.0<meterPerSecond>
                            Vy = scratch.VelocityY[index] * 1.0<meterPerSecond>
                            Vz = 0.0<meterPerSecond> }
                    Control = BallControl.Controlled(side, profile.Id)
                    GKHoldSince = Some world.State.Tick }
            elif controlledContact then
                // A controlled touch cushions the ball over a long contact; the foot's friction leaves it rolling
                // with its new velocity. Spin the trap left behind would otherwise survive into the next strike, whose
                // impulse cannot remove a component along its own direction, and curve the pass along the grass.
                let radius = float correctedBall.Radius
                let spin =
                    if correctedBall.Position.Z <= correctedBall.Radius then
                        { X = -float correctedBall.Position.Vy / radius * 1.0<radianPerSecond>
                          Y = float correctedBall.Position.Vx / radius * 1.0<radianPerSecond>
                          Z = 0.0<radianPerSecond> }
                    else
                        correctedBall.Spin

                { correctedBall with
                    Spin = spin
                    Control = BallControl.Receiving(side, profile.Id, world.State.Tick) }
            else
                correctedBall
        let recordId = ContactId world.NextContactId
        world.NextContactId <- world.NextContactId + 1UL

        let record =
            { Id = recordId
              Actor = profile.Id
              ActorGeneration = frame.ContactGeneration[slot]
              Other = None
              OtherGeneration = None
              Action = action |> Option.map _.Id
              ActionKind = action |> Option.map _.Kind
              Tick = world.State.Tick
              Fraction = fraction
              PointX = (float ball.Position.X - nx * float ball.Radius) * 1.0<meter>
              PointY = (float ball.Position.Y - ny * float ball.Radius) * 1.0<meter>
              PointZ = (ballCenterZ - nz * float ball.Radius) * 1.0<meter>
              NormalX = nx
              NormalY = ny
              NormalZ = nz
              SurfaceA = candidate.Surface
              SurfaceB = ContactSurface.ContactBall
              RelativeVelocityX = relativeX * 1.0<meterPerSecond>
              RelativeVelocityY = relativeY * 1.0<meterPerSecond>
              RelativeVelocityZ = relativeZ * 1.0<meterPerSecond>
              NormalImpulse = normalImpulse
              TangentialImpulse = tangentImpulse
              SupportBefore = oldState.Support
              SupportAfter = currentState.Support
              BallTouched = true
              BallTouchOrder = Some contactOrder
              ControlBefore = controlBefore
              ControlAfter = controlAfter }

        let lostControlledBall =
            match ball.Control, controlledContact with
            | (BallControl.Controlled _ | BallControl.Receiving _), false -> true
            | _ -> false

        correctedBall, record, lostControlledBall

    let private advanceActionCommitments (ctx: SystemContext) (world: World) (rng: Rng) : Rng =
        let mutable currentRng = rng
        let tick = world.State.Tick
        let cfg = ctx.Ctx.Config.ContactAction

        let owner =
            match world.State.Ball.Control with
            | BallControl.Controlled(_, player)
            | BallControl.Receiving(_, player, _) -> Some player
            | _ -> None

        let remainsRelevant (action: ContactAction) =
            match action.Kind, action.Target with
            | ContactActionKind.ContactShield, _ -> owner = Some action.Actor
            | ContactActionKind.ContactStandingTackle, Some target
            | ContactActionKind.ContactSlidingTackle, Some target
            | ContactActionKind.ContactShoulderChallenge, Some target -> owner = Some target
            | _ -> true

        for side in [ ClubSide.Home; ClubSide.Away ] do
            let frame = (MatchWorld.team world side).Frame

            for slot in MatchWorld.activeSlots frame do
                match frame.ContactActions[slot] with
                | None -> ()
                | Some action when not (remainsRelevant action) ->
                    if
                        action.Phase <> ContactActionPhase.ContactActionCompleted
                        && action.Phase <> ContactActionPhase.ContactActionCancelled
                    then
                        ContactActionPlanner.publish world action MotionEvidencePhase.Cancelled ValueNone

                    frame.ContactActions[slot] <- None
                | Some action when action.Generation <> frame.ContactGeneration[slot] || tick >= action.EndsAt ->
                    if
                        action.Phase <> ContactActionPhase.ContactActionCompleted
                        && action.Phase <> ContactActionPhase.ContactActionCancelled
                    then
                        ContactActionPlanner.publish world action MotionEvidencePhase.Cancelled ValueNone

                    frame.ContactActions[slot] <- None
                | Some action when action.Phase = ContactActionPhase.ContactActionPreparing && tick >= action.CommitAt ->
                    let profile = MatchWorld.profileOf ctx.Ctx side slot
                    let technique = MatchMath.clamp01 ((float profile.Dribbling + float profile.Composure) / 200.0)
                    // Fatigue degrades placement, rather than making every ordinary pass physically untrappable.
                    let scale = float cfg.ExecutionErrorScale * (1.0 - technique * float frame.Condition[slot])
                    let errorX, nextRng = Rng.uniform (-scale) scale currentRng
                    currentRng <- nextRng
                    let errorY, nextRng = Rng.uniform (-scale) scale currentRng
                    currentRng <- nextRng
                    let errorZ, nextRng = Rng.uniform (-scale) scale currentRng
                    currentRng <- nextRng
                    let magnitude = sqrt (errorX * errorX + errorY * errorY + errorZ * errorZ)
                    let limit = float cfg.MaximumExecutionError
                    let factor = if magnitude > limit && magnitude > 0.0 then limit / magnitude else 1.0

                    frame.ContactActions[slot] <-
                        Some
                            { action with
                                Phase = ContactActionPhase.ContactActionCommitted
                                ErrorX = float32 (errorX * factor)
                                ErrorY = float32 (errorY * factor)
                                ErrorZ = float32 (errorZ * factor) }

                    // A slide is the price of the extra reach: the tackler is on the ground until the action ends, and
                    // cannot recover the ball or a beaten duel during it.
                    if action.Kind = ContactActionKind.ContactSlidingTackle then
                        let state = frame.ContactState[slot]

                        frame.ContactState[slot] <-
                            { state with
                                Status = BodyContactStatus.BodyFalling
                                EnteredAt = tick
                                ExitsAt = action.EndsAt
                                Support = BodySupport.NoBodySupport }
                | Some action when
                    action.Phase = ContactActionPhase.ContactActionCommitted
                    && tick >= action.CommitAt + cfg.CommittedDuration ->
                    frame.ContactActions[slot] <- Some { action with Phase = ContactActionPhase.ContactActionRecovering }
                | _ -> ()

        currentRng

    let private initializeBodies (ctx: SystemContext) (world: World) (buffer: EventBuffer) : unit =
        let scratch = world.Contact
        let contactCfg = ctx.Ctx.Config.Contact
        let movementCfg = ctx.Ctx.Config.Movement
        let mutable count = 0

        for side in [ ClubSide.Home; ClubSide.Away ] do
            let team = MatchWorld.team world side
            let frame = team.Frame

            for slot in MatchWorld.activeSlots frame do
                if count < scratch.Sides.Length then
                    let profile = MatchWorld.profileOf ctx.Ctx side slot
                    let morphology = profile.Morphology
                    let validMorphology =
                        System.Double.IsFinite(float morphology.Height)
                        && System.Double.IsFinite(float morphology.Mass)
                        && System.Double.IsFinite(float morphology.ShoulderWidth)
                        && morphology.Height >= 1.0f
                        && morphology.ShoulderWidth > 0.0f

                    let height = if validMorphology then float morphology.Height else float movementCfg.StandingHeight
                    let radius =
                        if validMorphology then
                            max 0.05 (float morphology.ShoulderWidth * contactCfg.BodyRadiusScale)
                        else
                            float contactCfg.BodyRadiusFallback

                    scratch.Sides[count] <- side
                    scratch.Slots[count] <- slot
                    scratch.StartX[count] <- float frame.PosX[slot]
                    scratch.StartY[count] <- float frame.PosY[slot]
                    scratch.StartZ[count] <- float frame.PosZ[slot]
                    scratch.VelocityX[count] <- float frame.VelX[slot]
                    scratch.VelocityY[count] <- float frame.VelY[slot]
                    scratch.VelocityZ[count] <- float frame.VelZ[slot]
                    scratch.Radius[count] <- radius
                    scratch.Height[count] <- height
                    scratch.AirborneAtStart[count] <- frame.PosZ[slot] > 0.0f || frame.VelZ[slot] > 0.0f

                    let scale =
                        match frame.ContactState[slot].Status with
                        | BodyContactStatus.BodyStaggering -> contactCfg.StaggerAccelerationScale
                        | BodyContactStatus.BodyFalling -> contactCfg.FallingAccelerationScale
                        | _ -> 1.0

                    let maxAcceleration =
                        float movementCfg.Acceleration
                        * max 0.1 (min 1.0 (float movementCfg.Smoothing))
                        * scale

                    let dx = float frame.TargetVelX[slot] - float frame.VelX[slot]
                    let dy = float frame.TargetVelY[slot] - float frame.VelY[slot]
                    let magnitude = sqrt (dx * dx + dy * dy)
                    let acceleration = min maxAcceleration (magnitude / max (SimulationClock.dt ctx.Clock |> float) 1e-9)
                    let factor = if magnitude > 1e-9 then acceleration / magnitude else 0.0
                    scratch.AccelerationX[count] <- dx * factor
                    scratch.AccelerationY[count] <- dy * factor

                    frame.ContactState[slot] <-
                        { frame.ContactState[slot] with
                            Support = supportOf frame slot }

                    let grounded = frame.PosZ[slot] = 0.0f && frame.VelZ[slot] = 0.0f
                    let requested = frame.JumpRequested[slot]
                    frame.JumpRequested[slot] <- false

                    if requested && grounded && world.State.Flow = MatchFlow.Live then
                        let fatigue = 1.0 - (1.0 - float frame.Condition[slot]) * movementCfg.FatigueSpeedDecay
                        frame.VelZ[slot] <- float32 (float movementCfg.JumpSpeed * fatigue)
                        scratch.VelocityZ[count] <- float frame.VelZ[slot]
                        frame.JumpSequence[slot] <- frame.JumpSequence[slot] + 1UL

                        world.Motion.Append
                            { Sequence = 0UL
                              TaskId = frame.JumpSequence[slot]
                              Actor = profile.Id
                              Generation = frame.ContactGeneration[slot]
                              Family = MotionFamily.Jump
                              Effector = MotionEffector.Body
                              Phase = MotionEvidencePhase.Confirmed
                              OccurredAt = world.State.Tick
                              AvailableAt = world.State.Tick + 1<subtick>
                              StartedAt = world.State.Tick
                              CommitAt = world.State.Tick
                              EndsAt = world.State.Tick + 80<subtick>
                              PredictedPoint = Vector3(frame.PosX[slot], frame.PosY[slot], 0.0f)
                              ConfirmedPoint = ValueSome(Vector3(frame.PosX[slot], frame.PosY[slot], 0.0f))
                              BallCenter = Vector3(float32 world.State.Ball.Position.X, float32 world.State.Ball.Position.Y, float32 world.State.Ball.Position.Z)
                              IncomingVelocity = ValueNone
                              OutgoingVelocity = ValueNone }
                        |> ignore

                        let event =
                            { Tick = world.State.Tick
                              Data = EventData.PlayerBodyFlight(profile.Id, frame.JumpSequence[slot], true) }

                        buffer.Events.Add event
                        count <- count + 1
                    else
                        count <- count + 1
                else
                    scratch.SaturationCount <- scratch.SaturationCount + 1UL

        scratch.Count <- count

    let private updateBalance (ctx: SystemContext) (world: World) (scratch: ContactScratch) : unit =
        let cfg = ctx.Ctx.Config.Contact
        let dt = float (SimulationClock.dt ctx.Clock)
        let tick = world.State.Tick

        for side in [ ClubSide.Home; ClubSide.Away ] do
            let frame = (MatchWorld.team world side).Frame

            for slot in MatchWorld.activeSlots frame do
                let state = frame.ContactState[slot]
                let stability = MatchMath.clamp01 (float state.Stability + cfg.StabilityRecoveryPerSecond * dt)
                let perturbationDecay = max 0.0 (1.0 - cfg.StabilityRecoveryPerSecond * dt)
                let recoveredStatus =
                    match state.Status with
                    | BodyContactStatus.BodyFalling when tick >= state.ExitsAt && stability >= cfg.RecoverStabilityThreshold ->
                        BodyContactStatus.BodyRecovering
                    | BodyContactStatus.BodyStaggering when stability >= cfg.RecoverStabilityThreshold ->
                        BodyContactStatus.BodyRecovering
                    | BodyContactStatus.BodyRecovering when tick > state.EnteredAt && stability >= cfg.RecoverStabilityThreshold ->
                        BodyContactStatus.BodyStable
                    | _ -> state.Status

                frame.ContactState[slot] <-
                    { state with
                        Stability = float32 stability
                        PerturbationX = state.PerturbationX * float32 perturbationDecay
                        PerturbationY = state.PerturbationY * float32 perturbationDecay
                        AngularPerturbation = state.AngularPerturbation * float32 perturbationDecay
                        Status = recoveredStatus
                        EnteredAt = if recoveredStatus <> state.Status then tick else state.EnteredAt
                        Support = supportOf frame slot }

    let private publishContact
        (ctx: MatchContext)
        (world: World)
        (buffer: EventBuffer)
        (record: ContactRecord)
        =
        let event =
            { Tick = world.State.Tick
              Data = EventData.PhysicalContact record }

        buffer.Events.Add event

        match record.Action, MatchWorld.teamOfPlayer ctx record.Actor with
        | Some actionId, Some side ->
            match MatchWorld.slotOf ctx side record.Actor with
            | Some slot ->
                let frame = (MatchWorld.team world side).Frame

                match frame.ContactActions[slot] with
                | Some action when
                    action.Id = actionId
                    && (record.BallTouched || action.Kind <> ContactActionKind.ContactTrap) ->
                    ContactActionPlanner.publish
                        world
                        action
                        MotionEvidencePhase.Confirmed
                        (ValueSome(
                            System.Numerics.Vector3(
                                float32 record.PointX,
                                float32 record.PointY,
                                float32 record.PointZ
                            )
                        ))

                    frame.ContactActions[slot] <- Some { action with Phase = ContactActionPhase.ContactActionCompleted }
                | _ -> ()
            | None -> ()

            if world.Observation.Enabled then
                world.Observation.Associate(event, ActionCause actionId)
        | _ -> ()

    /// ballFirst: the challenge had already played the ball. That is a fair tackle unless the follow-through is
    /// dangerous, so only DangerousBallFirstSpeed and above makes it a foul.
    let private emitContactFoul
        (ctx: SystemContext)
        (world: World)
        (buffer: EventBuffer)
        (record: ContactRecord)
        (ballFirst: bool)
        =
        let config = ctx.Ctx.Config.ContactAction
        let relativeSpeed =
            sqrt (
                float record.RelativeVelocityX * float record.RelativeVelocityX
                + float record.RelativeVelocityY * float record.RelativeVelocityY
                + float record.RelativeVelocityZ * float record.RelativeVelocityZ
            )

        let challenge =
            match record.ActionKind with
            | Some ContactActionKind.ContactShoulderChallenge
            | Some ContactActionKind.ContactStandingTackle
            | Some ContactActionKind.ContactSlidingTackle -> true
            | _ -> false

        // A boot on the legs without the ball is a trip whatever its speed (Law 12: careless means a lack of
        // consideration, not a fast impact); body charges are legal until they are careless, so they need speed.
        let trip = record.SurfaceA = ContactSurface.ContactFoot && record.SurfaceB = ContactSurface.ContactLeg

        let threshold =
            if ballFirst then config.DangerousBallFirstSpeed
            elif trip then 0.0<meterPerSecond>
            else config.CarelessRelativeSpeed

        match challenge, record.Other with
        | true, Some victim when
            (trip && not ballFirst || record.NormalImpulse > 0.0) && relativeSpeed >= float threshold
            ->
            // Force is what the offender brings: a carrier running onto a boot doubles the closing speed without the
            // challenge being any more violent, so severity reads the offender's own speed, capped by the closing one.
            let offenderSpeed =
                match MatchWorld.teamOfPlayer ctx.Ctx record.Actor with
                | Some side ->
                    match MatchWorld.slotOf ctx.Ctx side record.Actor with
                    | Some slot ->
                        let frame = (MatchWorld.team world side).Frame
                        let vx, vy = float frame.VelX[slot], float frame.VelY[slot]
                        min relativeSpeed (sqrt (vx * vx + vy * vy))
                    | None -> relativeSpeed
                | None -> relativeSpeed

            let severity =
                if offenderSpeed >= float config.ExcessiveRelativeSpeed then
                    FoulSeverity.RedWorthy
                elif offenderSpeed >= float config.RecklessRelativeSpeed then
                    FoulSeverity.YellowWorthy
                else
                    FoulSeverity.Tactical

            let foul =
                { Tick = world.State.Tick
                  Data =
                    EventData.Foul(
                        record.Actor,
                        victim,
                        severity,
                        CardDecision.NoCard,
                        (record.PointX, record.PointY)
                    ) }

            buffer.Events.Add foul

            if world.Observation.Enabled then
                record.Action |> Option.iter (fun action -> world.Observation.Associate(foul, ActionCause action))
        | _ -> ()

    let private solve (ctx: SystemContext) (world: World) (buffer: EventBuffer) (rng: Rng) : Rng =
        let scratch = world.Contact
        let dt = float (SimulationClock.dt ctx.Clock)
        let segments = max 1 ctx.Ctx.Config.Contact.CurvedPathSegments
        let segmentDuration = dt / float segments
        let mutable ball = world.State.Ball
        let mutable currentRng = rng
        let mutable contactOrder = 0
        let mutable tickElapsed = 0.0
        let mutable tickImpacts = 0
        computeReach ctx.Ctx world scratch
        markBallCandidates ctx.Ctx world scratch ball dt

        for segment = 0 to segments - 1 do
            let mutable remaining = segmentDuration
            let mutable segmentElapsed = 0.0
            let mutable segmentImpacts = 0
            let mutable saturated = false

            while remaining > ctx.Ctx.Config.Contact.ToiTolerance && not saturated do
                let candidate, ballEnd = findEarliest ctx.Ctx world scratch ball remaining

                match candidate with
                | ValueNone ->
                    advancePlayers ctx world scratch remaining

                    if ball.GKHoldSince.IsNone then
                        ball <- ballEnd

                    tickElapsed <- tickElapsed + remaining
                    segmentElapsed <- segmentElapsed + remaining
                    remaining <- 0.0
                | ValueSome impact when segmentImpacts >= ctx.Ctx.Config.Contact.MaximumImpactIterations ->
                    scratch.SaturationCount <- scratch.SaturationCount + 1UL
                    saturated <- true

                    if impact.IsBall then
                        ball <- { ball with Position = { ball.Position with Vx = 0.0<meterPerSecond>; Vy = 0.0<meterPerSecond>; Vz = 0.0<meterPerSecond> } }
                    else
                        scratch.VelocityX[impact.PlayerA] <- 0.0
                        scratch.VelocityY[impact.PlayerA] <- 0.0
                        scratch.VelocityX[impact.PlayerB] <- 0.0
                        scratch.VelocityY[impact.PlayerB] <- 0.0
                | ValueSome impact ->
                    let advanceBy = max 0.0 (remaining * impact.Fraction)

                    if advanceBy > 0.0 then
                        advancePlayers ctx world scratch advanceBy

                        if ball.GKHoldSince.IsNone then
                            ball <- BallDynamics.integrate ctx.Ctx.Config.Ball advanceBy world.State.Tick ball

                        tickElapsed <- tickElapsed + advanceBy
                        segmentElapsed <- segmentElapsed + advanceBy
                        remaining <- remaining - advanceBy

                    let fraction = MatchMath.clamp01 ((float segment * segmentDuration + segmentElapsed) / dt)

                    if impact.IsBall then
                        let cancelledStrike =
                            match ball.PendingStrike with
                            | Some pending
                                when pending.Side <> scratch.Sides[impact.PlayerA]
                                     || pending.Slot <> scratch.Slots[impact.PlayerA] ->
                                Some pending.Impulse.ActorId
                            | _ -> None

                        let updatedBall, record, lostControl =
                            resolveBallContact ctx world scratch impact fraction ball (contactOrder + 1)

                        ball <- updatedBall
                        scratch.ResolvedBallPlayers[impact.PlayerA] <- true
                        contactOrder <- contactOrder + 1
                        publishContact ctx.Ctx world buffer record

                        match cancelledStrike with
                        | Some actor ->
                            world.Observation.CancelImpulse()
                            buffer.Events.Add
                                { Tick = world.State.Tick
                                  Data = EventData.FootStrikeCancelled actor }
                        | None -> ()

                        if lostControl then
                            buffer.Events.Add
                                { Tick = world.State.Tick
                                  Data = EventData.BallLoose(ball.Position.X, ball.Position.Y) }
                    elif impact.Surface = ContactSurface.ContactFoot then
                        let tacklerFrame = (MatchWorld.team world scratch.Sides[impact.PlayerA]).Frame
                        let tacklerSlot = scratch.Slots[impact.PlayerA]

                        match bootChallenge ctx.Ctx.Config.ContactAction world.State.Tick tacklerFrame tacklerSlot with
                        | Some action ->
                            let ballFirst = action.Phase = ContactActionPhase.ContactActionCompleted
                            let record = resolveBootContact ctx world scratch impact fraction action
                            publishContact ctx.Ctx world buffer record
                            emitContactFoul ctx world buffer record ballFirst
                            // One leg strike per challenge: the boot is spent, and a lingering overlap must not
                            // re-trip the same player every remaining subtick.
                            tacklerFrame.ContactActions[tacklerSlot] <-
                                Some { action with Phase = ContactActionPhase.ContactActionRecovering }
                        | None -> ()
                    else
                        let record = resolveBodyContact ctx world scratch impact fraction
                        scratch.ResolvedBodyPairs[impact.PlayerA * scratch.Sides.Length + impact.PlayerB] <- true

                        if record.NormalImpulse > 0.0 || record.TangentialImpulse > 0.0 || record.Action.IsSome then
                            publishContact ctx.Ctx world buffer record
                            emitContactFoul ctx world buffer record false

                    segmentImpacts <- segmentImpacts + 1
                    tickImpacts <- tickImpacts + 1
                    markBallCandidates ctx.Ctx world scratch ball (max 0.0 (dt - tickElapsed))

                    if advanceBy <= ctx.Ctx.Config.Contact.ToiTolerance then
                        remaining <- max 0.0 (remaining - ctx.Ctx.Config.Contact.ToiTolerance)
                        tickElapsed <- tickElapsed + ctx.Ctx.Config.Contact.ToiTolerance
                        segmentElapsed <- segmentElapsed + ctx.Ctx.Config.Contact.ToiTolerance

            if saturated && remaining > 0.0 then
                // Saturation constrains the implicated bodies; unrelated bodies and the ball still consume the subtick.
                advancePlayers ctx world scratch remaining

                if ball.GKHoldSince.IsNone then
                    ball <- BallDynamics.integrate ctx.Ctx.Config.Ball remaining world.State.Tick ball

        let originalBallPosition = world.State.Ball.Position

        for index = 0 to scratch.Count - 1 do
            let side, slot = scratch.Sides[index], scratch.Slots[index]
            let frame = (MatchWorld.team world side).Frame
            let oldX, oldY = float frame.PosX[slot], float frame.PosY[slot]
            frame.PosX[slot] <- float32 scratch.StartX[index]
            frame.PosY[slot] <- float32 scratch.StartY[index]
            frame.PosZ[slot] <- float32 scratch.StartZ[index]
            frame.VelX[slot] <- float32 scratch.VelocityX[index]
            frame.VelY[slot] <- float32 scratch.VelocityY[index]
            frame.VelZ[slot] <- float32 scratch.VelocityZ[index]
            frame.BodyTravelDistance[slot] <-
                frame.BodyTravelDistance[slot]
                + float32 (sqrt ((scratch.StartX[index] - oldX) ** 2.0 + (scratch.StartY[index] - oldY) ** 2.0))

            let wasAirborne = scratch.AirborneAtStart[index]
            let isGrounded = scratch.StartZ[index] = 0.0 && scratch.VelocityZ[index] = 0.0
            let gait = ctx.Ctx.Config.Gait
            let speed = sqrt (scratch.VelocityX[index] ** 2.0 + scratch.VelocityY[index] ** 2.0)

            // Airborne the legs do not cycle, and standing they do not either: landing or setting off resumes the
            // stride where it stopped instead of starting mid-swing.
            if isGrounded && not wasAirborne then
                let morphology = (profileAt ctx.Ctx world scratch index).Morphology
                let cycles = Gait.cyclesPerSecond gait morphology speed
                let phase = float32 (Gait.wrap (float frame.GaitPhase[slot] + cycles * dt))
                // Single precision can round a phase just below one up to one, outside the published range.
                frame.GaitPhase[slot] <- if phase >= 1.0f then 0.0f else phase

            if wasAirborne && isGrounded then
                let profile = profileAt ctx.Ctx world scratch index
                let event =
                    { Tick = world.State.Tick
                      Data = EventData.PlayerBodyFlight(profile.Id, frame.JumpSequence[slot], false) }

                buffer.Events.Add event

                world.Motion.Append
                    { Sequence = 0UL
                      TaskId = frame.JumpSequence[slot]
                      Actor = profile.Id
                      Generation = frame.ContactGeneration[slot]
                      Family = MotionFamily.Landing
                      Effector = MotionEffector.Body
                      Phase = MotionEvidencePhase.Confirmed
                      OccurredAt = world.State.Tick
                      AvailableAt = world.State.Tick + 1<subtick>
                      StartedAt = world.State.Tick
                      CommitAt = world.State.Tick
                      EndsAt = world.State.Tick
                      PredictedPoint = Vector3(frame.PosX[slot], frame.PosY[slot], 0.0f)
                      ConfirmedPoint = ValueSome(Vector3(frame.PosX[slot], frame.PosY[slot], 0.0f))
                      BallCenter = Vector3(float32 ball.Position.X, float32 ball.Position.Y, float32 ball.Position.Z)
                      IncomingVelocity = ValueNone
                      OutgoingVelocity = ValueNone }
                |> ignore

            frame.ContactState[slot] <-
                { frame.ContactState[slot] with
                    Support = if isGrounded then BodySupport.DoubleFootSupport else BodySupport.NoBodySupport }

        if ball.GKHoldSince.IsNone then
            ball <- { ball with PreviousPosition = ValueSome originalBallPosition }

        MatchWorld.updateBall world (fun _ -> ball)
        let _ = tickElapsed, tickImpacts
        currentRng

    let step: SystemStep =
        fun ctx rng world buffer ->
            Array.Clear world.Contact.ResolvedBodyPairs
            Array.Clear world.Contact.ResolvedBallPlayers
            let rng = advanceActionCommitments ctx world rng
            updateBalance ctx world world.Contact
            initializeBodies ctx world buffer
            solve ctx world buffer rng
