namespace FootballEngine.Core.Tests.Diagnostics

open System
open System.Security.Cryptography
open System.Text
open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module Gate0Tests =

    let private buffer () : EventBuffer =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private systemCtx (ctx: MatchContext) : SystemContext =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    // Sorting keyed state keeps hash comparisons independent of dictionary enumeration.
    let private canonicalTeam (team: TeamWorld) =
        let runs =
            team.Frame.RunLookup
            |> Seq.map (fun entry -> entry.Key, entry.Value)
            |> Seq.sortBy fst
            |> Array.ofSeq

        (team.Frame.PosX,
         team.Frame.PosY,
         team.Frame.VelX,
         team.Frame.VelY,
         team.Frame.Condition,
         team.Frame.Mental,
         team.Frame.Intent,
         team.Frame.Occupancy,
         team.Frame.SlotRoles,
         team.Frame.DefensiveRoles,
         team.Frame.CollectiveIntents,
         team.Frame.SupportX,
         team.Frame.SupportY,
         team.Frame.VisibilityTm,
         team.Frame.VisibilityOpp,
         team.Frame.OppsNear,
         team.Frame.PressCount,
         team.Frame.LaneClear,
         team.Frame.FaceCone,
         team.Frame.Cognitive,
         team.Frame.RoleBias,
         runs,
         team.Frame.ActiveCount,
         team.Frame.SlotCount,
         team.Frame.ActiveCache.Slots,
         team.Frame.ActiveCache.Revision,
         team.Frame.ActiveCache.ComputedFor,
         team.Memory,
         team.Cards,
         team.SubsUsed,
         team.Shape,
         (team.Directive, team.Tactics),
         team.Blackboard,
         team.Emergent,
         team.Intercept,
         team.Stats,
         team.CFrame,
         team.EventWindow.Head,
         team.EventWindow.Length,
         team.EventWindow.Ticks,
         team.EventWindow.Kinds,
         team.EventWindow.Counts,
         CollectiveAttackState.snapshot team.Attack,
         team.LastCarrierSlot,
         team.PendingActions
         |> Seq.map (fun entry -> entry.Key, entry.Value)
         |> Seq.sortBy fst
         |> Seq.toArray)

    let private canonicalState (world: World) : string =
        let state =
            (world.State.Tick,
             world.State.Clock,
             world.State.Flow,
             world.State.HomeScore,
             world.State.AwayScore,
             world.State.Ball,
             world.State.Momentum,
             world.State.LastAttackingClub,
             world.State.Possession,
             world.State.Stoppage)

        let reviews =
            world.Reviews.Offside
            |> Seq.map (fun entry -> entry.Key, entry.Value)
            |> Seq.sortBy fst
            |> Seq.toArray

        sprintf "%A|%A|%A|%A" state (canonicalTeam world.Home) (canonicalTeam world.Away) reviews

    let private canonicalHash (world: World) : string =
        canonicalState world
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString

    let tests =
        testList
            "FootballEngine.Core.Diagnostics.Gate0Tests"
            [ testCase "pass launch supplies impulse for continuous ball movement" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let sctx = systemCtx ctx
                  let world = MatchFactory.create ctx
                  let passerSlot = 5
                  let receiverSlot = 4
                  let passer = ctx.HomeLineup.Profiles.[passerSlot].Id
                  let origin = MatchWorld.position world.Home.Frame passerSlot

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Live
                          Ball =
                              { world.State.Ball with
                                  Position = origin
                                  Control = BallControl.Controlled(ClubSide.Home, passer) } }

                  Pass.launch sctx (Rng.ofSeed 17UL) world (buffer ()) ClubSide.Home passerSlot receiverSlot 0.0
                  |> ignore

                  let afterKick = world.State.Ball.Position
                  Expect.equal afterKick origin "request does not kick during preparation"

                  world.State <-
                      { world.State with
                          Tick = world.State.Tick + ctx.Config.Ball.KickWindup }

                  BallFootContact.step sctx (Rng.ofSeed 17UL) world (buffer ()) |> ignore

                  let impulseSpeed =
                      MatchMath.speedOf world.State.Ball.Position.Vx world.State.Ball.Position.Vy

                  Expect.isGreaterThan impulseSpeed 0.0 "a kick must create horizontal impulse"

                  BallPhysics.step sctx (Rng.ofSeed 17UL) world (buffer ()) |> ignore
                  let afterIntegration = world.State.Ball.Position

                  let displacement =
                      MatchMath.dist origin.X origin.Y afterIntegration.X afterIntegration.Y

                  Expect.isGreaterThan displacement 0.0 "the next position must derive from the kick")

              testCase "pause freezes play clock and does not end a half from simulation tick" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()
                  let playTick = 107999<subtick>

                  world.State <-
                      { world.State with
                          Tick = 108000<subtick>
                          Clock =
                              { Tick = playTick
                                Phase = ClockPhase.FirstHalf }
                          Flow = MatchFlow.GoalPause(3<tickDelta>, ClubSide.Home, None) }

                  let buf = buffer ()
                  Stepper.step [] (systemCtx ctx) (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.equal world.State.Clock.Tick playTick "pause does not consume play clock"

                  Expect.isFalse
                      (buf.Events |> Seq.exists (fun ev -> ev.Data = EventData.HalfTime))
                      "simulation tick alone cannot end a paused half")

              testCase "substitution activates the incoming player identity" (fun _ ->
                  let ctx =
                      { TestBuilders.defaultCtx () with
                          HomeLineup = TestBuilders.makeLineup 0 13 }

                  let world = MatchFactory.create ctx
                  let playerOut = ctx.HomeLineup.Profiles.[5].Id
                  let playerIn = ctx.HomeLineup.Profiles.[11].Id

                  EventApply.apply
                      ctx
                      world
                      { Tick = 0<subtick>
                        Data = EventData.Substitution(playerOut, playerIn) }

                  let outSlot = MatchWorld.slotOf ctx ClubSide.Home playerOut |> Option.get
                  let inSlot = MatchWorld.slotOf ctx ClubSide.Home playerIn |> Option.get
                  let active = MatchWorld.activeSlots world.Home.Frame

                  Expect.isFalse (Array.contains outSlot active) "the outgoing player is no longer active"
                  Expect.isTrue (Array.contains inSlot active) "the incoming player is active"
                  Expect.equal world.Home.Frame.ActiveCount 11 "substitution preserves the active-player count")

              testCase "a card event is applied exactly once through the stepper" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id
                  let buf = buffer ()

                  buf.Events.Add
                      { Tick = 0<subtick>
                        Data = EventData.Card(pid, CardColor.Yellow) }

                  let cardSystem: SystemSpec =
                      { Name = "Gate0.RefereeCards"
                        Side = SystemSide.Body
                        Triggers = [ Trigger.OnEvent(EventFlags.ofKind EventKind.Card) ]
                        Step = RefereeCards.step }

                  Stepper.step [ cardSystem ] (systemCtx ctx) (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.equal world.Home.Cards.Yellows 1 "one card event creates one team booking"
                  Expect.equal world.Home.Memory.[5].Yellows 1 "one card event creates one player booking")

              testCase "EventWindow preserves capacity and order after wrap" (fun _ ->
                  let window = EventWindow.create 3
                  EventWindow.push window 10<subtick> EventKind.Foul
                  EventWindow.push window 11<subtick> EventKind.Tackle
                  EventWindow.push window 12<subtick> EventKind.Foul
                  EventWindow.push window 13<subtick> EventKind.Card

                  Expect.equal window.Length 3 "wrapped window retains capacity entries"
                  Expect.equal window.Head 1 "head advances when the oldest entry expires"

                  let orderedTicks =
                      [| for i in 0 .. window.Length - 1 -> window.Ticks.[(window.Head + i) % window.Ticks.Length] |]

                  Expect.equal
                      orderedTicks
                      [| 11<subtick>; 12<subtick>; 13<subtick> |]
                      "entries remain in chronological order"

                  Expect.equal
                      (EventWindow.countInWindow window 13<subtick> 3<subtick> EventKind.Foul)
                      1
                      "counts follow the wrapped entries")

              testCase "same seed produces the same canonical full final state" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()

                  let fast =
                      Simulator.runMatch Scheduler.systems ctx 0xD00DFEEDUL Simulator.RunMode.Fast
                      |> TestBuilders.requireMatchOutcome

                  let full =
                      Simulator.runMatch Scheduler.systems ctx 0xD00DFEEDUL (Simulator.RunMode.Replay 20<tickDelta>)
                      |> TestBuilders.requireMatchOutcome

                  Expect.equal (canonicalHash fast.Final) (canonicalHash full.Final) "full state hash identical"
                  Expect.equal fast.EventsHash full.EventsHash "event hash identical"
                  Expect.equal fast.Final.State.Flow MatchFlow.MatchEnded "fast run ended"
                  Expect.equal full.Final.State.Flow MatchFlow.MatchEnded "full run ended") ]
