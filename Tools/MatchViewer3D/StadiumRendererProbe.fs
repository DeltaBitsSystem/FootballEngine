namespace FootballEngine.MatchViewer3D.Probe

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open FootballEngine.Client.Match3D
open FootballEngine.Client.Views.Components
open FootballEngine.Client.Animation
open FootballEngine.Core.Match

module private ProbeLifecycle =
    let private configuredCycles =
        match Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_CYCLES") with
        | null -> 0
        | value ->
            match Int32.TryParse value with
            | true, cycles when cycles > 0 -> cycles
            | _ -> 0

    let mutable private attached = 0
    let mutable private detached = 0

    let cycles = configuredCycles

    let interval =
        match Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_CYCLE_MS") with
        | null -> TimeSpan.FromMilliseconds 350.0
        | value -> TimeSpan.FromMilliseconds(Double.Parse(value, Globalization.CultureInfo.InvariantCulture))

    let attachCount () = attached
    let detachCount () = detached
    let attachedOnce () = attached <- attached + 1
    let detachedOnce () = detached <- detached + 1

[<Sealed>]
type private ProbePitch() as this =
    inherit Control()

    let mutable renderer: WebGpuStadiumRenderer option = None
    let mutable worker: RenderWorker<PixelRect, WebGpuStadiumRenderer> option = None
    let mutable nativeBounds = PixelRect(0, 0, 1, 1)
    let mutable parentWindow = 0n
    let mutable hostWindow: Window option = None
    let mutable time = 0.0

    let secondHalf =
        Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_SECOND_HALF") = "1"

    let cameraScenario =
        Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_CAMERA_SCENE")

    let isolatedMotion = cameraScenario = "motion"
    let motionPace =
        match Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_GAIT") with
        | "run" -> 2.25
        | _ -> 1.0
    let curvedMotion = Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_CURVE") = "1"
    let motionAxis =
        match Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_AXIS") with
        | "front" -> -Math.PI * 0.5
        | _ -> 0.0

    let fixedPose =
        Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_FIXED_POSE") = "1"

    let contactFoot = Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_CONTACT")

    let contactPhase =
        Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_CONTACT_PHASE")

    let capturePath = Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_CAPTURE")
    let sequencePath = Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_SEQUENCE")
    let measure = Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_MEASURE") = "1"

    let waitForGpu =
        measure
        && Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_WAIT_GPU") <> "0"

    let captureFrame =
        match Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_CAPTURE_FRAME") with
        | null -> 120
        | value -> Int32.Parse value

    let exerciseResize =
        Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_RESIZE") = "1"

    let frameLimit =
        match Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_FRAMES") with
        | null -> 0
        | value -> Int32.Parse value

    let timings = Collections.Generic.List<float>()
    let submissions = Collections.Generic.List<float>()
    let allocations = Collections.Generic.List<int64>()
    let uploads = Collections.Generic.List<int64>()
    let intervals = Collections.Generic.List<float>()
    let mutable previousStarted = 0L
    let mutable renderedFrames = 0

    let mutable presentation: PresentationLoop option = None

    // Explicit diagnostic pacing reproduces the baseline; normal runs use the display clock.
    let diagnosticInterval =
        match Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_INTERVAL_MS") with
        | null -> None
        | value -> Some(TimeSpan.FromMilliseconds(Double.Parse(value, Globalization.CultureInfo.InvariantCulture)))

    let players =
        let home =
            [| for row, count in [| 0, 1; 1, 4; 2, 3; 3, 3 |] do
                   for index = 0 to count - 1 do
                       let x = -46.0 + float row * 14.0
                       let y = (float index - float (count - 1) / 2.0) * 13.0

                       yield
                           { Id = row * 4 + index + 1
                             Position = { X = x; Y = y }
                             Velocity = Vector2.zero
                             Acceleration = Vector2.zero
                             BodyFacing = 0.0
                             AngularVelocity = 0.0
                             TravelDistance = 0.0
                             Height = 0.0
                             VelocityZ = 0.0
                             State = Idle
                             VisualSlot = index
                             ShortName = "HOME"
                             Condition = 90
                             IsHome = true
                             HasBall = false
                             IsSidelined = false } |]

        let away =
            [| for row, count in [| 0, 1; 1, 4; 2, 3; 3, 3 |] do
                   for index = 0 to count - 1 do
                       let x = 46.0 - float row * 14.0
                       let y = (float index - float (count - 1) / 2.0) * 13.0

                       yield
                           { Id = 100 + row * 4 + index + 1
                             Position = { X = x; Y = y }
                             Velocity = Vector2.zero
                             Acceleration = Vector2.zero
                             BodyFacing = 0.0
                             AngularVelocity = 0.0
                             TravelDistance = 0.0
                             Height = 0.0
                             VelocityZ = 0.0
                             State = Idle
                             VisualSlot = index
                             ShortName = "AWAY"
                             Condition = 90
                             IsHome = false
                             HasBall = false
                             IsSidelined = false } |]

        Array.append home away
        |> Array.mapi (fun index player ->
            { player with
                Position =
                    if isolatedMotion && index <> 9 then
                        { X = -40.0 + float (index % 6) * 9.0
                          Y = if index < 11 then -25.0 else 25.0 }
                    elif index = 9 then
                        match cameraScenario with
                        | "left-goal" -> { X = -44.0; Y = 0.0 }
                        | "right-goal" -> { X = 44.0; Y = 0.0 }
                        | "near-touchline" -> { X = 16.0; Y = -30.0 }
                        | "far-touchline" -> { X = 16.0; Y = 30.0 }
                        | "midfield" -> Vector2.zero
                        | "motion" -> Vector2.zero
                        | _ -> { player.Position with X = 16.0 }
                    else
                        player.Position
                VisualSlot = index % 11
                IsHome = index < 11
                HasBall = index = 9
                ShortName = if index = 9 then "ENZO FERNANDEZ" else player.ShortName
                Condition = if index = 9 then 78 else player.Condition })

    let samplePlayers at =
        players
        |> Array.mapi (fun index player ->
            let t = max 0.0 (at - float index * 0.04)
            let ramp = Math.Clamp(t - 1.0, 0.0, 2.0)
            let cruise = Math.Clamp(t - 3.0, 0.0, 3.0)
            let brake = Math.Clamp(t - 6.0, 0.0, 2.0)
            let pace = if isolatedMotion then 3.0 else 1.0
            let distance = pace * (0.4 * ramp * ramp + 1.6 * cruise + 1.6 * brake - 0.4 * brake * brake)

            let speed =
                if t < 3.0 then pace * 0.8 * ramp
                elif t < 6.0 then pace * 1.6
                elif t < 8.0 then pace * (1.6 - 0.8 * brake)
                else 0.0

            let pivot = Math.Clamp((t - 9.0) / 1.2, 0.0, 1.0)
            let sign = if player.IsHome then 1.0 else -1.0

            if isolatedMotion && index = 9 then
                let ramp = Math.Clamp(at - 0.75, 0.0, 1.0)
                let cruise = Math.Clamp(at - 1.75, 0.0, 1.0)
                let brake = Math.Clamp(at - 2.75, 0.0, 0.75)
                let distance = motionPace * (1.2 * ramp * ramp + 2.4 * cruise + 2.4 * brake - 1.6 * brake * brake)
                let unscaledSpeed =
                    if at < 1.75 then 2.4 * ramp
                    elif at < 2.75 then 2.4
                    elif at < 3.5 then 2.4 - 3.2 * brake
                    else 0.0
                let speed = motionPace * unscaledSpeed
                let curvature = if curvedMotion then 0.12 else 0.0
                let travelAngle = motionAxis + curvature * distance
                let position =
                    if curvedMotion then
                        Vector2.create ((sin travelAngle - sin motionAxis) / curvature) ((cos motionAxis - cos travelAngle) / curvature)
                    else
                        Vector2.create (cos motionAxis * distance) (sin motionAxis * distance)
                let turn = Math.Clamp(at - 4.1, 0.0, 1.0)
                let angle = travelAngle + turn * turn * (3.0 - 2.0 * turn) * Math.PI * 0.5
                let angular = curvature * speed + (if turn > 0.0 && turn < 1.0 then 6.0 * turn * (1.0 - turn) * Math.PI * 0.5 else 0.0)
                { player with
                    Position = position
                    Velocity = Vector2.create (cos travelAngle * speed) (sin travelAngle * speed)
                    BodyFacing = angle
                    AngularVelocity = angular
                    TravelDistance = distance
                    State = if speed > 0.05 then Walking else Idle }
            else
                { player with
                    Position =
                        { player.Position with
                            X = player.Position.X + sign * distance }
                    Velocity = Vector2.create (sign * speed) 0.0
                    BodyFacing =
                        (if player.IsHome then 0.0 else Math.PI)
                        + pivot * pivot * (3.0 - 2.0 * pivot) * Math.PI * 0.5
                    AngularVelocity =
                        if pivot > 0.0 && pivot < 1.0 then
                            (6.0 * pivot * (1.0 - pivot) / 1.2) * Math.PI * 0.5
                        else
                            0.0
                    TravelDistance = distance
                    State = if speed > 0.05 then Walking else Idle })

    let isolatedBall at =
        let commitCarrier = samplePlayers 3.5 |> Array.find _.HasBall
        let cx, cy = cos commitCarrier.BodyFacing, sin commitCarrier.BodyFacing
        let lateral = if contactFoot = "left" then 0.10 else -0.10
        let travel = if at < 3.5 then 3.5 - at else 3.0 * (at - 3.5)
        Vector2.create (commitCarrier.Position.X + cx * (0.31 + travel) - cy * lateral) (commitCarrier.Position.Y + cy * (0.31 + travel) + cx * lateral)

    let motionConfig = AnimationConfig.defaultConfig

    let motionRuntime =
        MotionRuntime(
            motionConfig,
            { PlannerVersion = MotionIdentity.plannerVersion
              ConfigHash = "probe-v1"
              RigHash = "probe-rig"
              ProfileHash = "probe-profile"
              TrackId = 1UL }
        )

    let evaluator =
        lazy
            (let path =
                IO.Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer", "player.glb")

             match CharacterAsset.load path with
             | Ok character -> MotionPaletteEvaluator(character.Rig, motionConfig)
             | Error message -> invalidOp message)

    let baseTime = (if secondHalf then 73.0 else 37.0) * 60.0 + 24.0

    let preparePalette (frame: RenderFrame) =
        let motionTime =
            MotionTime.ofTicks ((frame.TimeSeconds - baseTime) / float motionConfig.TickSeconds)

        let target = motionTime.Tick

        let first =
            if motionRuntime.Frontier < 0 then
                0
            else
                motionRuntime.Frontier + 1

        for tick = first to target do
            let roots =
                samplePlayers (float tick * float motionConfig.TickSeconds)
                |> Array.map (fun player ->
                    let ball = if isolatedMotion && (contactFoot = "left" || contactFoot = "right") then isolatedBall (float tick * float motionConfig.TickSeconds) else frame.Ball.Position
                    { Actor = PlayerId player.Id
                      Generation = 1
                      Position =
                        System.Numerics.Vector3(
                            float32 player.Position.X,
                            float32 player.Position.Y,
                            float32 player.Height
                        )
                      Velocity =
                        System.Numerics.Vector3(
                            float32 player.Velocity.X,
                            float32 player.Velocity.Y,
                            float32 player.VelocityZ
                        )
                      Facing = float32 player.BodyFacing
                      AngularVelocity = float32 player.AngularVelocity
                      Condition = float32 player.Condition / 100.0f
                      AttentionTarget =
                        ValueSome(
                            System.Numerics.Vector3(
                                float32 ball.X,
                                float32 ball.Y,
                                float32 (frame.Ball.Height + frame.Ball.Radius)
                            )
                        )
                      Morphology = PhysicalMorphology.standard
                      Active = true })

            let evidence =
                if isolatedMotion && (contactFoot = "left" || contactFoot = "right") && (tick = 112 || tick = 139) then
                    let carrier = samplePlayers (140.0 * float motionConfig.TickSeconds) |> Array.find _.HasBall
                    let dx, dy = cos carrier.BodyFacing, sin carrier.BodyFacing
                    let lateral = if contactFoot = "left" then 0.10 else -0.10
                    let point = System.Numerics.Vector3(float32 (carrier.Position.X + dx * 0.20 - dy * lateral), float32 (carrier.Position.Y + dy * 0.20 + dx * lateral), 0.07f)
                    [| { Sequence = if tick = 112 then 1UL else 2UL
                         TaskId = 1UL; Actor = PlayerId carrier.Id; Generation = 1
                         Family = MotionFamily.FootStrike
                         Effector = if contactFoot = "left" then MotionEffector.LeftFoot else MotionEffector.RightFoot
                         Phase = if tick = 112 then MotionEvidencePhase.Prepared else MotionEvidencePhase.Confirmed
                         OccurredAt = tick * 1<subtick>; AvailableAt = tick * 1<subtick>
                         StartedAt = 112<subtick>; CommitAt = 140<subtick>; EndsAt = 152<subtick>
                         PredictedPoint = point; ConfirmedPoint = if tick = 139 then ValueSome point else ValueNone
                         BallCenter = point + System.Numerics.Vector3(float32 dx * 0.11f, float32 dy * 0.11f, 0.04f)
                         IncomingVelocity = ValueNone; OutgoingVelocity = ValueNone } |]
                else
                    [||]

            match
                motionRuntime.Ingest
                    { Tick = tick * 1<subtick>
                      Roots = roots
                      Evidence = evidence }
            with
            | Ok() -> ()
            | Error error -> invalidOp (sprintf "%A" error)

        evaluator.Value.Evaluate(motionRuntime, frame, motionTime) |> ignore
        evaluator.Value.Palettes

    let frame () =
        time <- if fixedPose then 2.0 else time + 1.0 / 30.0
        let movingPlayers = samplePlayers time

        let carrier = movingPlayers |> Array.find _.HasBall
        let timeSeconds = (if secondHalf then 73.0 else 37.0) * 60.0 + 24.0 + time
        let contact = contactFoot = "left" || contactFoot = "right"
        let dx, dy = cos carrier.BodyFacing, sin carrier.BodyFacing
        let lateral = if contactFoot = "left" then 0.10 else -0.10

        let ballPosition =
            if contact && isolatedMotion then
                isolatedBall time
            elif contact then
                Vector2.create
                    (carrier.Position.X + dx * 0.32 - dy * lateral)
                    (carrier.Position.Y + dy * 0.32 + dx * lateral)
            else
                { carrier.Position with
                    X = carrier.Position.X + 0.7 }

        let motion: FootballEngine.Core.Match.FootMotion option =
            if not contact then
                None
            else
                let phaseOffset =
                    if contactPhase = "prepare" then 3
                    elif contactPhase = "recover" then -5
                    else 0

                let tick =
                    if isolatedMotion then int (round ((baseTime + 3.5) * 40.0))
                    else int (round (timeSeconds * 40.0)) + phaseOffset

                Some
                    { Actor = FootballEngine.Core.Match.PlayerId carrier.Id
                      Foot =
                        if contactFoot = "left" then
                            FootballEngine.Core.Match.ContactFoot.Left
                        else
                            FootballEngine.Core.Match.ContactFoot.Right
                      Kind = FootballEngine.Core.Match.FootTouchKind.Kick FootballEngine.Core.Match.StrikeKind.Pass
                      StartedAt = (tick - 6) * 1<FootballEngine.Core.Match.subtick>
                      ContactAt = tick * 1<FootballEngine.Core.Match.subtick>
                      EndsAt = (tick + 12) * 1<FootballEngine.Core.Match.subtick>
                      Point =
                        { X = (ballPosition.X - dx * 0.11) * 1.0<FootballEngine.Core.Match.meter>
                          Y = (ballPosition.Y - dy * 0.11) * 1.0<FootballEngine.Core.Match.meter>
                          Z = 0.11<FootballEngine.Core.Match.meter> }
                      BallCenter =
                        { X = ballPosition.X * 1.0<FootballEngine.Core.Match.meter>
                          Y = ballPosition.Y * 1.0<FootballEngine.Core.Match.meter>
                          Z = 0.11<FootballEngine.Core.Match.meter> } }

        { Players = movingPlayers
          Ball =
            { Position = ballPosition
              Velocity =
                if contact && isolatedMotion then
                    let speed = if time < 3.5 then -1.0 else 3.0
                    Vector2.create (cos motionAxis * speed) (sin motionAxis * speed)
                else
                    { X = 0.0; Y = 0.0 }
              VelocityZ = 0.0
              Orientation = System.Numerics.Quaternion.Identity
              AngularVelocity = System.Numerics.Vector3.Zero
              Radius = 0.11
              FootMotion = motion
              Height = 0.0 }
          Officials = [||]
          TimeSeconds = timeSeconds
          HomeScore = 1
          AwayScore = 0
          AttackingClubId = None
          Momentum = 0.0
          Situation = MatchSituation.Live
          HomeName = "River Plate"
          AwayName = "Boca Juniors"
          CompetitionName = Some "LIGA PROFESIONAL" }

    let render () =
        match renderer with
        | Some active ->
            let started = Diagnostics.Stopwatch.GetTimestamp()
            let allocated = GC.GetAllocatedBytesForCurrentThread()
            let uploaded = active.HudUploadBytes

            let presented =
                let frame = frame ()

                active.Render(
                    nativeBounds,
                    frame,
                    preparePalette frame,
                    [||],
                    { HomeShortName = "RIV"
                      AwayShortName = "BOC"
                      Period =
                        if secondHalf then
                            FootballEngine.LiveMatch.MatchPeriod.SecondHalf
                        else
                            FootballEngine.LiveMatch.MatchPeriod.FirstHalf
                      HomePossession = 62
                      AwayPossession = 38
                      HomeShots = 8
                      AwayShots = 5
                      HomeOnTarget = 4
                      AwayOnTarget = 2
                      HomePasses = 132
                      AwayPasses = 96
                      HomeColor = 0xD71920u
                      AwayColor = 0x00529Fu }
                )

            if not presented then
                invalidOp "The probe could not present its frame."

            let submission = Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds

            if waitForGpu then
                active.WaitForGpu()

            renderedFrames <- renderedFrames + 1

            if exerciseResize && (renderedFrames = 30 || renderedFrames = 50) then
                let shrink = renderedFrames = 30

                Dispatcher.UIThread.Post(fun () ->
                    hostWindow
                    |> Option.iter (fun window ->
                        window.Width <- if shrink then 940.0 else 1160.0
                        window.Height <- if shrink then 670.0 else 790.0))

            if measure && renderedFrames > 15 then
                timings.Add(Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds)
                submissions.Add submission
                allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated)
                uploads.Add(active.HudUploadBytes - uploaded)

                if
                    previousStarted > 0L
                    && (String.IsNullOrWhiteSpace capturePath || renderedFrames <> captureFrame + 1)
                then
                    intervals.Add(Diagnostics.Stopwatch.GetElapsedTime(previousStarted, started).TotalMilliseconds)

            previousStarted <- started

            if renderedFrames = captureFrame && not (String.IsNullOrWhiteSpace capturePath) then
                let info = Diagnostics.ProcessStartInfo("import")
                info.ArgumentList.Add("-window")
                info.ArgumentList.Add(string (uint64 parentWindow))
                info.ArgumentList.Add(capturePath)
                use process = Diagnostics.Process.Start(info)
                process.WaitForExit()

                if process.ExitCode <> 0 then
                    invalidOp "Native HDR screenshot failed."

                Console.Error.WriteLine($"Captured HDR frame: {capturePath}")

            if not (String.IsNullOrWhiteSpace sequencePath) && renderedFrames % 3 = 0 then
                IO.Directory.CreateDirectory(sequencePath) |> ignore
                let path = IO.Path.Combine(sequencePath, $"frame-{renderedFrames / 3:D4}.png")
                let info = Diagnostics.ProcessStartInfo("import")
                info.ArgumentList.Add("-window")
                info.ArgumentList.Add(string (uint64 parentWindow))
                info.ArgumentList.Add(path)
                use process = Diagnostics.Process.Start(info)
                process.WaitForExit()
                if process.ExitCode <> 0 then invalidOp "Native sequence capture failed."

            if frameLimit > 0 && renderedFrames >= frameLimit then
                if timings.Count > 0 then
                    let sorted = timings |> Seq.sort |> Seq.toArray
                    let median = sorted[sorted.Length / 2]
                    let p95 = sorted[min (sorted.Length - 1) (int (float sorted.Length * 0.95))]

                    let measurement =
                        if waitForGpu then
                            "completed CPU+GPU-wait"
                        else
                            "submission/backpressure (no GPU wait)"

                    Console.Error.WriteLine(
                        $"HDR {measurement} measurement n={sorted.Length}, medianMs={median:F3}, p95Ms={p95:F3}; not GPU timestamps"
                    )

                    let cpu = submissions |> Seq.sort |> Seq.toArray
                    let cpuP95 = cpu[min (cpu.Length - 1) (int (float cpu.Length * 0.95))]
                    let meanAllocation = allocations |> Seq.averageBy float
                    let meanUpload = uploads |> Seq.averageBy float

                    let fps =
                        if intervals.Count > 0 then
                            1000.0 / (intervals |> Seq.average)
                        else
                            0.0

                    Console.Error.WriteLine(
                        $"Submission CPU medianMs={cpu[cpu.Length / 2]:F3}, p95Ms={cpuP95:F3}; managedBytes/frame={meanAllocation:F0}; HUD uploadBytes/frame={meanUpload:F0}; presentedFPS={fps:F1}"
                    )

                Dispatcher.UIThread.Post(fun () ->
                    (Application.Current.ApplicationLifetime :?> IClassicDesktopStyleApplicationLifetime).Shutdown(0))
        | None -> ()

    override this.OnAttachedToVisualTree args =
        base.OnAttachedToVisualTree args

        match TopLevel.GetTopLevel(this).TryGetPlatformHandle() with
        | null -> invalidOp "Avalonia did not expose a native top-level handle."
        | handle when handle.HandleDescriptor <> "XID" ->
            invalidOp $"Expected an X11 XID for this probe; found '{handle.HandleDescriptor}'."
        | handle ->
            let parent = unativeint (uint64 handle.Handle)

            let asset = IO.Path.GetFullPath("FootballEngine.Client/Assets/MatchViewer")

            let exposure =
                match Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_EXPOSURE_STOPS") with
                | null -> None
                | value -> Some(Single.Parse(value, Globalization.CultureInfo.InvariantCulture))

            let topLevel = TopLevel.GetTopLevel(this)
            parentWindow <- handle.Handle
            hostWindow <- Some(topLevel :?> Window)

            let onError (error: exn) =
                Console.Error.WriteLine(error)

                Dispatcher.UIThread.Post(fun () ->
                    (Application.Current.ApplicationLifetime :?> IClassicDesktopStyleApplicationLifetime).Shutdown(1))

            let create bounds =
                let camera =
                    match Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_VIEW") with
                    | null
                    | "broadcast" -> ViewerCamera.Broadcast
                    | "tactical" -> ViewerCamera.Tactical
                    | "behind-goal" -> ViewerCamera.BehindGoal
                    | "touchline" -> ViewerCamera.Touchline
                    | "close-up" -> ViewerCamera.CloseUp
                    | value -> invalidArg "MATCHVIEWER_PROBE_VIEW" $"Unknown viewer camera: {value}"

                let active = new WebGpuStadiumRenderer(parent, bounds, asset, camera = camera, ?exposureStops = exposure)
                renderer <- Some active
                ProbeLifecycle.attachedOnce ()
                active

            let draw _ bounds =
                if frameLimit > 0 && renderedFrames >= frameLimit then
                    false
                else
                    nativeBounds <- bounds
                    render ()
                    diagnosticInterval |> Option.iter Threading.Thread.Sleep
                    true

            worker <- Some(new RenderWorker<_, _>(create, draw, onError))

            let publish () =
                worker
                |> Option.iter (fun active -> active.Publish(MatchPitchNative.bounds this))

            publish ()
            let loop = new PresentationLoop(topLevel.RequestAnimationFrame, publish, onError)
            presentation <- Some loop
            loop.Start()

    override this.OnDetachedFromVisualTree args =
        presentation |> Option.iter _.Stop()
        presentation <- None
        worker |> Option.iter (fun value -> (value :> IDisposable).Dispose())
        worker <- None
        let wasAttached = renderer.IsSome
        renderer <- None

        if wasAttached then
            ProbeLifecycle.detachedOnce ()

        base.OnDetachedFromVisualTree args

[<Sealed>]
type private ProbeApp() =
    inherit Application()

    override _.Initialize() = ()

    override this.OnFrameworkInitializationCompleted() =
        match base.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let createWindow () =
                let fullscreen =
                    Environment.GetEnvironmentVariable("MATCHVIEWER_PROBE_FULLSCREEN") = "1"

                let root = Grid()
                root.RowDefinitions.Add(RowDefinition((if fullscreen then 0.0 else 52.0), GridUnitType.Pixel))
                root.RowDefinitions.Add(RowDefinition(1.0, GridUnitType.Star))
                root.RowDefinitions.Add(RowDefinition((if fullscreen then 0.0 else 40.0), GridUnitType.Pixel))
                root.Background <- SolidColorBrush(Color.Parse("#0b1020"))
                let header = Grid()
                header.ColumnDefinitions.Add(ColumnDefinition(1.0, GridUnitType.Star))
                header.ColumnDefinitions.Add(ColumnDefinition(170.0, GridUnitType.Pixel))

                let title =
                    TextBlock(
                        Text = "MATCH VIEWER  ·  LIVE HUD PROBE",
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = Thickness(20.0, 0.0)
                    )

                header.Children.Add(title) |> ignore

                let hudButton =
                    Button(
                        Content = "HUD CONTROL",
                        Background = SolidColorBrush(Color.Parse("#18243a")),
                        Foreground = Brushes.White,
                        Margin = Thickness(8.0),
                        VerticalAlignment = VerticalAlignment.Center
                    )

                hudButton.Click.Add(fun _ -> hudButton.Content <- "HUD CLICK OK")
                Grid.SetColumn(hudButton, 1)
                header.Children.Add(hudButton) |> ignore
                Grid.SetRow(header, 0)
                root.Children.Add(header) |> ignore
                let pitch = ProbePitch()
                Grid.SetRow(pitch, 1)
                root.Children.Add(pitch) |> ignore

                let footer =
                    TextBlock(
                        Text = "LIVE MATCH  ·  WEBGPU BLEND SCENE",
                        Foreground = Brushes.LightGreen,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = Thickness(20.0, 0.0)
                    )

                Grid.SetRow(footer, 2)
                root.Children.Add(footer) |> ignore

                Window(
                    Title = "FootballEngine stadium renderer probe",
                    Width = 1160.0,
                    Height = 790.0,
                    WindowState =
                        (if fullscreen then
                             WindowState.FullScreen
                         else
                             WindowState.Normal),
                    Content = root
                )

            if ProbeLifecycle.cycles = 0 then
                let window = createWindow ()
                desktop.MainWindow <- window
                window.Show()
            else
                desktop.ShutdownMode <- ShutdownMode.OnExplicitShutdown
                let mutable completed = 0
                let mutable activeWindow: Window option = None
                let cycleTimer = DispatcherTimer(Interval = ProbeLifecycle.interval)

                cycleTimer.Tick.Add(fun _ ->
                    cycleTimer.Stop()
                    activeWindow |> Option.iter (fun window -> window.Close()))

                let rec openNext () =
                    let window = createWindow ()
                    activeWindow <- Some window
                    desktop.MainWindow <- window

                    window.Closed.Add(fun _ ->
                        completed <- completed + 1

                        if completed >= ProbeLifecycle.cycles then
                            let attached = ProbeLifecycle.attachCount ()
                            let detached = ProbeLifecycle.detachCount ()

                            Console.WriteLine(
                                $"Lifecycle cycles={completed}, renderer attaches={attached}, detaches={detached}"
                            )

                            desktop.Shutdown(
                                if attached = completed && detached = completed then
                                    0
                                else
                                    1
                            )
                        else
                            Dispatcher.UIThread.Post(fun () -> openNext ()))

                    window.Show()
                    cycleTimer.Start()

                openNext ()
        | _ -> ()

        base.OnFrameworkInitializationCompleted()

module Program =
    type private ValidationTraceListener() =
        inherit Diagnostics.TraceListener()
        let mutable errors = 0
        member _.ErrorCount = errors

        override _.Write(message: string) =
            if
                not (isNull message)
                && message.Contains("WebGPU uncaptured error", StringComparison.Ordinal)
            then
                Threading.Interlocked.Increment(&errors) |> ignore

        override this.WriteLine(message: string) = this.Write(message)

    let private runNative args =
        use validation = new ValidationTraceListener()
        Diagnostics.Trace.Listeners.Add(validation) |> ignore

        Diagnostics.Trace.Listeners.Add(new Diagnostics.TextWriterTraceListener(Console.Error))
        |> ignore

        Diagnostics.Trace.AutoFlush <- true

        let exitCode =
            AppBuilder.Configure<ProbeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args)

        Diagnostics.Trace.Listeners.Remove(validation)
        if validation.ErrorCount = 0 then exitCode else 1

    [<EntryPoint>]
    let main args =
        match args with
        | [| "--motion-bench"; output |] -> ProceduralMotionBench.run output
        | [| "--motion-corpus"; output |] -> MotionCorpus.run output
        | _ -> runNative args
