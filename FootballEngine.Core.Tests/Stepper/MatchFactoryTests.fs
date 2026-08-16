namespace FootballEngine.Core.Tests.Stepper

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module MatchFactoryTests =

    let private ctx = TestBuilders.defaultCtx ()
    let private clock = SimulationClock.defaultClock

    let matchFactoryTests =
        testList
            "FootballEngine.Core.Stepper.MatchFactoryTests"
            [ testCase "Fresh match starts with the kickoff delay" (fun _ ->
                  let world = MatchFactory.create ctx
                  let expected = Flow.delayFor ctx.Config clock SetPieceKind.KickOff

                  match world.State.Flow with
                  | MatchFlow.RestartDelay(d, plan) ->
                      Expect.equal d expected "kickoff delay"
                      Expect.equal plan.Kind SetPieceKind.KickOff "kickoff plan"
                      Expect.equal plan.Side ClubSide.Home "home kicks off"
                      Expect.equal (float plan.X) 0.0 "ball at centre"
                  | other -> failtestf "expected RestartDelay, got %A" other)

              testCase "Scoreboard and clock start at zero" (fun _ ->
                  let world = MatchFactory.create ctx

                  Expect.equal world.State.HomeScore 0 "home score"
                  Expect.equal world.State.AwayScore 0 "away score"
                  Expect.equal world.State.Tick 0<subtick> "tick zero"
                  Expect.equal world.State.Clock.Phase ClockPhase.FirstHalf "first half")

              testCase "Both teams field 11 active players" (fun _ ->
                  let world = MatchFactory.create ctx

                  Expect.equal world.Home.Frame.ActiveCount 11 "home active"
                  Expect.equal world.Away.Frame.ActiveCount 11 "away active"

                  Expect.equal
                      (world.Home.Frame.Occupancy
                       |> Array.filter (fun o ->
                           match o with
                           | OccupancyKind.Active _ -> true
                           | _ -> false)
                       |> Array.length)
                      11
                      "home occupancy")

              testCase "Away positions mirror home" (fun _ ->
                  let world = MatchFactory.create ctx

                  for i in 0..10 do
                      Expect.equal
                          (float world.Away.Frame.PosX.[i])
                          (-(float world.Home.Frame.PosX.[i]))
                          (sprintf "slot %d mirrored" i))

              testCase "Extra slots are sidelined as substitutes" (fun _ ->
                  let ctx16 =
                      { ctx with
                          HomeLineup = TestBuilders.makeLineup 0 16 }

                  let world = MatchFactory.create ctx16

                  Expect.equal world.Home.Frame.ActiveCount 11 "11 active with 16-man squad"

                  let sidelined =
                      world.Home.Frame.Occupancy
                      |> Array.filter (fun o ->
                          match o with
                          | OccupancyKind.Sidelined { Reason = PlayerOutReason.Substituted } -> true
                          | _ -> false)
                      |> Array.length

                  Expect.equal sidelined 5 "five on the bench")

              testCase "Ball starts free at the centre spot" (fun _ ->
                  let world = MatchFactory.create ctx

                  Expect.equal (float world.State.Ball.Position.X) 0.0 "x centre"
                  Expect.equal (float world.State.Ball.Position.Y) 0.0 "y centre"
                  Expect.equal world.State.Ball.Control BallControl.Free "free ball")

              testCase "Each team owns its own memory, stats and books" (fun _ ->
                  let world = MatchFactory.create ctx

                  Expect.equal world.Home.Memory.Length 11 "home memory"
                  Expect.equal world.Away.Memory.Length 11 "away memory"
                  Expect.equal world.Home.Cards.Yellows 0 "no cards yet"
                  Expect.equal world.Home.SubsUsed 0 "no subs yet") ]
