namespace FootballEngine.World

module WorldClockOps =

    let init (season: int) : WorldClock =
        { Current =
            { Day = 1
              Week = 1
              Month = 1
              Season = season }
          PendingEvents = [] }

    let advance (clock: WorldClock) : WorldClock =
        let d = clock.Current.Day + 1

        { clock with
            Current =
                { clock.Current with
                    Day = d
                    Week = d / 7
                    Month = d / 30 }
            PendingEvents = clock.PendingEvents |> List.filter (fun e -> e.At.Day > clock.Current.Day) }

    let schedule (ev: PendingEvent) (clock: WorldClock) : WorldClock =
        { clock with
            PendingEvents = ev :: clock.PendingEvents |> List.sortBy (fun e -> e.At.Day) }

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
