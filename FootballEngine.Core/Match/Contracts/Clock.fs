namespace FootballEngine.Core.Match

/// 40 Hz physics cadence: 2400 whole subticks per match minute keep minute
/// arithmetic exact and integer (see minuteSubticks).
[<Struct>]
type SimTick = SimTick of int<subtick>

[<Struct>]
type PlayClock = PlayClock of int<subtick>

module SimTick =

    let value (SimTick tick) : int<subtick> = tick

    let ofSubTicks (tick: int<subtick>) : SimTick = SimTick tick

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

module PlayClock =

    let value (PlayClock tick) : int<subtick> = tick

    let ofSubTicks (tick: int<subtick>) : PlayClock = PlayClock tick

    let toMatchMinute (clock: PlayClock) : int<matchMin> =
        clock |> value |> SimulationClock.toMatchMinute

[<Struct>]
type ClockPhase =
    | FirstHalf
    | HalfTime
    | SecondHalf
    | ExtraTimeFirstHalf
    | ExtraTimeSecondHalf
    | FullTime

[<Struct>]
type MatchClock =
    { Tick: int<subtick>
      Phase: ClockPhase }

module MatchClock =

    let playClock (clock: MatchClock) : PlayClock = PlayClock.ofSubTicks clock.Tick
// Invariant: monotonic; SecondHalf end = 90 min + decided stoppage (both from config)
