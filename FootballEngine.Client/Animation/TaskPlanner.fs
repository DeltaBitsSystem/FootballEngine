namespace FootballEngine.Client.Animation

open System
open System.Numerics
open FootballEngine.Core.Match

module TaskPlanner =
    let private locomotion config time (foot: FootPlan) =
        if foot.Phase = SupportPhase.Support || foot.Phase = SupportPhase.ToeOff then
            struct (foot.Anchor, Vector3.Zero)
        else
            Trajectory.evaluate config.TickSeconds time foot.Segment

    let overlayPoint config time (overlay: FootOverlay) =
        if time < overlay.Preparation.Finish then
            Trajectory.evaluatePreparation config.TickSeconds time overlay.Preparation
        else
            Trajectory.evaluate config.TickSeconds time overlay.Recovery

    let socketState tickSeconds time (overlay: FootOverlay) =
        let struct (segment, start, initialVelocity, target) =
            if time < overlay.Preparation.Finish then
                struct (overlay.Preparation, overlay.PreparationSocketStart, overlay.PreparationSocketVelocity, 1.0f)
            else
                struct (overlay.Recovery, overlay.RecoverySocketStart, overlay.RecoverySocketVelocity, 0.0f)
        let duration = (segment.Finish - segment.Start) * tickSeconds
        let u = Math.Clamp((time - segment.Start) / (segment.Finish - segment.Start), 0.0f, 1.0f)
        let u2 = u * u
        let u3 = u2 * u
        let value = start * (2.0f * u3 - 3.0f * u2 + 1.0f) + initialVelocity * duration * (u3 - 2.0f * u2 + u) + target * (-2.0f * u3 + 3.0f * u2)
        let velocity = (start * (6.0f * u2 - 6.0f * u) + initialVelocity * duration * (3.0f * u2 - 4.0f * u + 1.0f) + target * (-6.0f * u2 + 6.0f * u)) / duration
        struct (value, velocity)

    let private landing config time finish (root: MotionRootSample) effector =
        let horizon = max 0.0f (finish - time) * config.TickSeconds
        let future = { root with Position = root.Position + root.Velocity * horizon }
        SupportPlanner.nominal config future (effector = MotionEffector.LeftFoot)

    let private refreshRecovery config time root effector (overlay: FootOverlay) =
        let finish = overlay.Recovery.Finish
        let target = landing config time finish root effector
        if time < finish - 1.0f && Vector3.Distance(target, overlay.Recovery.Target) > config.RetargetDistance then
            if time < overlay.Recovery.Start then
                { overlay with Recovery = { overlay.Recovery with Target = target } }
            else
                let struct (mix, velocity) = socketState config.TickSeconds time overlay
                { overlay with
                    Recovery = Trajectory.retarget config.TickSeconds time target finish overlay.Recovery
                    RecoverySocketStart = mix
                    RecoverySocketVelocity = velocity }
        else
            overlay

    let sealFoot config tick (root: MotionRootSample) (reservations: TaskReservation[]) offset capacity effector (foot: FootPlan) (previous: FootOverlay) =
        let time = float32 tick
        let mutable selected = -1

        for i = offset to offset + capacity - 1 do
            let r = reservations[i]
            let e = r.Evidence
            let family =
                e.Family = MotionFamily.FootStrike
                || e.Family = MotionFamily.Reception
                || e.Family = MotionFamily.Dribble
                || e.Family = MotionFamily.PhysicalContact
            let pointAvailable = e.Phase = MotionEvidencePhase.Prepared || e.ConfirmedPoint.IsSome

            let existing = previous.Active && previous.TaskId = e.TaskId && previous.Sequence = e.Sequence
            if r.Active && family && e.Effector = effector && int e.AvailableAt <= tick && int e.EndsAt >= tick && pointAvailable && (int e.CommitAt > tick || existing) then
                if selected < 0 || e.CommitAt < reservations[selected].Evidence.CommitAt || (e.CommitAt = reservations[selected].Evidence.CommitAt && e.TaskId < reservations[selected].Evidence.TaskId) then
                    selected <- i

        let struct (current, velocity) =
            if previous.Active && time <= previous.Recovery.Finish then overlayPoint config time previous
            else locomotion config time foot
        let struct (currentSocket, socketVelocity) =
            if previous.Active && time <= previous.Recovery.Finish then socketState config.TickSeconds time previous
            else struct (0.0f, 0.0f)

        if selected >= 0 then
            let evidence = reservations[selected].Evidence
            if previous.Active && previous.TaskId = evidence.TaskId && previous.Sequence = evidence.Sequence then
                refreshRecovery config time root effector previous
            else
                let target =
                    if evidence.Phase = MotionEvidencePhase.Confirmed then evidence.ConfirmedPoint.Value
                    else evidence.PredictedPoint
                    |> MotionInput.vector
                let commit = float32 (int evidence.CommitAt)
                let finish = max (commit + 1.0f) (float32 (int evidence.EndsAt))
                let landing = landing config time finish root effector
                let preparation =
                    { Start = time; Finish = commit; From = current; Target = target
                      FromVelocity = velocity; TargetVelocity = Vector3.Zero; Clearance = config.Clearance }
                let recovery =
                    { Start = commit; Finish = finish; From = target; Target = landing
                      FromVelocity = Vector3.Zero; TargetVelocity = Vector3.Zero; Clearance = 0.0f }
                { TaskId = evidence.TaskId; Sequence = evidence.Sequence; Active = true
                  PreparationSocketStart = currentSocket; RecoverySocketStart = 1.0f
                  PreparationSocketVelocity = socketVelocity; RecoverySocketVelocity = 0.0f
                  Preparation = preparation; Recovery = recovery }
        elif previous.Active && time < previous.Recovery.Finish then
            if previous.Sequence = 0UL then
                refreshRecovery config time root effector previous
            else
                let finish = time + max 1.0f (config.SwingSeconds / config.TickSeconds)
                let landing = landing config time finish root effector
                let recovery =
                    { Start = time; Finish = finish; From = current; Target = landing
                      FromVelocity = velocity; TargetVelocity = Vector3.Zero; Clearance = 0.0f }
                { previous with Sequence = 0UL; PreparationSocketStart = currentSocket; RecoverySocketStart = currentSocket
                                PreparationSocketVelocity = socketVelocity; RecoverySocketVelocity = socketVelocity
                                Preparation = { recovery with Start = time - 1.0f; Finish = time }; Recovery = recovery }
        else
            FootOverlay.empty

    let ingest (reservations: TaskReservation[]) offset capacity (record: MotionEvidence) =
        let mutable slot = -1
        let mutable free = -1

        for index = offset to offset + capacity - 1 do
            let current = reservations[index]

            if current.Active && current.Evidence.TaskId = record.TaskId then
                slot <- index

            if not current.Active || current.Evidence.EndsAt < record.AvailableAt then
                if free < 0 then
                    free <- index

        if slot < 0 then
            slot <- free

        if slot < 0 then
            ValueSome(SupportConflict(record.Actor, record.Effector))
        else
            let previous = reservations[slot]

            let prepared =
                if record.Phase = MotionEvidencePhase.Prepared then
                    int record.AvailableAt
                elif previous.Active && previous.Evidence.TaskId = record.TaskId then
                    previous.PreparedAt
                else
                    -1

            reservations[slot] <-
                { Evidence = record
                  PreparedAt = prepared
                  CancelledAt =
                    if record.Phase = MotionEvidencePhase.Cancelled then
                        int record.AvailableAt
                    else
                        -1
                  Active = record.Phase <> MotionEvidencePhase.Cancelled }

            if record.Phase = MotionEvidencePhase.Confirmed && (prepared < 0 || record.AvailableAt >= record.CommitAt) then
                ValueSome(InsufficientPreparation(record.Actor, record.TaskId))
            else
                ValueNone
