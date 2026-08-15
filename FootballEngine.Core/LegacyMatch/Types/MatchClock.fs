namespace FootballEngine.Types

open FootballEngine
open FootballEngine.Types

[<Struct>]
type ClockPhase =
    | FirstHalf of endsAt: int<subtick>
    | HalftimeTransition of remainingTicks: int
    | SecondHalf of endsAt: int<subtick>
    | FullTime

[<Struct>]
type MatchClock =
    { SubTick: int<subtick>
      Phase: ClockPhase }

module MatchClockModule =

    let advance
        (clock: SimulationClock)
        (tracker: StoppageTimeTracker)
        (config: TimingConfig)
        (state: MatchClock)
        : MatchClock =
        let tick = state.SubTick + 1<subtick>

        match state.Phase with
        | FirstHalf endsAt ->
            if tick >= endsAt then
                let halftimeDelay = TickDelay.delayFrom clock config.KickOffDelay |> int

                { SubTick = tick
                  Phase = HalftimeTransition halftimeDelay }
            else
                { SubTick = tick
                  Phase = FirstHalf endsAt }

        | HalftimeTransition remaining ->
            if remaining <= 0 then
                let secondHalfEnds = SimulationClock.fullTime clock

                { SubTick = tick
                  Phase = SecondHalf secondHalfEnds }
            else
                { SubTick = tick
                  Phase = HalftimeTransition(remaining - 1) }

        | SecondHalf endsAt ->
            if tick >= endsAt then
                let fullAdded = TimeAdministrator.fullTimeAdded tracker

                let newEnds =
                    max
                        endsAt
                        (SimulationClock.fullTime clock
                         + fullAdded * int clock.SubTicksPerSecond * 1<subtick>)

                if tick >= newEnds then
                    { SubTick = tick; Phase = FullTime }
                else
                    { SubTick = tick
                      Phase = SecondHalf newEnds }
            else
                { SubTick = tick
                  Phase = SecondHalf endsAt }

        | FullTime -> state
