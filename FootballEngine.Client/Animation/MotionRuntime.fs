namespace FootballEngine.Client.Animation

open System
open FootballEngine.Core.Match

type MotionCheckpointIdentity =
    { PlannerVersion: int
      ConfigHash: string
      RigHash: string
      ProfileHash: string
      TrackId: uint64 }

type MotionCheckpoint =
    { Identity: MotionCheckpointIdentity
      Tick: int
      Cursor: uint64
      ActorCount: int
      Plans: ActorPlan[]
      Tasks: TaskReservation[] }

module MotionCheckpoint =
    let valid config identity (checkpoint: MotionCheckpoint) =
        checkpoint.Identity = identity
        && checkpoint.Tick >= 0
        && checkpoint.Cursor >= 1UL
        && checkpoint.ActorCount >= 0
        && checkpoint.ActorCount <= config.MaxActors
        && checkpoint.Plans.Length = checkpoint.ActorCount
        && checkpoint.Tasks.Length = checkpoint.ActorCount * config.TasksPerActor
        && (checkpoint.Plans
            |> Array.forall (fun plan -> MotionValidation.plan plan && plan.Tick = checkpoint.Tick))
        && (checkpoint.Plans |> Array.map _.Root.Actor |> Array.distinct |> Array.length) = checkpoint.ActorCount
        && (checkpoint.Tasks
            |> Array.mapi (fun index task ->
                if not task.Active then
                    true
                else
                    let root = checkpoint.Plans[index / config.TasksPerActor].Root

                    MotionValidation.evidence task.Evidence
                    && task.Evidence.Actor = root.Actor
                    && task.Evidence.Generation = root.Generation
                    && task.Evidence.Sequence < checkpoint.Cursor
                    && int task.Evidence.AvailableAt <= checkpoint.Tick
                    && task.PreparedAt >= -1
                    && task.PreparedAt <= checkpoint.Tick
                    && task.CancelledAt = -1
                    && task.Evidence.Phase <> MotionEvidencePhase.Cancelled)
            |> Array.forall id)

/// Sequential ownership ends at sealed plans; evaluating a retained plan never advances gait.
type MotionRuntime(config: AnimationConfig, identity: MotionCheckpointIdentity, ?historical: bool) =
    let accepted: Result<unit, MotionRuntimeError> = Ok()

    let windowTicks =
        if defaultArg historical false then
            config.RecoveryWindowTicks
        else
            config.WindowTicks

    let plans = Array.zeroCreate<ActorPlan> (windowTicks * config.MaxActors)
    let ticks = Array.create windowTicks -1
    let counts = Array.zeroCreate<int> windowTicks
    let current = Array.zeroCreate<ActorPlan> config.MaxActors
    let previousPlans = Array.zeroCreate<ActorPlan> config.MaxActors
    let mutable recovery: MotionRuntime option = None

    let tasks =
        Array.zeroCreate<TaskReservation> (config.MaxActors * config.TasksPerActor)

    let previousTasks = Array.zeroCreate<TaskReservation> tasks.Length

    let mutable actorCount = 0
    let mutable frontier = -1
    let mutable cursor = 1UL
    let mutable epoch = 0UL
    let mutable closed = false
    let mutable residualCount = 0UL

    let findActor actor =
        let mutable index = 0
        let mutable found = -1

        while index < actorCount && found < 0 do
            if current[index].Root.Actor = actor then
                found <- index

            index <- index + 1

        found

    member _.Frontier = frontier
    member _.Epoch = epoch
    member _.ResidualCount = residualCount
    member _.ActorCount = actorCount
    member _.Config = config
    member _.RetentionTicks = windowTicks
    member _.Identity = identity

    member this.PublishRecovery(work: MotionRuntime, expectedEpoch: uint64) =
        if closed then
            Error RuntimeClosed
        elif expectedEpoch <> epoch then
            Error StaleEpoch
        elif
            work.Identity <> identity
            || work.Config <> config
            || obj.ReferenceEquals(work, this)
        then
            Error IncompatibleCheckpoint
        else
            recovery <- Some work
            Ok()

    member this.Ingest(boundary: MotionBoundary) =
        let tick = int boundary.Tick

        if closed then
            Error RuntimeClosed
        elif frontier >= 0 && tick = frontier then
            accepted
        elif tick <> frontier + 1 then
            Error(NonCanonicalBoundary(frontier + 1, tick))
        elif boundary.Roots.Length > config.MaxActors then
            Error(ActorCapacityExceeded(config.MaxActors, boundary.Roots.Length))
        elif boundary.Roots |> Array.exists (fun root -> not (MotionValidation.root root)) then
            Error InvalidMotionBoundary
        else
            let mutable invalid = false

            for index = 0 to boundary.Roots.Length - 1 do
                for other = 0 to index - 1 do
                    if boundary.Roots[index].Actor = boundary.Roots[other].Actor then
                        invalid <- true

            let mutable nextCursor = cursor
            let mutable sequenceError = None

            for record in boundary.Evidence do
                if not (MotionValidation.evidence record) || record.AvailableAt > boundary.Tick then
                    invalid <- true

                if record.Sequence <> nextCursor && sequenceError.IsNone then
                    sequenceError <- Some(MotionSequenceGap(nextCursor, record.Sequence))

                nextCursor <- record.Sequence + 1UL

            if invalid then Error InvalidMotionBoundary
            elif sequenceError.IsSome then Error sequenceError.Value
            else this.IngestValidated boundary

    member private _.IngestValidated(boundary: MotionBoundary) =
        let tick = int boundary.Tick
        let previousCount = actorCount
        Array.Copy(current, previousPlans, current.Length)
        Array.Copy(tasks, previousTasks, tasks.Length)
        Array.Clear tasks
        actorCount <- boundary.Roots.Length

        for actor = 0 to actorCount - 1 do
            let root = boundary.Roots[actor]
            let mutable previousIndex = -1
            let mutable scan = 0

            while scan < previousCount && previousIndex < 0 do
                if previousPlans[scan].Root.Actor = root.Actor then
                    previousIndex <- scan

                scan <- scan + 1

            let previous =
                if previousIndex < 0 then
                    current[actor]
                else
                    previousPlans[previousIndex]

            current[actor] <-
                if
                    frontier < 0
                    || previousIndex < 0
                    || root.Actor <> previous.Root.Actor
                    || root.Generation <> previous.Root.Generation
                    || root.Active <> previous.Root.Active
                then
                    SupportPlanner.initialize config tick root
                else
                    SupportPlanner.advance config tick root previous

            if previousIndex >= 0 && previous.Root.Generation = root.Generation then
                Array.Copy(
                    previousTasks,
                    previousIndex * config.TasksPerActor,
                    tasks,
                    actor * config.TasksPerActor,
                    config.TasksPerActor
                )

        for record in boundary.Evidence do
            let actor = findActor record.Actor

            if actor >= 0 && current[actor].Root.Generation = record.Generation then
                match TaskPlanner.ingest tasks (actor * config.TasksPerActor) config.TasksPerActor record with
                | ValueSome _ -> residualCount <- residualCount + 1UL
                | ValueNone -> ()

            // Retired participation still consumes journal sequence without reserving a new actor's limbs.
            cursor <- record.Sequence + 1UL

        let slot = tick % windowTicks
        Array.Copy(current, 0, plans, slot * config.MaxActors, actorCount)
        ticks[slot] <- tick
        counts[slot] <- actorCount
        frontier <- tick
        accepted

    member _.TryPlan(actor: PlayerId, time: MotionTime) =
        let tick = min frontier time.Tick
        let oldest = max 0 (frontier - windowTicks + 1)

        if closed then
            Error RuntimeClosed
        elif tick < oldest || tick < 0 || ticks[tick % windowTicks] <> tick then
            Error(WindowExpired oldest)
        else
            let mutable index = 0
            let mutable found = ValueNone
            let offset = (tick % windowTicks) * config.MaxActors

            while index < counts[tick % windowTicks] && found.IsNone do
                let plan = plans[offset + index]

                if plan.Root.Actor = actor then
                    found <- ValueSome plan

                index <- index + 1

            Ok found

    member _.Checkpoint() =
        { Identity = identity
          Tick = frontier
          Cursor = cursor
          ActorCount = actorCount
          Plans = current[0 .. actorCount - 1]
          Tasks = tasks[0 .. actorCount * config.TasksPerActor - 1] }

    member _.BeginSeek() =
        epoch <- epoch + 1UL
        recovery <- None
        epoch

    member _.Restore(checkpoint: MotionCheckpoint, expectedEpoch: uint64) =
        if closed then
            Error RuntimeClosed
        elif expectedEpoch <> epoch then
            Error StaleEpoch
        elif not (MotionCheckpoint.valid config identity checkpoint) then
            Error IncompatibleCheckpoint
        else
            Array.Fill(ticks, -1)
            Array.Clear current
            Array.Clear tasks
            Array.Copy(checkpoint.Plans, current, checkpoint.ActorCount)
            Array.Copy(checkpoint.Tasks, tasks, checkpoint.Tasks.Length)
            actorCount <- checkpoint.ActorCount
            frontier <- checkpoint.Tick
            cursor <- checkpoint.Cursor

            if frontier >= 0 then
                let slot = frontier % windowTicks
                ticks[slot] <- frontier
                counts[slot] <- actorCount
                Array.Copy(current, 0, plans, slot * config.MaxActors, actorCount)

            Ok()

    member _.Close() =
        epoch <- epoch + 1UL
        closed <- true
        recovery <- None

    interface IMotionPlanSource with
        member _.TryCopyPlan(actor, time, plan) =
            let tick = min frontier time.Tick
            let mutable found = false

            if
                not closed
                && tick >= max 0 (frontier - windowTicks + 1)
                && tick >= 0
                && ticks[tick % windowTicks] = tick
            then
                let offset = (tick % windowTicks) * config.MaxActors
                let mutable index = 0

                while index < counts[tick % windowTicks] && not found do
                    if plans[offset + index].Root.Actor = actor then
                        plan <- plans[offset + index]
                        found <- true

                    index <- index + 1

            if not found then
                match recovery with
                | Some work when time.Tick <= work.Frontier ->
                    found <- (work :> IMotionPlanSource).TryCopyPlan(actor, time, &plan)
                | _ -> ()

            found
