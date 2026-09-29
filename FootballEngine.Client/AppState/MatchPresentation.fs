namespace FootballEngine

open System
open System.Diagnostics
open System.Threading
open FootballEngine.AppTypes
open FootballEngine.Core.Match
open FootballEngine.LiveMatch

type PresentationDiscontinuity =
    | MatchFlowChanged of previous: byte * current: byte
    | ParticipationChanged
    | SimulationTickRegressed

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
    let mutable lastClockTimestamp = 0L
    let mutable presentationTick = 0.0
    let mutable paused = false
    let mutable speed = Normal

    let speedValue value =
        match value with
        | Normal -> 1.0
        | Fast -> 2.0
        | VeryFast -> 4.0

    let ticksPerSecond = float SimulationClock.defaultClock.SubTicksPerSecond

    let advanceClock timestamp =
        if clockStarted && not paused then
            let elapsed = float (timestamp - lastClockTimestamp) / float Stopwatch.Frequency
            let desired = presentationTick + elapsed * ticksPerSecond * speedValue speed

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

    member _.Close() =
        lock gate (fun () ->
            closed <- true

            for index = 0 to slots.Length - 1 do
                slots[index] <- None

            count <- 0
            events.Clear())

    interface IDisposable with
        member this.Dispose() = this.Close()
