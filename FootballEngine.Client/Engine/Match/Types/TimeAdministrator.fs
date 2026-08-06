namespace FootballEngine

open FootballEngine.Types

module TimeAdministrator =

    let recordStoppage (tracker: StoppageTimeTracker) (subTick: int) (reason: StoppageReason) (clock: SimulationClock) =
        let seconds = tracker.Add(subTick, reason)
        let subTicks = SimulationClock.secondsToSubTicks clock (float seconds) |> int
        subTicks * 1<subtick>, seconds

    let totalAccumulated (tracker: StoppageTimeTracker) = tracker.AccumulatedSeconds

    let byCategory (tracker: StoppageTimeTracker) = tracker.AccumulatedByCategory

    let categorySeconds (tracker: StoppageTimeTracker) (reason: StoppageReason) =
        tracker.AccumulatedByCategory |> Map.tryFind reason |> Option.defaultValue 0

    let halfTimeAdded (tracker: StoppageTimeTracker) = tracker.DecideHalfTime()

    let fullTimeAdded (tracker: StoppageTimeTracker) = tracker.DecideFullTime()

    let isHalfTimeDecided (tracker: StoppageTimeTracker) = tracker.HalfTimeDecided

    let isFullTimeDecided (tracker: StoppageTimeTracker) = tracker.FullTimeDecided

    let entriesInHalf (tracker: StoppageTimeTracker) (halfTimeSubTick: int<subtick>) =
        tracker.Entries
        |> Seq.filter (fun e -> e.SubTick <= int halfTimeSubTick)
        |> Seq.toArray

    let entriesInSecondHalf (tracker: StoppageTimeTracker) (halfTimeSubTick: int<subtick>) =
        tracker.Entries
        |> Seq.filter (fun e -> e.SubTick > int halfTimeSubTick)
        |> Seq.toArray
