namespace FootballEngine.Core.Match

/// 40 Hz physics cadence: 2400 whole subticks per match minute keep minute
/// arithmetic exact and integer (see minuteSubticks).
type SimulationClock = { SubTicksPerSecond: int }

module SimulationClock =

    let defaultClock = { SubTicksPerSecond = 40 }

    let dt (clock: SimulationClock) : float<second> =
        (1.0<second> / float clock.SubTicksPerSecond)

    /// Players perceive and decide at half the physics rate (20 Hz) — cognition
    /// is the amortized layer; ball physics is not.
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
