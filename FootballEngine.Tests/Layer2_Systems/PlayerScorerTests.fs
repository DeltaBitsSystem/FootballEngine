module FootballEngine.Tests.Layer2.PlayerScorerTests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Player.Decision
open FootballEngine.Tests.Infrastructure.Builders

let private scoreTests =
    testList
        "PlayerScorer"
        [ test "cross score differentiates by zone" {
              let winger = makePlayer 1 ML 15
              let opp = [| makePlayer 3 DC 10 |]

              let teamAtt =
                  makeTeamPerspective
                      [| winger; makePlayer 10 ST 10 |]
                      [| 75.0, 10.0; 65.0, 34.0 |]
                      opp
                      [| 50.0, 34.0 |]
                      LeftToRight

              let teamDef =
                  makeTeamPerspective
                      [| winger; makePlayer 10 ST 10 |]
                      [| 25.0, 10.0; 65.0, 34.0 |]
                      opp
                      [| 50.0, 34.0 |]
                      LeftToRight

              let ctxAtt =
                  buildAgentContext
                      winger
                      0
                      teamAtt
                      AttackingZone
                      25.0<meter>
                      Attack
                      ValueNone
                      ValueNone
                      defaultSituation
                      (spatialAt 75.0 10.0)

              let ctxDef =
                  buildAgentContext
                      winger
                      0
                      teamDef
                      DefensiveZone
                      65.0<meter>
                      BuildUp
                      ValueNone
                      ValueNone
                      defaultSituation
                      (spatialAt 25.0 10.0)

              let scoresAtt = PlayerScorer.computeAll ctxAtt MatchMemory.empty defaultExp
              let scoresDef = PlayerScorer.computeAll ctxDef MatchMemory.empty defaultExp

              Expect.isGreaterThan
                  (float scoresAtt.Cross)
                  (float scoresDef.Cross)
                  "attacking zone should give cross bonus"
          }

          test "ST gets higher shoot score than DC at same position" {
              let st = makePlayer 1 ST 15
              let dc = makePlayer 2 DC 15
              let opp = [| makePlayer 3 DC 10 |]

              let teamSt =
                  makeTeamPerspective
                      [| st; makePlayer 10 MC 10 |]
                      [| 65.0, 34.0; 40.0, 34.0 |]
                      opp
                      [| 75.0, 34.0 |]
                      LeftToRight

              let teamDc =
                  makeTeamPerspective
                      [| dc; makePlayer 10 MC 10 |]
                      [| 65.0, 34.0; 40.0, 34.0 |]
                      opp
                      [| 75.0, 34.0 |]
                      LeftToRight

              let ctxSt =
                  buildAgentContext
                      st
                      0
                      teamSt
                      AttackingZone
                      12.0<meter>
                      Attack
                      ValueNone
                      ValueNone
                      defaultSituation
                      (spatialAt 88.0 34.0)

              let ctxDc =
                  buildAgentContext
                      dc
                      0
                      teamDc
                      AttackingZone
                      12.0<meter>
                      Attack
                      ValueNone
                      ValueNone
                      defaultSituation
                      (spatialAt 88.0 34.0)

              let scoresSt = PlayerScorer.computeAll ctxSt MatchMemory.empty defaultExp
              let scoresDc = PlayerScorer.computeAll ctxDc MatchMemory.empty defaultExp

              Expect.isGreaterThan
                  (float scoresSt.Shoot)
                  (float scoresDc.Shoot)
                  "ST should have higher shoot score than DC at same position"
          }

          test "different zones produce different score profiles" {
              let player = makePlayer 1 MC 15
              let opp = [| makePlayer 3 DC 10 |]

              let teamAtt =
                  makeTeamPerspective
                      [| player; makePlayer 10 MC 10 |]
                      [| 80.0, 34.0; 40.0, 34.0 |]
                      opp
                      [| 90.0, 34.0 |]
                      LeftToRight

              let teamDef =
                  makeTeamPerspective
                      [| player; makePlayer 10 MC 10 |]
                      [| 20.0, 34.0; 40.0, 34.0 |]
                      opp
                      [| 30.0, 34.0 |]
                      LeftToRight

              let ctxAtt =
                  buildAgentContext
                      player
                      0
                      teamAtt
                      AttackingZone
                      20.0<meter>
                      Attack
                      ValueNone
                      ValueNone
                      defaultSituation
                      (spatialAt 80.0 34.0)

              let ctxDef =
                  buildAgentContext
                      player
                      0
                      teamDef
                      DefensiveZone
                      60.0<meter>
                      BuildUp
                      ValueNone
                      ValueNone
                      defaultSituation
                      (spatialAt 20.0 34.0)

              let scoresAtt = PlayerScorer.computeAll ctxAtt MatchMemory.empty defaultExp
              let scoresDef = PlayerScorer.computeAll ctxDef MatchMemory.empty defaultExp

              let anyDifferent =
                  float scoresAtt.Shoot <> float scoresDef.Shoot
                  || float scoresAtt.Pass <> float scoresDef.Pass
                  || float scoresAtt.Dribble <> float scoresDef.Dribble
                  || float scoresAtt.Cross <> float scoresDef.Cross
                  || float scoresAtt.LongBall <> float scoresDef.LongBall

              Expect.isTrue anyDifferent "different zones should produce at least one different score"
          } ]

let playerScorerTests = scoreTests
