namespace FootballEngine.Core.Tests.Contracts

open Expecto
open FootballEngine.Core.Match

module ClockTests =

    let clockTests =
        testList
            "FootballEngine.Core.Contracts.Clock"
            [ test "dt and dtPlayer values" {
                  let clock = SimulationClock.defaultClock
                  Expect.equal (SimulationClock.dt clock) 0.025<second> "dt must be 1/40 s"
                  Expect.equal (SimulationClock.dtPlayer clock) 0.05<second> "dtPlayer must be 2/40 s"
              }

              test "toMatchMinute floors" {
                  let clock = SimulationClock.defaultClock

                  let minute (tick: int<subtick>) = SimulationClock.toMatchMinute tick

                  Expect.equal (minute 0<subtick>) 0<matchMin> "tick 0 -> minute 0"
                  Expect.equal (minute 2399<subtick>) 0<matchMin> "just before 1 min -> 0"
                  Expect.equal (minute 2400<subtick>) 1<matchMin> "2400 ticks -> minute 1"
                  Expect.equal (minute 107999<subtick>) 44<matchMin> "45 min boundary floors to 44"
                  Expect.equal (minute 108000<subtick>) 45<matchMin> "45 min exactly"
              }

              test "secondsToSubTicks round-trips" {
                  let clock = SimulationClock.defaultClock
                  let back = SimulationClock.secondsToSubTicks clock 2.5<second>
                  Expect.equal back 100<subtick> "2.5 s -> 100 subticks"

                  let roundTrip = SimulationClock.secondsToSubTicks clock 90.0<second>
                  Expect.equal roundTrip 3600<subtick> "90 s -> 3600 subticks"
              } ]
