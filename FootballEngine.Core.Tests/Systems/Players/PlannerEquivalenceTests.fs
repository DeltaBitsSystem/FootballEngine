namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module PlannerEquivalenceTests =

    type private Input =
        { Config: PlannerConfig
          Chain: ChainState
          Plan: PlanSlot
          Options: (int * int * float)[]
          Intercept: InterceptTable.InterceptParams }

    /// Every input a real carrier's decision hands the planner during the first ticks of a seeded match: the
    /// decision system is wrapped so the inputs are built by the same function `prepare` uses.
    let private capture (wanted: int) : Input[] =
        let ctx = TestBuilders.defaultCtx ()
        let captured = ResizeArray<Input>()

        // The second config makes the beam, budget and chain length differ from the default, so the comparison
        // is not confined to one parameter set.
        let alternate =
            { ctx.Config.Planner with
                MaxChainLength = 4
                BeamWidth = 3
                MaxEvaluateLimit = 500 }

        let recording =
            Scheduler.systems
            |> List.map (fun spec ->
                if spec.Name <> "PlayerDecision" then
                    spec
                else
                    { spec with
                        Step =
                            fun sctx rng world buffer ->
                                match
                                    world.Cognition.Plan.Active,
                                    world.State.Ball.PendingStrike,
                                    MatchWorld.ballCarrier sctx.Ctx world
                                with
                                | false, None, ValueSome(struct (side, slot, _)) when
                                    captured.Count < wanted
                                    && (MatchWorld.profileOf sctx.Ctx side slot).Role <> SlotRole.Goalkeeper
                                    ->
                                    let inputs =
                                        PlayerDecision.decisionInputs sctx.Ctx world side slot world.State.Tick

                                    captured.Add
                                        { Config =
                                            (if captured.Count % 2 = 0 then
                                                 ctx.Config.Planner
                                             else
                                                 alternate)
                                          Chain = inputs.Chain
                                          Plan = inputs.Plan
                                          Options = inputs.PlannerOptions
                                          Intercept = inputs.Intercept }
                                | _ -> ()

                                spec.Step sctx rng world buffer })

        match Simulator.start recording ctx 42UL MatchRunMode.Fast with
        | Error error -> failtestf "match did not start: %A" error
        | Ok session ->
            match MatchSession.advanceTo 15000<subtick> session with
            | Error error -> failtestf "match did not advance: %A" error
            | Ok _ -> ()

        captured.ToArray()

    let tests =
        testList
            "FootballEngine.Core.Systems.Planner.Equivalence"
            [ testCase "the arena search returns exactly what the reference search returns on real decisions" (fun _ ->
                  let inputs = capture 2500

                  Expect.isGreaterThanOrEqual
                      inputs.Length
                      2000
                      "the match must supply at least 2000 real planner inputs"

                  let e = BalanceConfig.defaultConfig.ExpectedValue

                  let chained =
                      inputs
                      |> Array.filter (fun i -> i.Options |> Array.exists (fun (code, _, _) -> code = 1 || code = 3))
                      |> Array.length

                  Expect.isGreaterThan
                      chained
                      1000
                      "most inputs must have a first action that can chain, or nothing deep is compared"

                  let mutable deep = 0

                  inputs
                  |> Array.iteri (fun index i ->
                      let expected = PlannerReference.search i.Config e i.Intercept i.Chain i.Options
                      let actual = Planner.search i.Config e i.Intercept i.Chain i.Options

                      if expected |> Array.exists (fun r -> r.Chain.Length > 1) then
                          deep <- deep + 1

                      Expect.equal actual expected (sprintf "input %d: planner evaluations differ" index)

                      // The production search runs over the slot's own arrays, filled by projectInto, with the
                      // planner config of this input.
                      let config =
                          { BalanceConfig.defaultConfig with
                              Planner = i.Config }

                      Planner.searchInto config i.Intercept i.Plan
                      let result = i.Plan.Result
                      Expect.equal result.Count expected.Length (sprintf "input %d: searchInto result count" index)

                      expected
                      |> Array.iteri (fun k r ->
                          Expect.equal result.Value[k] r.Value (sprintf "input %d option %d: value" index k)
                          Expect.equal result.ActionCode[k] r.ActionCode (sprintf "input %d option %d: code" index k)

                          Expect.equal
                              result.TargetSlot[k]
                              r.TargetSlot
                              (sprintf "input %d option %d: target" index k)

                          Expect.equal
                              result.CandidateEvaluations[k]
                              r.CandidateEvaluations
                              (sprintf "input %d option %d: evaluations" index k)))

                  Expect.isGreaterThan deep 200 "enough searches must reach multi-step chains to exercise continuation")

              testCase
                  "a hand-built state with more than eleven a side and many options is searched without limit"
                  (fun _ ->
                      let slots = [| 0..29 |]

                      let ours =
                          slots
                          |> Array.map (fun s ->
                              struct (s, float (s * 2 - 30) * 1.0<meter>, float (s % 7 * 8 - 24) * 1.0<meter>))

                      let opps =
                          slots
                          |> Array.map (fun s ->
                              struct (s, float (s * 2 - 28) * 1.0<meter>, float (s % 5 * 10 - 20) * 1.0<meter>))

                      let cfg = BalanceConfig.defaultConfig

                      let chain =
                          { BallX = 0.0<meter>
                            BallY = 0.0<meter>
                            CarrierSlot = 15
                            Side = ClubSide.Home
                            OurPos = ours
                            OppPos = opps
                            OffsideLine = 45.0<meter>
                            Dir = 1.0
                            OurMotion = [||]
                            OppMotion = [||]
                            Horizon = cfg.CollectiveAttack.AnticipationHorizon
                            Confidence = 1.0
                            GoalkeeperSlots = SlotMask.add 0 0UL
                            OurGoalkeeperSlots = SlotMask.add 0 0UL
                            PassBall = cfg.Ball
                            PassArrivalSpeed = cfg.Actions.PassArrivalSpeed
                            PassArrivalSpeedGain = cfg.Actions.PassArrivalSpeedGain
                            MaxPassDistance = cfg.Actions.MaxPassExecutionDistance
                            CarrySpeed = 4.0<meterPerSecond> }

                      let options = [| for s in 1..29 -> (1, s, 0.1) |] |> Array.append [| (3, -1, 0.2) |]

                      let planner =
                          { cfg.Planner with
                              MaxEvaluateLimit = 4096
                              BeamWidth = 8
                              MaxChainLength = 5 }

                      let pparams = PassIntercept.passParams cfg.Intercept cfg.Movement cfg.Ball

                      let expected =
                          PlannerReference.search planner cfg.ExpectedValue pparams chain options

                      let actual = Planner.search planner cfg.ExpectedValue pparams chain options

                      Expect.equal
                          actual
                          expected
                          "a lineup larger than the arena's first sizing gives the reference's answer") ]
