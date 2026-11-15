namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module PlannerTests =

    let private weights = BalanceConfig.defaultConfig.ExpectedValue

    let private cfg: PlannerConfig =
        { BalanceConfig.defaultPlanner with
            MaxChainLength = 4
            MaxEvaluateLimit = 500 }

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
          Dir = 1.0
          OurMotion = [||]
          OppMotion = [||]
          Horizon = BalanceConfig.defaultConfig.CollectiveAttack.AnticipationHorizon
          Confidence = 1.0
          GoalkeeperSlots = SlotMask.add 0 0UL
          OurGoalkeeperSlots = SlotMask.add 0 0UL
          PassBall = BalanceConfig.defaultConfig.Ball
          PassArrivalSpeed = BalanceConfig.defaultConfig.Actions.PassArrivalSpeed
          PassArrivalSpeedGain = BalanceConfig.defaultConfig.Actions.PassArrivalSpeedGain
          MaxPassDistance = BalanceConfig.defaultConfig.Actions.MaxPassExecutionDistance
          CarrySpeed = 4.0<meterPerSecond> }

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

    let temporalTests =
        testList
            "PlannerTemporal"
            [ testCase
                  "each evaluated candidate consumes the work budget and roots receive first consideration"
                  (fun _ ->
                      let options =
                          [| for slot in [ 6; 9 ] do
                                 yield 1, slot, 0.6 |]

                      let limited =
                          Planner.search { cfg with MaxEvaluateLimit = 5 } weights pparams s options

                      Expect.isLessThanOrEqual
                          (limited |> Array.sumBy _.CandidateEvaluations)
                          5
                          "rejected and terminal branches count too"

                      for ev in limited do
                          Expect.isGreaterThan
                              ev.CandidateEvaluations
                              0
                              "early continuation cannot starve a later initial option")
              testCase "offside uses ball halfway and projected actual defenders" (fun _ ->
                  let behindBall =
                      { s with
                          BallX = 30.0<meter>
                          OffsideLine = 20.0<meter> }

                  Expect.isFalse (Planner.isOffside behindBall 25.0<meter>) "behind the ball is legal"

                  Expect.isFalse
                      (Planner.isOffside
                          { s with
                              BallX = -20.0<meter>
                              OffsideLine = -10.0<meter> }
                          -5.0<meter>)
                      "own half is legal"

                  let moving =
                      { s with
                          OppPos =
                              [| struct (1, 20.0<meter>, 20.0<meter>)
                                 struct (2, 22.0<meter>, -20.0<meter>) |]
                          OppMotion =
                              [| struct (1, -3.0<meterPerSecond>, 0.0<meterPerSecond>)
                                 struct (2, -3.0<meterPerSecond>, 0.0<meterPerSecond>) |]
                          OffsideLine = 20.0<meter> }

                  let next = Planner.simulatePass moving 6 12.0<meter> 8.0<meter>

                  Expect.isLessThan
                      next.OffsideLine
                      moving.OffsideLine
                      "observed retreat changes legality at the next touch")
              testCase "horizon bounds all legs configured launch speed and uncertainty bounds dribble value" (fun _ ->
                  let first = Planner.simulatePass s 6 12.0<meter> 8.0<meter>
                  let second = Planner.simulatePass first 9 26.0<meter> 0.0<meter>
                  Expect.isLessThan second.Horizon first.Horizon "future legs cannot restart the horizon"

                  let slow =
                      Planner.simulatePass
                          { s with
                              PassArrivalSpeed = 1.0<meterPerSecond>
                              PassArrivalSpeedGain = 0.0 }
                          6
                          12.0<meter>
                          8.0<meter>

                  Expect.isLessThan
                      slow.Horizon
                      first.Horizon
                      "a softer configured arrival speed takes longer to travel the same distance"

                  let result =
                      Planner.search cfg weights pparams { s with Confidence = 0.0 } [| 3, -1, 0.6 |]

                  Expect.equal result[0].Value 0.6 "uncertain dribble forecast cannot manufacture value")
              testCase "delayed moving receiver changes anticipation without consulting rival choices" (fun _ ->
                  let moving =
                      { s with
                          OurMotion = [| struct (9, 5.0<meterPerSecond>, 0.0<meterPerSecond>) |] }

                  let original = search s [ 1, 6, 0.6 ]
                  let forecast = search moving [ 1, 6, 0.6 ]
                  Expect.notEqual forecast[0].Value original[0].Value "observed future advantage matters"
                  Expect.isLessThanOrEqual forecast[0].Chain.Length cfg.MaxChainLength "bounded nontrivial search"

                  let slots =
                      forecast[0].Chain
                      |> Array.choose (function
                          | Planner.Pass(slot, _, _) -> Some slot
                          | _ -> None)

                  Expect.equal (Array.distinct slots).Length slots.Length "no profitable endless return-pass cycles") ]

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

              testCase "received forecast co-locates ball and receiver and spends cumulative time" (fun _ ->
                  let moving =
                      { s with
                          OurMotion = [| struct (6, 3.0<meterPerSecond>, 1.0<meterPerSecond>) |] }

                  let received = Planner.simulatePass moving 6 12.0<meter> 8.0<meter>

                  let struct (_, x, y) =
                      received.OurPos |> Array.find (fun struct (slot, _, _) -> slot = 6)

                  Expect.equal
                      (received.BallX, received.BallY)
                      (x, y)
                      "ball belongs to the predicted carrier, not his abandoned launch position"

                  Expect.isLessThan received.Horizon moving.Horizon "one temporal budget across the entire chain")

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
                  Expect.isGreaterThanOrEqual r.[0].Chain.Length 2 "the pass stays playable past the keeper")

              testCase "tabled passes fly the exact roll within a millisecond at every playable length" (fun _ ->
                  let s = state 5 [ 5, 0.0, 0.0 ] []
                  let mutable worstSeconds = 0.0
                  let mutable worstSpeed = 0.0

                  // Millimetre lengths fall between the table's samples, where interpolation error peaks.
                  for millimetres in 0..1 .. int (float s.MaxPassDistance * 1000.0) do
                      let distance = float millimetres / 1000.0

                      let exact =
                          BallAero.pass s.PassBall s.PassArrivalSpeed s.PassArrivalSpeedGain distance

                      let tabled = Planner.tabledPass cfg.PassTableResolution s distance
                      worstSeconds <- max worstSeconds (abs (tabled.Seconds - exact.Seconds))
                      worstSpeed <- max worstSpeed (abs (tabled.LaunchSpeed - exact.LaunchSpeed))

                  Expect.isLessThan worstSeconds 1e-3 $"flight error {worstSeconds:E2} s"
                  Expect.isLessThan worstSpeed 1e-2 $"launch error {worstSpeed:E2} m/s") ]
