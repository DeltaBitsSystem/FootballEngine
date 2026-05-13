namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.TeamOrchestrator
open FootballEngine.Types
open SimStateOps


module AdaptiveSystem =

    let run (subTick: int) (clock: SimulationClock) (state: SimState) : SystemOutput[] =
        if subTick % clock.SubTicksPerSecond = 0 then
            [| for clubSide in [| HomeClub; AwayClub |] do
                  let stats = getMatchStats state clubSide
                  let emergent = getEmergentState state clubSide
                  let frame = getFrame state clubSide

                  let shortPassRate =
                      if stats.PassAttempts > 0 then
                          float stats.PassSuccesses / float stats.PassAttempts
                      else
                          0.5

                  let pressRate =
                      if stats.PressAttempts > 0 then
                          float stats.PressSuccesses / float stats.PressAttempts
                      else
                          0.5

                  let flankRate =
                      if stats.FlankAttempts > 0 then
                          float stats.FlankSuccesses / float stats.FlankAttempts
                      else
                          0.5

                  let mutable totalCond = 0
                  let mutable active = 0

                  for i = 0 to frame.SlotCount - 1 do
                      match frame.Physics.Occupancy[i] with
                      | OccupancyKind.Active _ ->
                          totalCond <- totalCond + int frame.Condition[i]
                          active <- active + 1
                      | _ -> ()

                  let avgCond = if active > 0 then float totalCond / float active else 50.0

                  let updated =
                      emergent
                      |> EmergentLoops.updateCompactness shortPassRate
                      |> EmergentLoops.updatePressing pressRate
                      |> EmergentLoops.updateWingPlay flankRate
                      |> EmergentLoops.updateFatigueSpiral avgCond 0

                  yield EmergentUpdate(clubSide, updated)

                  let adaptiveState = getAdaptiveState state clubSide
                  let recent = EventWindow.recentEvents 1200 state.MatchEvents

                  let updatedRecords =
                      adaptiveState.Records
                      |> Array.map (fun r -> EventWindow.patternResults r.Pattern recent)

                  yield
                      AdaptiveUpdate(
                          clubSide,
                          { AdaptiveTactics.initial with
                               Records = updatedRecords }
                       ) |]
        else
            [||]
