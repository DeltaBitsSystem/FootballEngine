namespace FootballEngine.MatchViewer3D.Probe

open System
open System.Collections.Generic
open System.IO
open System.Numerics
open System.Text.Json
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure
open FootballEngine.Client.Animation
open FootballEngine.Client.Match3D

/// Animation baseline over real match motion. One seeded match is simulated twice: the first pass only chooses the
/// windows (deterministic, so the second pass sees the same football), the second streams every boundary through
/// the runtime and samples poses at the 60 Hz display rate inside each window. Synthetic roots hide the defects
/// that matter (abrupt Core turns, contacts at awkward reach), which is why the corpus is real play.
module MotionCorpus =

    type WindowKind =
        | Start = 0
        | Stop = 1
        | Turn = 2
        | Sprint = 3
        | Dribble = 4
        | Pass = 5
        | Shot = 6
        | Reception = 7
        | Tackle = 8
        | Header = 9
        | Jump = 10
        | Goalkeeper = 11

    [<Struct>]
    type Window =
        { Kind: WindowKind
          Actor: PlayerId
          Centre: int }

    type WindowReport =
        { Kind: string
          Actor: int
          FromTick: int
          ToTick: int
          Samples: int
          SupportSamples: int
          /// Horizontal drift of a planted sole between consecutive display frames, summed over the window.
          SkateMetres: float32
          MaxSkateSpeed: float32
          /// Worst joint acceleration seen at display rate; a pop is a frame-to-frame jump the body cannot make.
          MaxJointAcceleration: float32
          Pops: int
          ContactSamples: int
          MaxContactErrorMetres: float32
          MaxSolePenetrationMetres: float32
          Residuals: int }

    /// Carry touches over the whole half, judged at Core's contact instant: the support foot must be down and the
    /// touching boot at the ball.
    type TouchReport =
        { Touches: int
          /// The pose's capture labels the support foot as down: the foot is not in the air.
          SupportLabelled: int
          /// The stance lock holds the support foot at weight 0.5 or more; below that it yields to slip.
          SupportPlanted: int
          SwingPlanted: int
          /// Capture stride phase within 0.1 cycle of Core's.
          PhaseAligned: int
          /// Touches whose task Core published ahead, so the boot could be swung to the ball.
          Announced: int
          MedianBootErrorMetres: float32
          P90BootErrorMetres: float32
          P95BootErrorMetres: float32 }

    let private require =
        function
        | Ok value -> value
        | Error error -> failwithf "motion corpus failed: %A" error

    let private halfWindowTicks = 60
    let private openingTicks = 60 * 40
    let private displayHz = 60.0
    // A sprinting foot peaks near 250 m/s² in swing; 400 m/s² is ~11 cm of deviation from its own path in one
    // 60 Hz frame, which no limb produces and which reads on screen as a pop.
    let private popAcceleration = 400.0f
    // A planted boot that slides more than 0.5 m/s is visibly skating; slow creep under it is filtered as noise.
    let private skateSpeedFloor = 0.5f

    let private input seed =
        { MatchId = seed
          Context = TestBuilders.defaultCtx ()
          Seed = seed
          EffectiveConfig = EffectiveConfig.defaultConfig
          EngineVersion = "motion-corpus" }

    let private play seed (limit: int) (visit: MotionBoundary -> MatchEvent[] -> unit) =
        let session = MatchSession.create Scheduler.systems (input seed) MatchRunMode.Fast |> require
        visit (MatchSession.initialMotion session |> require) [||]

        while session.World.State.Flow <> MatchFlow.MatchEnded && int session.World.State.Tick < limit do
            let advance = MatchSession.advanceTo (session.World.State.Tick + 1<subtick>) session |> require

            for boundary in advance.Motion do
                visit boundary advance.Events

    let private planarSpeed (v: Vector3) = Vector2(v.X, v.Y).Length()

    /// First qualifying moment for each kind after the opening minute, so kickoff choreography (everyone still,
    /// then moving at once) does not supply every window, and windows do not overlap one another.
    let chooseWindows seed limit =
        let found = Dictionary<WindowKind, Window>()
        let history = Dictionary<PlayerId, Queue<MotionRootSample>>()

        let claim kind actor tick =
            let clear =
                found.Values |> Seq.forall (fun window -> abs (window.Centre - tick) > 2 * halfWindowTicks)

            if tick > openingTicks && tick < limit - halfWindowTicks && clear && not (found.ContainsKey kind) then
                found[kind] <- { Kind = kind; Actor = actor; Centre = tick }

        play seed limit (fun boundary events ->
            let tick = int boundary.Tick

            for root in boundary.Roots do
                if root.Active then
                    let queue =
                        match history.TryGetValue root.Actor with
                        | true, queue -> queue
                        | _ ->
                            let queue = Queue<MotionRootSample>()
                            history[root.Actor] <- queue
                            queue

                    queue.Enqueue root

                    if queue.Count > 40 then
                        let past = queue.Dequeue()
                        let before, now = planarSpeed past.Velocity, planarSpeed root.Velocity

                        if before < 0.4f && now > 4.0f then claim WindowKind.Start root.Actor tick
                        if before > 5.0f && now < 0.6f then claim WindowKind.Stop root.Actor tick
                        if now > 7.0f then claim WindowKind.Sprint root.Actor tick
                        if root.Position.Z > 0.25f then claim WindowKind.Jump root.Actor tick

                        let turn = abs (MathF.IEEERemainder(root.Facing - past.Facing, 2.0f * MathF.PI))

                        if turn > MathF.PI * 0.6f && now > 2.0f then claim WindowKind.Turn root.Actor tick

            for evidence in boundary.Evidence do
                if evidence.Phase = MotionEvidencePhase.Confirmed then
                    let at = int evidence.CommitAt

                    match evidence.Family with
                    | MotionFamily.FootStrike ->
                        let speed = evidence.OutgoingVelocity |> ValueOption.map _.Length() |> ValueOption.defaultValue 0.0f
                        claim (if speed > 18.0f then WindowKind.Shot else WindowKind.Pass) evidence.Actor at
                    | MotionFamily.Reception -> claim WindowKind.Reception evidence.Actor at
                    | MotionFamily.Dribble -> claim WindowKind.Dribble evidence.Actor at
                    | MotionFamily.Header -> claim WindowKind.Header evidence.Actor at
                    | MotionFamily.Jump -> claim WindowKind.Jump evidence.Actor at
                    // Core resolves challenges as physical contact tasks; the Tackle event only names a winner.
                    | MotionFamily.PhysicalContact -> claim WindowKind.Tackle evidence.Actor at
                    | MotionFamily.Goalkeeper -> claim WindowKind.Goalkeeper evidence.Actor at
                    | _ -> ()

            for event in events do
                match event.Data with
                | EventData.Tackle(winner, _) -> claim WindowKind.Tackle winner (int event.Tick)
                | EventData.KeeperDive(keeper, _, _, _, contactAt) -> claim WindowKind.Goalkeeper keeper (int contactAt)
                | _ -> ())

        found.Values |> Seq.sortBy (fun window -> int window.Kind) |> Seq.toArray

    type private Accumulator(window: Window) =
        member val Samples = 0 with get, set
        member val SupportSamples = 0 with get, set
        member val Skate = 0.0f with get, set
        member val MaxSkateSpeed = 0.0f with get, set
        member val MaxAcceleration = 0.0f with get, set
        member val Pops = 0 with get, set
        member val ContactSamples = 0 with get, set
        member val MaxContactError = 0.0f with get, set
        member val MaxPenetration = 0.0f with get, set
        member val Residuals = 0 with get, set
        member val Previous: Vector3[] = [||] with get, set
        member val PreviousVelocity: Vector3[] = [||] with get, set
        member val PreviousSole = [| Vector3.Zero; Vector3.Zero |] with get, set
        member val PreviousSupport = [| false; false |] with get, set
        member _.Window = window

    let private rootAt (boundaries: Dictionary<int, MotionBoundary>) actor (ticks: float) =
        let tick = int (floor ticks)
        let fraction = float32 (ticks - floor ticks)

        let find tick =
            match boundaries.TryGetValue tick with
            | true, boundary -> boundary.Roots |> Array.tryFind (fun root -> root.Actor = actor)
            | _ -> None

        match find tick, find (tick + 1) with
        | Some a, Some b ->
            let facing = a.Facing + MathF.IEEERemainder(b.Facing - a.Facing, 2.0f * MathF.PI) * fraction
            Some(Vector3.Lerp(a.Position, b.Position, fraction), facing)
        | Some a, None -> Some(a.Position, a.Facing)
        | _ -> None

    /// `matched` evaluates poses with the motion matcher over the shipped capture; otherwise the procedural gait.
    let measure seed limit (windows: Window[]) matched =
        let config = AnimationConfig.defaultConfig
        let assets = Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer")

        let character =
            CharacterAsset.load (Path.Combine(assets, "player.glb"))
            |> require

        let rig = character.Rig

        let matcher =
            if matched then
                MotionDatabase.load (Path.Combine(assets, "motion-db.bin"))
                |> Result.mapError string
                |> Result.bind (fun db -> MotionMatcher.Create(db, rig, config, MatchingConfig.defaultConfig))
                |> require
                |> Some
            else
                None

        let database = MotionDatabase.load (Path.Combine(assets, "motion-db.bin")) |> Result.mapError string |> require

        let identity =
            { PlannerVersion = MotionIdentity.plannerVersion
              ConfigHash = MotionIdentity.configHash config
              RigHash = rig.Hash
              ProfileHash = "motion-corpus-default-context"
              TrackId = seed }

        let runtime = MotionRuntime(config, identity)
        let scratch = PoseScratch rig
        let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity
        let accumulators = windows |> Array.map Accumulator
        let boundaries = Dictionary<int, MotionBoundary>()
        let ticksPerFrame = float (1.0f / config.TickSeconds) / displayHz
        let frameSeconds = float32 (1.0 / displayHz)

        let soles =
            character.Batches[0].Vertices
            |> Array.filter (fun vertex -> abs (vertex.Position.Y - 0.005f) < 0.00001f && abs vertex.Position.X > 0.04f && abs vertex.Position.X < 0.17f)

        let sample (acc: Accumulator) (ticks: float) =
            let window = acc.Window

            match rootAt boundaries window.Actor ticks with
            | None -> ()
            | Some(position, facing) ->
                let time = MotionTime.ofTicks ticks

                match runtime.TryPlan(window.Actor, time) |> require with
                | ValueNone -> ()
                | ValueSome plan ->
                    let root = MotionInput.vector position
                    let yaw = MotionInput.facing facing
                    let rotation = Matrix4x4.CreateRotationY yaw

                    // Planted means what the evaluator holds still: the matcher's full foot lock, or the procedural
                    // support phase outside a ball overlay.
                    let struct (left, right, plantedLeft, plantedRight) =
                        match matcher with
                        | Some matcher ->
                            match matcher.Evaluate(runtime :> IMotionPlanSource, plan, time, root, yaw, scratch, palette, 0) with
                            | ValueSome pose -> struct (pose.LeftResidual, pose.RightResidual, pose.LeftLock >= 1.0f, pose.RightLock >= 1.0f)
                            | ValueNone -> struct (ValueNone, ValueNone, false, false)
                        | None ->
                            let struct (left, right) = PoseEvaluation.evaluateInto (config, rig, plan, time, root, yaw, scratch, palette, 0)
                            struct (left, right, plan.Left.Phase = SupportPhase.Support && not plan.LeftOverlay.Active,
                                    plan.Right.Phase = SupportPhase.Support && not plan.RightOverlay.Active)

                    if left.IsSome then acc.Residuals <- acc.Residuals + 1
                    if right.IsSome then acc.Residuals <- acc.Residuals + 1

                    let worldOf (local: Vector3) joint = Vector3.Transform(Vector3.Transform(local, scratch.World[joint]), rotation) + root
                    let joints = Array.init rig.JointNodes.Length (fun joint -> worldOf Vector3.Zero joint)

                    if acc.Previous.Length = joints.Length then
                        let velocities = Array.init joints.Length (fun joint -> (joints[joint] - acc.Previous[joint]) / frameSeconds)

                        if acc.PreviousVelocity.Length = joints.Length then
                            let mutable worst = 0.0f

                            for joint = 0 to joints.Length - 1 do
                                worst <- max worst ((velocities[joint] - acc.PreviousVelocity[joint]).Length() / frameSeconds)

                            acc.MaxAcceleration <- max acc.MaxAcceleration worst
                            if worst > popAcceleration then acc.Pops <- acc.Pops + 1

                        acc.PreviousVelocity <- velocities

                    acc.Previous <- joints

                    let feet =
                        [| rig.LeftLeg, plantedLeft, plan.LeftOverlay
                           rig.RightLeg, plantedRight, plan.RightOverlay |]

                    for index = 0 to 1 do
                        let chain, planted, overlay = feet[index]
                        let sole = worldOf chain.Socket chain.End

                        if planted then
                            acc.SupportSamples <- acc.SupportSamples + 1

                            if acc.PreviousSupport[index] then
                                let drift = Vector2(sole.X - acc.PreviousSole[index].X, sole.Z - acc.PreviousSole[index].Z).Length()
                                let speed = drift / frameSeconds
                                acc.MaxSkateSpeed <- max acc.MaxSkateSpeed speed
                                if speed > skateSpeedFloor then acc.Skate <- acc.Skate + drift

                        acc.PreviousSupport[index] <- planted
                        acc.PreviousSole[index] <- sole

                        if overlay.Active && abs (float32 ticks - overlay.Preparation.Finish) <= float32 ticksPerFrame * 0.5f then
                            let error = Vector3.Distance(worldOf chain.BallSocket chain.End, overlay.Preparation.Target)
                            acc.MaxContactError <- max acc.MaxContactError error
                            acc.ContactSamples <- acc.ContactSamples + 1

                    for vertex in soles do
                        let joints, weights = vertex.Joints, vertex.Weights
                        let posed =
                            Vector3.Transform(vertex.Position, palette[int joints.X]) * weights.X
                            + Vector3.Transform(vertex.Position, palette[int joints.Y]) * weights.Y
                            + Vector3.Transform(vertex.Position, palette[int joints.Z]) * weights.Z
                            + Vector3.Transform(vertex.Position, palette[int joints.W]) * weights.W
                        acc.MaxPenetration <- max acc.MaxPenetration (max 0.0f -posed.Y)

                    acc.Samples <- acc.Samples + 1

        let pendingTouches = Queue<MotionEvidence>()
        let mutable touches, supportPlanted, swingPlanted = 0, 0, 0
        let mutable supportLabelled, phaseAligned, announced = 0, 0, 0
        let preparedTasks = HashSet<uint64>()
        let bootErrors = ResizeArray<float32>()

        let judgeTouch (touch: MotionEvidence) =
            match matcher, rootAt boundaries touch.Actor (float touch.CommitAt) with
            | Some matcher, Some(position, facing) ->
                let time = MotionTime.ofTicks (float touch.CommitAt)

                match runtime.TryPlan(touch.Actor, time) |> require with
                | ValueSome plan ->
                    let root = MotionInput.vector position
                    let yaw = MotionInput.facing facing

                    match matcher.Evaluate(runtime :> IMotionPlanSource, plan, time, root, yaw, scratch, palette, 0) with
                    | ValueSome pose ->
                        let rightTouches = touch.Effector = MotionEffector.RightFoot
                        let support, swing = if rightTouches then pose.LeftLock, pose.RightLock else pose.RightLock, pose.LeftLock
                        let chain = if rightTouches then rig.RightLeg else rig.LeftLeg
                        let rotation = Matrix4x4.CreateRotationY yaw
                        let boot = Vector3.Transform(Vector3.Transform(chain.BallSocket, scratch.World[chain.End]), rotation) + root
                        touches <- touches + 1
                        if preparedTasks.Contains touch.TaskId then announced <- announced + 1
                        if support >= 0.5f then supportPlanted <- supportPlanted + 1
                        if swing >= 0.5f then swingPlanted <- swingPlanted + 1
                        let supportBit = if rightTouches then 1uy else 2uy
                        let frame = int (MathF.Round pose.Frame)
                        if database.Contacts[frame] &&& supportBit <> 0uy then supportLabelled <- supportLabelled + 1

                        let core = boundaries[int touch.CommitAt].Roots |> Array.find (fun root -> root.Actor = touch.Actor)
                        let gap = core.GaitPhase - matcher.Features.Phase[frame]
                        if abs (gap - MathF.Round gap) < 0.1f then phaseAligned <- phaseAligned + 1
                        bootErrors.Add(Vector3.Distance(boot, MotionInput.vector touch.PredictedPoint))
                    | ValueNone -> ()
                | ValueNone -> ()
            | _ -> ()

        let mutable nextFrame = 0.0

        play seed limit (fun boundary _ ->
            runtime.Ingest boundary |> require
            let tick = int boundary.Tick
            boundaries[tick] <- boundary
            boundaries.Remove(tick - runtime.RetentionTicks) |> ignore

            for evidence in boundary.Evidence do
                if evidence.Phase = MotionEvidencePhase.Prepared && evidence.Family = MotionFamily.Dribble then
                    preparedTasks.Add evidence.TaskId |> ignore

                if evidence.Phase = MotionEvidencePhase.Confirmed && evidence.Family = MotionFamily.Dribble && int evidence.CommitAt > openingTicks then
                    pendingTouches.Enqueue evidence

            // A touch is judged once the boundary after it exists, as the presentation delay guarantees.
            while pendingTouches.Count > 0 && int (pendingTouches.Peek()).CommitAt + 1 <= tick do
                judgeTouch (pendingTouches.Dequeue())

            // Display frames are evaluated once the boundary after them exists, as the presentation delay guarantees.
            while nextFrame + 1.0 <= float tick do
                for acc in accumulators do
                    let window = acc.Window
                    if nextFrame >= float (window.Centre - halfWindowTicks) && nextFrame <= float (window.Centre + halfWindowTicks) then
                        sample acc nextFrame

                nextFrame <- nextFrame + ticksPerFrame)

        let sorted = bootErrors |> Seq.sort |> Array.ofSeq
        let quantile q = if sorted.Length = 0 then 0.0f else sorted[min (sorted.Length - 1) (int (q * float sorted.Length))]

        let touchReport =
            { Touches = touches
              SupportLabelled = supportLabelled
              SupportPlanted = supportPlanted
              SwingPlanted = swingPlanted
              PhaseAligned = phaseAligned
              Announced = announced
              MedianBootErrorMetres = quantile 0.5
              P90BootErrorMetres = quantile 0.9
              P95BootErrorMetres = quantile 0.95 }

        touchReport,
        accumulators
        |> Array.map (fun acc ->
            { Kind = string acc.Window.Kind
              Actor = (let (PlayerId id) = acc.Window.Actor in id)
              FromTick = acc.Window.Centre - halfWindowTicks
              ToTick = acc.Window.Centre + halfWindowTicks
              Samples = acc.Samples
              SupportSamples = acc.SupportSamples
              SkateMetres = acc.Skate
              MaxSkateSpeed = acc.MaxSkateSpeed
              MaxJointAcceleration = acc.MaxAcceleration
              Pops = acc.Pops
              ContactSamples = acc.ContactSamples
              MaxContactErrorMetres = acc.MaxContactError
              MaxSolePenetrationMetres = acc.MaxPenetration
              Residuals = acc.Residuals })

    let run (outputPath: string) matched =
        let seed = 1UL
        // A full half gives every family, keeper dives and headers included, a chance to occur in open play.
        let limit = 45 * 60 * 40
        let windows = chooseWindows seed limit
        let touchReport, reports = measure seed limit windows matched

        let missing =
            Enum.GetValues<WindowKind>()
            |> Array.filter (fun kind -> windows |> Array.exists (fun window -> window.Kind = kind) |> not)
            |> Array.map string

        let report =
            {| SchemaVersion = 1
               Seed = seed
               TickLimit = limit
               DisplayHz = displayHz
               PopAcceleration = popAcceleration
               SkateSpeedFloor = skateSpeedFloor
               Evaluator = (if matched then "motion matching" else "procedural")
               Root = "Root interpolated linearly between Core boundaries; the viewer interpolates replay snapshots"
               MissingKinds = missing
               CarryTouches = (if matched then Some touchReport else None)
               Windows = reports
               TotalSkateMetres = reports |> Array.sumBy _.SkateMetres
               TotalPops = reports |> Array.sumBy _.Pops
               MaxContactErrorMetres = if reports.Length = 0 then 0.0f else reports |> Array.map _.MaxContactErrorMetres |> Array.max |}

        let json = JsonSerializer.Serialize(report, JsonSerializerOptions(WriteIndented = true))
        let path = Path.GetFullPath outputPath
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        File.WriteAllText(path, json)
        Console.WriteLine(json)
        Console.WriteLine($"report={path}")
        if missing.Length = 0 then 0 else 2
