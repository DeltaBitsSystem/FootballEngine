namespace FootballEngine.Core.Tests.Contracts

open System
open Expecto
open FootballEngine.Core.Match
open FootballEngine.Domain
open FootballEngine.World.Phases

/// Spec for the adaptive-coach Phase-1 world integration: the CounterPlan injection
/// seam in MatchContextBuilder and the per-match memory update seam in
/// MatchPhase. Pure and data-driven — no GameState construction required.
module ScoutingWorldTests =

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

    let private passOf passer receiver dist fromX fromY toX toY =
        ev 0<subtick> (EventData.Pass(passer, receiver, dist, 0.7, true, fromX, fromY, toX, toY))

    let private defaultCounter =
        { MaxSideBias = 1.0
          BiasScale = 1.5
          MaxPressBoost = 1.0
          MaxLineDrop = 1.0
          MaxWidthTighten = 1.0
          MaxTempoShift = 1.0
          MaxDirectnessShift = 1.0
          SignatureChangePenalty = 0.6 }

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

    let private day = DateTime(2026, 8, 19)

    let private sigInstructions =
        { TacticalInstructions.defaultInstructions with
            Tempo = 1 }

    let private sigSignature = ("4-3-3", sigInstructions)

    let private baseTactics: TacticsConfig =
        { Formation = "4-3-3"
          DefensiveLine = 2.0f
          PressIntensity = 2.0f
          Width = 2.0f
          Tempo = 2.0f
          Directness = 2.0f }

    let private longBallMemory =
        { OpponentId = 42
          Pattern =
            PatternVector.withSignature
                "4-3-3"
                sigInstructions
                { PatternVector.empty with
                    Directness = 0.8
                    PassLong = 0.7
                    CorridorLeft = 0.1
                    CorridorCenter = 0.8
                    CorridorRight = 0.1 }
          Weighted =
            PatternVector.withSignature
                "4-3-3"
                sigInstructions
                { PatternVector.empty with
                    Directness = 0.8
                    PassLong = 0.7
                    CorridorLeft = 0.1
                    CorridorCenter = 0.8
                    CorridorRight = 0.1 }
          MatchesSeen = 4.0
          LastSeen = day }

    let adjustTacticsTests =
        testList
            "FootballEngine.Core.Contracts.MatchContextBuilder.adjustTactics"
            [ test "an empty memory leaves tactics untouched" {
                  let t =
                      MatchContextBuilder.adjustTactics defaultCounter ScoutingMemory.empty 42 sigSignature baseTactics

                  Expect.equal t baseTactics "unchanged"
              }

              test "only the opponent's own memory matters" {
                  let otherRival =
                      ScoutingMemory.update
                          defaultScout
                          day
                          99
                          (PatternVector.withSignature "4-3-3" sigInstructions PatternVector.empty)
                          ScoutingMemory.empty

                  let t =
                      MatchContextBuilder.adjustTactics defaultCounter otherRival 42 sigSignature baseTactics

                  Expect.equal t baseTactics "memory about another rival is irrelevant"
              }

              test "a long-ball rival drops the line and raises the press" {
                  let mem =
                      ScoutingMemory.update defaultScout day 42 longBallMemory.Pattern ScoutingMemory.empty

                  let t =
                      MatchContextBuilder.adjustTactics defaultCounter mem 42 sigSignature baseTactics

                  Expect.floatClose Accuracy.medium (float t.DefensiveLine) 1.0 "line dropped by MaxLineDrop"
                  Expect.floatClose Accuracy.medium (float t.PressIntensity) 3.0 "press boosted by MaxPressBoost"
              }

              test "shifts clamp into the slider range" {
                  let high =
                      { baseTactics with
                          PressIntensity = 3.8f
                          Width = 0.1f }

                  let mem =
                      ScoutingMemory.update defaultScout day 42 longBallMemory.Pattern ScoutingMemory.empty

                  let t = MatchContextBuilder.adjustTactics defaultCounter mem 42 sigSignature high
                  Expect.floatClose Accuracy.medium (float t.PressIntensity) 4.0 "press capped"
                  Expect.floatClose Accuracy.medium (float t.Width) 0.0 "width floored"
              }

              test "a changed formation dampens but never cancels" {
                  let mem =
                      ScoutingMemory.update defaultScout day 42 longBallMemory.Pattern ScoutingMemory.empty

                  let t =
                      MatchContextBuilder.adjustTactics
                          defaultCounter
                          mem
                          42
                          ("4-4-2", TacticalInstructions.defaultInstructions)
                          baseTactics

                  Expect.floatClose
                      Accuracy.medium
                      (float t.DefensiveLine)
                      (2.0 - defaultCounter.MaxLineDrop * defaultCounter.SignatureChangePenalty)
                      "line drop scaled by the penalty"

                  Expect.isTrue (t.DefensiveLine < 2.0f) "still counters after a change"
              } ]

    let recordPatternTests =
        testList
            "FootballEngine.Core.Contracts.MatchPhase.recordPattern"
            [ test "the first sight creates a memory stamped with the rival's signature" {
                  let events =
                      [ passOf (pid 2) (Some(pid 3)) 12.0<meter> 0.0<meter> 0.0<meter> -20.0<meter> 0.0<meter>
                        ev 1<subtick> (EventData.Possession(ClubSide.Away, pid 2)) ]

                  let mem =
                      MatchPhase.recordPattern
                          defaultScout
                          day
                          homeId
                          awayId
                          42
                          sigSignature
                          clubOf
                          events
                          ScoutingMemory.empty

                  let entry = mem[42]
                  Expect.equal entry.OpponentId 42 "opponent id"
                  Expect.equal entry.MatchesSeen 1.0 "first sighting"
                  Expect.equal entry.LastSeen day "seen now"
                  Expect.equal entry.Pattern.Formation "4-3-3" "signature formation stamped"
                  Expect.equal entry.Pattern.Instructions sigInstructions "signature instructions stamped"
                  Expect.floatClose Accuracy.medium entry.Pattern.CorridorCenter 1.0 "the pass corridor was read"

                  Expect.floatClose
                      Accuracy.medium
                      entry.Pattern.Directness
                      1.0
                      "forward pass counted for the away side"
              }

              test "only the rival's events are recorded, possessions included" {
                  // the subject's own long ball comes first and must be ignored;
                  // the rival's short backward pass, possession and cross count
                  let events =
                      [ passOf (pid 0) (Some(pid 1)) 40.0<meter> -30.0<meter> 0.0<meter> 10.0<meter> 0.0<meter>
                        passOf (pid 2) (Some(pid 3)) 10.0<meter> 0.0<meter> 0.0<meter> 10.0<meter> 0.0<meter>
                        ev 2<subtick> (EventData.Possession(ClubSide.Away, pid 2))
                        ev 3<subtick> (EventData.CrossAttempt(pid 2, -40.0<meter>, -25.0<meter>)) ]

                  let mem =
                      MatchPhase.recordPattern
                          defaultScout
                          day
                          homeId
                          awayId
                          42
                          sigSignature
                          clubOf
                          events
                          ScoutingMemory.empty

                  let p = mem[42].Pattern
                  Expect.floatClose Accuracy.medium p.PassShort 1.0 "only the rival's short pass counts"
                  Expect.floatClose Accuracy.medium p.PassLong 0.0 "the subject's long ball excluded"
                  Expect.floatClose Accuracy.medium p.Directness 0.0 "a backward pass is not direct"
                  Expect.floatClose Accuracy.medium p.CrossRate 1.0 "rival possessions feed the cross rate"
                  Expect.floatClose Accuracy.medium p.CorridorLeft 0.5 "cross on the left wing"
                  Expect.floatClose Accuracy.medium p.CorridorCenter 0.5 "pass through the middle"
              }

              test "a second sighting blends with equal weight" {
                  let first =
                      [ passOf (pid 2) (Some(pid 3)) 10.0<meter> 0.0<meter> -20.0<meter> -20.0<meter> -20.0<meter> ]

                  let second =
                      [ passOf (pid 2) (Some(pid 3)) 10.0<meter> 0.0<meter> 20.0<meter> 20.0<meter> 20.0<meter> ]

                  let afterOne =
                      MatchPhase.recordPattern
                          defaultScout
                          day
                          homeId
                          awayId
                          42
                          sigSignature
                          clubOf
                          first
                          ScoutingMemory.empty

                  let afterTwo =
                      MatchPhase.recordPattern defaultScout day homeId awayId 42 sigSignature clubOf second afterOne

                  Expect.equal afterTwo[42].MatchesSeen 2.0 "two sightings"
                  Expect.floatClose Accuracy.medium afterTwo[42].Weighted.CorridorLeft 0.5 "left and right average out"
                  Expect.floatClose Accuracy.medium afterTwo[42].Weighted.CorridorRight 0.5 "right and left average out"
              } ]

    let scoutingWorldTests =
        testList "FootballEngine.Core.Contracts.ScoutingWorld" [ adjustTacticsTests; recordPatternTests ]
