namespace FootballEngine.Core.Tests.Contracts

open System
open Expecto
open FootballEngine.Core.Match
open FootballEngine.Domain

/// Spec for the adaptive-coach Phase-1 scouting layer: pattern extraction, temporal
/// decay memory, asymmetric reports and pre-match counter-planning. These tests
/// pin the public API of `Domain/Scouting.fs` — implementation must make them
/// compile and pass without changing their meaning.
module ScoutingTests =

    let private pid n = FootballEngine.Core.Match.PlayerId n

    let private homeId = 1
    let private awayId = 2

    // engine player ids: 0/1 belong to home, 2/3 to away
    let private clubOf (p: FootballEngine.Core.Match.PlayerId) : ClubId option =
        match p with
        | FootballEngine.Core.Match.PlayerId n when n < 2 -> Some homeId
        | FootballEngine.Core.Match.PlayerId n when n < 4 -> Some awayId
        | _ -> None

    let private ev tick data : MatchEvent = { Tick = tick; Data = data }

    let private defaultScout =
        { MemoryHalfLifeDays = 30.0
          MinConfidenceToReport = 0.3
          AiReportNoise = 0.0
          NoiseByAbility = 0.25
          RivalPruneAfterDays = 200.0
          ConfidenceMatches = 3.0
          ShortPassMaxMeters = 15.0<meter>
          LongPassMinMeters = 30.0<meter>
          NearShotMaxMeters = 16.5<meter>
          AreaShotMaxMeters = 25.0<meter> }

    let private defaultCounter =
        { MaxSideBias = 1.0
          BiasScale = 1.5
          MaxPressBoost = 1.0
          MaxLineDrop = 1.0
          MaxWidthTighten = 1.0
          MaxTempoShift = 1.0
          MaxDirectnessShift = 1.0
          SignatureChangePenalty = 0.6 }

    let private defaultTransfer =
        { BenchWarmerAgeMin = 25
          MinMatchesForSquadPlayer = 10
          MaxOver25BenchWarmers = 2
          OverpayTolerance = 0.25m }

    // ── PatternExtractor ────────────────────────────────────────────────────

    let private passOf passer receiver dist completed fromX fromY toX toY =
        ev 0<subtick> (EventData.Pass(passer, receiver, dist, 0.7, completed, fromX, fromY, toX, toY))

    let patternExtractorTests =
        testList
            "FootballEngine.Core.Contracts.PatternExtractor"
            [ test "empty events produce the zero vector" {
                  let v = PatternExtractor.extract defaultScout homeId awayId clubOf []

                  Expect.equal v.CorridorLeft 0.0 "left corridor 0"
                  Expect.equal v.CorridorCenter 0.0 "center corridor 0"
                  Expect.equal v.CorridorRight 0.0 "right corridor 0"
                  Expect.equal v.PassShort 0.0 "short 0"
                  Expect.equal v.PassMedium 0.0 "medium 0"
                  Expect.equal v.PassLong 0.0 "long 0"
                  Expect.equal v.Directness 0.0 "directness 0"
                  Expect.equal v.CrossRate 0.0 "cross rate 0"
                  Expect.equal v.ShotNear 0.0 "shot near 0"
                  Expect.equal v.ShotArea 0.0 "shot area 0"
                  Expect.equal v.ShotFar 0.0 "shot far 0"
                  Expect.equal v.Formation "" "no formation"
                  Expect.equal v.Instructions TacticalInstructions.defaultInstructions "default instructions"
              }

              test "corridors sum to one and use the TeamEmergent sign convention" {
                  // y <= -10 left, |y| < 10 center, y >= +10 right (same as Emergent.fs channels)
                  let events =
                      [ passOf
                            (pid 0)
                            (Some(pid 1))
                            12.0<meter>
                            true
                            -30.0<meter>
                            -20.0<meter>
                            -20.0<meter>
                            -20.0<meter>
                        passOf (pid 0) (Some(pid 1)) 12.0<meter> true 30.0<meter> 20.0<meter> 20.0<meter> 20.0<meter>
                        passOf (pid 0) (Some(pid 1)) 12.0<meter> true 0.0<meter> 5.0<meter> 5.0<meter> 5.0<meter>
                        passOf
                            (pid 0)
                            (Some(pid 1))
                            12.0<meter>
                            true
                            -30.0<meter>
                            -20.0<meter>
                            -20.0<meter>
                            -20.0<meter> ]

                  let v = PatternExtractor.extract defaultScout homeId awayId clubOf events
                  Expect.floatClose Accuracy.medium v.CorridorLeft 0.5 "two left of four"
                  Expect.floatClose Accuracy.medium v.CorridorCenter 0.25 "one center of four"
                  Expect.floatClose Accuracy.medium v.CorridorRight 0.25 "one right of four"

                  Expect.floatClose
                      Accuracy.medium
                      (v.CorridorLeft + v.CorridorCenter + v.CorridorRight)
                      1.0
                      "sum to one"
              }

              test "pass lengths bucket at the configured thresholds" {
                  let events =
                      [ passOf (pid 0) (Some(pid 1)) 10.0<meter> true 0.0<meter> 0.0<meter> 10.0<meter> 0.0<meter>
                        passOf (pid 0) (Some(pid 1)) 20.0<meter> true 0.0<meter> 0.0<meter> 20.0<meter> 0.0<meter>
                        passOf (pid 0) (Some(pid 1)) 40.0<meter> true 0.0<meter> 0.0<meter> 40.0<meter> 0.0<meter>
                        passOf (pid 0) (Some(pid 1)) 8.0<meter> true 0.0<meter> 0.0<meter> 8.0<meter> 0.0<meter> ]

                  let v = PatternExtractor.extract defaultScout homeId awayId clubOf events
                  Expect.floatClose Accuracy.medium v.PassShort 0.5 "two short of four"
                  Expect.floatClose Accuracy.medium v.PassMedium 0.25 "one medium"
                  Expect.floatClose Accuracy.medium v.PassLong 0.25 "one long"
                  Expect.floatClose Accuracy.medium (v.PassShort + v.PassMedium + v.PassLong) 1.0 "sum to one"
              }

              test "directness is forward passes over all passes per attacking side" {
                  // home attacks +x: forward means toX > fromX
                  let events =
                      [ passOf (pid 0) (Some(pid 1)) 12.0<meter> true 0.0<meter> 0.0<meter> 20.0<meter> 0.0<meter>
                        passOf (pid 0) (Some(pid 1)) 12.0<meter> true 0.0<meter> 0.0<meter> -20.0<meter> 0.0<meter> ]

                  let home = PatternExtractor.extract defaultScout homeId awayId clubOf events
                  Expect.floatClose Accuracy.medium home.Directness 0.5 "home: one of two forward"

                  // away attacks -x: forward means toX < fromX
                  let awayEvents =
                      [ passOf (pid 2) (Some(pid 3)) 12.0<meter> true 0.0<meter> 0.0<meter> -20.0<meter> 0.0<meter>
                        passOf (pid 2) (Some(pid 3)) 12.0<meter> true 0.0<meter> 0.0<meter> 20.0<meter> 0.0<meter> ]

                  let away = PatternExtractor.extract defaultScout homeId awayId clubOf awayEvents
                  Expect.floatClose Accuracy.medium away.Directness 0.5 "away: one of two forward"
              }

              test "ignores events of the other club" {
                  let events =
                      [ passOf (pid 0) (Some(pid 1)) 40.0<meter> true 0.0<meter> 0.0<meter> 40.0<meter> 0.0<meter>
                        passOf (pid 2) (Some(pid 3)) 12.0<meter> true 0.0<meter> 0.0<meter> 10.0<meter> 0.0<meter> ]

                  let v = PatternExtractor.extract defaultScout homeId awayId clubOf events
                  Expect.floatClose Accuracy.medium v.PassLong 1.0 "only home's long ball counted"
                  Expect.floatClose Accuracy.medium v.PassShort 0.0 "away's short ball excluded"
              }

              test "cross rate is crosses per own possession" {
                  let events =
                      [ ev 0<subtick> (EventData.CrossAttempt(pid 0, -40.0<meter>, -25.0<meter>))
                        ev 1<subtick> (EventData.CrossAttempt(pid 0, -40.0<meter>, 25.0<meter>))
                        ev 2<subtick> (EventData.Possession(ClubSide.Home, pid 1))
                        ev 3<subtick> (EventData.Possession(ClubSide.Home, pid 0))
                        ev 4<subtick> (EventData.Possession(ClubSide.Home, pid 1))
                        ev 5<subtick> (EventData.Possession(ClubSide.Home, pid 0)) ]

                  let v = PatternExtractor.extract defaultScout homeId awayId clubOf events
                  Expect.floatClose Accuracy.medium v.CrossRate 0.5 "two crosses over four possessions"
              }

              test "shot zones bucket by distance to the attacking goal" {
                  // home attacks +x -> goal at +52.5m
                  let events =
                      [ ev
                            0<subtick>
                            (EventData.Shot(
                                pid 0,
                                ShotResult.OffTarget,
                                0.1,
                                20.0<meterPerSecond>,
                                47.5<meter>,
                                0.0<meter>
                            )) // 5m near
                        ev
                            1<subtick>
                            (EventData.Shot(
                                pid 0,
                                ShotResult.OffTarget,
                                0.1,
                                20.0<meterPerSecond>,
                                32.5<meter>,
                                0.0<meter>
                            )) // 20m area
                        ev
                            2<subtick>
                            (EventData.Shot(
                                pid 0,
                                ShotResult.OffTarget,
                                0.1,
                                20.0<meterPerSecond>,
                                22.5<meter>,
                                0.0<meter>
                            )) // 30m far
                        ev
                            3<subtick>
                            (EventData.Shot(
                                pid 2,
                                ShotResult.OffTarget,
                                0.1,
                                20.0<meterPerSecond>,
                                47.5<meter>,
                                0.0<meter>
                            )) ] // away shot excluded

                  let v = PatternExtractor.extract defaultScout homeId awayId clubOf events
                  Expect.floatClose Accuracy.medium v.ShotNear (1.0 / 3.0) "one third near"
                  Expect.floatClose Accuracy.medium v.ShotArea (1.0 / 3.0) "one third area"
                  Expect.floatClose Accuracy.medium v.ShotFar (1.0 / 3.0) "one third far"
              }

              test "dribbles count as corridor actions" {
                  let events =
                      [ ev 0<subtick> (EventData.Dribble(pid 0, true, -30.0<meter>, -20.0<meter>))
                        ev 1<subtick> (EventData.Dribble(pid 0, false, 30.0<meter>, 20.0<meter>)) ]

                  let v = PatternExtractor.extract defaultScout homeId awayId clubOf events
                  Expect.floatClose Accuracy.medium v.CorridorLeft 0.5 "left dribble"
                  Expect.floatClose Accuracy.medium v.CorridorRight 0.5 "right dribble"
              }

              test "is deterministic: same events produce the same vector" {
                  let events =
                      [ passOf (pid 0) (Some(pid 1)) 40.0<meter> true 0.0<meter> -20.0<meter> 0.0<meter> -20.0<meter>
                        ev
                            1<subtick>
                            (EventData.Shot(
                                pid 0,
                                ShotResult.OnTarget false,
                                0.1,
                                20.0<meterPerSecond>,
                                40.0<meter>,
                                0.0<meter>
                            )) ]

                  let a = PatternExtractor.extract defaultScout homeId awayId clubOf events
                  let b = PatternExtractor.extract defaultScout homeId awayId clubOf events
                  Expect.equal a b "same input -> same output"
              }

              test "withSignature records the tactical signature" {
                  let instructions =
                      { TacticalInstructions.defaultInstructions with
                          Tempo = 3
                          Directness = 4 }

                  let v = PatternVector.withSignature "4-3-3" instructions PatternVector.empty
                  Expect.equal v.Formation "4-3-3" "formation recorded"
                  Expect.equal v.Instructions instructions "instructions recorded"
              } ]

    // ── ScoutingMemory ──────────────────────────────────────────────────────

    let private day = DateTime(2026, 8, 19)

    let private vec corridors =
        { PatternVector.empty with
            CorridorLeft = float corridors
            CorridorCenter = float corridors
            CorridorRight = float corridors }

    let scoutingMemoryTests =
        testList
            "FootballEngine.Core.Contracts.ScoutingMemory"
            [ test "first sight creates a fresh memory" {
                  let m = ScoutingMemory.update defaultScout day 42 (vec 0.5) ScoutingMemory.empty
                  let mem = m[42]
                  Expect.equal mem.OpponentId 42 "opponent id"
                  Expect.equal mem.Pattern.CorridorLeft 0.5 "pattern recorded"
                  Expect.equal mem.Weighted.CorridorLeft 0.5 "weighted starts as pattern"
                  Expect.equal mem.MatchesSeen 1.0 "one match seen"
                  Expect.equal mem.LastSeen day "seen now"
              }

              test "same-day second match blends with equal weight" {
                  let v1 = vec 0.2
                  let v2 = vec 0.8
                  let afterOne = ScoutingMemory.update defaultScout day 42 v1 ScoutingMemory.empty
                  let afterTwo = ScoutingMemory.update defaultScout day 42 v2 afterOne
                  Expect.equal afterTwo[42].MatchesSeen 2.0 "two matches"
                  Expect.floatClose Accuracy.medium afterTwo[42].Weighted.CorridorLeft 0.5 "average of the two"
                  Expect.equal afterTwo[42].Pattern.CorridorLeft 0.8 "pattern tracks the last match"
              }

              test "a match one half-life ago contributes half its weight" {
                  let v1 = vec 0.9
                  let v2 = vec 0.1
                  let afterOne = ScoutingMemory.update defaultScout day 42 v1 ScoutingMemory.empty
                  let later = day.AddDays(defaultScout.MemoryHalfLifeDays)
                  let afterTwo = ScoutingMemory.update defaultScout later 42 v2 afterOne
                  // old weight 0.5, new weight 1 -> (0.5 * 0.9 + 1.0 * 0.1) / 1.5
                  Expect.floatClose
                      Accuracy.medium
                      afterTwo[42].Weighted.CorridorLeft
                      ((0.5 * 0.9 + 0.1) / 1.5)
                      "decayed blend"
              }

              test "decay never reaches zero even after long gaps" {
                  let afterOne =
                      ScoutingMemory.update defaultScout day 42 (vec 0.5) ScoutingMemory.empty

                  let farLater = day.AddDays(3650.0)
                  let afterTwo = ScoutingMemory.update defaultScout farLater 42 (vec 1.0) afterOne
                  Expect.isTrue (afterTwo[42].Weighted.CorridorLeft > 0.0) "old memory leaves a trace"
              }

              test "prune drops rivals not seen for over the configured window" {
                  let fresh = ScoutingMemory.update defaultScout day 42 (vec 0.5) ScoutingMemory.empty
                  // rival 43 was seen one day later, so at prune time it is exactly
                  // inside the window while 42 is over it
                  let stale = ScoutingMemory.update defaultScout (day.AddDays 1.0) 43 (vec 0.5) fresh

                  let pruned =
                      ScoutingMemory.prune defaultScout (day.AddDays(defaultScout.RivalPruneAfterDays + 1.0)) stale

                  Expect.isFalse (pruned.ContainsKey 42) "rival 42 forgotten"
                  Expect.isTrue (pruned.ContainsKey 43) "rival 43 kept"
              }

              test "update does not touch other opponents" {
                  let afterOne =
                      ScoutingMemory.update defaultScout day 42 (vec 0.5) ScoutingMemory.empty

                  let afterTwo = ScoutingMemory.update defaultScout day 42 (vec 0.5) afterOne
                  let afterThree = ScoutingMemory.update defaultScout day 99 (vec 0.9) afterTwo
                  Expect.equal afterThree[42].MatchesSeen 2.0 "rival 42 untouched"
                  Expect.equal afterThree[99].MatchesSeen 1.0 "rival 99 added"
              } ]

    // ── ScoutReport ─────────────────────────────────────────────────────────

    let private memoryFor corridors matchesSeen =
        { OpponentId = 42
          Pattern = vec corridors
          Weighted = vec corridors
          MatchesSeen = matchesSeen
          LastSeen = day }

    let scoutReportTests =
        testList
            "FootballEngine.Core.Contracts.ScoutReport"
            [ test "AI reads the truth: zero noise keeps strengths exact" {
                  let mem = memoryFor 0.8 5.0
                  let report = ScoutReport.generate defaultScout 0.0 42UL 1 42 mem
                  let left = report.Findings |> List.tryFind (fun f -> f.Label = "Left corridor")
                  Expect.isSome left "left corridor reported"
                  Expect.floatClose Accuracy.medium left.Value.Strength 0.8 "strength is the truth"
                  Expect.equal report.Formation None "no formation in a numeric-only memory"
              }

              test "a fresh memory reports nothing below the confidence floor" {
                  let mem = memoryFor 0.8 1.0
                  // confidence (1 - 1) / ConfidenceMatches = 0 < MinConfidenceToReport 0.3:
                  // the first match is the discovery, it cannot confirm a habit
                  let report = ScoutReport.generate defaultScout 0.0 42UL 1 42 mem
                  Expect.isEmpty report.Findings "findings withheld until enough sightings"
                  Expect.equal report.Formation None "formation withheld"
              }

              test "human noise is deterministic per seed and bounded in [0,1]" {
                  let mem = memoryFor 0.8 5.0
                  let a = ScoutReport.generate defaultScout 0.15 42UL 1 42 mem
                  let b = ScoutReport.generate defaultScout 0.15 42UL 1 42 mem
                  Expect.equal a b "same seed -> same report"

                  for f in a.Findings do
                      Expect.isTrue (f.Strength >= 0.0 && f.Strength <= 1.0) (sprintf "%s in range" f.Label)

                  let c = ScoutReport.generate defaultScout 0.15 43UL 1 42 mem
                  Expect.notEqual a c "different seed -> different strengths"
              }

              test "confidence rises with sightings and never exceeds one" {
                  let low = memoryFor 0.8 1.0
                  let high = memoryFor 0.8 9.0
                  let rLow = ScoutReport.generate defaultScout 0.0 1UL 1 42 low
                  let rHigh = ScoutReport.generate defaultScout 0.0 2UL 1 42 high
                  Expect.isTrue (rHigh.Confidence = 1.0) "saturated confidence"
                  Expect.isTrue (rHigh.Confidence > rLow.Confidence) "more sightings, more confidence"
              }

              test "findings are sorted by strength and capped at three" {
                  let mem = memoryFor 0.95 9.0
                  let report = ScoutReport.generate defaultScout 0.0 1UL 1 42 mem
                  Expect.isTrue (report.Findings.Length <= 3) "at most three findings"
                  let strengths = report.Findings |> List.map _.Strength
                  Expect.equal strengths (List.sortDescending strengths) "sorted descending"
              } ]

    // ── CounterPlan ─────────────────────────────────────────────────────────

    let private sigInstructions =
        { TacticalInstructions.defaultInstructions with
            Tempo = 1 }

    let private sigMemory corridors matchesSeen =
        { OpponentId = 42
          Pattern =
            { (vec corridors) with
                Formation = "4-3-3"
                Instructions = sigInstructions }
          Weighted =
            { (vec corridors) with
                Formation = "4-3-3"
                Instructions = sigInstructions }
          MatchesSeen = matchesSeen
          LastSeen = day }

    let private planTestsMemory () =
        { OpponentId = 42
          Pattern =
            { (vec 0.1) with
                Directness = 0.8
                PassLong = 0.7
                CorridorLeft = 0.8
                CorridorCenter = 0.1
                CorridorRight = 0.1
                Formation = "4-3-3"
                Instructions = sigInstructions }
          Weighted =
            { (vec 0.1) with
                Directness = 0.8
                PassLong = 0.7
                CorridorLeft = 0.8
                CorridorCenter = 0.1
                CorridorRight = 0.1
                Formation = "4-3-3"
                Instructions = sigInstructions }
          MatchesSeen = 4.0
          LastSeen = day }

    let counterPlanTests =
        testList
            "FootballEngine.Core.Contracts.CounterPlan"
            [ test "a memory with no sightings yields an inactive plan" {
                  let plan =
                      CounterPlan.ofMemory defaultCounter ("4-3-3", sigInstructions) (sigMemory 0.5 0.0)

                  Expect.isFalse plan.Active "inactive"
                  Expect.equal plan CounterPlan.empty "identical to empty"
              }

              test "side bias points at the opponent's dominant corridor" {
                  let leftDominant =
                      { (vec 0.1) with
                          CorridorLeft = 0.8
                          CorridorCenter = 0.1
                          CorridorRight = 0.1 }

                  let plan =
                      CounterPlan.ofMemory
                          defaultCounter
                          ("4-3-3", sigInstructions)
                          { (sigMemory 0.1 4.0) with
                              Weighted = leftDominant }

                  Expect.isTrue (plan.SideBias < 0.0) "left dominance biases toward the left"

                  let rightDominant =
                      { (vec 0.1) with
                          CorridorLeft = 0.1
                          CorridorCenter = 0.1
                          CorridorRight = 0.8 }

                  let planR =
                      CounterPlan.ofMemory
                          defaultCounter
                          ("4-3-3", sigInstructions)
                          { (sigMemory 0.1 4.0) with
                              Weighted = rightDominant }

                  Expect.isTrue (planR.SideBias > 0.0) "right dominance biases toward the right"
                  Expect.equal (abs plan.SideBias) defaultCounter.MaxSideBias "bias clamps at the maximum"
              }

              test "direct play is met with pressing" {
                  let mem = planTestsMemory ()
                  let plan = CounterPlan.ofMemory defaultCounter ("4-3-3", sigInstructions) mem
                  Expect.equal plan.PressBoost defaultCounter.MaxPressBoost "full boost at directness 0.8"
              }

              test "long balls lower the defensive line" {
                  let mem = planTestsMemory ()
                  let plan = CounterPlan.ofMemory defaultCounter ("4-3-3", sigInstructions) mem
                  Expect.equal plan.LineDrop defaultCounter.MaxLineDrop "full drop for long-ball play"
              }

              test "a changed signature weakens but never cancels the counter" {
                  let mem = planTestsMemory ()
                  let matching = CounterPlan.ofMemory defaultCounter ("4-3-3", sigInstructions) mem

                  let changed =
                      CounterPlan.ofMemory defaultCounter ("4-4-2", TacticalInstructions.defaultInstructions) mem

                  Expect.isTrue changed.Active "still counters after a change"

                  Expect.equal
                      changed.PressBoost
                      (defaultCounter.MaxPressBoost * defaultCounter.SignatureChangePenalty)
                      "scaled by penalty"

                  Expect.isTrue (matching.PressBoost > changed.PressBoost) "repeated signature is countered harder"
              }

              test "an unrecorded signature is not a changed one" {
                  // a numeric-only memory never saw a shape: nobody can tell the
                  // rival changed, so the full counter stands against any formation
                  let mem =
                      { OpponentId = 42
                        Pattern = vec 0.1
                        Weighted = vec 0.1
                        MatchesSeen = 4.0
                        LastSeen = day }

                  let plan =
                      CounterPlan.ofMemory defaultCounter ("4-4-2", TacticalInstructions.defaultInstructions) mem

                  Expect.isTrue plan.Active "still active"
                  Expect.equal plan.PressBoost defaultCounter.MaxPressBoost "full strength against an unknown shape"
              }

              test "apply adds deltas onto engine-scale tactics" {
                  // Engine scales run intensities 0..1 with the line in meters;
                  // slider-unit boosts convert (/4) while the line drop lands in meters.
                  let baseTactics =
                      { Formation = "4-3-3"
                        DefensiveLine = 40.0f
                        PressIntensity = 0.5f
                        Width = 0.5f
                        Tempo = 0.5f
                        Directness = 0.5f
                        Mentality = 0.5f
                        PressTriggerZone = 0.5f }

                  let plan =
                      { Active = true
                        SideBias = 0.5
                        PressBoost = 0.6
                        LineDrop = 0.5
                        WidthTighten = 0.4
                        TempoShift = 0.3
                        DirectnessShift = 0.2 }

                  let t = CounterPlan.apply defaultCounter baseTactics plan
                  Expect.floatClose Accuracy.medium (float t.PressIntensity) 0.65 "press boosted by boost/4"
                  Expect.floatClose Accuracy.medium (float t.DefensiveLine) 39.5 "line dropped in meters"
                  Expect.floatClose Accuracy.medium (float t.Width) 0.4 "width tightened by tighten/4"
                  Expect.floatClose Accuracy.medium (float t.Tempo) 0.425 "tempo lowered by shift/4"
                  Expect.floatClose Accuracy.medium (float t.Directness) 0.55 "directness raised by shift/4"
                  Expect.equal t.Formation baseTactics.Formation "formation unchanged"
                  Expect.equal t.Mentality baseTactics.Mentality "counter never touches mentality"
                  Expect.equal t.PressTriggerZone baseTactics.PressTriggerZone "counter never touches trigger zone"
              }

              test "apply never leaves engine range" {
                  let high =
                      { Formation = "4-3-3"
                        DefensiveLine = 16.0f
                        PressIntensity = 0.95f
                        Width = 0.05f
                        Tempo = 0.05f
                        Directness = 0.95f
                        Mentality = 0.5f
                        PressTriggerZone = 0.5f }

                  let plan =
                      { Active = true
                        SideBias = 0.0
                        PressBoost = 1.0
                        LineDrop = 1.0
                        WidthTighten = 1.0
                        TempoShift = 1.0
                        DirectnessShift = 1.0 }

                  let t = CounterPlan.apply defaultCounter high plan
                  Expect.floatClose Accuracy.medium (float t.PressIntensity) 1.0 "press capped"
                  // the line drops in meters but never below the playable floor
                  Expect.floatClose Accuracy.medium (float t.DefensiveLine) 15.0 "line floored at 15m"
                  Expect.floatClose Accuracy.medium (float t.Width) 0.0 "width floored"
                  Expect.floatClose Accuracy.medium (float t.Tempo) 0.0 "tempo floored"
                  Expect.floatClose Accuracy.medium (float t.Directness) 1.0 "directness capped"
              }

              test "apply ignores inactive plans" {
                  let baseTactics =
                      { Formation = "4-3-3"
                        DefensiveLine = 40.0f
                        PressIntensity = 0.5f
                        Width = 0.5f
                        Tempo = 0.5f
                        Directness = 0.5f
                        Mentality = 0.5f
                        PressTriggerZone = 0.5f }

                  let t = CounterPlan.apply defaultCounter baseTactics CounterPlan.empty
                  Expect.equal t baseTactics "unchanged"
              } ]

    // ── ConfigValidation for the new sections ───────────────────────────────

    let configValidationTests =
        testList
            "FootballEngine.Core.Contracts.ScoutConfigValidation"
            [ test "default scout config validates clean" {
                  Expect.isEmpty (ConfigValidation.validateScout defaultScout) "default scout is valid"
              }

              test "scout config rejects nonsense" {
                  Expect.isNonEmpty
                      (ConfigValidation.validateScout
                          { defaultScout with
                              MemoryHalfLifeDays = 0.0 })
                      "zero half-life rejected"

                  Expect.isNonEmpty
                      (ConfigValidation.validateScout
                          { defaultScout with
                              MinConfidenceToReport = 1.5 })
                      "confidence over one rejected"

                  Expect.isNonEmpty
                      (ConfigValidation.validateScout
                          { defaultScout with
                              ShortPassMaxMeters = 30.0<meter> })
                      "thresholds out of order rejected"
              }

              test "default counter and transfer config validate clean" {
                  Expect.isEmpty (ConfigValidation.validateCounter defaultCounter) "default counter is valid"
                  Expect.isEmpty (ConfigValidation.validateTransfer defaultTransfer) "default transfer is valid"
              }

              test "counter config rejects inverted maxima" {
                  Expect.isNonEmpty
                      (ConfigValidation.validateCounter
                          { defaultCounter with
                              MaxPressBoost = -1.0 })
                      "negative max rejected"

                  Expect.isNonEmpty
                      (ConfigValidation.validateCounter
                          { defaultCounter with
                              SignatureChangePenalty = 2.0 })
                      "penalty over one rejected"
              }

              test "transfer config rejects negative sizes" {
                  Expect.isNonEmpty
                      (ConfigValidation.validateTransfer
                          { defaultTransfer with
                              MaxOver25BenchWarmers = -1 })
                      "negative bench warmers rejected"

                  Expect.isNonEmpty
                      (ConfigValidation.validateTransfer
                          { defaultTransfer with
                              OverpayTolerance = -0.1m })
                      "negative tolerance rejected"
              } ]

    let scoutingTests =
        testList
            "FootballEngine.Core.Contracts.Scouting"
            [ patternExtractorTests
              scoutingMemoryTests
              scoutReportTests
              counterPlanTests
              configValidationTests ]
