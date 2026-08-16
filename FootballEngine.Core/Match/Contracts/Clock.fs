namespace FootballEngine.Core.Match

type SimulationClock = { SubTicksPerSecond: int }

module SimulationClock =

    let defaultClock = { SubTicksPerSecond = 40 }

    let dt (clock: SimulationClock) : float<second> =
        (1.0<second> / float clock.SubTicksPerSecond)

    let dtPlayer (clock: SimulationClock) : float<second> =
        (2.0<second> / float clock.SubTicksPerSecond)

    let secondsToSubTicks (clock: SimulationClock) (seconds: float<second>) : int<subtick> =
        int (round (float seconds * float clock.SubTicksPerSecond)) * 1<subtick>

    let toMatchMinute (tick: int<subtick>) : int<matchMin> =
        int (float tick / (float defaultClock.SubTicksPerSecond * 60.0)) * 1<matchMin>

    let minuteSubticks: int<subtick> =
        int (60.0 * float defaultClock.SubTicksPerSecond) * 1<subtick>

[<Struct>]
type ClockPhase =
    | FirstHalf
    | HalfTime
    | SecondHalf
    | FullTime

[<Struct>]
type MatchClock =
    { Tick: int<subtick>
      Phase: ClockPhase }
// Invariant: monotonic; SecondHalf end = 90 min + decided stoppage (both from config)
