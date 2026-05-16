namespace FootballEngine.Types

open FootballEngine
open FootballEngine.Types.PhysicsContract

[<Measure>] type subtick
[<Measure>] type matchMin
[<Measure>] type tickDelta

type SimulationClock =
    { SubTicksPerSecond: int }

module SimulationClock =
    let defaultClock =
        { SubTicksPerSecond = 40 }

    let dt (c: SimulationClock) : float<second> =
        1.0 / float c.SubTicksPerSecond * 1.0<second>

    let dtPlayer (c: SimulationClock) : float<second> =
        2.0 / float c.SubTicksPerSecond * 1.0<second>

    let secondsToSubTicks (c: SimulationClock) (s: float) : int<subtick> =
        int (s * float c.SubTicksPerSecond) * 1<subtick>

    let subTicksToSeconds (c: SimulationClock) (t: int<subtick>) : float =
        float t / float c.SubTicksPerSecond

    let halfTime (c: SimulationClock) : int<subtick> =
        secondsToSubTicks c (45.0 * 60.0)

    let fullTime (c: SimulationClock) : int<subtick> =
        secondsToSubTicks c (95.0 * 60.0)

    let toMatchMin (c: SimulationClock) (t: int<subtick>) : int<matchMin> =
        int (subTicksToSeconds c t / 60.0) * 1<matchMin>

    let deltaToSubtick (d: int<tickDelta>) : int<subtick> =
        int d * 1<subtick>

    let subtickToDelta (t: int<subtick>) : int<tickDelta> =
        int t * 1<tickDelta>

type TickDelay =
    { MeanST: int<tickDelta>
      StdST: int<tickDelta>
      MinST: int<tickDelta>
      MaxST: int<tickDelta> }

module TickDelay =
    let ofSeconds (clock: SimulationClock) mean std min max =
        { MeanST = SimulationClock.secondsToSubTicks clock mean |> int |> fun x -> x * 1<tickDelta>
          StdST = SimulationClock.secondsToSubTicks clock std |> int |> fun x -> x * 1<tickDelta>
          MinST = SimulationClock.secondsToSubTicks clock min |> int |> fun x -> x * 1<tickDelta>
          MaxST = SimulationClock.secondsToSubTicks clock max |> int |> fun x -> x * 1<tickDelta> }

    let delayFrom (clock: SimulationClock) (d: TickDelay) : int<tickDelta> =
        Stats.normalInt (float d.MeanST) (float d.StdST) (int d.MinST) (int d.MaxST)
        |> fun x -> x * 1<tickDelta>
