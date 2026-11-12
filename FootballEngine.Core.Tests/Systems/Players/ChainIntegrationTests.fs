namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module ChainIntegrationTests =

    let private ctx = TestBuilders.formationCtx ()

    /// Build a world with geometry designed for chain tests:
    /// carrier slot 5 at (0,0); slot 6 at (12,8); slot 9 at (26,0);
    /// opp at (13,0) blocks direct pass to 9; two opps at (2,1.5), (2,-1.5)
    /// suppress dribble (pressure=2) but don't block passes.
    let private chainWorld () =
        let world = TestBuilders.formationWorld ()

        for slot in 1..10 do
            world.Away.Frame.PosX.[slot] <- -40.0f
            world.Away.Frame.PosY.[slot] <- float32 (if slot % 2 = 0 then 25.0 else -25.0)

        world.Away.Frame.PosX.[1] <- 13.0f
        world.Away.Frame.PosY.[1] <- 0.0f
        world.Away.Frame.PosX.[2] <- 2.0f
        world.Away.Frame.PosY.[2] <- 1.5f
        world.Away.Frame.PosX.[3] <- 2.0f
        world.Away.Frame.PosY.[3] <- -1.5f

        world.Home.Frame.PosX.[5] <- 0.0f
        world.Home.Frame.PosY.[5] <- 0.0f
        world.Home.Frame.PosX.[6] <- 12.0f
        world.Home.Frame.PosY.[6] <- 8.0f
        world.Home.Frame.PosX.[9] <- 26.0f
        world.Home.Frame.PosY.[9] <- 0.0f

        world.State <-
            { world.State with
                Tick = 100<subtick>
                Ball =
                    { world.State.Ball with
                        Position =
                            { world.State.Ball.Position with
                                X = 0.0<meter>
                                Y = 0.0<meter> }
                        Control = BallControl.Controlled(ClubSide.Home, ctx.HomeLineup.Profiles.[5].Id) } }

        world

    let chainIntegrationTests =
        testList
            "FootballEngine.Core.Systems.PlayerDecision.Chain"
            [ testCase "choose writes a typed action with chain scores" (fun _ ->
                  let world = chainWorld ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, ctx.HomeLineup.Profiles.[5].Id) } }

                  let rng = Rng.ofSeed 42UL

                  let _ = PlayerDecision.choose ctx world ClubSide.Home 5 100<subtick> rng

                  let assignment = world.Home.Proposals[5]

                  Expect.equal
                      assignment.ExpiresAt
                      (100<subtick> + ctx.Config.Movement.CommittedRunDuration)
                      "action validity"

                  Expect.isFalse
                      (world.Home.Frame.RunLookup.ContainsKey 5)
                      "choosing an action does not occupy movement assignments")

              testCase "choose with chains is deterministic" (fun _ ->
                  let world = chainWorld ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, ctx.HomeLineup.Profiles.[5].Id) } }

                  let a = Rng.ofSeed 42UL
                  let _ = PlayerDecision.choose ctx world ClubSide.Home 5 100<subtick> a
                  let mA = world.Home.Proposals[5]
                  let pA = CollectiveAttackState.snapshot world.Home.Attack

                  let world2 = chainWorld ()

                  world2.State <-
                      { world2.State with
                          Ball =
                              { world2.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, ctx.HomeLineup.Profiles.[5].Id) } }

                  let b = Rng.ofSeed 42UL
                  let _ = PlayerDecision.choose ctx world2 ClubSide.Home 5 100<subtick> b
                  let mB = world2.Home.Proposals[5]
                  let pB = CollectiveAttackState.snapshot world2.Home.Attack

                  Expect.equal mA mB "markers identical"
                  Expect.equal pA pB "plans identical")

              testCase "choosing an action does not overwrite the carrier movement intent" (fun _ ->
                  let world = chainWorld ()
                  let frame = world.Home.Frame
                  let slot = 5
                  frame.Intent.Kind.[slot] <- IntentCode.BallCarry
                  frame.Intent.TargetX.[slot] <- 24.0f
                  frame.Intent.TargetY.[slot] <- -8.0f
                  frame.Intent.Phase.[slot] <- IntentPhase.Executing

                  let _ =
                      PlayerDecision.choose ctx world ClubSide.Home slot 100<subtick> (Rng.ofSeed 42UL)

                  Expect.equal frame.Intent.Kind.[slot] IntentCode.BallCarry "intent kind remains owned by Intent"
                  Expect.equal frame.Intent.TargetX.[slot] 24.0f "decision does not pin the carrier x target"
                  Expect.equal frame.Intent.TargetY.[slot] -8.0f "decision does not pin the carrier y target"

                  Expect.equal
                      frame.Intent.Phase.[slot]
                      IntentPhase.Executing
                      "decision does not alter intent lifetime")

              testCase "the deciding carrier is never planned as a future receiver" (fun _ ->
                  let world = chainWorld ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, ctx.HomeLineup.Profiles.[5].Id) } }

                  let rng = Rng.ofSeed 42UL
                  let _ = PlayerDecision.choose ctx world ClubSide.Home 5 100<subtick> rng

                  Expect.isNone world.Home.Attack.Episode "individual Decision cannot invent or overwrite a collective episode"
                  Expect.equal world.Home.Attack.NextId 1UL "team owner alone allocates commitments") ]
