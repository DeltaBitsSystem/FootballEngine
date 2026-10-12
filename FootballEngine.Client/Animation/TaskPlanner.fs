namespace FootballEngine.Client.Animation

open FootballEngine.Core.Match

module TaskPlanner =
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

            if record.Phase = MotionEvidencePhase.Confirmed && prepared < 0 then
                ValueSome(InsufficientPreparation(record.Actor, record.TaskId))
            else
                ValueNone
