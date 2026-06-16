module FootballEngine.Tests.Layer2.SemanticE2ETests

open Expecto
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open FootballEngine.Player.Decision
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Tests.Infrastructure.Runners

let private countMatchEvents (events: MatchEvent list) (clubId: ClubId) =
    let mutable goals = 0
    let mutable shots = 0
    let mutable shotsOnTarget = 0
    let mutable passCompleted = 0
    let mutable passTotal = 0
    let mutable corners = 0
    let mutable fouls = 0
    let mutable longBalls = 0
    let mutable dribbleSuccess = 0
    let mutable tackleSuccess = 0

    for e in events do
        if e.ClubId = clubId then
            match e.Type with
            | MatchEventType.Goal -> goals <- goals + 1
            | MatchEventType.ShotLaunched
            | MatchEventType.ShotOffTarget
            | MatchEventType.ShotBlocked -> shots <- shots + 1
            | MatchEventType.ShotOnTarget ->
                shots <- shots + 1
                shotsOnTarget <- shotsOnTarget + 1
            | MatchEventType.PassCompleted _ ->
                passCompleted <- passCompleted + 1
                passTotal <- passTotal + 1
            | MatchEventType.PassMisplaced _
            | MatchEventType.PassIntercepted _
            | MatchEventType.PassDeflected _ -> passTotal <- passTotal + 1
            | MatchEventType.Corner -> corners <- corners + 1
            | MatchEventType.FoulCommitted -> fouls <- fouls + 1
            | MatchEventType.LongBall _ -> longBalls <- longBalls + 1
            | MatchEventType.DribbleSuccess -> dribbleSuccess <- dribbleSuccess + 1
            | MatchEventType.TackleSuccess -> tackleSuccess <- tackleSuccess + 1
            | _ -> ()

    {| Goals = goals
       Shots = shots
       ShotsOnTarget = shotsOnTarget
       PassCompleted = passCompleted
       PassTotal = passTotal
       Corners = corners
       Fouls = fouls
       LongBalls = longBalls
       DribbleSuccess = dribbleSuccess
       TackleSuccess = tackleSuccess |}

// ── Level 1: Individual decision invariants ─────────────────────

let private level1Tests =
    testList
        "Level1_DecisionInvariants"
        [ test "ST alone at 12m chooses Shoot" {
              let striker = makePlayer 1 ST 20
              let teammate = makePlayer 2 MC 10
              let opp = [| makePlayer 3 DC 10 |]

              let team =
                  makeTeamPerspective
                      [| striker; teammate |]
                      [| 85.0, 34.0; 40.0, 34.0 |]
                      opp
                      [| 75.0, 34.0 |]
                      LeftToRight

              let baseCtx =
                  buildAgentContext
                      striker
                      0
                      team
                      AttackingZone
                      12.0<meter>
                      Attack
                      ValueNone
                      ValueNone
                      defaultSituation
                      (spatialAt 88.0 34.0)

              let ctx =
                  { baseCtx with
                      Weights =
                          { BalanceConfig.defaultConfig with
                              Diagnostics =
                                  { BalanceConfig.defaultConfig.Diagnostics with
                                      TraceDecisions = true } } }

              let scores = PlayerScorer.computeAll ctx MatchMemory.empty defaultExp
              let intent, trace = PlayerDecision.decide ctx scores allEligible

              Expect.isSome intent "ST at 12m should produce an intent"

              match trace with
              | Some t -> Expect.equal t.ChosenAction "Shoot" "chosen action should be Shoot"
              | None -> failtest "trace should be present when TraceDecisions = true"
          }

          test "DC under high pressure does not choose Dribble" {
              let defender = makePlayer 1 DC 15
              let teammate = makePlayer 2 MC 10
              let opp = [| makePlayer 3 ST 10 |]

              let team =
                  makeTeamPerspective
                      [| defender; teammate |]
                      [| 25.0, 34.0; 40.0, 34.0 |]
                      opp
                      [| 30.0, 34.0 |]
                      LeftToRight

              let baseCtx =
                  buildAgentContext
                      defender
                      0
                      team
                      DefensiveZone
                      60.0<meter>
                      BuildUp
                      (ValueSome 0)
                      ValueNone
                      defaultSituation
                      (spatialAt 25.0 34.0)

              let ctx =
                  { baseCtx with
                      ImmediatePressure = 0.8
                      Weights =
                          { BalanceConfig.defaultConfig with
                              Diagnostics =
                                  { BalanceConfig.defaultConfig.Diagnostics with
                                      TraceDecisions = true } } }

              let scores = PlayerScorer.computeAll ctx MatchMemory.empty defaultExp
              let intent, trace = PlayerDecision.decide ctx scores allEligible

              Expect.isSome intent "DC under pressure should produce an intent"

              match intent with
              | Some SchedulingTypes.OnBallIntent.Dribble -> failtest "DC under pressure should not dribble"
              | _ -> ()
          }

          test "player with best pass target chooses Pass" {
              let player = makePlayer 1 MC 15
              let striker = makePlayer 2 ST 15
              let opp = [| makePlayer 3 DC 10 |]

              let team =
                  makeTeamPerspective
                      [| player; striker |]
                      [| 50.0, 34.0; 80.0, 34.0 |]
                      opp
                      [| 70.0, 34.0 |]
                      LeftToRight

              let ctx =
                  buildAgentContext
                      player
                      0
                      team
                      MidfieldZone
                      30.0<meter>
                      Midfield
                      ValueNone
                      (ValueSome 1)
                      defaultSituation
                      (spatialAt 50.0 34.0)

              let scores = PlayerScorer.computeAll ctx MatchMemory.empty defaultExp
              let intent, _ = PlayerDecision.decide ctx scores allEligible

              match intent with
              | Some(SchedulingTypes.OnBallIntent.Pass _) -> ()
              | Some _ -> failtest "player with pass target should choose Pass"
              | None -> failtest "should produce an intent"
          }

          test "MatchSituation ShootMod is > 1.0 when losing late" {
              let ctx, state = buildStandardMatch ()
              state.Flow <- MatchFlow.Live
              state.HomeScore <- 0
              state.AwayScore <- 1
              state.SubTick <- 80 * 40 * 60 * 1<subtick>

              let clock = SimulationClock.defaultClock
              let sit = MatchSituationBuilder.compute HomeClub ctx state clock

              Expect.isTrue sit.IsLosingLate "should be losing late"
              Expect.isGreaterThan sit.ShootMod 1.1 "ShootMod should boost when losing late"
          } ]

// ── Level 2: Collective invariants ──────────────────────────────

let private level2Tests =
    testList
        "Level2_CollectiveInvariants"
        [ test "Attacking tactics generates more shots than Balanced" {
              let ctxDirect, stateDirect = buildStandardMatch ()
              stateDirect.Flow <- MatchFlow.Live
              stateDirect.Home.Tactics <- TeamTactics.Attacking

              let ctxBal, stateBal = buildStandardMatch ()
              stateBal.Flow <- MatchFlow.Live
              stateBal.Home.Tactics <- TeamTactics.Balanced

              let _, eventsDirect = advanceTicks 5000 ctxDirect stateDirect
              let _, eventsBal = advanceTicks 5000 ctxBal stateBal

              let directStats = countMatchEvents eventsDirect ctxDirect.Home.Id
              let balStats = countMatchEvents eventsBal ctxBal.Home.Id

              Expect.isGreaterThan
                  directStats.Shots
                  balStats.Shots
                  "Attacking tactics should generate more shots than Balanced"
          }

          test "MatchSituation differentiates winning vs losing" {
              let ctx, stateWin = buildStandardMatch ()
              let clock = SimulationClock.defaultClock
              stateWin.Flow <- MatchFlow.Live
              stateWin.HomeScore <- 2
              stateWin.AwayScore <- 0
              stateWin.SubTick <- 70 * 40 * 60 * 1<subtick>
              let sitWin = MatchSituationBuilder.compute HomeClub ctx stateWin clock

              let _, stateLose = buildStandardMatch ()
              stateLose.Flow <- MatchFlow.Live
              stateLose.HomeScore <- 0
              stateLose.AwayScore <- 2
              stateLose.SubTick <- 70 * 40 * 60 * 1<subtick>
              let sitLose = MatchSituationBuilder.compute HomeClub ctx stateLose clock

              Expect.isGreaterThan sitWin.PassMod sitLose.PassMod "winning should have higher PassMod"
              Expect.isGreaterThan sitLose.ShootMod sitWin.ShootMod "losing should have higher ShootMod"
          }

          test "home team completes passes" {
              let ctx, state = buildStandardMatch ()
              state.Flow <- MatchFlow.Live
              let _, events = advanceTicks 5000 ctx state

              let homeStats = countMatchEvents events ctx.Home.Id

              Expect.isGreaterThan homeStats.PassCompleted 0 "home should have some completed passes"
          }

          test "Pressing generates match activity" {
              let ctx, state = buildStandardMatch ()
              state.Flow <- MatchFlow.Live
              state.Home.Tactics <- TeamTactics.Pressing
              let _, events = advanceTicks 5000 ctx state

              let homeStats = countMatchEvents events ctx.Home.Id

              Expect.isGreaterThan
                  (homeStats.Shots + homeStats.PassCompleted)
                  0
                  "pressing should generate shots or passes"
          } ]

let semanticE2ETests = testList "SemanticE2E" [ level1Tests; level2Tests ]
