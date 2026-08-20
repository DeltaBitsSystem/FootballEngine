namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

/// Pure behaviour of the Voronoi-style field evaluator: the refined point
/// avoids opponents, separates from clumped teammates, makes progress toward
/// the goal, stays bounded by the search radius, and is deterministic.
module FieldEvaluatorTests =

    let private p: FieldEvaluator.EvaluatorParams =
        { TeammateWeight = 0.3
          OpponentWeight = 0.5
          ProgressWeight = 0.4
          OppOurWeight = 0.0
          SearchRadius = 2.0
          CandidateCount = 9
          MinTeammateDist = 1.5
          OffsideX = None }

    let private evalParams: FieldEvaluator.EvaluatorParams =
        { TeammateWeight = 0.3
          OpponentWeight = 0.3
          ProgressWeight = 0.2
          OppOurWeight = 0.3
          SearchRadius = 5.0
          CandidateCount = 12
          MinTeammateDist = 4.0
          OffsideX = None }

    let private noTeams: FPoint[] = [||]
    let private noOpps: FPoint[] = [||]

    let private pts (arr: (float * float)[]) : FPoint[] = arr |> Array.map FPoint.ofTuple

    let fieldEvaluatorTests =
        testList
            "FootballEngine.Core.Systems.Team.FieldEvaluatorTests"
            [ testCase "avoids a nearby opponent" (fun _ ->
                  // Opponent just to the east; the west candidate is farthest from it.
                  let opps = pts [| 3.0, 0.0 |]
                  let rx = (FieldEvaluator.refine p noTeams opps 0.0 0.0).X
                  Expect.isLessThan rx 0.0 "refined point moves away from the opponent")

              testCase "separates from a clumped teammate" (fun _ ->
                  // A teammate exactly on the base; the ring candidates spread out.
                  let teams = pts [| 0.0, 0.0 |]
                  let refined = FieldEvaluator.refine p teams noOpps 0.0 0.0
                  let rx = refined.X
                  let ry = refined.Y
                  let moved = MatchMath.distF32 (float32 rx) (float32 ry) 0.0f 0.0f
                  Expect.isGreaterThan moved 0.1 "refined point leaves the clumped base")

              testCase "makes progress toward the rival goal" (fun _ ->
                  // No neighbours; only the progress term differentiates, +X = attack.
                  let rx = (FieldEvaluator.refine p noTeams noOpps 0.0 0.0).X
                  Expect.isGreaterThan rx 0.0 "refined point advances toward +X goal")

              testCase "never moves more than the search radius" (fun _ ->
                  let rx = (FieldEvaluator.refine p noTeams noOpps 2.0 1.0).X
                  let ry = (FieldEvaluator.refine p noTeams noOpps 2.0 1.0).Y
                  let dx = rx - 2.0
                  let dy = ry - 1.0
                  let dist = MatchMath.distF32 (float32 dx) (float32 dy) 0.0f 0.0f
                  Expect.isLessThanOrEqual dist (p.SearchRadius + 1e-6) "refined point is within the search radius")

              testCase "is deterministic" (fun _ ->
                  let opps = pts [| -3.0, 2.0; 4.0, -1.0 |]
                  let teams = pts [| 1.0, 1.0 |]
                  let a = FieldEvaluator.refine p teams opps 0.0 0.0
                  let b = FieldEvaluator.refine p teams opps 0.0 0.0
                  Expect.equal a b "same inputs yield the same refined point")

              testCase "openness rewards gap between two symmetric opponents" (fun _ ->
                  let opps = pts [| 5.0, -3.0; 5.0, 3.0 |]
                  let teams = pts [| 0.0, 0.0 |]
                  let refined = FieldEvaluator.refine evalParams teams opps 0.0 0.0
                  let rx = refined.X
                  let ry = refined.Y

                  let candOpp =
                      opps
                      |> Array.map (fun pt ->
                          MatchMath.distF32 (float32 rx) (float32 ry) (float32 pt.X) (float32 pt.Y))
                      |> Array.min

                  let baseOpp = MatchMath.distF32 0.0f 0.0f 5.0f 3.0f

                  Expect.isGreaterThan
                      candOpp
                      baseOpp
                      "refined point is farther from the nearest opponent than the base"

                  let gapScore = FieldEvaluator.score evalParams teams opps 0.0 0.0
                  let hugScore = FieldEvaluator.score evalParams teams opps 0.0 3.0
                  Expect.isGreaterThan gapScore hugScore "the gap point scores higher than a point hugging an opponent")

              testCase "deterministic with openness params" (fun _ ->
                  let opps = pts [| 5.0, -3.0; 5.0, 3.0 |]
                  let teams = pts [| 0.0, 0.0 |]
                  let a = FieldEvaluator.refine evalParams teams opps 0.0 0.0
                  let b = FieldEvaluator.refine evalParams teams opps 0.0 0.0
                  Expect.equal a b "same openness inputs yield the same refined point")

              testCase "openness favors the compact-and-far point" (fun _ ->
                  let openParams: FieldEvaluator.EvaluatorParams =
                      { TeammateWeight = 0.0
                        OpponentWeight = 0.0
                        ProgressWeight = 0.0
                        OppOurWeight = 1.0
                        SearchRadius = 5.0
                        CandidateCount = 12
                        MinTeammateDist = 4.0
                        OffsideX = None }

                  let teams = pts [| 3.0, 0.0 |]
                  let opps = pts [| -10.0, 0.0 |]
                  let refined = FieldEvaluator.refine openParams teams opps 0.0 0.0
                  let rx = refined.X
                  let ry = refined.Y
                  Expect.isLessThan (abs (rx - 5.0)) 1e-9 "openness picks the point maximizing dOpp - dOur"
                  Expect.isLessThan (abs (ry - 0.0)) 1e-9 "openness keeps the point on the team's axis")

              testCase "GK does not move through the evaluator" (fun _ ->
                  let ctx = TestBuilders.formationCtx ()
                  let lineup = ctx.HomeLineup

                  let gkSlot =
                      lineup.Profiles |> Array.findIndex (fun pr -> pr.Role = SlotRole.Goalkeeper)

                  let gkBaseX = float lineup.BasePositions.[gkSlot].X
                  let world = TestBuilders.formationWorld ()

                  let sctx =
                      { Ctx = ctx
                        Clock = SimulationClock.defaultClock }

                  let buffer =
                      { Events = ResizeArray()
                        Flags = { EventFlags.Bits = 0UL }
                        Applied = ResizeArray() }

                  TeamPosition.step sctx (Rng.ofSeed 42UL) world buffer |> ignore
                  let gkX = float world.Home.Shape.TargetsX.[gkSlot]
                  Expect.isLessThan (abs (gkX - gkBaseX)) 0.5 "GK target stays at its formation rest spot")

              testCase "candidates include offside line crossing" (fun _ ->
                  let withLine = { evalParams with OffsideX = Some 12.0 }
                  let cands = FieldEvaluator.candidates withLine 0.0 0.0

                  let has =
                      cands
                      |> Array.exists (fun pt -> abs (pt.X - 11.0) < 1e-9 && abs (pt.Y - 0.0) < 1e-9)

                  Expect.isTrue has "candidate cloud contains the point just behind the offside line")

              testCase "offside crossing candidate is valid at base Y" (fun _ ->
                  let withLine = { evalParams with OffsideX = Some 10.0 }
                  let cands = FieldEvaluator.candidates withLine 0.0 5.0

                  let has =
                      cands
                      |> Array.exists (fun pt -> abs (pt.X - 9.0) < 1e-9 && abs (pt.Y - 5.0) < 1e-9)

                  Expect.isTrue has "offside candidate reuses the base Y coordinate")

              testCase "no offside candidate when None" (fun _ ->
                  let cands = FieldEvaluator.candidates evalParams 0.0 5.0
                  let has = cands |> Array.exists (fun pt -> abs (pt.X - 9.0) < 1e-9)
                  Expect.isFalse has "no offside candidate when no offside line is provided") ]
