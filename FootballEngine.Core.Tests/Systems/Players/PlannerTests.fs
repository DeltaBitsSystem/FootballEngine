namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module PlannerTests =

    let private weights: WeightSectionsPlanner =
        { ProgressionWeight = 0.6
          ShootValueWeight = 0.35
          ShootFinishingAssumption = 75.0
          DangerBase = [| 20.0; 10.0; 5.0; 3.0; 2.0; 1.0; 0.0 |]
          DangerZoneWeights = [| 1.0; 0.5; 0.25 |]
          PossessionSecurityWeight = 0.8 }

    let private cfg: PlannerConfig =
        { MaxChainLength = 4
          MaxEvaluateLimit = 500
          PlanTTL = 480<subtick> }

    /// The same intercept model the production path uses, built from the same
    /// defaults: the planner and the tests must never hardcode a second,
    /// drifting feasibility model.
    let private pparams: InterceptTable.InterceptParams =
        PassIntercept.passParams
            BalanceConfig.defaultConfig.Intercept
            BalanceConfig.defaultConfig.Movement
            BalanceConfig.defaultConfig.Ball

    let private state
        (carrier: int)
        (ours: (int * float * float) list)
        (opps: (int * float * float) list)
        : ChainState =
        { BallX = 0.0<meter>
          BallY = 0.0<meter>
          CarrierSlot = carrier
          Side = ClubSide.Home
          OurPos =
            ours
            |> List.toArray
            |> Array.map (fun (s, x, y) -> struct (s, x * 1.0<meter>, y * 1.0<meter>))
          OppPos =
            opps
            |> List.toArray
            |> Array.map (fun (s, x, y) -> struct (s, x * 1.0<meter>, y * 1.0<meter>))
          OffsideLine = 45.0<meter>
          Dir = 1.0 }

    let private farOpps =
        [ 1, -40.0, 25.0
          2, -40.0, -25.0
          3, -40.0, 10.0
          4, -40.0, -10.0
          5, -40.0, 0.0
          6, -40.0, 15.0
          7, -40.0, -15.0
          8, -40.0, 20.0
          9, -40.0, -20.0
          10, -40.0, 5.0 ]

    let private opts = [ (1, 6, 0.6); (1, 9, 0.4); (3, -1, 0.5) ]

    let private s = state 5 [ 5, 0.0, 0.0; 6, 12.0, 8.0; 9, 26.0, 0.0 ] farOpps

    let private search state options =
        Planner.search cfg weights pparams state (List.toArray options)

    let private containsTarget slot (chain: Planner.ChainAction[]) =
        chain
        |> Array.exists (fun action ->
            match action with
            | Planner.Pass(s, _, _) -> s = slot
            | _ -> false)

    let plannerTests =
        testList
            "FootballEngine.Core.Systems.Planner.PlannerTests"
            [ testCase "returns one evaluation per level-1 option in input order" (fun _ ->
                  let r = search s opts
                  Expect.hasLength r 3 "three options"

                  Expect.equal r.[0].ActionCode 1 "first code"
                  Expect.equal r.[0].TargetSlot 6 "first target"
                  Expect.equal r.[1].ActionCode 1 "second code"
                  Expect.equal r.[1].TargetSlot 9 "second target"
                  Expect.equal r.[2].ActionCode 3 "third code"
                  Expect.equal r.[2].TargetSlot -1 "third target")

              testCase "a two-pass chain extends past level-1 and adds continuation value" (fun _ ->
                  let r = search s [ (1, 6, 0.6) ]
                  Expect.hasLength r 1 "single option"
                  Expect.equal r.[0].ActionCode 1 "code"
                  Expect.equal r.[0].TargetSlot 6 "target"
                  Expect.isGreaterThanOrEqual r.[0].Chain.Length 2 "chain extends past level-1"
                  Expect.isGreaterThan r.[0].Value 0.6 "chain adds value over the option's utility")

              testCase "offside receiver never appears in a chain" (fun _ ->
                  let s' =
                      state 5 [ 5, 0.0, 0.0; 6, 12.0, 8.0; 9, 26.0, 0.0 ] farOpps
                      |> fun st -> { st with OffsideLine = 20.0<meter> }

                  let r = search s' [ (1, 6, 0.6) ]
                  Expect.isGreaterThan r.Length 0 "at least one eval"

                  for ev in r do
                      Expect.isFalse (containsTarget 9 ev.Chain) "offside slot 9 must never be planned")

              testCase "an infeasible later pass is not planned" (fun _ ->
                  let s' =
                      state 5 [ 5, 0.0, 0.0; 6, 12.0, 8.0; 9, 26.0, 0.0 ] (farOpps @ [ 11, 19.0, 4.0 ])

                  let r = search s' [ (1, 6, 0.6) ]

                  for ev in r do
                      match ev.Chain with
                      | [| Planner.Pass(6, _, _); Planner.Pass(9, _, _); _ |] ->
                          failtest "must not plan an infeasible 6->9 pass"
                      | _ -> ())

              testCase "the option that opens a shot outscores a dead-end option" (fun _ ->
                  let sDead =
                      state 5 [ 5, 0.0, 0.0; 6, 12.0, 0.0; 8, -30.0, -26.0; 9, 26.0, 0.0 ] farOpps

                  let r = search sDead [ (1, 6, 0.5); (1, 8, 0.5) ]

                  Expect.isGreaterThan r.Length 1 "two options returned"

                  let via6 = r |> Array.find (fun e -> e.TargetSlot = 6)
                  let via8 = r |> Array.find (fun e -> e.TargetSlot = 8)
                  Expect.isGreaterThan via6.Value via8.Value "shot-opening pass wins")

              testCase "identical geometry with a higher utility wins" (fun _ ->
                  let r = search s [ (1, 6, 1.2); (1, 6, 0.3) ]
                  Expect.hasLength r 2 "two evaluations of the same pass"
                  Expect.isGreaterThan r.[0].Value r.[1].Value "the better player's option ranks first")

              testCase "search is deterministic" (fun _ ->
                  let a = search s opts
                  let b = search s opts
                  Expect.equal a b "identical state produces identical results")

              testCase "tiny MaxEvaluateLimit caps depth of lower-ranked options" (fun _ ->
                  let cfgTiny = { cfg with MaxEvaluateLimit = 1 }

                  let passOpts = [ (1, 6, 0.6); (1, 9, 0.5) ]
                  let rTiny = Planner.search cfgTiny weights pparams s (List.toArray passOpts)
                  Expect.isGreaterThanOrEqual rTiny.Length 1 "at least one"

                  match rTiny with
                  | [| first; second |] ->
                      Expect.equal first.Chain.Length 1 "top option gets one expansion"
                      Expect.equal second.Chain.Length 0 "lower option gets nothing"
                  | [| first |] -> Expect.equal first.Chain.Length 1 "sole option gets one expansion"
                  | _ -> failtest "unexpected result count")

              testCase "options absent from level-1 list never appear as first actions" (fun _ ->
                  let r = search s [ (1, 6, 0.6) ]
                  Expect.hasLength r 1 "single first-action"

                  Expect.equal r.[0].ActionCode 1 "pass code"
                  Expect.equal r.[0].TargetSlot 6 "pass target"
                  Expect.isFalse (r |> Array.exists (fun e -> e.ActionCode = 2)) "no shot first action"
                  Expect.isFalse (r |> Array.exists (fun e -> e.ActionCode = 3)) "no dribble first action")

              testCase "a cross option is planned as a cross, valued at its own utility" (fun _ ->
                  let r = search s [ (5, -1, 0.5) ]
                  Expect.hasLength r 1 "one cross option"

                  match r.[0].Chain with
                  | [| Planner.Cross _ |] -> ()
                  | other -> failtestf "cross must stay a cross, got %A" other

                  Expect.equal r.[0].Value 0.5 "a terminal cross keeps its calibrated utility")

              testCase "the opponent GK is not an interceptor" (fun _ ->
                  let sGkOnLine = state 5 [ 5, 0.0, 0.0; 9, 10.0, 0.0 ] [ 0, 9.5, 0.0 ]

                  let r = search sGkOnLine [ (1, 9, 0.6) ]
                  Expect.hasLength r 1 "one pass option"
                  Expect.isGreaterThanOrEqual r.[0].Chain.Length 2 "the pass stays playable past the keeper") ]
