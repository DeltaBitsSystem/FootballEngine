namespace FootballEngine.Client.Animation

open System
open System.Numerics
open FootballEngine.Core.Match

module SupportPlanner =
    let private gazeTarget config (root: MotionRootSample) =
        match root.AttentionTarget with
        | ValueNone -> Vector2.Zero
        | ValueSome target ->
            let eye =
                MotionInput.vector root.Position
                + Vector3.UnitY * (root.Morphology.Height * (1.65f / 1.8f))

            let local =
                MotionInput.localPoint eye (MotionInput.facing root.Facing) (MotionInput.vector target)

            let horizontal =
                max config.GazeNearDistance (MathF.Sqrt(local.X * local.X + local.Z * local.Z))

            Vector2(
                Math.Clamp(MathF.Atan2(local.X, local.Z), -config.MaxGazeYaw, config.MaxGazeYaw),
                Math.Clamp(-MathF.Atan2(local.Y, horizontal), -config.MaxGazePitch, config.MaxGazePitch)
            )

    let private standing time anchor =
        { Phase = SupportPhase.Support
          Anchor = anchor
          Segment =
            { Start = time
              Finish = time + 1.0f
              From = anchor
              Target = anchor
              FromVelocity = Vector3.Zero
              TargetVelocity = Vector3.Zero
              Clearance = 0.0f }
          ReleaseAt = time }

    let nominal config (root: MotionRootSample) left =
        let yaw = MotionInput.facing root.Facing
        let width = config.StepWidth * root.Morphology.Height / 1.8f
        let lateral = if left then width else -width

        MotionInput.vector root.Position
        + Vector3.Transform(Vector3(lateral, 0.0f, 0.0f), Matrix4x4.CreateRotationY yaw)

    let initialize config tick (root: MotionRootSample) =
        let (PlayerId identity) = root.Actor
        let seed = uint64 (uint32 identity) * 0x9E3779B97F4A7C15UL + uint64 root.Generation

        { Root = root
          Tick = tick
          Left = standing (float32 tick) (nominal config root true)
          Right = standing (float32 tick) (nominal config root false)
          LeftOverlay = FootOverlay.empty
          RightOverlay = FootOverlay.empty
          Acceleration = Vector3.Zero
          Lean = Vector3.Zero
          Gaze = gazeTarget config root
          Seed = seed }

    let private boundedLead config (root: MotionRootSample) (proposal: Vector3) =
        let maximum =
            min
                config.StepReach
                ((root.Morphology.ThighLength + root.Morphology.ShinLength)
                 * config.MaxLegLeadFraction)

        if proposal.Length() > maximum then
            Vector3.Normalize(proposal) * maximum
        else
            proposal

    let private nextFoot
        (config, time, root: MotionRootSample, left, other: FootPlan, otherTaskActive, foot: FootPlan)
        =
        let airborne = root.Position.Z > 0.02f || abs root.Velocity.Z > 0.1f
        let nominal = nominal config root left
        let velocity = MotionInput.vector root.Velocity
        let horizontal = Vector3(velocity.X, 0.0f, velocity.Z)
        let speed = horizontal.Length()
        let displacement = nominal - foot.Anchor

        let stepDistance =
            if speed > 1e-5f then
                max 0.0f (Vector3.Dot(displacement, horizontal) / speed)
            else
                displacement.Length()

        if airborne then
            // No world support is asserted while the sporting root is airborne.
            { foot with
                Phase = SupportPhase.Swing
                Segment =
                    { foot.Segment with
                        Start = time
                        Finish = time + 1.0f
                        From = nominal
                        Target = nominal
                        FromVelocity = horizontal
                        TargetVelocity = horizontal
                        Clearance = 0.0f } }
        elif foot.Phase = SupportPhase.Swing || foot.Phase = SupportPhase.Landing then
            if time >= foot.Segment.Finish then
                let planted = standing time foot.Segment.Target
                { planted with ReleaseAt = time + 2.0f }
            else
                let remainingSeconds = (foot.Segment.Finish - time) * config.TickSeconds

                let desired =
                    nominal
                    + horizontal * remainingSeconds
                    + boundedLead config root (horizontal * config.StepLeadSeconds)

                let change = desired - foot.Segment.Target
                let remaining = foot.Segment.Finish - time

                let segment =
                    if change.Length() > config.RetargetDistance && remaining > 2.0f then
                        let bounded =
                            foot.Segment.Target
                            + Vector3.Normalize(change) * min (change.Length()) config.RetargetDistance

                        Trajectory.retarget config.TickSeconds time bounded foot.Segment.Finish foot.Segment
                    else
                        foot.Segment

                { foot with
                    Segment = segment
                    Phase =
                        if foot.Segment.Finish - time < 2.0f then
                            SupportPhase.Landing
                        else
                            SupportPhase.Swing }
        elif
            time >= foot.ReleaseAt
            && (other.Phase = SupportPhase.Support
                || otherTaskActive
                || (speed >= config.RunFlightSpeed
                    && other.Phase = SupportPhase.Landing
                    && other.Segment.Finish - time
                       <= config.RunFlightOverlapSeconds / config.TickSeconds))
            && stepDistance > min
                config.StepReach
                (max
                    (config.StepTrigger * config.PivotTriggerMinimumRatio)
                    (config.StepTrigger
                     - speed * config.StepSpeedTrigger
                     - abs root.AngularVelocity * config.PivotSensitivity))
        then
            let duration =
                Math.Clamp(
                    config.SwingSeconds / (1.0f + speed * config.SwingSpeedFactor),
                    config.MinSwingSeconds,
                    config.SwingSeconds
                )

            let mutable best = nominal
            let mutable score = Single.PositiveInfinity
            // Stable candidate order breaks equal scores without RNG or unbounded search.
            for candidate = 0 to 3 do
                let lead = float32 candidate * 0.075f
                let proposal = boundedLead config root (horizontal * (duration * 0.5f + lead))
                let target = nominal + horizontal * duration + proposal
                let reach = proposal.Length()
                let separation = Vector3.Distance(target, other.Anchor)

                let cost =
                    max 0.0f (reach - config.StepReach) * 100.0f + abs (separation - 0.35f) + lead

                if cost < score then
                    score <- cost
                    best <- target

            { Phase = SupportPhase.Swing
              Anchor = foot.Anchor
              ReleaseAt = time
              Segment =
                { Start = time
                  Finish = time + duration / config.TickSeconds
                  From = foot.Anchor
                  Target = best
                  FromVelocity = Vector3.Zero
                  TargetVelocity = Vector3.Zero
                  Clearance =
                    config.Clearance
                    * (1.0f
                       + Math.Clamp((speed - config.RunFlightSpeed) / (config.RunFlightSpeed * 0.5f), 0.0f, 1.0f)) } }
        else
            foot

    let private updateFoot config time root left other (foot: FootPlan) (overlay: FootOverlay) otherTask =
        if overlay.Active && time >= overlay.Recovery.Finish then
            standing time overlay.Recovery.Target
        elif overlay.Active then
            foot
        else
            nextFoot (config, time, root, left, other, otherTask, foot)

    let private urgency config root horizontal speed left (foot: FootPlan) =
        let displacement = nominal config root left - foot.Anchor

        if speed > 1e-5f then
            Vector3.Dot(displacement, horizontal) / speed
        else
            displacement.Length()

    let advance config tick (root: MotionRootSample) (previous: ActorPlan) =
        let time = float32 tick
        let velocity = MotionInput.vector root.Velocity
        let horizontal = Vector3(velocity.X, 0.0f, velocity.Z)
        let speed = horizontal.Length()

        let struct (left, right) =
            if
                urgency config root horizontal speed false previous.Right > urgency
                    config
                    root
                    horizontal
                    speed
                    true
                    previous.Left
            then
                let right =
                    updateFoot
                        config
                        time
                        root
                        false
                        previous.Left
                        previous.Right
                        previous.RightOverlay
                        previous.LeftOverlay.Active

                let left =
                    updateFoot
                        config
                        time
                        root
                        true
                        right
                        previous.Left
                        previous.LeftOverlay
                        previous.RightOverlay.Active

                struct (left, right)
            else
                let left =
                    updateFoot
                        config
                        time
                        root
                        true
                        previous.Right
                        previous.Left
                        previous.LeftOverlay
                        previous.RightOverlay.Active

                let right =
                    updateFoot
                        config
                        time
                        root
                        false
                        left
                        previous.Right
                        previous.RightOverlay
                        previous.LeftOverlay.Active

                struct (left, right)

        let acceleration =
            (MotionInput.vector root.Velocity - MotionInput.vector previous.Root.Velocity)
            / config.TickSeconds

        // Sustained running has a small whole-body forward inclination even once acceleration falls to zero.
        let travelLean = horizontal * (config.MaxLean / (speed + config.RunFlightSpeed))

        let desired =
            Vector3(
                Math.Clamp(acceleration.X * 0.012f + travelLean.X, -config.MaxLean, config.MaxLean),
                0.0f,
                Math.Clamp(acceleration.Z * 0.012f + travelLean.Z, -config.MaxLean, config.MaxLean)
            )

        let decay = MathF.Exp(-config.TickSeconds / config.LeanSeconds)
        let desiredGaze = gazeTarget config root
        let gazeStep = config.GazeRate * config.TickSeconds

        let gaze =
            Vector2(
                previous.Gaze.X
                + Math.Clamp(desiredGaze.X - previous.Gaze.X, -gazeStep, gazeStep),
                previous.Gaze.Y
                + Math.Clamp(desiredGaze.Y - previous.Gaze.Y, -gazeStep, gazeStep)
            )

        { previous with
            Root = root
            Tick = tick
            Left = left
            Right = right
            Acceleration = acceleration
            Lean = desired + (previous.Lean - desired) * decay
            Gaze = gaze }
