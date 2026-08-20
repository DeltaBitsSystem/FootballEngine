namespace FootballEngine.Core.Tests.Systems.Players

open System.Diagnostics
open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module PlannerBudgetTests =

    let private ctx = TestBuilders.formationCtx ()

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

    let private measureMs (f: unit -> unit) =
        let sw = Stopwatch.StartNew()
        f ()
        sw.Stop()
        sw.Elapsed.TotalMilliseconds

    let plannerBudgetTests =
        testList
            "FootballEngine.Core.Systems.Planner.Budget"
            [ testCase "choose with a full chain completes well under the ms budget" (fun _ ->
                  let world = chainWorld ()
                  let warmup = Rng.ofSeed 0UL
                  let _ = PlayerDecision.choose ctx world ClubSide.Home 5 100<subtick> warmup

                  let ms =
                      measureMs (fun () ->
                          for i in 1..20 do
                              let rng = Rng.ofSeed (uint64 i)
                              let _ = PlayerDecision.choose ctx world ClubSide.Home 5 100<subtick> rng
                              ())

                  Expect.isLessThan (ms / 20.0) 5.0 "choose must complete under 5ms per call")

              testCase "full matches with chains end cleanly" (fun _ ->
                  let systems = Scheduler.systems
                  let seeds = [ 201UL; 202UL ]

                  for seed in seeds do
                      let out = Simulator.runMatch systems ctx seed Simulator.RunMode.Fast None

                      Expect.equal out.Final.State.Flow MatchFlow.MatchEnded "match ended"
                      Expect.isGreaterThanOrEqual out.Final.State.HomeScore 0 "home score sane"
                      Expect.isGreaterThanOrEqual out.Final.State.AwayScore 0 "away score sane") ]
