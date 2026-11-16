namespace FootballEngine

open System
open System.Diagnostics
open System.Threading
open FootballEngine.AppTypes
open FootballEngine.Core.Match
open FootballEngine.LiveMatch
open FootballEngine.Client.Animation

type PresentationDiscontinuity =
    | MatchFlowChanged of previous: byte * current: byte
    | ParticipationChanged
    | SimulationTickRegressed
    | Seeked

type PresentationPose =
    { Sequence: uint64
      Epoch: uint64
      EpochStart: PresentationDiscontinuity option
      Tick: int<subtick>
      CapturedAt: int64
      Snapshot: Replay.ReplaySnapshot }

type PresentationSample =
    | Hold of PresentationPose
    | Between of PresentationPose * PresentationPose * float

type PresentationEvent =
    { Sequence: uint64
      Id: MatchEventId
      Tick: int<subtick>
      Event: MatchEvent }

/// A latest-window visual timeline. Rendering never waits for the simulation.
type MatchPresentationStream(runtimeId: MatchRuntimeId, policy: LiveExecutionPolicy) =
    let gate = obj ()

    let mutable motion: MotionRuntime option = None
    let motionArchive = MotionCheckpointArchive.Writer()
    let mutable motionRecoveryRequest = ValueNone
    let mutable motionRecoveryInFlight = ValueNone
    let mutable motionRecoveryError: string option = None
    // The checkpoint archive must be contiguous from kickoff; a timeline reset by a seek is not, so it stops recording.
    let mutable archiveEnabled = true

    let slots: PresentationPose option[] = Array.zeroCreate policy.BufferCapacity
    let events = ResizeArray<PresentationEvent>()
    let mutable writeIndex = 0
    let mutable count = 0
    let mutable sequence = 0UL
    let mutable eventSequence = 0UL
    let mutable epoch = 0UL
    let mutable timelineStartSequence = 0UL
    let mutable droppedPoseCount = 0UL
    let mutable underrunCount = 0UL
    let mutable underrunActive = false
    let mutable lastCapturedAt = Int64.MinValue
    let mutable closed = false
    let mutable clockStarted = false
    let mutable pendingSeek = false
    let mutable lastClockTimestamp = 0L
    let mutable presentationTick = 0.0
    let mutable paused = false
    let mutable speed = Normal
    // Fast-forward factor of the key-moment pacing, applied on top of the chosen speed so the viewer clock keeps up
    // with the sparse poses published while cruising.
    let mutable pace = 1.0

    let speedValue value =
        match value with
        | Normal -> 1.0
        | Fast -> 2.0
        | VeryFast -> 4.0

    let ticksPerSecond = float SimulationClock.defaultClock.SubTicksPerSecond

    let advanceClock timestamp =
        if clockStarted && not paused then
            let elapsed = float (timestamp - lastClockTimestamp) / float Stopwatch.Frequency
            let desired = presentationTick + elapsed * ticksPerSecond * speedValue speed * pace

            let latestIndex = (writeIndex - 1 + slots.Length) % slots.Length

            presentationTick <-
                match slots[latestIndex] with
                | Some latest -> min desired (float latest.Tick)
                | None -> presentationTick

        lastClockTimestamp <- timestamp

    let orderedSnapshot () =
        [| for offset in 0 .. count - 1 do
               let index = (writeIndex - count + offset + slots.Length) % slots.Length

               match slots[index] with
               | Some pose -> yield pose
               | None -> () |]

    let currentTimeline () =
        orderedSnapshot ()
        |> Array.filter (fun pose -> pose.Sequence >= timelineStartSequence)

    member _.RuntimeId = runtimeId
    member _.Capacity = slots.Length
    member _.IsClosed = lock gate (fun () -> closed)
    member _.DroppedPoseCount = lock gate (fun () -> droppedPoseCount)
    member _.UnderrunCount = lock gate (fun () -> underrunCount)

    member _.ConfigureMotion(identity: MotionCheckpointIdentity) =
        lock gate (fun () ->
            if closed then
                Error RuntimeClosed
            elif
                identity.PlannerVersion <> MotionIdentity.plannerVersion
                || identity.ConfigHash <> MotionIdentity.configHash AnimationConfig.defaultConfig
            then
                Error IncompatibleCheckpoint
            else
                match motion with
                | Some runtime when runtime.Identity = identity -> Ok()
                | Some _ -> Error IncompatibleCheckpoint
                | None ->
                    motion <- Some(MotionRuntime(AnimationConfig.defaultConfig, identity))
                    Ok())

    /// Restarts the timeline at a seek target. Sequence counters stay monotonic so reader cursors held
    /// before the reset remain valid and simply see nothing older.
    member _.Reset(startTick: int<subtick>, startCursor: uint64) =
        lock gate (fun () ->
            if not closed then
                for index = 0 to slots.Length - 1 do
                    slots[index] <- None

                count <- 0
                writeIndex <- 0
                events.Clear()
                clockStarted <- false
                underrunActive <- false
                pendingSeek <- true
                archiveEnabled <- false
                motionRecoveryRequest <- ValueNone
                motionRecoveryInFlight <- ValueNone
                motionRecoveryError <- None

                match motion with
                | Some old ->
                    let identity = old.Identity
                    old.Close()

                    motion <-
                        Some(
                            MotionRuntime(
                                AnimationConfig.defaultConfig,
                                identity,
                                startTick = int startTick,
                                startCursor = startCursor
                            )
                        )
                | None -> ())

    member _.PublishMotion(boundaries: MotionBoundary[]) =
        lock gate (fun () ->
            let mutable error = None

            for boundary in boundaries do
                if error.IsNone then
                    match motion with
                    | None -> error <- Some MotionIdentityNotConfigured
                    | Some runtime ->
                        let previous = runtime.Frontier

                        match runtime.Ingest boundary with
                        | Error value -> error <- Some value
                        | Ok() when
                            archiveEnabled
                            && int boundary.Tick > previous
                            && int boundary.Tick % MotionTrack.checkpointInterval = 0
                            ->
                            match motionArchive.Append(runtime.Checkpoint()) with
                            | Ok() -> ()
                            | Error _ -> error <- Some IncompatibleCheckpoint
                        | Ok() -> ()

            match error with
            | Some value -> Error value
            | None -> Ok())

    member _.MotionPlan(actor: FootballEngine.Core.Match.PlayerId, time: MotionTime) =
        lock gate (fun () ->
            match motion with
            | Some runtime -> runtime.TryPlan(actor, time)
            | None -> Error MotionIdentityNotConfigured)

    member _.MotionCheckpoint() =
        lock gate (fun () -> motion |> Option.map _.Checkpoint())

    member _.MotionArchiveSection() : Replay.ReplaySection =
        lock gate (fun () ->
            { Kind = 2u
              Version = 1
              Payload = motionArchive.Complete() })

    member _.MotionRigMatches(hash: string) =
        lock gate (fun () -> motion |> Option.exists (fun runtime -> runtime.Identity.RigHash = hash))

    member _.MotionRecoveryError = lock gate (fun () -> motionRecoveryError)

    /// The simulation/archive owner supplies a block; reconstruction never runs on the render thread.
    member _.RecoverMotion(readBlock, cancellation: CancellationToken) =
        let request =
            lock gate (fun () ->
                match motion, motionRecoveryRequest with
                | Some runtime, ValueSome tick when not closed ->
                    motionRecoveryRequest <- ValueNone

                    match motionArchive.Preceding(runtime.Config, runtime.Identity, tick) with
                    | Error message ->
                        motionRecoveryError <- Some message
                        Error message
                    | Ok checkpoint ->
                        let epoch = runtime.BeginSeek()
                        motionRecoveryInFlight <- ValueSome epoch

                        let target =
                            min
                                runtime.Frontier
                                (min
                                    (checkpoint.Tick + MotionTrack.checkpointInterval - 1)
                                    (tick + runtime.Config.RecoveryWindowTicks - 1))

                        Ok(Some(runtime.Config, runtime.Identity, checkpoint, target, epoch))
                | _ -> Ok None)

        match request with
        | Error message -> Error message
        | Ok None -> Ok false
        | Ok(Some(config, identity, checkpoint, target, epoch)) ->
            let rebuilt =
                MotionReconstruction.rebuild config identity checkpoint target readBlock cancellation

            lock gate (fun () ->
                match motion with
                | Some runtime when not closed && runtime.Epoch = epoch && not cancellation.IsCancellationRequested ->
                    motionRecoveryInFlight <- ValueNone

                    match rebuilt with
                    | Error message ->
                        motionRecoveryError <- Some message
                        Error message
                    | Ok work ->
                        runtime.PublishRecovery(work, epoch)
                        |> Result.map (fun () ->
                            motionRecoveryError <- None
                            true)
                        |> Result.mapError (sprintf "%A")
                | _ -> Error "motion recovery publication was cancelled or superseded")

    member _.Publish(tick: int<subtick>, snapshot: Replay.ReplaySnapshot, capturedAt: int64) =
        lock gate (fun () ->
            if not closed then
                sequence <- sequence + 1UL

                let previous =
                    if count = 0 then
                        None
                    else
                        let latestIndex = (writeIndex - 1 + slots.Length) % slots.Length
                        slots[latestIndex]

                let epochStart =
                    if pendingSeek then
                        pendingSeek <- false
                        Some Seeked
                    else
                        previous
                        |> Option.bind (fun pose ->
                            if pose.Snapshot.Flow <> snapshot.Flow then
                                Some(MatchFlowChanged(pose.Snapshot.Flow, snapshot.Flow))
                            elif
                                pose.Snapshot.HomeActive <> snapshot.HomeActive
                                || pose.Snapshot.AwayActive <> snapshot.AwayActive
                            then
                                Some ParticipationChanged
                            elif tick <= pose.Tick then
                                Some SimulationTickRegressed
                            else
                                None)

                if epochStart.IsSome then
                    epoch <- epoch + 1UL

                let capturedAt = max capturedAt (lastCapturedAt + 1L)
                lastCapturedAt <- capturedAt

                if count = slots.Length then
                    droppedPoseCount <- droppedPoseCount + 1UL

                if epochStart = Some SimulationTickRegressed then
                    timelineStartSequence <- sequence
                    presentationTick <- float tick
                    lastClockTimestamp <- capturedAt
                    underrunActive <- false

                slots[writeIndex] <-
                    Some
                        { Sequence = sequence
                          Epoch = epoch
                          EpochStart = epochStart
                          Tick = tick
                          CapturedAt = capturedAt
                          Snapshot = snapshot }

                writeIndex <- (writeIndex + 1) % slots.Length
                count <- min slots.Length (count + 1)

                if not clockStarted then
                    clockStarted <- true
                    lastClockTimestamp <- capturedAt
                    presentationTick <- float tick - policy.PresentationDelay.TotalSeconds * ticksPerSecond

            sequence)

    member _.TryReadAfter(afterSequence: uint64) : PresentationPose array =
        lock gate (fun () -> orderedSnapshot () |> Array.filter (fun pose -> pose.Sequence > afterSequence))

    member _.PublishEvents(published: MatchEvent array) =
        lock gate (fun () ->
            if not closed then
                for event in published do
                    eventSequence <- eventSequence + 1UL

                    events.Add
                        { Sequence = eventSequence
                          Id = MatchEventId(EventId.value (MatchEvent.id event))
                          Tick = event.Tick
                          Event = event }

            eventSequence)

    member _.ReadEventsAfter(afterSequence: uint64) : PresentationEvent array =
        lock gate (fun () -> events |> Seq.filter (fun item -> item.Sequence > afterSequence) |> Seq.toArray)

    member _.Bracket(renderTick: float) : (PresentationPose * PresentationPose * float) option =
        lock gate (fun () ->
            let poses = currentTimeline ()

            if poses.Length = 0 then
                None
            else
                let mutable previous = poses[0]
                let mutable result = None

                for pose in poses do
                    if result.IsNone && float pose.Tick >= renderTick then
                        if pose.Sequence = previous.Sequence then
                            result <- Some(previous, pose, 0.0)
                        elif pose.Epoch <> previous.Epoch then
                            let held = if renderTick < float pose.Tick then previous else pose
                            result <- Some(held, held, 0.0)
                        elif pose.Tick <= previous.Tick then
                            result <- Some(pose, pose, 0.0)
                        else
                            let duration = float pose.Tick - float previous.Tick
                            let t = Math.Clamp((renderTick - float previous.Tick) / duration, 0.0, 1.0)
                            result <- Some(previous, pose, t)

                    previous <- pose

                result |> Option.orElseWith (fun () -> Some(previous, previous, 1.0)))

    member _.Sample(nowTimestamp: int64) : PresentationSample option =
        lock gate (fun () ->
            if closed || count = 0 then
                None
            else
                advanceClock nowTimestamp
                let poses = currentTimeline ()
                let mutable target = presentationTick
                let mutable first = None
                let mutable last = None
                let mutable previous = None
                let mutable result = None

                if poses.Length = 0 then
                    None
                else
                    let oldest = poses[0]

                    if droppedPoseCount > 0UL && target < float oldest.Tick then
                        if not underrunActive then
                            underrunCount <- underrunCount + 1UL

                        underrunActive <- true
                        presentationTick <- float oldest.Tick
                        target <- float oldest.Tick
                    else
                        underrunActive <- false

                    for pose in poses do
                        if first.IsNone then
                            first <- Some pose

                        last <- Some pose

                        if result.IsNone && float pose.Tick >= target then
                            match previous with
                            | None -> result <- Some(Hold pose)
                            | Some prior when pose.Epoch <> prior.Epoch ->
                                result <- Some(Hold(if target < float pose.Tick then prior else pose))
                            | Some prior when pose.Tick > prior.Tick ->
                                let dt = float pose.Tick - float prior.Tick
                                let t = Math.Clamp((target - float prior.Tick) / dt, 0.0, 1.0)
                                result <- Some(Between(prior, pose, t))
                            | Some _ -> result <- Some(Hold pose)

                        previous <- Some pose

                    match first, last, result with
                    | Some firstPose, Some _, _ when target <= float firstPose.Tick -> Some(Hold firstPose)
                    | Some _, Some lastPose, _ when target >= float lastPose.Tick -> Some(Hold lastPose)
                    | _, _, Some sample -> Some sample
                    | _ -> None)

    member _.SetPlayback(value: LiveSpeed, isPaused: bool, nowTimestamp: int64) =
        lock gate (fun () ->
            if not closed then
                advanceClock nowTimestamp
                speed <- value
                paused <- isPaused

                if not clockStarted then
                    lastClockTimestamp <- nowTimestamp

                clockStarted <- true
                lastClockTimestamp <- nowTimestamp)

    member _.SetPace(multiplier: float, nowTimestamp: int64) =
        lock gate (fun () ->
            if not closed then
                advanceClock nowTimestamp
                pace <- multiplier)

    member _.Close() =
        lock gate (fun () ->
            closed <- true
            motion |> Option.iter _.Close()
            motionRecoveryRequest <- ValueNone
            motionRecoveryInFlight <- ValueNone

            for index = 0 to slots.Length - 1 do
                slots[index] <- None

            count <- 0
            events.Clear())

    interface IDisposable with
        member this.Dispose() = this.Close()

    interface IMotionPlanSource with
        member _.TryCopyPlan(actor, time, plan) =
            Monitor.Enter gate

            try
                match motion with
                | Some runtime when not closed ->
                    let ready = (runtime :> IMotionPlanSource).TryCopyPlan(actor, time, &plan)

                    if
                        not ready
                        && time.Tick >= 0
                        && time.Tick < runtime.Frontier - runtime.Config.WindowTicks + 1
                        && motionRecoveryRequest.IsNone
                        && motionRecoveryInFlight.IsNone
                        && motionRecoveryError.IsNone
                    then
                        motionRecoveryRequest <- ValueSome time.Tick

                    ready
                | Some _ -> false
                | None -> false
            finally
                Monitor.Exit gate
