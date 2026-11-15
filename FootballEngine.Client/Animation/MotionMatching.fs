namespace FootballEngine.Client.Animation

open System
open System.Collections.Generic
open System.Numerics
open FootballEngine.Core.Match

/// Presentation of retargeted capture on top of Core roots. Kept apart from AnimationConfig so the support planner's
/// identity hash, which keys motion checkpoints, does not change when only the mocap layer is retuned.
type MatchingConfig =
    {
        SearchIntervalTicks: int
        AnchorIntervalTicks: int
        CrossfadeTicks: int
        /// Normalised cost a candidate must save over continuing before the pose jumps to it.
        SwitchMargin: float32
        InertialHalfLife: float32
        LockReleaseFrames: float32
        LockEngageFrames: float32
        /// Separation between anchor and captured sole at which a stance starts yielding, and is fully released.
        LockSlipStart: float32
        LockSlipEnd: float32
        MinPlaybackRate: float32
        MaxPlaybackRate: float32
        TrajectoryWeight: float32
        FacingWeight: float32
        FootPositionWeight: float32
        FootVelocityWeight: float32
        PelvisVelocityWeight: float32
        /// Weight of Core's stride phase against the capture's, so the foot Core touches the ball with is the one
        /// swinging in the pose.
        PhaseWeight: float32
        /// Seconds over which playback rate closes the gap between the capture's stride phase and Core's.
        PhaseLockSeconds: float32
        /// Root speed below which Core's stride clock holds, so phase says nothing about the legs.
        CyclingSpeed: float32
        /// Playback rate change allowed per second; a step change of rate is a step change of every joint's velocity.
        RateSlewPerSecond: float32
        /// Root speed from which playback follows Core's cadence instead of its speed.
        RunningSpeed: float32
        MinimumJumpFrames: int
        /// Capture a candidate must still have ahead of it, so continuing never runs out before the next search.
        HeadroomFrames: int
        OverlayBlendSeconds: float32
        /// Distance from full leg extension at which the reach starts easing toward the limit.
        SoftReach: float32
        /// The same easing for a planted foot. It never reaches zero: an anchor at the edge of reach would otherwise
        /// snap the knee straight as the lock engages, yet the most it can cost the anchor is ~37 % of this.
        LockedSoftReach: float32
        /// Families the locomotion search may enter; kicks and reaches are action clips, not gait.
        Families: string[]
    }

[<Struct>]
type MatchedPose =
    { Clip: int
      Frame: float32
      PlaybackRate: float32
      LeftLock: float32
      RightLock: float32
      Switched: bool
      LeftResidual: MotionResidual voption
      RightResidual: MotionResidual voption }

module MatchingConfig =
    // 128-tick motion checkpoints must start a fresh search chain, or a recovered window disagrees with live play.
    let private checkpointTicks = 128

    let defaultConfig =
        { SearchIntervalTicks = 4
          AnchorIntervalTicks = 64
          CrossfadeTicks = 16
          SwitchMargin = 0.15f
          // ~0.1 s hides a switch inside one footfall without smearing the next one.
          InertialHalfLife = 0.1f
          LockReleaseFrames = 6.0f
          // Short enough that a sprint stance (~8 capture frames) still spends most of its time fully planted.
          LockEngageFrames = 3.0f
          // Holding a foot the capture has carried a hand-width away stretches the leg past its reach; yielding
          // there is a visible slide of millimetres per frame instead of a knee locked straight.
          LockSlipStart = 0.05f
          LockSlipEnd = 0.12f
          // Beyond ±25 % a time-warped stride stops reading as the captured gait.
          MinPlaybackRate = 0.8f
          MaxPlaybackRate = 1.25f
          TrajectoryWeight = 1.0f
          FacingWeight = 1.5f
          FootPositionWeight = 0.75f
          FootVelocityWeight = 1.0f
          PelvisVelocityWeight = 1.0f
          PhaseWeight = 2.0f
          // About one step: shorter would visibly speed the legs up and down, longer lets touches drift a step.
          PhaseLockSeconds = 0.3f
          // Core's GaitConfig.StandingSpeed; the viewer reads the same clock.
          CyclingSpeed = 0.3f
          // ±0.2 per search step: enough to follow a sprint start within half a second, too little to read as a jolt.
          RateSlewPerSecond = 2.0f
          // Walk-run transition of human gait.
          RunningSpeed = 2.5f
          MinimumJumpFrames = 20
          // Two search steps at the fastest playback rate (2 × 4 ticks × 1.5 frames × 1.25).
          HeadroomFrames = 16
          OverlayBlendSeconds = 0.12f
          SoftReach = 0.04f
          LockedSoftReach = 0.012f
          Families = [| "locomotion"; "walking"; "footwork" |] }

    let validate (config: MatchingConfig) =
        let positive values =
            values |> Array.forall (fun value -> Single.IsFinite value && value > 0.0f)

        if
            config.SearchIntervalTicks < 1
            || config.AnchorIntervalTicks % config.SearchIntervalTicks <> 0
            || checkpointTicks % config.AnchorIntervalTicks <> 0
            || config.CrossfadeTicks % config.SearchIntervalTicks <> 0
            || config.CrossfadeTicks < config.SearchIntervalTicks
            || config.CrossfadeTicks > config.AnchorIntervalTicks
        then
            Error "matching intervals must nest: search divides crossfade and anchor, anchor divides the checkpoint"
        elif
            not (
                positive
                    [| config.SwitchMargin
                       config.InertialHalfLife
                       config.LockReleaseFrames
                       config.LockEngageFrames
                       config.LockSlipStart
                       config.MinPlaybackRate
                       config.MaxPlaybackRate
                       config.OverlayBlendSeconds
                       config.SoftReach
                       config.LockedSoftReach
                       config.PhaseLockSeconds
                       config.RateSlewPerSecond
                       config.RunningSpeed |]
            )
            || config.LockedSoftReach > config.SoftReach
            || config.MinPlaybackRate > 1.0f
            || config.MaxPlaybackRate < 1.0f
        then
            Error "matching timings and playback limits must be positive, with 1.0 inside the playback range"
        elif
            not (
                [| config.TrajectoryWeight
                   config.FacingWeight
                   config.FootPositionWeight
                   config.FootVelocityWeight
                   config.PelvisVelocityWeight
                   config.PhaseWeight
                   config.CyclingSpeed |]
                |> Array.forall (fun value -> Single.IsFinite value && value >= 0.0f)
            )
            || config.MinimumJumpFrames < 0
            || config.HeadroomFrames < 1
            || config.LockSlipEnd <= config.LockSlipStart
            || config.Families.Length = 0
        then
            Error "matching weights must be finite and non-negative, with at least one family"
        else
            Ok config

/// A stance holds the foot at a world anchor. When it ends, or a switch replaces the clip under it, the pull it
/// exerted at that instant is kept as a local offset that fades, so the foot never chases an anchor it has left.
[<Struct>]
type private FootLock =
    {
        Active: bool
        Start: int
        End: int
        World: Vector3
        /// A new stance eases onto its anchor over the engage window: capture labels contact with the sole still
        /// a little above the grass, and the anchor sits on it. Only an anchor carried across a switch skips this.
        RampIn: bool
        EndOffset: Vector3
        /// Lock weights also soften the leg solve, so each fading offset keeps the weight it had when it was taken:
        /// a stance cut short before it fully engaged must not release from full strength.
        EndWeight: float32
        HasEndOffset: bool
        /// The stance this lock replaced, still releasing its end offset.
        ReleaseEnd: int
        ReleaseOffset: Vector3
        ReleaseWeight: float32
        /// Pull carried across a switch; clip frames reset there, so it fades with time instead.
        SwitchOffset: Vector3
        SwitchWeight: float32
        SwitchTick: float32
    }

type private Chain(start: int, joints: int) =
    member val Start = start
    member val Tick = start with get, set
    member val Frame = 0.0f with get, set
    member val Rate = 1.0f with get, set
    member val SwitchTick = start with get, set
    member val Switched = false with get, set
    member val OffsetX = Array.zeroCreate<Vector3> joints
    member val OffsetV = Array.zeroCreate<Vector3> joints
    member val PelvisX = Vector3.Zero with get, set
    member val PelvisV = Vector3.Zero with get, set
    member val Locks = Array.zeroCreate<FootLock> 2

[<Struct>]
type private RootState =
    { Position: Vector3
      Yaw: float32
      Velocity: Vector3 }

module private Rotation =
    let log (q: Quaternion) =
        let q = if q.W < 0.0f then Quaternion.Negate q else q
        let s = Vector3(q.X, q.Y, q.Z)
        let length = s.Length()

        if length < 1e-6f then
            s * 2.0f
        else
            s * (2.0f * MathF.Atan2(length, q.W) / length)

    let exp (v: Vector3) =
        let angle = v.Length()

        if angle < 1e-6f then
            Quaternion.Normalize(Quaternion(v * 0.5f, 1.0f))
        else
            Quaternion.CreateFromAxisAngle(v / angle, angle)

    let wrap (angle: float32) =
        let mutable a = angle

        while a > MathF.PI do
            a <- a - 2.0f * MathF.PI

        while a < -MathF.PI do
            a <- a + 2.0f * MathF.PI

        a

/// Feature rows for every frame plus the subset a search may land on. Values are normalised per feature group and
/// weighted, so a squared distance is directly the matching cost.
type MotionFeatures =
    {
        Dimensions: int
        Values: float32[]
        Mean: float32[]
        Scale: float32[]
        Candidates: int[]
        ChunkMin: float32[]
        ChunkMax: float32[]
        /// Bounds over `groupSize` consecutive chunks, so most of the database is rejected a group at a time.
        GroupMin: float32[]
        GroupMax: float32[]
        ClipOf: int[]
        Speed: float32[]
        /// Capture stride phase on Core's convention (left strike 0, right strike 0.5), NaN where the clip does not
        /// stride, and its rate in cycles per capture frame.
        Phase: float32[]
        PhaseRate: float32[]
    }

module MotionFeatures =
    let dimensions = 29
    /// Future samples in database frames (60 Hz): one, two and three thirds of a second ahead.
    let futureFrames = [| 20; 40; 60 |]
    let chunkSize = 16
    let groupSize = 16

    let trajectoryOnly =
        Array.init dimensions (fun d -> if d < 12 || d >= 24 then 1.0f else 0.0f)

    let full = Array.create dimensions 1.0f

    /// Standing, Core's stride clock holds and must not pull the pose toward any phase.
    let trajectoryOnlyStill =
        Array.init dimensions (fun d -> if d < 12 || (d >= 24 && d < 27) then 1.0f else 0.0f)

    let fullStill = Array.init dimensions (fun d -> if d < 27 then 1.0f else 0.0f)

    /// Phase from the contact labels: a left strike is 0 and a right strike 0.5, linear in between. Gaps longer
    /// than a slow walking step are pauses, not strides.
    let internal stridePhase (db: MotionDatabase) (clip: MotionClip) (phase: float32[]) (rate: float32[]) =
        let last = clip.Start + clip.Count - 1
        let longest = db.RateHz
        let strikes = ResizeArray<struct (int * float32)>()

        for frame = clip.Start + 1 to last do
            let now, before = db.Contacts[frame], db.Contacts[frame - 1]

            if now &&& 1uy <> 0uy && before &&& 1uy = 0uy then
                strikes.Add(struct (frame, 0.0f))

            if now &&& 2uy <> 0uy && before &&& 2uy = 0uy then
                strikes.Add(struct (frame, 0.5f))

        for frame = clip.Start to last do
            phase[frame] <- Single.NaN
            rate[frame] <- 0.0f

        let span i =
            let struct (fa, a) = strikes[i]
            let struct (fb, b) = strikes[i + 1]
            let delta = b - a - MathF.Floor(b - a)
            // Two strikes of the same foot in a row: a whole cycle with the other strike unlabelled.
            let delta = if delta = 0.0f then 1.0f else delta
            struct (fa, a, fb, delta)

        for i = 0 to strikes.Count - 2 do
            let struct (fa, a, fb, delta) = span i

            if fb - fa <= longest then
                // The first and last spans extend to the clip edges at their own cadence.
                let first = if i = 0 then clip.Start else fa
                let past = if i = strikes.Count - 2 then last else fb - 1
                let perFrame = delta / float32 (fb - fa)

                for frame = first to past do
                    let value = a + perFrame * float32 (frame - fa)
                    phase[frame] <- value - MathF.Floor value
                    rate[frame] <- perFrame

    let internal drivenNodes (db: MotionDatabase) (rig: RigDefinition) =
        db.DrivenJoints |> Array.map (fun name -> Array.IndexOf(rig.Names, name))

    /// Joint-local pose of a database frame at the given stature; undriven joints keep their bind.
    let internal framePose
        (db: MotionDatabase)
        (rig: RigDefinition)
        (nodes: int[])
        stature
        frame
        (local: RigLocalPose[])
        =
        for node = 0 to rig.Bind.Length - 1 do
            local[node] <-
                { rig.Bind[node] with
                    Translation = rig.Bind[node].Translation * stature }

        for joint = 0 to nodes.Length - 1 do
            local[nodes[joint]] <-
                { local[nodes[joint]] with
                    Rotation = db.Rotation(frame, joint) }

        local[rig.Pelvis] <-
            { local[rig.Pelvis] with
                Translation = db.Pelvis[frame] * stature }

    let internal forward (rig: RigDefinition) (local: RigLocalPose[]) (world: Matrix4x4[]) =
        for node = 0 to rig.Bind.Length - 1 do
            let pose = local[node]

            let matrix =
                Matrix4x4.CreateFromQuaternion(pose.Rotation)
                * Matrix4x4.CreateTranslation(pose.Translation)

            world[node] <-
                if rig.Parents[node] < 0 then
                    matrix
                else
                    matrix * world[rig.Parents[node]]

    let internal sole (world: Matrix4x4[]) (leg: RigLimb) stature =
        world[leg.End].Translation
        + Vector3.TransformNormal(leg.Socket * stature, world[leg.End])

    let internal toWorld (groundX: float32) (groundZ: float32) (yaw: float32) (local: Vector3) =
        Vector3.Transform(local, Matrix4x4.CreateRotationY yaw)
        + Vector3(groundX, 0.0f, groundZ)

    let internal toLocal (yaw: float32) (delta: Vector3) =
        Vector3.Transform(delta, Matrix4x4.CreateRotationY(-yaw))

    let private validNodes (db: MotionDatabase) (rig: RigDefinition) =
        let nodes = drivenNodes db rig

        if nodes |> Array.exists (fun node -> node < 0) then
            Error "motion database drives joints the rig does not have"
        elif Array.IndexOf(db.DrivenJoints, rig.Names[rig.Pelvis]) < 0 then
            Error "motion database does not drive the pelvis"
        else
            // The pelvis offset is stored relative to the ground point, so every ancestor must be an identity bind.
            let mutable ancestor = rig.Parents[rig.Pelvis]
            let mutable identity = true

            while ancestor >= 0 do
                let bind = rig.Bind[ancestor]

                if
                    bind.Translation.LengthSquared() > 1e-8f
                    || abs (abs bind.Rotation.W - 1.0f) > 1e-5f
                then
                    identity <- false

                ancestor <- rig.Parents[ancestor]

            if identity then
                Ok nodes
            else
                Error "pelvis ancestors are not identity binds"

    let build (db: MotionDatabase) (rig: RigDefinition) (config: MatchingConfig) =
        validNodes db rig
        |> Result.map (fun nodes ->
            let d = dimensions
            let count = db.FrameCount
            let raw = Array.zeroCreate<float32> (count * d)
            let clipOf = Array.zeroCreate<int> count
            let speed = Array.zeroCreate<float32> count
            let local = Array.zeroCreate<RigLocalPose> rig.Bind.Length
            let world = Array.create rig.Bind.Length Matrix4x4.Identity
            let soles = Array.zeroCreate<Vector3> (count * 2)
            let pelvis = Array.zeroCreate<Vector3> count
            let phase = Array.zeroCreate<float32> count
            let phaseRate = Array.zeroCreate<float32> count
            let dt = 1.0f / float32 db.RateHz

            for clip in db.Clips do
                stridePhase db clip phase phaseRate

            for clipIndex = 0 to db.Clips.Length - 1 do
                let clip = db.Clips[clipIndex]

                for frame = clip.Start to clip.Start + clip.Count - 1 do
                    clipOf[frame] <- clipIndex
                    framePose db rig nodes 1.0f frame local
                    forward rig local world
                    let x, z, yaw = db.GroundX[frame], db.GroundZ[frame], db.Yaw[frame]
                    soles[frame * 2] <- toWorld x z yaw (sole world rig.LeftLeg 1.0f)
                    soles[frame * 2 + 1] <- toWorld x z yaw (sole world rig.RightLeg 1.0f)
                    pelvis[frame] <- toWorld x z yaw db.Pelvis[frame]

            for clip in db.Clips do
                let last = clip.Start + clip.Count - 1

                for frame = clip.Start to last do
                    let row = frame * d
                    let x, z, yaw = db.GroundX[frame], db.GroundZ[frame], db.Yaw[frame]

                    // Running captures are single strides of ~1 s, shorter than the trajectory horizon; past the clip the
                    // last frame's velocity and turn rate continue, so its whole length stays searchable.
                    let before = max clip.Start (last - 1)
                    let endVelocityX = db.GroundX[last] - db.GroundX[before]
                    let endVelocityZ = db.GroundZ[last] - db.GroundZ[before]
                    let endTurn = Rotation.wrap (db.Yaw[last] - db.Yaw[before])

                    for k = 0 to futureFrames.Length - 1 do
                        let ahead = frame + futureFrames[k]
                        let future = min last ahead
                        let beyond = float32 (ahead - future)
                        let futureX = db.GroundX[future] + endVelocityX * beyond
                        let futureZ = db.GroundZ[future] + endVelocityZ * beyond
                        let futureYaw = db.Yaw[future] + Math.Clamp(endTurn * beyond, -1.0f, 1.0f)
                        let offset = toLocal yaw (Vector3(futureX - x, 0.0f, futureZ - z))
                        let turn = Rotation.wrap (futureYaw - yaw)
                        raw[row + k * 4] <- offset.X
                        raw[row + k * 4 + 1] <- offset.Z
                        raw[row + k * 4 + 2] <- MathF.Sin turn
                        raw[row + k * 4 + 3] <- MathF.Cos turn

                    // The first frame of a clip has no past; its forward difference stands in.
                    let previous, next =
                        (if frame > clip.Start then
                             frame - 1, frame
                         else
                             frame, min last (frame + 1))

                    for foot = 0 to 1 do
                        let position = toLocal yaw (soles[frame * 2 + foot] - Vector3(x, 0.0f, z))

                        let velocity =
                            toLocal yaw ((soles[next * 2 + foot] - soles[previous * 2 + foot]) / dt)

                        raw[row + 12 + foot * 3] <- position.X
                        raw[row + 13 + foot * 3] <- position.Y
                        raw[row + 14 + foot * 3] <- position.Z
                        raw[row + 18 + foot * 3] <- velocity.X
                        raw[row + 19 + foot * 3] <- velocity.Y
                        raw[row + 20 + foot * 3] <- velocity.Z

                    let pelvisVelocity = toLocal yaw ((pelvis[next] - pelvis[previous]) / dt)
                    raw[row + 24] <- pelvisVelocity.X
                    raw[row + 25] <- pelvisVelocity.Y
                    raw[row + 26] <- pelvisVelocity.Z

                    // A frame that does not stride sits at the circle's centre: equally far from every Core phase.
                    if not (Single.IsNaN phase[frame]) then
                        let angle = 2.0f * MathF.PI * phase[frame]
                        raw[row + 27] <- MathF.Sin angle
                        raw[row + 28] <- MathF.Cos angle

                    let gx = db.GroundX[next] - db.GroundX[previous]
                    let gz = db.GroundZ[next] - db.GroundZ[previous]
                    speed[frame] <- sqrt (gx * gx + gz * gz) / dt

            let families = Set.ofArray config.Families

            let candidates =
                [| for clip in db.Clips do
                       if families.Contains clip.Family then
                           // One past frame for velocity and enough capture ahead to play until the next search.
                           for frame = clip.Start + 1 to clip.Start + clip.Count - 1 - config.HeadroomFrames do
                               frame |]

            let groups =
                [| [| 0; 1; 4; 5; 8; 9 |], config.TrajectoryWeight
                   [| 2; 3; 6; 7; 10; 11 |], config.FacingWeight
                   [| 12..17 |], config.FootPositionWeight
                   [| 18..23 |], config.FootVelocityWeight
                   [| 24..26 |], config.PelvisVelocityWeight
                   [| 27; 28 |], config.PhaseWeight |]

            let mean = Array.zeroCreate<float32> d
            let scale = Array.zeroCreate<float32> d
            let n = float32 (max 1 candidates.Length)

            for dims, weight in groups do
                let mutable variance = 0.0f

                for dim in dims do
                    let mutable sum = 0.0f

                    for frame in candidates do
                        sum <- sum + raw[frame * d + dim]

                    mean[dim] <- sum / n
                    let mutable squares = 0.0f

                    for frame in candidates do
                        let delta = raw[frame * d + dim] - mean[dim]
                        squares <- squares + delta * delta

                    variance <- variance + squares / n

                // A group shares one scale so its dimensions keep their relative geometry.
                let deviation = max 1e-3f (sqrt (variance / float32 dims.Length))

                for dim in dims do
                    scale[dim] <- weight / deviation

            let values = Array.zeroCreate<float32> (count * d)

            for frame = 0 to count - 1 do
                for dim = 0 to d - 1 do
                    values[frame * d + dim] <- (raw[frame * d + dim] - mean[dim]) * scale[dim]

            let chunks = (candidates.Length + chunkSize - 1) / chunkSize
            let chunkMin = Array.create (chunks * d) Single.MaxValue
            let chunkMax = Array.create (chunks * d) Single.MinValue

            for index = 0 to candidates.Length - 1 do
                let chunk = index / chunkSize

                for dim = 0 to d - 1 do
                    let value = values[candidates[index] * d + dim]
                    chunkMin[chunk * d + dim] <- min chunkMin[chunk * d + dim] value
                    chunkMax[chunk * d + dim] <- max chunkMax[chunk * d + dim] value

            let groups = (chunks + groupSize - 1) / groupSize
            let groupMin = Array.create (groups * d) Single.MaxValue
            let groupMax = Array.create (groups * d) Single.MinValue

            for chunk = 0 to chunks - 1 do
                let group = chunk / groupSize

                for dim = 0 to d - 1 do
                    groupMin[group * d + dim] <- min groupMin[group * d + dim] chunkMin[chunk * d + dim]
                    groupMax[group * d + dim] <- max groupMax[group * d + dim] chunkMax[chunk * d + dim]

            { Dimensions = d
              Values = values
              Mean = mean
              Scale = scale
              Candidates = candidates
              ChunkMin = chunkMin
              ChunkMax = chunkMax
              GroupMin = groupMin
              GroupMax = groupMax
              ClipOf = clipOf
              Speed = speed
              Phase = phase
              PhaseRate = phaseRate })

    let private bound (lower: float32[]) (upper: float32[]) (block: int) d (query: float32[]) (mask: float32[]) limit =
        let mutable total = 0.0f
        let mutable dim = 0

        while dim < d && total < limit do
            let q = query[dim]
            let lo = lower[block * d + dim]
            let hi = upper[block * d + dim]

            let gap =
                if q < lo then lo - q
                elif q > hi then q - hi
                else 0.0f

            total <- total + mask[dim] * gap * gap
            dim <- dim + 1

        total

    /// Exact nearest candidate under the masked cost among those cheaper than `limit` (-1 when none is); group and
    /// chunk bounds only skip work that cannot win, so the answer equals a full scan's.
    let search (features: MotionFeatures) (query: float32[]) (mask: float32[]) (limit: float32) =
        let d = features.Dimensions
        let mutable best = -1
        let mutable bestCost = limit
        let chunks = features.ChunkMin.Length / d
        let groups = features.GroupMin.Length / d

        for group = 0 to groups - 1 do
            if bound features.GroupMin features.GroupMax group d query mask bestCost < bestCost then
                for chunk = group * groupSize to min chunks ((group + 1) * groupSize) - 1 do
                    if bound features.ChunkMin features.ChunkMax chunk d query mask bestCost < bestCost then
                        let last = min features.Candidates.Length ((chunk + 1) * chunkSize) - 1

                        for index = chunk * chunkSize to last do
                            let row = features.Candidates[index] * d
                            let mutable cost = 0.0f
                            let mutable dim = 0

                            while dim < d && cost < bestCost do
                                let delta = query[dim] - features.Values[row + dim]
                                cost <- cost + mask[dim] * delta * delta
                                dim <- dim + 1

                            if cost < bestCost then
                                bestCost <- cost
                                best <- features.Candidates[index]

        struct (best, bestCost)

    let cost (features: MotionFeatures) (query: float32[]) (mask: float32[]) frame =
        let row = frame * features.Dimensions
        let mutable cost = 0.0f

        for dim = 0 to features.Dimensions - 1 do
            let delta = query[dim] - features.Values[row + dim]
            cost <- cost + mask[dim] * delta * delta

        cost

/// Motion matching evaluated as a pure function of the Core roots: a search chain restarts at every anchor tick and
/// advances on a fixed grid, so a cold evaluation (seek, recovered window) reproduces the palette of live playback.
/// Consecutive chains are crossfaded before the next anchor takes over, which removes the restart pop.
type MotionMatcher
    (
        db: MotionDatabase,
        rig: RigDefinition,
        animation: AnimationConfig,
        config: MatchingConfig,
        features: MotionFeatures
    ) =
    let nodes = MotionFeatures.drivenNodes db rig
    let joints = nodes.Length
    let framesPerTick = float32 db.RateHz * animation.TickSeconds
    let damping = 2.0f * MathF.Log 2.0f / config.InertialHalfLife
    let query = Array.zeroCreate<float32> MotionFeatures.dimensions
    let mainLocal = Array.zeroCreate<RigLocalPose> rig.Bind.Length
    let nextLocal = Array.zeroCreate<RigLocalPose> rig.Bind.Length
    let lockLocal = Array.zeroCreate<RigLocalPose> rig.Bind.Length
    let lockWorld = Array.create rig.Bind.Length Matrix4x4.Identity
    let caches = Dictionary<PlayerId, struct (int * Dictionary<int, Chain>)>()
    let stale = List<int>()
    let mutable lastStay = 0.0f
    let mutable lastBest = 0.0f
    let mutable lastForced = false
    let mutable searches = 0L

    let template =
        SupportPlanner.initialize
            animation
            0
            { Actor = PlayerId 0
              Generation = 0
              Position = Vector3.Zero
              Velocity = Vector3.Zero
              Facing = 0.0f
              AngularVelocity = 0.0f
              Condition = 1.0f
              GaitPhase = 0.0f
              AttentionTarget = ValueNone
              Morphology = PhysicalMorphology.standard
              Active = false }

    let clipLast frame =
        let clip = db.Clips[features.ClipOf[frame]]
        clip.Start + clip.Count - 1

    let tryPlan (source: IMotionPlanSource) actor tick (plan: byref<ActorPlan>) =
        tick >= 0 && source.TryCopyPlan(actor, { Tick = tick; Fraction = 0.0f }, &plan)

    let rootOf (plan: ActorPlan) =
        { Position = MotionInput.vector plan.Root.Position
          Yaw = MotionInput.facing plan.Root.Facing
          Velocity = MotionInput.vector plan.Root.Velocity }

    /// Core's stride phase at a tick; past the frontier the latest plan's phase stands.
    let phaseAt (source: IMotionPlanSource) actor (tick: int) =
        let mutable plan = template

        if tryPlan source actor tick &plan then
            plan.Root.GaitPhase
        else
            0.0f

    /// Root at a fractional tick. Past the published frontier the source returns its latest plan, which is carried
    /// forward at its own velocity instead of freezing the trajectory.
    let rootAt (source: IMotionPlanSource) actor (time: float32) =
        let tick = int (floor time)
        let mutable plan = template

        if not (tryPlan source actor tick &plan) then
            ValueNone
        else
            let first = rootOf plan

            if plan.Tick < tick then
                let seconds = (time - float32 plan.Tick) * animation.TickSeconds
                let turn = Math.Clamp(plan.Root.AngularVelocity * seconds, -1.0f, 1.0f)

                ValueSome
                    { first with
                        Position = first.Position + first.Velocity * seconds
                        Yaw = first.Yaw + turn }
            else
                let fraction = time - float32 tick
                let mutable next = plan

                if fraction > 0.0f && tryPlan source actor (tick + 1) &next && next.Tick = tick + 1 then
                    let second = rootOf next

                    ValueSome
                        { Position = Vector3.Lerp(first.Position, second.Position, fraction)
                          Yaw = first.Yaw + Rotation.wrap (second.Yaw - first.Yaw) * fraction
                          Velocity = Vector3.Lerp(first.Velocity, second.Velocity, fraction) }
                else
                    ValueSome
                        { first with
                            Position = first.Position + first.Velocity * (fraction * animation.TickSeconds) }

    let decay (x0: Vector3) (v0: Vector3) seconds =
        let e = MathF.Exp(-damping * seconds)
        let j = v0 + x0 * damping
        struct (e * (x0 + j * seconds), e * (v0 - j * (damping * seconds)))

    let frameAt (chain: Chain) (time: float32) =
        let frame = chain.Frame + framesPerTick * chain.Rate * (time - float32 chain.Tick)
        min frame (float32 (clipLast (int chain.Frame)))

    let clipFirst frame = db.Clips[features.ClipOf[frame]].Start

    let catmullRom (p0: Vector3) (p1: Vector3) (p2: Vector3) (p3: Vector3) (t: float32) =
        let t2 = t * t
        let t3 = t2 * t

        0.5f
        * (2.0f * p1
           + (p2 - p0) * t
           + (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2
           + (3.0f * p1 - p0 - 3.0f * p2 + p3) * t3)

    let catmullRom4 (p0: Vector4) (p1: Vector4) (p2: Vector4) (p3: Vector4) (t: float32) =
        let t2 = t * t
        let t3 = t2 * t

        0.5f
        * (2.0f * p1
           + (p2 - p0) * t
           + (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2
           + (3.0f * p1 - p0 - 3.0f * p2 + p3) * t3)

    let slope4 (p0: Vector4) (p1: Vector4) (p2: Vector4) (p3: Vector4) (t: float32) =
        0.5f
        * ((p2 - p0)
           + 2.0f * (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t
           + 3.0f * (3.0f * p1 - p0 - 3.0f * p2 + p3) * t * t)

    let toVector (q: Quaternion) = Vector4(q.X, q.Y, q.Z, q.W)

    /// Control points of the spline through capture frames around `frame`, on the hemisphere of the earlier frame.
    let controls (frame: float32) joint =
        let f1 = int frame
        let last = clipLast f1
        let f2 = min (f1 + 1) last
        let f0 = max (clipFirst f1) (f1 - 1)
        let f3 = min last (f2 + 1)
        let p1 = toVector (db.Rotation(f1, joint))

        let aligned f =
            let p = toVector (db.Rotation(f, joint))
            if Vector4.Dot(p, p1) < 0.0f then -p else p

        struct (aligned f0, p1, aligned f2, aligned f3, frame - float32 f1)

    /// Rotations between capture frames follow a normalised Catmull-Rom spline through the frame quaternions.
    /// Piecewise slerp has a velocity jump at every frame, which fingertips and feet show as a flick when playback
    /// runs faster than capture or the display faster than 60 Hz; capture steps are small enough that the
    /// component-wise spline stays within a fraction of a degree of the tangent-space one at a fraction of its cost.
    let sampleRotation (frame: float32) joint =
        let struct (p0, p1, p2, p3, t) = controls frame joint
        let p = Vector4.Normalize(catmullRom4 p0 p1 p2 p3 t)
        Quaternion(p.X, p.Y, p.Z, p.W)

    let catmullRomSlope (p0: Vector3) (p1: Vector3) (p2: Vector3) (p3: Vector3) (t: float32) =
        0.5f
        * ((p2 - p0)
           + 2.0f * (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t
           + 3.0f * (3.0f * p1 - p0 - 3.0f * p2 + p3) * t * t)

    let samplePelvis (frame: float32) =
        let f1 = int frame
        let last = clipLast f1
        let f2 = min (f1 + 1) last
        let f0 = max (clipFirst f1) (f1 - 1)
        let f3 = min last (f2 + 1)
        catmullRom db.Pelvis[f0] db.Pelvis[f1] db.Pelvis[f2] db.Pelvis[f3] (frame - float32 f1)

    /// Parent-space angular velocity of the sampled spline, so a switch hands inertialization the velocity the
    /// viewer actually saw rather than a frame difference the spline never followed.
    let angularVelocity (frame: float32) rate joint =
        let struct (p0, p1, p2, p3, t) = controls frame joint
        let raw = catmullRom4 p0 p1 p2 p3 t
        let length = raw.Length()
        let q = raw / length
        let d = slope4 p0 p1 p2 p3 t
        let dq = (d - q * Vector4.Dot(q, d)) / length
        // ω = 2 q̇ q* in the parent frame, matching the left-multiplied inertialization offset.
        let w =
            Quaternion(dq.X, dq.Y, dq.Z, dq.W)
            * Quaternion.Conjugate(Quaternion(q.X, q.Y, q.Z, q.W))

        Vector3(w.X, w.Y, w.Z) * (2.0f * float32 db.RateHz * rate)

    let pelvisVelocity (frame: float32) rate =
        let f1 = int frame
        let last = clipLast f1
        let f2 = min (f1 + 1) last
        let f0 = max (clipFirst f1) (f1 - 1)
        let f3 = min last (f2 + 1)

        catmullRomSlope db.Pelvis[f0] db.Pelvis[f1] db.Pelvis[f2] db.Pelvis[f3] (frame - float32 f1)
        * (float32 db.RateHz * rate)

    /// Output pose of a chain: database frame with the decaying inertialization offset layered in parent space.
    let poseInto (chain: Chain) (frame: float32) (time: float32) stature (local: RigLocalPose[]) =
        for node = 0 to rig.Bind.Length - 1 do
            local[node] <-
                { rig.Bind[node] with
                    Translation = rig.Bind[node].Translation * stature }

        let seconds = max 0.0f ((time - float32 chain.SwitchTick) * animation.TickSeconds)

        for joint = 0 to joints - 1 do
            let struct (offset, _) = decay chain.OffsetX[joint] chain.OffsetV[joint] seconds

            local[nodes[joint]] <-
                { local[nodes[joint]] with
                    Rotation = Quaternion.Normalize(Rotation.exp offset * sampleRotation frame joint) }

        let struct (pelvisOffset, _) = decay chain.PelvisX chain.PelvisV seconds

        local[rig.Pelvis] <-
            { local[rig.Pelvis] with
                Translation = (samplePelvis frame + pelvisOffset) * stature }

    let contact frame foot =
        db.Contacts[frame] &&& (1uy <<< foot) <> 0uy

    let stanceEnd frame foot =
        let last = clipLast frame
        let mutable cursor = frame

        while cursor <= last && contact cursor foot do
            cursor <- cursor + 1

        cursor

    let leg foot =
        if foot = 0 then rig.LeftLeg else rig.RightLeg

    /// World sole of the chain's output at a stance start, placed on the grass under the root of that instant.
    let localSole (chain: Chain) (frame: float32) (time: float32) foot stature =
        let frame = min frame (float32 (clipLast (int chain.Frame)))
        poseInto chain frame time stature lockLocal
        MotionFeatures.forward rig lockLocal lockWorld
        MotionFeatures.sole lockWorld (leg foot) stature

    let anchorAt source actor (chain: Chain) (frame: float32) (time: float32) foot stature =
        match rootAt source actor time with
        | ValueNone -> ValueNone
        | ValueSome root ->
            let world =
                MotionFeatures.toWorld
                    root.Position.X
                    root.Position.Z
                    root.Yaw
                    (localSole chain frame time foot stature)

            ValueSome(Vector3(world.X, 0.0f, world.Z))

    let poseQuery (frame: float32) =
        let row = int (MathF.Round frame) * features.Dimensions

        for dim = 12 to 23 do
            query[dim] <- features.Values[row + dim]

    let trajectoryQuery source actor (tick: int) =
        match rootAt source actor (float32 tick) with
        | ValueNone -> false
        | ValueSome now ->
            let mutable complete = true

            for k = 0 to MotionFeatures.futureFrames.Length - 1 do
                let ahead = float32 MotionFeatures.futureFrames[k] / framesPerTick

                match rootAt source actor (float32 tick + ahead) with
                | ValueSome future ->
                    let offset =
                        MotionFeatures.toLocal
                            now.Yaw
                            (Vector3(future.Position.X - now.Position.X, 0.0f, future.Position.Z - now.Position.Z))

                    let turn = Rotation.wrap (future.Yaw - now.Yaw)

                    let put i value =
                        let dim = k * 4 + i
                        query[dim] <- (value - features.Mean[dim]) * features.Scale[dim]

                    put 0 offset.X
                    put 1 offset.Z
                    put 2 (MathF.Sin turn)
                    put 3 (MathF.Cos turn)
                | ValueNone -> complete <- false

            let velocity =
                MotionFeatures.toLocal now.Yaw (Vector3(now.Velocity.X, 0.0f, now.Velocity.Z))

            query[24] <- (velocity.X - features.Mean[24]) * features.Scale[24]
            query[25] <- (0.0f - features.Mean[25]) * features.Scale[25]
            query[26] <- (velocity.Z - features.Mean[26]) * features.Scale[26]
            let angle = 2.0f * MathF.PI * phaseAt source actor tick
            query[27] <- (MathF.Sin angle - features.Mean[27]) * features.Scale[27]
            query[28] <- (MathF.Cos angle - features.Mean[28]) * features.Scale[28]
            complete

    let cycling source actor tick =
        match rootAt source actor (float32 tick) with
        | ValueSome root -> Vector2(root.Velocity.X, root.Velocity.Z).Length() > config.CyclingSpeed
        | ValueNone -> false

    let playbackRate source actor tick frame =
        match rootAt source actor (float32 tick) with
        | ValueSome root ->
            let core = Vector2(root.Velocity.X, root.Velocity.Z).Length()
            let captured = features.Speed[frame]

            // Near standstill the ratio is noise; the stance lock absorbs the remaining slip.
            let speedRate =
                if core < 0.3f || captured < 0.3f then
                    1.0f
                else
                    core / captured

            // While Core's legs cycle, cadence rules: the capture plays at Core's stride rate plus a correction that
            // closes the phase gap, so the foot reaches the ball on the step Core touched it. Stride length then
            // differs from Core's by the speed mismatch, which the stance locks absorb.
            let captureCycles = features.PhaseRate[frame] * float32 db.RateHz

            let rate =
                if
                    core > config.CyclingSpeed
                    && captureCycles > 0.0f
                    && not (Single.IsNaN features.Phase[frame])
                then
                    let now = phaseAt source actor tick
                    let ahead = phaseAt source actor (tick + 1) - now
                    let coreCycles = (ahead - MathF.Floor ahead) / animation.TickSeconds
                    let gap = now - features.Phase[frame]
                    let gap = gap - MathF.Round gap
                    let correction = gap / config.PhaseLockSeconds / captureCycles

                    // Walking strides are long relative to cadence changes, so speed keeps the soles honest there
                    // and phase only nudges; running stance is short enough that cadence can lead.
                    if core >= config.RunningSpeed then
                        coreCycles / captureCycles + correction
                    else
                        speedRate + correction
                else
                    speedRate

            Math.Clamp(rate, config.MinPlaybackRate, config.MaxPlaybackRate)
        | ValueNone -> 1.0f

    let slew current target =
        let limit =
            config.RateSlewPerSecond
            * animation.TickSeconds
            * float32 config.SearchIntervalTicks

        Math.Clamp(target, current - limit, current + limit)

    let release frame finish =
        max 0.0f (1.0f - (frame - float32 finish) / config.LockReleaseFrames)

    let fade (lock: FootLock) (time: float32) =
        max 0.0f (1.0f - (time - lock.SwitchTick) * framesPerTick / config.LockReleaseFrames)

    /// Where a lock puts a foot whose capture sole is `sole`, and how strongly it holds it. `local` maps a world
    /// anchor into the frame the sole is expressed in.
    let pull (lock: FootLock) (frame: float32) (time: float32) (sole: Vector3) (local: Vector3 -> Vector3) stature =
        let mutable target = sole
        let mutable weight = 0.0f

        if lock.Active && frame >= float32 lock.Start && frame < float32 lock.End then
            let anchor = local lock.World

            let engage =
                if lock.RampIn then
                    Math.Clamp((frame - float32 lock.Start) / config.LockEngageFrames, 0.0f, 1.0f)
                else
                    1.0f

            let slip = Vector3.Distance(anchor, sole) / stature

            let hold =
                Math.Clamp((config.LockSlipEnd - slip) / (config.LockSlipEnd - config.LockSlipStart), 0.0f, 1.0f)

            weight <- engage * hold * hold * (3.0f - 2.0f * hold)
            target <- Vector3.Lerp(sole, anchor, weight)
        elif lock.Active && frame >= float32 lock.End && lock.HasEndOffset then
            let fading = release frame lock.End
            weight <- lock.EndWeight * fading
            target <- sole + lock.EndOffset * fading
        elif lock.ReleaseEnd <> Int32.MinValue && frame >= float32 lock.ReleaseEnd then
            let fading = release frame lock.ReleaseEnd
            weight <- lock.ReleaseWeight * fading
            target <- sole + lock.ReleaseOffset * fading

        let switchWeight = fade lock time

        if
            switchWeight > 0.0f
            && (lock.SwitchOffset <> Vector3.Zero || lock.SwitchWeight > 0.0f)
        then
            target <- target + lock.SwitchOffset * switchWeight
            weight <- max weight (lock.SwitchWeight * switchWeight)

        struct (target, weight)

    let idle =
        { Active = false
          Start = 0
          End = 0
          World = Vector3.Zero
          RampIn = false
          EndOffset = Vector3.Zero
          EndWeight = 0.0f
          HasEndOffset = false
          ReleaseEnd = Int32.MinValue
          ReleaseOffset = Vector3.Zero
          ReleaseWeight = 0.0f
          SwitchOffset = Vector3.Zero
          SwitchWeight = 0.0f
          SwitchTick = 0.0f }

    let replace (current: FootLock) start finish world =
        { current with
            Active = true
            Start = start
            End = finish
            World = world
            RampIn = true
            EndOffset = Vector3.Zero
            EndWeight = 0.0f
            HasEndOffset = false
            ReleaseEnd =
                (if current.Active && current.HasEndOffset then
                     current.End
                 else
                     Int32.MinValue)
            ReleaseOffset = current.EndOffset
            ReleaseWeight = current.EndWeight }

    let toLocalAt source actor time =
        match rootAt source actor time with
        | ValueSome root -> ValueSome(fun (world: Vector3) -> MotionInput.localPoint root.Position root.Yaw world)
        | ValueNone -> ValueNone

    /// Locks follow the capture's contact labels. `switchedFrom` is the frame shown just before a switch, if any.
    let planLocks source actor (chain: Chain) (previous: FootLock[]) (switchedFrom: float32 voption) stature =
        let frame = chain.Frame
        let start = int frame
        let rate = framesPerTick * chain.Rate
        let horizon = frame + rate * float32 config.SearchIntervalTicks
        let last = clipLast start
        let tick = float32 chain.Tick

        for foot = 0 to 1 do
            let current = previous[foot]
            chain.Locks[foot] <- current

            match switchedFrom, toLocalAt source actor tick with
            | ValueSome before, ValueSome local ->
                // The new chain's output equals the old one at the switch, so its sole is the visible capture sole.
                let sole = localSole chain frame tick foot stature
                let struct (target, held) = pull current before tick sole local stature

                let planted =
                    current.Active
                    && before >= float32 current.Start
                    && before < float32 current.End
                    && not current.RampIn

                let carried =
                    { idle with
                        SwitchOffset = target - sole
                        SwitchWeight = held
                        SwitchTick = tick }

                chain.Locks[foot] <-
                    if contact start foot && planted then
                        // A foot planted on both sides of a switch must not move: keep the old world anchor.
                        { idle with
                            Active = true
                            Start = start
                            End = stanceEnd start foot
                            World = current.World }
                    elif contact start foot then
                        match anchorAt source actor chain frame tick foot stature with
                        | ValueSome world ->
                            { carried with
                                Active = true
                                Start = start
                                End = stanceEnd start foot
                                World = world
                                RampIn = true }
                        | ValueNone -> carried
                    else
                        carried
            | ValueSome _, ValueNone -> chain.Locks[foot] <- idle
            | ValueNone, _ -> ()

            // The pull the stance exerts as it ends becomes the offset its release fades out. It runs before a new
            // stance replaces this one (which inherits it) and again for a stance that also ends before next step.
            let settleEnd () =
                let lock = chain.Locks[foot]

                if lock.Active && not lock.HasEndOffset && float32 lock.End < horizon then
                    let time = tick + (float32 lock.End - frame) / rate

                    match toLocalAt source actor time with
                    | ValueSome local ->
                        let sole = localSole chain (float32 lock.End) time foot stature
                        // The last instant of the stance, with no switch pull, is exactly what the release starts from.
                        let stance =
                            { lock with
                                SwitchOffset = Vector3.Zero
                                ReleaseEnd = Int32.MinValue }

                        let struct (held, weight) =
                            pull stance (float32 lock.End - 1e-3f) time sole local stature

                        chain.Locks[foot] <-
                            { lock with
                                EndOffset = held - sole
                                EndWeight = weight
                                HasEndOffset = true }
                    | ValueNone -> chain.Locks[foot] <- { lock with HasEndOffset = true }

            settleEnd ()
            let lock = chain.Locks[foot]

            if not lock.Active || frame >= float32 lock.End then
                // A stance beginning before the next grid step is anchored now, at its own instant.
                let mutable cursor =
                    if lock.Active then
                        max (start + 1) lock.End
                    else
                        max (start + 1) (int (ceil frame))

                let mutable found = false

                while not found && float32 cursor < horizon && cursor <= last do
                    if contact cursor foot && not (contact (cursor - 1) foot) then
                        found <- true
                    else
                        cursor <- cursor + 1

                if found then
                    let time = tick + (float32 cursor - frame) / rate

                    match anchorAt source actor chain (float32 cursor) time foot stature with
                    | ValueSome world ->
                        chain.Locks[foot] <- replace lock cursor (stanceEnd cursor foot) world
                        settleEnd ()
                    | ValueNone -> ()

    /// Switches the chain to a new frame at its current tick, carrying the visible pose through an offset.
    let switchTo (chain: Chain) (target: int) =
        let tick = float32 chain.Tick
        let seconds = max 0.0f ((tick - float32 chain.SwitchTick) * animation.TickSeconds)
        let source = chain.Frame

        for joint = 0 to joints - 1 do
            let struct (x, v) = decay chain.OffsetX[joint] chain.OffsetV[joint] seconds
            let visible = Rotation.exp x * sampleRotation source joint
            let destination = db.Rotation(target, joint)
            chain.OffsetX[joint] <- Rotation.log (Quaternion.Normalize(visible * Quaternion.Conjugate destination))

            chain.OffsetV[joint] <-
                v + angularVelocity source chain.Rate joint
                - angularVelocity (float32 target) 1.0f joint

        let struct (px, pv) = decay chain.PelvisX chain.PelvisV seconds
        let sourceVelocity = pelvisVelocity source chain.Rate
        let targetVelocity = pelvisVelocity (float32 target) 1.0f
        chain.PelvisX <- px + samplePelvis source - db.Pelvis[target]
        chain.PelvisV <- pv + sourceVelocity - targetVelocity
        chain.SwitchTick <- chain.Tick
        chain.Frame <- float32 target

    let start source actor (chain: Chain) stature =
        if not (trajectoryQuery source actor chain.Tick) then
            false
        else
            searches <- searches + 1L

            let mask =
                if cycling source actor chain.Tick then
                    MotionFeatures.trajectoryOnly
                else
                    MotionFeatures.trajectoryOnlyStill

            let struct (best, _) = MotionFeatures.search features query mask Single.MaxValue

            if best < 0 then
                false
            else
                chain.Frame <- float32 best
                chain.SwitchTick <- chain.Tick
                chain.Rate <- slew chain.Rate (playbackRate source actor chain.Tick best)
                chain.Switched <- true
                planLocks source actor chain [| idle; idle |] ValueNone stature
                true

    let step source actor (chain: Chain) stature =
        let previous = Array.copy chain.Locks

        let advanced =
            chain.Frame + framesPerTick * chain.Rate * float32 config.SearchIntervalTicks

        chain.Tick <- chain.Tick + config.SearchIntervalTicks
        let continuing = min (int (MathF.Round advanced)) (clipLast (int chain.Frame))

        let horizon =
            framesPerTick * config.MaxPlaybackRate * float32 config.SearchIntervalTicks

        let exhausted = float32 continuing + horizon >= float32 (clipLast (int chain.Frame))
        chain.Frame <- min advanced (float32 (clipLast (int chain.Frame)))

        let before = chain.Frame

        let switched =
            if trajectoryQuery source actor chain.Tick then
                poseQuery chain.Frame
                searches <- searches + 1L

                let mask =
                    if cycling source actor chain.Tick then
                        MotionFeatures.full
                    else
                        MotionFeatures.fullStill

                let candidate = Array.BinarySearch(features.Candidates, continuing) >= 0

                let stay =
                    if exhausted || not candidate then
                        Single.MaxValue
                    else
                        MotionFeatures.cost features query mask continuing

                // Only a candidate beating the margin can be taken, so the search may prune against it directly.
                let limit =
                    if stay = Single.MaxValue then
                        Single.MaxValue
                    else
                        stay - config.SwitchMargin

                let struct (best, bestCost) = MotionFeatures.search features query mask limit

                lastStay <- stay
                lastBest <- bestCost
                lastForced <- exhausted || not candidate

                let nearby =
                    best >= 0
                    && features.ClipOf[best] = features.ClipOf[continuing]
                    && abs (best - continuing) < config.MinimumJumpFrames

                if
                    best >= 0
                    && (exhausted || (not nearby && bestCost < stay - config.SwitchMargin))
                then
                    switchTo chain best
                    true
                else
                    false
            else
                false

        chain.Switched <- switched
        chain.Rate <- slew chain.Rate (playbackRate source actor chain.Tick (int chain.Frame))
        planLocks source actor chain previous (if switched then ValueSome before else ValueNone) stature

    let chainAt source actor (chains: Dictionary<int, Chain>) startTick grid stature =
        let fresh () =
            let chain = Chain(startTick, joints)

            if start source actor chain stature then
                chains[startTick] <- chain
                ValueSome chain
            else
                ValueNone

        let chain =
            match chains.TryGetValue startTick with
            | true, chain when chain.Tick <= grid -> ValueSome chain
            | _ -> fresh ()

        match chain with
        | ValueSome chain ->
            while chain.Tick < grid do
                step source actor chain stature

            ValueSome chain
        | ValueNone -> ValueNone

    let footTarget (chain: Chain) (frame: float32) (time: float32) foot (world: Matrix4x4[]) stature root yaw =
        let current = MotionFeatures.sole world (leg foot) stature
        pull chain.Locks[foot] frame time current (fun anchor -> MotionInput.localPoint root yaw anchor) stature

    let overlayWeight (time: MotionTime) (overlay: FootOverlay) =
        let now = MotionTime.value time

        if not overlay.Active || now > overlay.Recovery.Finish then
            0.0f
        else
            let ramp = config.OverlayBlendSeconds / animation.TickSeconds
            let rising = (now - overlay.Preparation.Start) / ramp
            let falling = (overlay.Recovery.Finish - now) / ramp
            Math.Clamp(min rising falling, 0.0f, 1.0f)

    member _.Features = features
    member _.Searches = searches
    /// Costs of the latest search step: continuing, best candidate, and whether continuing was impossible.
    member _.LastSearch = struct (lastStay, lastBest, lastForced)

    static member Create(db: MotionDatabase, rig: RigDefinition, animation: AnimationConfig, config: MatchingConfig) =
        MatchingConfig.validate config
        |> Result.bind (fun config ->
            MotionFeatures.build db rig config
            |> Result.map (fun features -> MotionMatcher(db, rig, animation, config, features)))

    /// Drops every cached chain; the next evaluation rebuilds from its anchor exactly as a cold start would.
    member _.Reset() = caches.Clear()

    member _.Evaluate
        (
            source: IMotionPlanSource,
            plan: ActorPlan,
            time: MotionTime,
            rootPosition: Vector3,
            facing: float32,
            scratch: PoseScratch,
            palette: Matrix4x4[],
            offset: int
        ) : MatchedPose voption =
        let actor = plan.Root.Actor
        let stature = plan.Root.Morphology.Height / 1.8f

        let chains =
            match caches.TryGetValue actor with
            | true, struct (generation, chains) when generation = plan.Root.Generation -> chains
            | _ ->
                let chains = Dictionary<int, Chain>()
                caches[actor] <- struct (plan.Root.Generation, chains)
                chains

        let now = MotionTime.value time
        let anchor = (time.Tick / config.AnchorIntervalTicks) * config.AnchorIntervalTicks
        let grid = (time.Tick / config.SearchIntervalTicks) * config.SearchIntervalTicks
        let mainStart = max 0 (anchor - config.AnchorIntervalTicks)
        let fadeStart = anchor + config.AnchorIntervalTicks - config.CrossfadeTicks

        stale.Clear()

        for key in chains.Keys do
            if key < mainStart || key > anchor then
                stale.Add key

        for key in stale do
            chains.Remove key |> ignore

        let main =
            match chainAt source actor chains mainStart grid stature with
            | ValueSome chain -> ValueSome chain
            // Plans older than the live window are gone; restart at the present rather than show nothing.
            | ValueNone -> chainAt source actor chains anchor grid stature

        match main with
        | ValueNone -> ValueNone
        | ValueSome main ->
            let blend =
                if main.Start <> anchor && time.Tick >= fadeStart then
                    let u =
                        Math.Clamp((now - float32 fadeStart) / float32 config.CrossfadeTicks, 0.0f, 1.0f)

                    u * u * (3.0f - 2.0f * u)
                else
                    0.0f

            let next =
                if blend > 0.0f then
                    chainAt source actor chains anchor grid stature
                else
                    ValueNone

            let mainFrame = frameAt main now
            poseInto main mainFrame now stature mainLocal

            // Each chain locks its own soles; blending targets taken against the blended pose would let one
            // chain's anchor yank a foot the other chain still has in the air.
            let blendedTargets =
                match next with
                | ValueSome next ->
                    let nextFrame = frameAt next now
                    poseInto next nextFrame now stature nextLocal
                    MotionFeatures.forward rig mainLocal lockWorld

                    let struct (mainLeft, mainLeftWeight) =
                        footTarget main mainFrame now 0 lockWorld stature rootPosition facing

                    let struct (mainRight, mainRightWeight) =
                        footTarget main mainFrame now 1 lockWorld stature rootPosition facing

                    MotionFeatures.forward rig nextLocal lockWorld

                    let struct (nextLeft, nextLeftWeight) =
                        footTarget next nextFrame now 0 lockWorld stature rootPosition facing

                    let struct (nextRight, nextRightWeight) =
                        footTarget next nextFrame now 1 lockWorld stature rootPosition facing

                    for node = 0 to rig.Bind.Length - 1 do
                        let a = mainLocal[node]
                        let b = nextLocal[node]

                        mainLocal[node] <-
                            { a with
                                Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, blend)
                                Translation = Vector3.Lerp(a.Translation, b.Translation, blend) }

                    ValueSome(
                        struct (Vector3.Lerp(mainLeft, nextLeft, blend),
                                mainLeftWeight + (nextLeftWeight - mainLeftWeight) * blend,
                                Vector3.Lerp(mainRight, nextRight, blend),
                                mainRightWeight + (nextRightWeight - mainRightWeight) * blend)
                    )
                | ValueNone -> ValueNone

            BodyPose.applyGaze animation rig plan 0.0f 0.0f mainLocal
            let world = scratch.World
            MotionFeatures.forward rig mainLocal world

            let struct (leftCapture, leftCaptureLock, rightCapture, rightCaptureLock) =
                match blendedTargets with
                | ValueSome targets -> targets
                | ValueNone ->
                    let struct (left, leftWeight) =
                        footTarget main mainFrame now 0 world stature rootPosition facing

                    let struct (right, rightWeight) =
                        footTarget main mainFrame now 1 world stature rootPosition facing

                    struct (left, leftWeight, right, rightWeight)

            let withTask foot (target: Vector3) weight =
                let overlay = if foot = 0 then plan.LeftOverlay else plan.RightOverlay
                let taskWeight = overlayWeight time overlay

                if taskWeight > 0.0f then
                    // Ball tasks own the foot while active; the capture only fills the ramps either side.
                    let support = if foot = 0 then plan.Left else plan.Right

                    let task =
                        MotionInput.localPoint
                            rootPosition
                            facing
                            (PoseEvaluation.footTaskTarget animation time support overlay)

                    struct (Vector3.Lerp(target, task, taskWeight), weight * (1.0f - taskWeight), taskWeight)
                else
                    struct (target, weight, 0.0f)

            let solverLeg foot (target: Vector3) =
                let leg = leg foot
                let overlay = if foot = 0 then plan.LeftOverlay else plan.RightOverlay
                let hip = world[leg.Upper].Translation
                let reach = target - hip

                let direction =
                    if reach.LengthSquared() > 1e-8f then
                        Vector3.Normalize reach
                    else
                        -Vector3.UnitY

                let thigh = world[leg.Lower].Translation - hip
                let bend = thigh - direction * Vector3.Dot(thigh, direction)
                // The captured knee already points where this stride needs it; IK only corrects the reach. Capture
                // slightly hyperextends at push-off, which would aim the pole backwards and fold the knee the wrong
                // way for a frame, so the bend is never allowed behind the body (the actor-local frame faces +Z).
                { leg with
                    Socket = PoseEvaluation.taskSocket animation time overlay leg stature
                    Pole = Vector3(bend.X, bend.Y, max 0.0f bend.Z) + Vector3.UnitZ * (0.05f * stature) }

            let struct (leftTarget, leftLock, leftTask) = withTask 0 leftCapture leftCaptureLock

            let struct (rightTarget, rightLock, rightTask) =
                withTask 1 rightCapture rightCaptureLock

            let leftLeg = solverLeg 0 leftTarget
            let rightLeg = solverLeg 1 rightTarget

            // The capture already carries the right pelvis height for its gait; only a ball task reaching below the
            // captured hip lowers it, and only as far as that task owns the foot. Gait targets never do: the drop
            // grows like a square root near full extension and would bob the whole body as a foot releases.
            let drop =
                min
                    (animation.PelvisMaxDrop * stature)
                    (max
                        (leftTask * PoseEvaluation.requiredPelvisDrop world leftLeg leftTarget)
                        (rightTask * PoseEvaluation.requiredPelvisDrop world rightLeg rightTarget))

            if drop > 0.0f then
                let lowered = Matrix4x4.CreateTranslation(0.0f, -drop, 0.0f)

                for node = rig.Pelvis to rig.DescendantEnds[rig.Pelvis] do
                    world[node] <- world[node] * lowered

            // Near full extension a two-bone solve turns millimetres of reach into centimetres of knee travel; the
            // reach approaches the limit exponentially instead (soft IK), trading a few mm at the limit for a knee
            // that never snaps straight.
            let soften (leg: RigLimb) (target: Vector3) (held: float32) =
                let hip = world[leg.Upper].Translation
                let socket = Vector3.TransformNormal(leg.Socket, world[leg.End])
                let reach = target - socket - hip
                let distance = reach.Length()

                let limit =
                    Vector3.Distance(hip, world[leg.Lower].Translation)
                    + Vector3.Distance(world[leg.Lower].Translation, world[leg.End].Translation)

                let soft =
                    (config.SoftReach + (config.LockedSoftReach - config.SoftReach) * held)
                    * stature

                if distance > limit - soft && distance > 1e-5f then
                    let eased = limit - soft * MathF.Exp(-(distance - (limit - soft)) / soft)
                    hip + reach * (eased / distance) + socket
                else
                    target

            // The limb solve swings the foot with the shin, which would carry the sole off its target; the captured
            // foot orientation is restored about the ankle, so the socket offset the solve assumed holds again.
            let solve (leg: RigLimb) effector target =
                let normal = Vector3.TransformNormal(leg.SocketNormal, world[leg.End])
                let forward = Vector3.TransformNormal(leg.SocketForward, world[leg.End])
                ConstraintSolve.limb (rig, world, actor, effector, leg, target) |> ignore
                ConstraintSolve.orient rig world leg (Vector3.Normalize normal) (Vector3.Normalize forward)

            // The limb solve only sees the softened target, which is always reachable; the residual is judged against
            // the real one, beyond the few millimetres the easing gives up by design.
            let residual (leg: RigLimb) effector (target: Vector3) =
                let reached =
                    world[leg.End].Translation + Vector3.TransformNormal(leg.Socket, world[leg.End])

                let gap = Vector3.Distance(reached, target)

                if gap > config.SoftReach * stature then
                    ValueSome(UnreachableContact(actor, effector, gap))
                else
                    ValueNone

            solve leftLeg MotionEffector.LeftFoot (soften leftLeg leftTarget leftLock)
            solve rightLeg MotionEffector.RightFoot (soften rightLeg rightTarget rightLock)
            let leftResidual = residual leftLeg MotionEffector.LeftFoot leftTarget
            let rightResidual = residual rightLeg MotionEffector.RightFoot rightTarget

            ConstraintSolve.correctives rig world

            for joint = 0 to rig.JointNodes.Length - 1 do
                palette[offset + joint] <-
                    rig.InverseBind[joint]
                    * Matrix4x4.CreateScale(stature)
                    * world[rig.JointNodes[joint]]

            ValueSome
                { Clip = features.ClipOf[int mainFrame]
                  Frame = mainFrame
                  PlaybackRate = main.Rate
                  LeftLock = leftLock
                  RightLock = rightLock
                  Switched = main.Switched
                  LeftResidual = leftResidual
                  RightResidual = rightResidual }
