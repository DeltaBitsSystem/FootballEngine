namespace FootballEngine.Client.Views.Components

open FootballEngine
open FootballEngine.Domain

module CoreReplay = FootballEngine.Core.Match.Replay

module MatchProjection =

    let restartDescription (restart: RestartVisual) =
        match restart with
        | KickOff -> "KICK-OFF"
        | ThrowIn -> "THROW-IN"
        | Corner -> "CORNER"
        | GoalKick -> "GOAL KICK"
        | FreeKick -> "FREE KICK"
        | Penalty -> "PENALTY"

    let private restartOfCode signal =
        match signal with
        | 10uy -> Some KickOff
        | 11uy -> Some ThrowIn
        | 12uy -> Some Corner
        | 13uy -> Some GoalKick
        | 14uy -> Some FreeKick
        | 15uy -> Some Penalty
        | _ -> None

    let private signalOfCode signal =
        match restartOfCode signal with
        | Some restart -> OfficialSignal.RestartDirection restart
        | None when signal = 1uy -> OfficialSignal.Whistle
        | None -> OfficialSignal.NoSignal

    let private situationOfCodes flow signal =
        match flow with
        | 1uy -> MatchSituation.Goal
        | 2uy -> MatchSituation.VarReview
        | 3uy -> MatchSituation.Injury
        | 4uy -> MatchSituation.Restart(restartOfCode signal)
        | 5uy -> MatchSituation.HalfTime
        | 6uy -> MatchSituation.ExtraTimeBreak
        | 7uy -> MatchSituation.FullTimeReview
        | 8uy -> MatchSituation.Shootout
        | 9uy -> MatchSituation.Ended
        | _ -> MatchSituation.Live

    let private shortName (name: string) =
        let parts = name.Split([| ' ' |])
        if parts.Length >= 2 then parts[parts.Length - 1] else name

    let private projectTeam
        (isHome: bool)
        (xs: float32[])
        (ys: float32[])
        (zs: float32[])
        (vzs: float32[])
        (vxs: float32[])
        (vys: float32[])
        (facings: float32[])
        (angularVelocities: float32[])
        (travelDistances: float32[])
        (conditions: float32[])
        (active: bool[])
        (ids: FootballEngine.Core.Match.PlayerId[])
        (names: string[])
        (starterCount: int)
        (controller: int option)
        : RenderPlayer[] =
        let slotCount = xs.Length

        if
            ys.Length <> slotCount
            || zs.Length <> slotCount
            || vzs.Length <> slotCount
            || vxs.Length <> slotCount
            || vys.Length <> slotCount
            || facings.Length <> slotCount
            || angularVelocities.Length <> slotCount
            || travelDistances.Length <> slotCount
            || conditions.Length <> slotCount
            || active.Length <> slotCount
            || ids.Length <> slotCount
            || names.Length <> slotCount
            || starterCount < 0
            || starterCount > 11
            || starterCount > slotCount
        then
            invalidArg "snapshot" "team pose arrays do not match the fixture roster contract"

        let visualSlots = Array.create slotCount -1
        let occupiedStarters = Array.zeroCreate<bool> starterCount

        for slot = 0 to starterCount - 1 do
            if active[slot] then
                occupiedStarters[slot] <- true
                visualSlots[slot] <- slot

        let vacancies =
            [| for slot = 0 to starterCount - 1 do
                   if not occupiedStarters[slot] then
                       yield slot |]

        let activeSubstitutes =
            [| for slot = starterCount to slotCount - 1 do
                   if active[slot] then
                       yield slot |]

        if activeSubstitutes.Length > vacancies.Length then
            invalidArg "snapshot" "active roster exceeds the eleven on-pitch visual slots"

        for index = 0 to activeSubstitutes.Length - 1 do
            visualSlots[activeSubstitutes[index]] <- vacancies[index]

        xs
        |> Array.mapi (fun i x -> i, x)
        |> Array.choose (fun (i, x) ->
            if not active[i] then
                None
            else
                let (FootballEngine.Core.Match.PlayerId id) = ids[i]

                if System.String.IsNullOrWhiteSpace names[i] then
                    invalidArg "names" $"active player slot {i} has no display name"

                let name = shortName names[i]

                let vx = float vxs[i]
                let vy = float vys[i]
                let speed = sqrt (vx * vx + vy * vy)

                let visualState =
                    if speed < 0.25 then Idle
                    elif speed < 2.0 then Walking
                    elif speed < 6.0 then Running
                    else Sprinting

                Some
                    { Id = id
                      Position = { X = float x; Y = float ys[i] }
                      Height = float zs[i]
                      VelocityZ = float vzs[i]
                      Velocity = { X = vx; Y = vy }
                      Acceleration = Vector2.zero
                      BodyFacing = float facings[i]
                      AngularVelocity = float angularVelocities[i]
                      TravelDistance = float travelDistances[i]
                      State = visualState
                      VisualSlot = visualSlots[i]
                      ShortName = name
                      Condition = int (System.Math.Clamp(float conditions[i], 0.0, 1.0) * 100.0)
                      IsHome = isHome
                      HasBall = controller = Some id
                      IsSidelined = false })

    let project
        (homeNames: string[])
        (awayNames: string[])
        (homeIds: FootballEngine.Core.Match.PlayerId[])
        (awayIds: FootballEngine.Core.Match.PlayerId[])
        (homeStarterCount: int)
        (awayStarterCount: int)
        (homeClubId: ClubId)
        (awayClubId: ClubId)
        (homeName: string)
        (awayName: string)
        (competitionName: string option)
        (snapshot: CoreReplay.ReplaySnapshot)
        (timeSeconds: float)
        : RenderFrame =
        if snapshot.HomePlayerIds <> homeIds || snapshot.AwayPlayerIds <> awayIds then
            invalidArg "snapshot" "replay roster identity does not match its fixture"

        let homePlayers =
            projectTeam
                true
                snapshot.HomeX
                snapshot.HomeY
                snapshot.HomeZ
                snapshot.HomeVz
                snapshot.HomeVx
                snapshot.HomeVy
                snapshot.HomeFacing
                snapshot.HomeAngularVelocity
                snapshot.HomeTravelDistance
                snapshot.HomeCondition
                snapshot.HomeActive
                snapshot.HomePlayerIds
                homeNames
                homeStarterCount
                (if snapshot.BallControlSide = 0y then
                     Some snapshot.BallController
                 else
                     None)

        let awayPlayers =
            projectTeam
                false
                snapshot.AwayX
                snapshot.AwayY
                snapshot.AwayZ
                snapshot.AwayVz
                snapshot.AwayVx
                snapshot.AwayVy
                snapshot.AwayFacing
                snapshot.AwayAngularVelocity
                snapshot.AwayTravelDistance
                snapshot.AwayCondition
                snapshot.AwayActive
                snapshot.AwayPlayerIds
                awayNames
                awayStarterCount
                (if snapshot.BallControlSide = 1y then
                     Some snapshot.BallController
                 else
                     None)

        let renderBall =
            { Position =
                { X = float snapshot.BallX
                  Y = float snapshot.BallY }
              Velocity =
                { X = float snapshot.BallVx
                  Y = float snapshot.BallVy }
              VelocityZ = float snapshot.BallVz
              Orientation = snapshot.BallOrientation
              AngularVelocity = System.Numerics.Vector3(snapshot.BallSpinX, snapshot.BallSpinY, snapshot.BallSpinZ)
              Radius = float snapshot.BallRadius
              FootMotion = snapshot.FootMotion
              Height = float snapshot.BallZ }

        let officials =
            [| { Position = Vector2.create (float snapshot.RefereeX) (float snapshot.RefereeY)
                 Signal = signalOfCode snapshot.RefereeSignal
                 IsAssistant = false }
               { Position = Vector2.create (float snapshot.NearAssistantX) (float snapshot.NearAssistantY)
                 Signal = OfficialSignal.NoSignal
                 IsAssistant = true }
               { Position = Vector2.create (float snapshot.FarAssistantX) (float snapshot.FarAssistantY)
                 Signal = OfficialSignal.NoSignal
                 IsAssistant = true } |]

        let players = Array.append homePlayers awayPlayers
        Array.sortInPlaceBy (fun (player: RenderPlayer) -> player.Position.Y) players

        { Players = players
          Ball = renderBall
          Officials = officials
          TimeSeconds = timeSeconds
          HomeScore = snapshot.HomeScore
          AwayScore = snapshot.AwayScore
          AttackingClubId =
            if snapshot.BallControlSide = 0y then Some homeClubId
            elif snapshot.BallControlSide = 1y then Some awayClubId
            else None
          Momentum = 0.0
          Situation = situationOfCodes snapshot.Flow snapshot.RefereeSignal
          HomeName = homeName
          AwayName = awayName
          CompetitionName = competitionName }

module MatchInterp =

    let private advanceOrientation orientation (rotation: System.Numerics.Vector3) =
        let angle = rotation.Length()

        if angle < 1e-8f then
            orientation
        else
            System.Numerics.Quaternion.Normalize(
                System.Numerics.Quaternion.CreateFromAxisAngle(rotation / angle, angle)
                * orientation
            )

    let private orientationBetween (a: RenderBall) (b: RenderBall) t dt =
        // A rolling football can turn more than pi per pose; shortest-arc slerp alone would reverse its spin.
        let average = (a.AngularVelocity + b.AngularVelocity) * 0.5f
        let predictedEnd = advanceOrientation a.Orientation (average * float32 dt)
        let correction = b.Orientation * System.Numerics.Quaternion.Inverse predictedEnd

        let rotation =
            (a.AngularVelocity * float32 t
             + (b.AngularVelocity - a.AngularVelocity) * float32 (0.5 * t * t))
            * float32 dt

        System.Numerics.Quaternion.Normalize(
            System.Numerics.Quaternion.Slerp(System.Numerics.Quaternion.Identity, correction, float32 t)
            * advanceOrientation a.Orientation rotation
        )

    let private hermiteScalar (p0: float) (v0: float) (p1: float) (v1: float) (t: float) (dt: float) =
        let t2 = t * t
        let t3 = t2 * t
        let h00 = 2.0 * t3 - 3.0 * t2 + 1.0
        let h10 = t3 - 2.0 * t2 + t
        let h01 = -2.0 * t3 + 3.0 * t2
        let h11 = t3 - t2
        h00 * p0 + h10 * v0 * dt + h01 * p1 + h11 * v1 * dt

    let private hermiteVec2 (a: Vector2) (va: Vector2) (b: Vector2) (vb: Vector2) (t: float) (dt: float) =
        { X = hermiteScalar a.X va.X b.X vb.X t dt
          Y = hermiteScalar a.Y va.Y b.Y vb.Y t dt }

    let private hermitePositive (frame0: RenderFrame) (frame1: RenderFrame) (t: float) (dt: float) : RenderFrame =
        let t = System.Math.Clamp(t, 0.0, 1.0)

        let players =
            frame0.Players
            |> Array.choose (fun (p0: RenderPlayer) ->
                match frame1.Players |> Array.tryFind (fun p1 -> p1.Id = p0.Id) with
                | None -> if t < 0.5 then Some p0 else None
                | Some p1 ->
                    let t2 = t * t
                    let dh00 = 6.0 * t2 - 6.0 * t
                    let dh10 = 3.0 * t2 - 4.0 * t + 1.0
                    let dh01 = -6.0 * t2 + 6.0 * t
                    let dh11 = 3.0 * t2 - 2.0 * t

                    Some
                        { p0 with
                            Position = hermiteVec2 p0.Position p0.Velocity p1.Position p1.Velocity t dt
                            Height = max 0.0 (hermiteScalar p0.Height p0.VelocityZ p1.Height p1.VelocityZ t dt)
                            VelocityZ =
                                dh00 * p0.Height / dt
                                + dh10 * p0.VelocityZ
                                + dh01 * p1.Height / dt
                                + dh11 * p1.VelocityZ
                            Velocity =
                                { X =
                                    dh00 * p0.Position.X / dt
                                    + dh10 * p0.Velocity.X
                                    + dh01 * p1.Position.X / dt
                                    + dh11 * p1.Velocity.X
                                  Y =
                                    dh00 * p0.Position.Y / dt
                                    + dh10 * p0.Velocity.Y
                                    + dh01 * p1.Position.Y / dt
                                    + dh11 * p1.Velocity.Y }
                            Acceleration =
                                { X = (p1.Velocity.X - p0.Velocity.X) / dt
                                  Y = (p1.Velocity.Y - p0.Velocity.Y) / dt }
                            BodyFacing =
                                let delta =
                                    atan2 (sin (p1.BodyFacing - p0.BodyFacing)) (cos (p1.BodyFacing - p0.BodyFacing))

                                atan2 (sin (p0.BodyFacing + t * delta)) (cos (p0.BodyFacing + t * delta))
                            AngularVelocity = p0.AngularVelocity + (p1.AngularVelocity - p0.AngularVelocity) * t
                            TravelDistance = p0.TravelDistance + (p1.TravelDistance - p0.TravelDistance) * t
                            State = p0.State })

            |> fun existing ->
                if t < 0.5 then
                    existing
                else
                    Array.append
                        existing
                        (frame1.Players
                         |> Array.filter (fun p1 -> frame0.Players |> Array.exists (fun p0 -> p0.Id = p1.Id) |> not))

        let ball =
            let b0 = frame0.Ball
            let b1 = frame1.Ball
            let t2 = t * t
            let dh00 = 6.0 * t2 - 6.0 * t
            let dh10 = 3.0 * t2 - 4.0 * t + 1.0
            let dh01 = -6.0 * t2 + 6.0 * t
            let dh11 = 3.0 * t2 - 2.0 * t

            { Position = hermiteVec2 b0.Position b0.Velocity b1.Position b1.Velocity t dt
              Velocity =
                { X =
                    dh00 * b0.Position.X / dt
                    + dh10 * b0.Velocity.X
                    + dh01 * b1.Position.X / dt
                    + dh11 * b1.Velocity.X
                  Y =
                    dh00 * b0.Position.Y / dt
                    + dh10 * b0.Velocity.Y
                    + dh01 * b1.Position.Y / dt
                    + dh11 * b1.Velocity.Y }
              VelocityZ =
                dh00 * b0.Height / dt
                + dh10 * b0.VelocityZ
                + dh01 * b1.Height / dt
                + dh11 * b1.VelocityZ
              Orientation = orientationBetween b0 b1 t dt
              AngularVelocity = System.Numerics.Vector3.Lerp(b0.AngularVelocity, b1.AngularVelocity, float32 t)
              Radius = b0.Radius
              FootMotion =
                if t < 1.0 then
                    b0.FootMotion |> Option.orElse b1.FootMotion
                else
                    b1.FootMotion
              Height = max 0.0 (hermiteScalar b0.Height b0.VelocityZ b1.Height b1.VelocityZ t dt) }

        let officials =
            Array.map2
                (fun (a: RenderOfficial) (b: RenderOfficial) ->
                    { a with
                        Position = hermiteVec2 a.Position Vector2.zero b.Position Vector2.zero t dt
                        Signal = if t < 0.5 then a.Signal else b.Signal })
                frame0.Officials
                frame1.Officials

        let ball =
            match frame1.Ball.FootMotion with
            | Some motion when
                float motion.ContactAt / 40.0 > frame0.TimeSeconds
                && float motion.ContactAt / 40.0 <= frame1.TimeSeconds
                ->
                // Split at the impulse: outgoing velocity must never bend the pre-contact trajectory.
                let contactTime = float motion.ContactAt / 40.0
                let now = frame0.TimeSeconds + t * dt

                let contactPosition =
                    Vector2.create (float motion.BallCenter.X) (float motion.BallCenter.Y)

                let contactHeight = max 0.0 (float motion.BallCenter.Z - ball.Radius)
                let before = now < contactTime

                let startTime, endTime =
                    if before then
                        frame0.TimeSeconds, contactTime
                    else
                        contactTime, frame1.TimeSeconds

                let duration = endTime - startTime

                let fraction =
                    if duration <= 0.0 then
                        1.0
                    else
                        System.Math.Clamp((now - startTime) / duration, 0.0, 1.0)

                let a, b, velocity, heightA, heightB, velocityZ =
                    if before then
                        frame0.Ball.Position,
                        contactPosition,
                        frame0.Ball.Velocity,
                        frame0.Ball.Height,
                        contactHeight,
                        frame0.Ball.VelocityZ
                    else
                        contactPosition,
                        frame1.Ball.Position,
                        frame1.Ball.Velocity,
                        contactHeight,
                        frame1.Ball.Height,
                        frame1.Ball.VelocityZ

                let contactOrientation =
                    advanceOrientation
                        frame0.Ball.Orientation
                        (frame0.Ball.AngularVelocity * float32 (contactTime - frame0.TimeSeconds))

                let orientation =
                    if before then
                        advanceOrientation
                            frame0.Ball.Orientation
                            (frame0.Ball.AngularVelocity * float32 (now - frame0.TimeSeconds))
                    else
                        let contactBall =
                            { frame1.Ball with
                                Orientation = contactOrientation }

                        orientationBetween contactBall frame1.Ball fraction duration

                { ball with
                    Position = hermiteVec2 a velocity b velocity fraction duration
                    Height = max 0.0 (hermiteScalar heightA velocityZ heightB velocityZ fraction duration)
                    Velocity = velocity
                    VelocityZ = velocityZ
                    Orientation = orientation
                    AngularVelocity =
                        if before then
                            frame0.Ball.AngularVelocity
                        else
                            frame1.Ball.AngularVelocity
                    FootMotion = Some motion }
            | _ -> ball

        { frame0 with
            Players = players
            Ball = ball
            Officials = officials
            TimeSeconds = frame0.TimeSeconds + t * dt
            HomeScore = if t < 0.5 then frame0.HomeScore else frame1.HomeScore
            AwayScore = if t < 0.5 then frame0.AwayScore else frame1.AwayScore
            Situation = if t < 0.5 then frame0.Situation else frame1.Situation }

    let hermite (frame0: RenderFrame) (frame1: RenderFrame) (t: float) (dt: float) : RenderFrame =
        if not (System.Double.IsFinite t) then
            invalidArg "t" "interpolation parameter must be finite"
        elif not (System.Double.IsFinite dt) then
            invalidArg "dt" "pose duration must be finite"
        elif dt <= 0.0 then
            if t < 0.5 then frame0 else frame1
        else
            hermitePositive frame0 frame1 t dt
