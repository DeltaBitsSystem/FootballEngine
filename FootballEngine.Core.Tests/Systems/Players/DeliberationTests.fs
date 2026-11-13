namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module DeliberationTests =
    let private deliberation = 4<subtick>

    let private ctx =
        let ctx = TestBuilders.formationCtx ()
        { ctx with Config = { ctx.Config with Planner = { ctx.Config.Planner with DeliberationTime = deliberation } } }

    let private system = { Ctx = ctx; Clock = SimulationClock.defaultClock }
    let private buffer () = { Events = ResizeArray(); Applied = ResizeArray(); Flags = { Bits = 0UL } }

    /// A home carrier in open play with every opponent far away, so nothing but the decision acts on him.
    let private carrierWorld () =
        let world = MatchFactory.create ctx
        world.State <- { world.State with Tick = 4<subtick>; Flow = MatchFlow.Live; Ball = TestBuilders.emptyBall }
        world.Home.Frame.PosX[5] <- 0.0f
        world.Home.Frame.PosY[5] <- 0.0f

        for slot in MatchWorld.activeSlots world.Away.Frame do
            world.Away.Frame.PosX[slot] <- 30.0f
            world.Away.Frame.PosY[slot] <- 20.0f

        world

    let private decided (world: World) slot =
        world.Home.Proposals.ContainsKey slot || world.Home.PendingActions.ContainsKey slot

    let private possession (world: World) side player =
        { Tick = world.State.Tick; Data = EventData.Possession(side, player) }

    let tests =
        testList
            "Deliberation"
            [ testCase "a carrier's choice arrives when the deliberation is due, not before" (fun _ ->
                  let world = carrierWorld ()
                  let carrier = world.Home.RosterIds[5]
                  let started = world.State.Tick
                  let buf = buffer ()
                  buf.Events.Add(possession world ClubSide.Home carrier)
                  let mutable rng = Rng.ofSeed 3UL
                  let next () = let r, _, _ = Stepper.step Scheduler.systems system rng world buf in rng <- r

                  next ()
                  Expect.isTrue world.Cognition.Plan.IsSome "the decision began a deliberation"
                  Expect.isFalse (decided world 5) "no choice on the tick the decision began"

                  for _ in 1 .. int deliberation - 1 do
                      next ()
                      Expect.isFalse (decided world 5) $"no choice by tick {int world.State.Tick - 1}, before it is due"

                  next ()

                  Expect.equal world.State.Ball.Control (BallControl.Controlled(ClubSide.Home, carrier)) "the fixture keeps the same carrier"
                  Expect.isTrue (decided world 5) "the choice is made once the deliberation is due"

                  let proposal =
                      match world.Home.Proposals.TryGetValue 5 with
                      | true, p -> p
                      | _ -> world.Home.PendingActions[5]

                  Expect.equal proposal.DecidedAt (started + deliberation) "the choice is dated when it was made")

              testCase "a deliberation whose carrier lost the ball chooses nothing" (fun _ ->
                  let world = carrierWorld ()
                  let carrier = world.Home.RosterIds[5]
                  let started = world.State.Tick
                  let buf = buffer ()
                  buf.Events.Add(possession world ClubSide.Home carrier)
                  let mutable rng = Rng.ofSeed 3UL
                  let next () = let r, _, _ = Stepper.step Scheduler.systems system rng world buf in rng <- r

                  next ()
                  Expect.isTrue world.Cognition.Plan.IsSome "the decision began a deliberation"
                  world.Away.Frame.PosX[5] <- 0.5f
                  world.Away.Frame.PosY[5] <- 0.0f
                  buf.Events.Add(possession world ClubSide.Away world.Away.RosterIds[5])

                  while world.State.Tick <= started + deliberation do
                      next ()

                  Expect.isTrue world.Cognition.Plan.IsNone "the deliberation ended when due"
                  Expect.isFalse (decided world 5) "the former carrier chose nothing") ]
