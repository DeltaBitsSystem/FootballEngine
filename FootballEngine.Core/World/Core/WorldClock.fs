namespace FootballEngine.World

open System

module WorldClockOps =

    let init (season: int) : WorldClock =
        { Current =
            { Day = 1
              Week = 1
              Month = 1
              Season = season }
          PendingEvents = []
          Revision = 0UL
          ScheduledWork = []
          ConsumedWork = Set.empty
          PhaseHistory = [] }

    let advance (clock: WorldClock) : WorldClock =
        let d = clock.Current.Day + 1

        { clock with
            Current =
                { clock.Current with
                    Day = d
                    Week = (d - 1) / 7 + 1
                    Month = (d - 1) / 30 + 1 }
            PendingEvents = clock.PendingEvents |> List.filter (fun e -> e.At.Day > clock.Current.Day)
            PhaseHistory = [] }

    let schedule (ev: PendingEvent) (clock: WorldClock) : WorldClock =
        { clock with
            PendingEvents = ev :: clock.PendingEvents |> List.sortBy (fun e -> e.At.Day) }

    let scheduleWork (work: ScheduledWork) (clock: WorldClock) : WorldClock =
        { clock with
            ScheduledWork =
                work :: clock.ScheduledWork
                |> List.sortBy (fun w -> w.EffectiveDate, w.Phase, w.Id) }

    let dueWork (date: DateTime) (phase: WorldPhaseTag) (clock: WorldClock) =
        clock.ScheduledWork
        |> List.filter (fun w ->
            w.EffectiveDate.Date <= date.Date
            && w.Phase = phase
            && not (Set.contains w.Id clock.ConsumedWork))
        |> List.sortBy (fun w -> w.EffectiveDate, w.Phase, w.Id)

    let consumeWork (work: ScheduledWork list) (clock: WorldClock) =
        { clock with
            ConsumedWork = work |> List.map _.Id |> Set.ofList |> Set.union clock.ConsumedWork }

    let appendTrace (trace: WorldPhaseTrace) (clock: WorldClock) =
        { clock with
            PhaseHistory = clock.PhaseHistory @ [ trace ] }

    let currentEvents (clock: WorldClock) : WorldEventType list =
        clock.PendingEvents
        |> List.filter (fun e -> e.At.Day = clock.Current.Day)
        |> List.map (fun e -> e.Type)

    let shouldWake (trigger: Trigger) (clock: WorldClock) : bool =
        match trigger with
        | OnSchedule freq -> WorldTime.shouldRun freq clock.Current
        | OnEvent evType -> currentEvents clock |> List.contains evType
        | Both(freq, evType) ->
            WorldTime.shouldRun freq clock.Current
            || currentEvents clock |> List.contains evType
