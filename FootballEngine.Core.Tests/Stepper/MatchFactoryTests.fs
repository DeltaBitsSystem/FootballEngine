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
            [ testCase "Fresh match enters kickoff through the event pipeline" (fun _ ->
                  let world = MatchFactory.create ctx
                  Expect.equal world.State.Flow MatchFlow.PreMatch "kickoff has not bypassed event application")

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

              testCase "Initial body facing follows each team's attack direction" (fun _ ->
                  let world = MatchFactory.create ctx

                  for slot in 0..10 do
                      Expect.equal world.Home.Frame.BodyFacing.[slot] 0.0f "home starts facing positive x"
                      Expect.equal world.Away.Frame.BodyFacing.[slot] System.MathF.PI "away starts facing negative x")

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

              testCase "Participation starts only for the starting eleven" (fun _ ->
                  let ctx16 =
                      { ctx with
                          HomeLineup = TestBuilders.makeLineup 0 16
                          HomeParticipants = MatchLineupBoundary.fromLineup (TestBuilders.makeLineup 0 16) }

                  let world = MatchFactory.create ctx16
                  let intervals = ParticipationBook.snapshot world.Home.Participation 3600<subtick>

                  Expect.hasLength intervals 11 "the bench has no playing interval"

                  Expect.isTrue
                      (intervals |> Array.forall (fun interval -> interval.StartAt = 0<subtick>))
                      "starters begin at kickoff"

                  Expect.isTrue
                      (intervals |> Array.forall (fun interval -> interval.PlayerId <> PlayerId 12))
                      "bench identity is not accidentally active")

              testCase "Substitution preserves player identity and participation intervals" (fun _ ->
                  let ctx16 =
                      { ctx with
                          HomeLineup = TestBuilders.makeLineup 0 16
                          HomeParticipants = MatchLineupBoundary.fromLineup (TestBuilders.makeLineup 0 16) }

                  let world = MatchFactory.create ctx16
                  let at = 3600<subtick>
                  let outgoing = ctx16.HomeLineup.Profiles.[0].Id
                  let incoming = ctx16.HomeLineup.Profiles.[11].Id
                  let incomingRole = ctx16.HomeLineup.Profiles.[11].Role

                  world.State <-
                      { world.State with
                          Tick = at
                          Clock = { world.State.Clock with Tick = at } }

                  world.Home.Frame.BodyFacing.[0] <- 1.0f
                  world.Home.Frame.BodyAngularVelocity.[0] <- -0.4f
                  world.Home.Frame.BodyFacing.[11] <- -1.0f
                  world.Home.Frame.BodyAngularVelocity.[11] <- 0.6f

                  EventApply.apply
                      ctx16
                      world
                      { Tick = at
                        Data = EventData.SubstitutionApplied(outgoing, incoming) }

                  match world.Home.Frame.Occupancy.[0] with
                  | OccupancyKind.Sidelined { Id = id
                                              Reason = PlayerOutReason.Substituted } ->
                      Expect.equal id outgoing "the outgoing identity remains attached to the slot"
                  | other -> failtestf "expected outgoing slot to be sidelined, got %A" other

                  match world.Home.Frame.Occupancy.[11] with
                  | OccupancyKind.Active slot -> Expect.equal slot 11 "the incoming occupies the bench slot"
                  | other -> failtestf "expected incoming slot to be active, got %A" other

                  Expect.equal
                      world.Home.Frame.SlotRoles.[11]
                      incomingRole
                      "the incoming role is read from its own identity slot"

                  Expect.equal world.Home.SubsUsed 1 "one substitution is consumed"

                  Expect.equal
                      world.Home.Frame.BodyFacing.[11]
                      0.0f
                      "incoming player starts with the team's neutral attack-facing"

                  Expect.equal
                      world.Home.Frame.BodyAngularVelocity.[11]
                      0.0f
                      "incoming player does not inherit an old turn"

                  let intervals = ParticipationBook.snapshot world.Home.Participation at

                  Expect.equal
                      (intervals
                       |> Array.tryFind (fun interval -> interval.PlayerId = outgoing)
                       |> Option.map _.EndAt)
                      (Some(Some at))
                      "the outgoing interval ends at the event tick"

                  Expect.equal
                      (intervals
                       |> Array.tryFind (fun interval -> interval.PlayerId = incoming)
                       |> Option.map _.StartAt)
                      (Some at)
                      "the incoming interval starts at the event tick")

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
