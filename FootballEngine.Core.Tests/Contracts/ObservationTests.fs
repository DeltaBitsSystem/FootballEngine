namespace FootballEngine.Core.Tests.Contracts

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module ObservationTests =
    let private unwrap =
        function
        | Ok value -> value
        | Error error -> failtestf "unexpected error: %A" error

    let private create () =
        let input =
            { MatchId = 11UL
              Context = TestBuilders.defaultCtx ()
              Seed = 11UL
              EffectiveConfig = EffectiveConfig.defaultConfig
              EngineVersion = "observation-tests" }

        MatchSession.create Scheduler.systems input MatchRunMode.Fast |> unwrap

    let tests =
        testList
            "FootballEngine.Core.Contracts.Observation"
            [ testCase "bounded ring exposes eviction and does not mutate published snapshots" (fun _ ->
                  let buffer = ObservationBuffer(2)
                  buffer.Record(0<subtick>, ValueNone, CommandEffective(1UL, 1UL)) |> ignore
                  let before = buffer.Snapshot()

                  for sequence in 2UL .. 5UL do
                      buffer.Record(int sequence * 1<subtick>, ValueNone, CommandEffective(1UL, sequence))
                      |> ignore

                  let after = buffer.Snapshot()
                  Expect.equal after.Evicted 3UL "retention loss is explicit"
                  Expect.equal after.Total 5UL "occurrence sequencing survives eviction"

                  Expect.equal
                      (after.Entries |> Array.map _.Id)
                      [| ObservationId 4UL; ObservationId 5UL |]
                      "chronological retention"

                  Expect.equal before.Entries[0].Id (ObservationId 1UL) "publication owns its array")
              testCase "decisions link only to impulses executed in their explicit action scope" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let actor = PlayerId 1

                  world.Observation.Decision(
                      0<subtick>,
                      { Actor = actor
                        ActionCode = 1
                        TargetSlot = 2
                        Condition = 0.8f
                        Cognitive = 0.7f
                        Pressure = 1
                        OptionCount = 2
                        ChosenValue = 0.6
                        Temperature = 0.1
                        Sample = 0.4 }
                  )

                  world.Observation.BeginAction actor

                  let impulse =
                      BallPhysics.createImpulse
                          actor
                          { X = 0.0<meter>
                            Y = 0.0<meter>
                            Z = 0.0<meter> }
                          { X = 10.0<meterPerSecond>
                            Y = 0.0<meterPerSecond>
                            Z = 0.0<meterPerSecond> }
                          { Top = 0.0<radianPerSecond>
                            Side = 0.0<radianPerSecond>
                            Roll = 0.0<radianPerSecond> }
                          None
                          StrikeKind.Pass
                          (SimTick.ofSubTicks 0<subtick>)
                          ExecutionContext.OpenPlay

                  BallPhysics.applyImpulse world impulse TrajectoryIntent.Cleared None
                  world.Observation.EndAction()
                  let snapshot = world.Observation.Snapshot()
                  let applied = snapshot.Entries[1]

                  Expect.equal
                      applied.Cause
                      (ValueSome(DecisionCause snapshot.Entries[0].Id))
                      "explicit decision source"

                  Expect.hasLength (Observation.causes snapshot applied) 1 "cause is queryable"
                  world.Observation.Impulse(1<subtick>, impulse)

                  Expect.equal
                      (world.Observation.Snapshot().Entries[2].Cause)
                      ValueNone
                      "no nearest-in-time invented cause")
              testCase
                  "restart impulses retain their explicit restart identity even without a player decision"
                  (fun _ ->
                      let world = TestBuilders.minimalWorld ()

                      let award =
                          { Id = RestartId 7UL
                            Cause = RestartCause.MatchStart
                            Kind = SetPieceKind.KickOff
                            Side = ClubSide.Home
                            X = 0.0<meter>
                            Y = 0.0<meter> }

                      world.Observation.Record(0<subtick>, ValueNone, RestartCreated award) |> ignore

                      let impulse =
                          BallPhysics.createImpulse
                              (PlayerId 1)
                              { X = 0.0<meter>
                                Y = 0.0<meter>
                                Z = 0.0<meter> }
                              { X = 10.0<meterPerSecond>
                                Y = 0.0<meterPerSecond>
                                Z = 0.0<meterPerSecond> }
                              { Top = 0.0<radianPerSecond>
                                Side = 0.0<radianPerSecond>
                                Roll = 0.0<radianPerSecond> }
                              None
                              (StrikeKind.Restart SetPieceKind.KickOff)
                              (SimTick.ofSubTicks 1<subtick>)
                              (ExecutionContext.SetPiece SetPieceKind.KickOff)
                          |> BallPhysics.withRestartId award.Id

                      world.Observation.Impulse(1<subtick>, impulse)
                      let snapshot = world.Observation.Snapshot()

                      Expect.equal
                          snapshot.Entries[1].Cause
                          (ValueSome(RestartReference award.Id))
                          "restart execution never invents an individual utility decision"

                      Expect.hasLength
                          (Observation.chain snapshot.Entries[1].Id snapshot |> unwrap).Entries
                          2
                          "award to restart impulse is explicit"

                      let evicted =
                          { snapshot with
                              Entries = [| snapshot.Entries[1] |] }

                      Expect.equal
                          (Observation.chain snapshot.Entries[1].Id evicted |> unwrap).MissingCauses
                          [| RestartReference award.Id |]
                          "evicted award stays visibly absent")
              testCase "capture disabled preserves events RNG and complete sporting state" (fun _ ->
                  let recorded = create ()
                  let unrecorded = create ()
                  unrecorded.World.Observation.Enabled <- false
                  MatchSession.advanceTo 4000<subtick> recorded |> unwrap |> ignore
                  MatchSession.advanceTo 4000<subtick> unrecorded |> unwrap |> ignore
                  Expect.equal recorded.Rng unrecorded.Rng "capture does not consume randomness"
                  Expect.equal (List.ofSeq recorded.Events) (List.ofSeq unrecorded.Events) "same authoritative events"
                  Expect.equal recorded.World.State unrecorded.World.State "same ball flow score clock"
                  Expect.equal recorded.World.Home.Frame.PosX unrecorded.World.Home.Frame.PosX "same player movement"

                  Expect.equal
                      (Replay.capture recorded.World)
                      (Replay.capture unrecorded.World)
                      "same complete published physical state"

                  Expect.isEmpty (MatchSession.observations unrecorded).Entries "disabled collector produces no rows"
                  Expect.isNonEmpty (MatchSession.observations recorded).Entries "normal matches carry evidence")
              testCase "selective alternatives use existing evaluations without changing the decision" (fun _ ->
                  let first = TestBuilders.minimalWorld ()
                  let second = TestBuilders.minimalWorld ()
                  let ctx = TestBuilders.defaultCtx ()
                  let actor = ctx.HomeLineup.Profiles[1].Id

                  first.Observation.SelectDetail(
                      Some
                          { Actor = actor
                            FromTick = 0<subtick>
                            UntilTick = 1<subtick> }
                  )
                  |> unwrap

                  let rng = Rng.ofSeed 42UL
                  let observed = PlayerDecision.choose ctx first ClubSide.Home 1 0<subtick> rng
                  let plain = PlayerDecision.choose ctx second ClubSide.Home 1 0<subtick> rng
                  Expect.equal observed plain "detail capture consumes no RNG"

                  let markers world =
                      world.Home.PendingActions
                      |> Seq.map (fun pair -> pair.Key, pair.Value)
                      |> Seq.sortBy fst
                      |> Seq.toArray

                  Expect.equal (markers first) (markers second) "chosen action is identical"

                  let details =
                      first.Observation.Snapshot().Entries
                      |> Array.filter (fun entry ->
                          match entry.Data with
                          | DecisionAlternatives _ -> true
                          | _ -> false)

                  Expect.hasLength details 1 "selected actor decision is captured"
                  Expect.isFalse (first.Observation.WantsDetail(2<subtick>, actor)) "interval is bounded"

                  Expect.isFalse
                      (first.Observation.WantsDetail(0<subtick>, PlayerId 999))
                      "other actors are not captured"

                  Expect.isError
                      (first.Observation.SelectDetail(
                          Some
                              { Actor = actor
                                FromTick = 2<subtick>
                                UntilTick = 1<subtick> }
                      ))
                      "invalid selection rejected")
              testCase "source associations survive deferred application and expose capacity loss" (fun _ ->
                  let buffer = ObservationBuffer(1)

                  let first =
                      { Tick = 0<subtick>
                        Data = EventData.BallLoose(0.0<meter>, 0.0<meter>) }

                  let second =
                      { Tick = 1<subtick>
                        Data = EventData.BallLoose(1.0<meter>, 0.0<meter>) }

                  buffer.Associate(first, ActionCause(ActionId 1UL))
                  buffer.Associate(second, ActionCause(ActionId 2UL))

                  Expect.equal
                      (buffer.ConsumeCause second)
                      (ValueSome(ActionCause(ActionId 2UL)))
                      "explicit source survives tick boundary"

                  Expect.equal (buffer.ConsumeCause second) ValueNone "source consumed once"
                  Expect.equal (buffer.Snapshot().Unlinked) 1UL "link capacity loss is visible")
              testCase "full match result is unchanged with collection off" (fun _ ->
                  let first = create ()
                  let second = create ()
                  second.World.Observation.Enabled <- false
                  let recorded = MatchSession.finish first |> unwrap
                  let unrecorded = MatchSession.finish second |> unwrap
                  Expect.equal recorded.EventsHash unrecorded.EventsHash "terminal causal event hash"
                  Expect.equal recorded.Final.State unrecorded.Final.State "terminal sporting state"
                  Expect.equal first.Rng second.Rng "terminal RNG"
                  Expect.isGreaterThan (MatchSession.observations first).Total 0UL "evidence remains available")
              testCase "actor queries follow retained links but never temporal proximity or evicted ancestry" (fun _ ->
                  let buffer = ObservationBuffer(3)
                  let actor = PlayerId 1

                  buffer.Record(
                      0<subtick>,
                      ValueNone,
                      ActionRejected(actor, PassDistanceLimit(12.0<meter>, 10.0<meter>))
                  )
                  |> ignore

                  buffer.Record(
                      0<subtick>,
                      ValueSome(DecisionCause(ObservationId 1UL)),
                      ProbabilityResolved(Reception, None, 0.5, false)
                  )
                  |> ignore

                  let event =
                      { Tick = 0<subtick>
                        Data = EventData.BallLoose(0.0<meter>, 0.0<meter>) }

                  buffer.Record(0<subtick>, ValueSome(ResolutionCause(ObservationId 2UL)), EventCommitted event)
                  |> ignore

                  let query =
                      { Actor = Some actor
                        Action = None
                        Event = None
                        FromTick = 0<subtick>
                        UntilTick = 0<subtick> }

                  Expect.hasLength
                      (Observation.query query (buffer.Snapshot()) |> unwrap)
                      3
                      "explicit ancestry includes its consequences"

                  buffer.Record(0<subtick>, ValueNone, CommandEffective(1UL, 1UL)) |> ignore

                  Expect.isEmpty
                      (Observation.query query (buffer.Snapshot()) |> unwrap)
                      "same-tick unrelated rows and missing actor context cannot establish identity")
              testCase "malformed causal cycles terminate without inventing an actor" (fun _ ->
                  let buffer = ObservationBuffer(2)

                  buffer.Record(0<subtick>, ValueSome(ResolutionCause(ObservationId 2UL)), CommandEffective(1UL, 1UL))
                  |> ignore

                  buffer.Record(0<subtick>, ValueSome(ResolutionCause(ObservationId 1UL)), CommandEffective(1UL, 2UL))
                  |> ignore

                  let query =
                      { Actor = Some(PlayerId 1)
                        Action = None
                        Event = None
                        FromTick = 0<subtick>
                        UntilTick = 1<subtick> }

                  Expect.isEmpty
                      (Observation.query query (buffer.Snapshot()) |> unwrap)
                      "visited occurrence IDs bound traversal")
              testCase "text chains identify cycles missing evidence and future sources" (fun _ ->
                  let buffer = ObservationBuffer(4)

                  buffer.Record(0<subtick>, ValueSome(ResolutionCause(ObservationId 2UL)), CommandEffective(1UL, 1UL))
                  |> ignore

                  buffer.Record(0<subtick>, ValueSome(ResolutionCause(ObservationId 1UL)), CommandEffective(1UL, 2UL))
                  |> ignore

                  buffer.Record(1<subtick>, ValueSome(ActionCause(ActionId 999UL)), CommandEffective(1UL, 3UL))
                  |> ignore

                  buffer.Record(2<subtick>, ValueNone, CommandEffective(1UL, 4UL)) |> ignore
                  let snapshot = buffer.Snapshot()
                  let cycle = Observation.chain (ObservationId 1UL) snapshot |> unwrap
                  Expect.hasLength cycle.Entries 2 "each retained occurrence is emitted once"
                  Expect.equal cycle.Cycles [| ObservationId 1UL |] "cycle is reported instead of silently rewritten"
                  let missing = Observation.chain (ObservationId 3UL) snapshot |> unwrap

                  Expect.equal
                      missing.MissingCauses
                      [| ActionCause(ActionId 999UL) |]
                      "missing physical context remains absent"

                  let changed =
                      { snapshot with
                          Entries = Array.copy snapshot.Entries }

                  changed.Entries[0] <-
                      { changed.Entries[0] with
                          Cause = ValueSome(ResolutionCause(ObservationId 4UL)) }

                  changed.Entries[3] <-
                      { changed.Entries[3] with
                          Data = ActionRejected(PlayerId 3, PassDistanceLimit(12.0<meter>, 10.0<meter>)) }

                  let future = Observation.chain (ObservationId 1UL) changed |> unwrap
                  Expect.hasLength future.Entries 1 "later records cannot be reported as ancestors"

                  Expect.equal
                      future.FutureCauses
                      [| ObservationId 1UL, ObservationId 4UL |]
                      "invalid temporal edge remains diagnosable"

                  let earlier =
                      { Actor = Some(PlayerId 3)
                        Action = None
                        Event = None
                        FromTick = 0<subtick>
                        UntilTick = 0<subtick> }

                  Expect.isEmpty
                      (Observation.query earlier changed |> unwrap)
                      "text and UI queries cannot attribute an earlier event through a future source"

                  Expect.isError
                      (Observation.chain (ObservationId 99UL) snapshot)
                      "unretained root does not fabricate a chain")
              testCase "duel consequences retain the actual sampled resolution without changing RNG or events" (fun _ ->
                  let mutable sawFoul, sawWin, sawLoss = false, false, false

                  for chance in [ 0.0; 1.0 ] do
                      for seed in 1UL .. 16UL do
                          let baseCtx = TestBuilders.defaultCtx ()

                          let ctx =
                              { baseCtx with
                                  Config =
                                      { baseCtx.Config with
                                          Actions =
                                              { baseCtx.Config.Actions with
                                                  DuelFoulChance = chance } } }

                          let sctx =
                              { Ctx = ctx
                                Clock = SimulationClock.defaultClock }

                          let captured = TestBuilders.minimalWorld ()
                          let plain = TestBuilders.minimalWorld ()
                          plain.Observation.Enabled <- false
                          let pid = ctx.HomeLineup.Profiles[1].Id

                          captured.Observation.Decision(
                              0<subtick>,
                              { Actor = pid
                                ActionCode = 3
                                TargetSlot = 0
                                Condition = 1.0f
                                Cognitive = 1.0f
                                Pressure = 1
                                OptionCount = 1
                                ChosenValue = 0.5
                                Temperature = 0.2
                                Sample = 0.1 }
                          )

                          captured.Observation.BeginAction pid

                          let events =
                              { Events = ResizeArray()
                                Applied = ResizeArray()
                                Flags = { Bits = 0UL } }

                          let plainEvents =
                              { Events = ResizeArray()
                                Applied = ResizeArray()
                                Flags = { Bits = 0UL } }

                          let rng = Rng.ofSeed seed
                          let actual = Duel.adjudicate sctx rng captured events ClubSide.Home 1
                          let expected = Duel.adjudicate sctx rng plain plainEvents ClubSide.Home 1
                          captured.Observation.EndAction()
                          Expect.equal actual expected "observation consumes no sporting draws"

                          Expect.equal
                              (Seq.toArray events.Events)
                              (Seq.toArray plainEvents.Events)
                              "evidence does not replace current sporting outcomes"

                          let resolution =
                              captured.Observation.Snapshot().Entries
                              |> Array.find (fun entry ->
                                  match entry.Data with
                                  | DuelResolved _ -> true
                                  | _ -> false)

                          match resolution.Data with
                          | DuelResolved duel ->
                              use json =
                                  System.Text.Json.JsonDocument.Parse(
                                      Observation.exportJson
                                          { captured.Observation.Snapshot() with
                                              Entries = [| resolution |] }
                                  )

                              let row = json.RootElement.GetProperty("Entries")[0]
                              let data = row.GetProperty("Duel")[0]

                              Expect.equal
                                  (data.GetProperty("WinDraw").GetDouble())
                                  duel.WinDraw
                                  "JSON exports the actual draw, not only prose"

                              Expect.equal
                                  (data.GetProperty("FoulProbability").GetDouble())
                                  chance
                                  "JSON exports resolved configuration"

                              Expect.equal
                                  duel.Foul
                                  (duel.FoulDraw < duel.FoulProbability)
                                  "foul follows its actual draw"

                              Expect.equal
                                  duel.Won
                                  (not duel.Foul && duel.WinDraw < duel.WinProbability)
                                  "foul branch precedes win result"

                              sawFoul <- sawFoul || duel.Foul
                              sawWin <- sawWin || duel.Won
                              sawLoss <- sawLoss || (not duel.Foul && not duel.Won)
                          | _ -> failtest "expected duel evidence"

                          for event in events.Events do
                              let cause = captured.Observation.ConsumeCause event

                              Expect.equal
                                  cause
                                  (ValueSome(ResolutionCause resolution.Id))
                                  "each emitted consequence has its actual adjudication source"

                              let root = captured.Observation.Record(event.Tick, cause, EventCommitted event)

                              match root with
                              | ValueSome id ->
                                  Expect.hasLength
                                      (Observation.chain id (captured.Observation.Snapshot()) |> unwrap).Entries
                                      3
                                      "decision to resolution to consequence"
                              | ValueNone -> failtest "enabled capture rejected fixture evidence"

                  Expect.isTrue sawFoul "forced foul cases exercise precedence"
                  Expect.isTrue sawWin "fixture includes successful dribble"
                  Expect.isTrue sawLoss "fixture includes failed dribble/tackle/possession")
              testCase "duel without an eligible opponent is rejected without draws or invented contact" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  for slot in 0 .. world.Away.Frame.SlotCount - 1 do
                      world.Away.Frame.PosX[slot] <- 1000.0f

                  let ctx = TestBuilders.defaultCtx ()

                  let events =
                      { Events = ResizeArray()
                        Applied = ResizeArray()
                        Flags = { Bits = 0UL } }

                  let rng = Rng.ofSeed 11UL
                  let state = world.State

                  Expect.equal
                      (Duel.adjudicate
                          { Ctx = ctx
                            Clock = SimulationClock.defaultClock }
                          rng
                          world
                          events
                          ClubSide.Home
                          1)
                      rng
                      "no candidate means no draws"

                  Expect.equal world.State state "no adjudication changes sporting state"
                  Expect.isEmpty events.Events "no invisible fabricated contact consequence"
                  let evidence = world.Observation.Snapshot()
                  Expect.hasLength evidence.Entries 1 "failed attempt remains observable"

                  Expect.equal
                      evidence.Entries[0].Data
                      (ActionRejected(ctx.HomeLineup.Profiles[1].Id, NoDuelOpponent))
                      "reason is the actual failed feasibility gate")
              testCase "cross attempt distinguishes an applied impulse from a rejected target search" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()

                  let sctx =
                      { Ctx = ctx
                        Clock = SimulationClock.defaultClock }

                  for available in [ false; true ] do
                      let captured, plain = TestBuilders.minimalWorld (), TestBuilders.minimalWorld ()
                      plain.Observation.Enabled <- false

                      if available then
                          let x = float32 (float (MatchMath.goalX ClubSide.Home) - 6.0)
                          captured.Home.Frame.PosX[2] <- x
                          plain.Home.Frame.PosX[2] <- x

                      let pid = ctx.HomeLineup.Profiles[1].Id

                      for world in [ captured; plain ] do
                          world.Home.Frame.PosX[1] <- 0.0f
                          world.Home.Frame.PosY[1] <- 0.0f

                          world.State <-
                              { world.State with
                                  Flow = MatchFlow.Live
                                  Ball =
                                      { world.State.Ball with
                                          Control = BallControl.Controlled(ClubSide.Home, pid) } }

                      captured.Observation.Decision(
                          0<subtick>,
                          { Actor = pid
                            ActionCode = 5
                            TargetSlot = 2
                            Condition = 1.0f
                            Cognitive = 1.0f
                            Pressure = 0
                            OptionCount = 1
                            ChosenValue = 0.5
                            Temperature = 0.2
                            Sample = 0.1 }
                      )

                      captured.Observation.BeginAction pid

                      let events =
                          { Events = ResizeArray()
                            Applied = ResizeArray()
                            Flags = { Bits = 0UL } }

                      let plainEvents =
                          { Events = ResizeArray()
                            Applied = ResizeArray()
                            Flags = { Bits = 0UL } }

                      let rng = Rng.ofSeed 11UL

                      Expect.equal
                          (Cross.launch sctx rng captured events ClubSide.Home 1)
                          (Cross.launch sctx rng plain plainEvents ClubSide.Home 1)
                          "capture changes no RNG"

                      captured.Observation.EndAction()

                      if available then
                          Expect.isNone captured.State.Ball.Impulse "preparation is not impulse evidence"

                          for world, buffer in [ captured, events; plain, plainEvents ] do
                              world.State <-
                                  { world.State with
                                      Tick = ctx.Config.Ball.KickWindup }

                              BallFootContact.step sctx rng world buffer |> ignore

                      Expect.equal captured.State plain.State "capture changes no sporting state"

                      Expect.equal
                          (Seq.toArray events.Events)
                          (Seq.toArray plainEvents.Events)
                          "attempt semantics remain unchanged"

                      let attempt =
                          events.Events
                          |> Seq.find (fun event ->
                              match event.Data with
                              | EventData.CrossAttempt _ -> true
                              | _ -> false)

                      let cause = captured.Observation.ConsumeCause attempt
                      let root = captured.Observation.Record(attempt.Tick, cause, EventCommitted attempt)
                      let snapshot = captured.Observation.Snapshot()

                      let impulses =
                          snapshot.Entries
                          |> Array.filter (fun entry ->
                              match entry.Data with
                              | ImpulseApplied _ -> true
                              | _ -> false)

                      let rejections =
                          snapshot.Entries
                          |> Array.filter (fun entry ->
                              match entry.Data with
                              | ActionRejected(_, NoCrossTarget) -> true
                              | _ -> false)

                      Expect.hasLength
                          impulses
                          (if available then 1 else 0)
                          "only an executed delivery has impulse evidence"

                      Expect.hasLength rejections (if available then 0 else 1) "missing target has explicit rejection"

                      match root with
                      | ValueSome id ->
                          let chain = Observation.chain id snapshot |> unwrap

                          Expect.hasLength
                              chain.Entries
                              3
                              "attempt links execution or rejection and the actual chosen decision"

                          Expect.isEmpty chain.MissingCauses "fixture contains all actual sources"
                      | ValueNone -> failtest "enabled fixture has no root")
              testCase "dismissals link their actual card source without duplicating sporting consequences" (fun _ ->
                  for color, yellows, enabled, expected in
                      [ CardColor.Red, 0, true, 1
                        CardColor.Yellow, 2, true, 1
                        CardColor.Yellow, 1, true, 0
                        CardColor.Yellow, 2, false, 0 ] do
                      let baseCtx = TestBuilders.defaultCtx ()

                      let ctx =
                          { baseCtx with
                              Config =
                                  { baseCtx.Config with
                                      Referee =
                                          { baseCtx.Config.Referee with
                                              SecondYellowSuspension = enabled } } }

                      let sctx =
                          { Ctx = ctx
                            Clock = SimulationClock.defaultClock }

                      let captured, plain = TestBuilders.minimalWorld (), TestBuilders.minimalWorld ()

                      captured.Home.Memory[1] <-
                          { captured.Home.Memory[1] with
                              Yellows = yellows }

                      plain.Home.Memory[1] <-
                          { plain.Home.Memory[1] with
                              Yellows = yellows }

                      plain.Observation.Enabled <- false

                      let card =
                          { Tick = 0<subtick>
                            Data = EventData.Card(ctx.HomeLineup.Profiles[1].Id, color) }

                      let events =
                          { Events = ResizeArray()
                            Applied = ResizeArray([ card ])
                            Flags = EventFlags.ofKind EventKind.Card }

                      let plainEvents =
                          { Events = ResizeArray()
                            Applied = ResizeArray([ card ])
                            Flags = EventFlags.ofKind EventKind.Card }

                      let rng = Rng.ofSeed 11UL

                      Expect.equal
                          (RefereeCards.step sctx rng captured events)
                          (RefereeCards.step sctx rng plain plainEvents)
                          "evidence consumes no dismissal RNG"

                      Expect.equal
                          (Seq.toArray events.Events)
                          (Seq.toArray plainEvents.Events)
                          "card-to-dismissal policy remains unchanged"

                      Expect.hasLength
                          (Seq.toArray events.Events)
                          expected
                          "direct red and enabled second yellow dismiss, other cases do not"

                      captured.Observation.Record(card.Tick, ValueNone, EventCommitted card) |> ignore

                      for event in events.Events do
                          let cause = captured.Observation.ConsumeCause event

                          Expect.equal
                              cause
                              (ValueSome(EventCause(MatchEvent.id card)))
                              "dismissal records its actual disciplinary source"

                          let id = captured.Observation.Record(event.Tick, cause, EventCommitted event)

                          match id with
                          | ValueSome root ->
                              Expect.hasLength
                                  (Observation.chain root (captured.Observation.Snapshot()) |> unwrap).Entries
                                  2
                                  "card and dismissal chain"
                          | ValueNone -> failtest "enabled fixture lost dismissal evidence"

                      RefereeCards.step sctx rng captured events |> ignore

                      Expect.hasLength
                          (Seq.toArray events.Events)
                          expected
                          "reprocessing cannot duplicate a queued dismissal")
              testCase "whole-window audit reports every malformed branch without certifying uncaused roots" (fun _ ->
                  let buffer = ObservationBuffer(4)
                  buffer.Record(0<subtick>, ValueNone, CommandEffective(1UL, 1UL)) |> ignore

                  buffer.Record(1<subtick>, ValueSome(ResolutionCause(ObservationId 1UL)), CommandEffective(1UL, 2UL))
                  |> ignore

                  buffer.Record(2<subtick>, ValueSome(EventCause(EventId 99UL)), CommandEffective(1UL, 3UL))
                  |> ignore

                  buffer.Record(3<subtick>, ValueNone, CommandEffective(1UL, 4UL)) |> ignore
                  let snapshot = buffer.Snapshot()

                  snapshot.Entries[3] <-
                      { snapshot.Entries[3] with
                          Id = ObservationId 1UL }

                  let audit = Observation.audit snapshot

                  Expect.equal
                      audit.DuplicateOccurrences
                      [| ObservationId 1UL |]
                      "duplicate occurrences cannot hide in an index"

                  Expect.equal
                      audit.MissingCauses
                      [| ObservationId 3UL, EventCause(EventId 99UL) |]
                      "missing branch includes child and typed source"

                  Expect.equal
                      audit.AmbiguousCauses
                      [| ObservationId 2UL, ResolutionCause(ObservationId 1UL) |]
                      "duplicate source does not select an arbitrary ancestor"

                  Expect.equal
                      audit.FutureCauses
                      [| ObservationId 2UL, ObservationId 1UL |]
                      "invalid later occurrence is explicit"

                  Expect.hasLength audit.Uncaused 2 "roots are classified, not silently treated as legal coverage"
                  use json = System.Text.Json.JsonDocument.Parse(Observation.exportAuditJson snapshot)

                  Expect.equal
                      (json.RootElement.GetProperty("Retained").GetInt32())
                      4
                      "audit records its actual captured window"

                  Expect.equal
                      (json.RootElement.GetProperty("MissingCauses").GetArrayLength())
                      1
                      "JSON preserves all missing edges"

                  let group = json.RootElement.GetProperty("UncausedKinds")[0]

                  Expect.equal
                      (group.GetProperty("Kind").GetString())
                      "command"
                      "root types are explicit rather than silently counted as explained consequences"

                  Expect.equal
                      (group.GetProperty("Count").GetInt32())
                      2
                      "both retained uncaused occurrences are counted")
              testCase "long retained chains traverse iteratively without dropping shared ancestors" (fun _ ->
                  let count = 4096
                  let buffer = ObservationBuffer(count)

                  for sequence in 1..count do
                      let cause =
                          if sequence = 1 then
                              ValueNone
                          else
                              ValueSome(ResolutionCause(ObservationId(uint64 (sequence - 1))))

                      buffer.Record(sequence * 1<subtick>, cause, CommandEffective(1UL, uint64 sequence))
                      |> ignore

                  let snapshot = buffer.Snapshot()
                  let result = Observation.chain (ObservationId(uint64 count)) snapshot |> unwrap
                  Expect.hasLength result.Entries count "every retained ancestor survives a deep traversal"

                  Expect.equal
                      result.Entries[count - 1].Id
                      (ObservationId 1UL)
                      "the earliest actual source is preserved"

                  Expect.isEmpty result.Cycles "depth alone is not a cycle"
                  Expect.isEmpty result.MissingCauses "bounded complete fixture loses no ancestors"
                  Expect.isEmpty result.FutureCauses "strictly earlier causes remain legal")
              testCase
                  "JSON causal chains preserve diagnostics and uint64 identities without invented entries"
                  (fun _ ->
                      let buffer = ObservationBuffer(3)

                      buffer.Record(
                          0<subtick>,
                          ValueSome(ResolutionCause(ObservationId 2UL)),
                          CommandEffective(1UL, 1UL)
                      )
                      |> ignore

                      buffer.Record(
                          0<subtick>,
                          ValueSome(ResolutionCause(ObservationId 1UL)),
                          CommandEffective(1UL, 2UL)
                      )
                      |> ignore

                      buffer.Record(
                          1<subtick>,
                          ValueSome(EventCause(EventId System.UInt64.MaxValue)),
                          CommandEffective(1UL, 3UL)
                      )
                      |> ignore

                      let snapshot = buffer.Snapshot()

                      use cyclic =
                          System.Text.Json.JsonDocument.Parse(
                              Observation.exportChainJson (ObservationId 1UL) snapshot |> unwrap
                          )

                      Expect.equal (cyclic.RootElement.GetProperty("Root").GetUInt64()) 1UL "explicit root identity"

                      Expect.equal
                          (cyclic.RootElement.GetProperty("Entries").GetArrayLength())
                          2
                          "each occurrence appears once"

                      let cycle = cyclic.RootElement.GetProperty("Cycles")[0]
                      Expect.equal (cycle.GetUInt64()) 1UL "cycle is machine-readable"

                      use missing =
                          System.Text.Json.JsonDocument.Parse(
                              Observation.exportChainJson (ObservationId 3UL) snapshot |> unwrap
                          )

                      let absent = missing.RootElement.GetProperty("MissingCauses")[0]

                      Expect.equal
                          (absent.GetProperty("Id").GetUInt64())
                          System.UInt64.MaxValue
                          "source IDs are not narrowed or packed into strings"

                      Expect.equal (absent.GetProperty("Kind").GetString()) "event" "missing source remains typed"

                      Expect.equal
                          (missing.RootElement.GetProperty("Entries").GetArrayLength())
                          1
                          "absent cause does not become an entry"

                      let changed =
                          { snapshot with
                              Entries = Array.copy snapshot.Entries }

                      changed.Entries[0] <-
                          { changed.Entries[0] with
                              Cause = ValueSome(ResolutionCause(ObservationId 3UL)) }

                      use future =
                          System.Text.Json.JsonDocument.Parse(
                              Observation.exportChainJson (ObservationId 1UL) changed |> unwrap
                          )

                      let edge = future.RootElement.GetProperty("FutureCauses")[0]
                      Expect.equal (edge.GetProperty("Child").GetUInt64()) 1UL "invalid edge retains its child"
                      Expect.equal (edge.GetProperty("Source").GetUInt64()) 3UL "invalid future source is explicit"

                      Expect.equal
                          (future.RootElement.GetProperty("Entries").GetArrayLength())
                          1
                          "later cause is excluded from ancestors"

                      Expect.isError
                          (Observation.exportChainJson (ObservationId 99UL) snapshot)
                          "absent root fails rather than exporting a fabricated chain")
              testCase "queries and JSON export retain occurrence identities and loss counters" (fun _ ->
                  let buffer = ObservationBuffer(2)
                  buffer.Record(0<subtick>, ValueNone, CommandEffective(1UL, 1UL)) |> ignore

                  buffer.Record(
                      1<subtick>,
                      ValueSome(ActionCause(ActionId 7UL)),
                      ProbabilityResolved(Reception, Some(ActionId 7UL), 0.5, false)
                  )
                  |> ignore

                  buffer.Record(2<subtick>, ValueNone, CommandEffective(1UL, 2UL)) |> ignore
                  let snapshot = buffer.Snapshot()

                  let filter =
                      { Actor = None
                        Action = Some(ActionId 7UL)
                        Event = None
                        FromTick = 0<subtick>
                        UntilTick = 2<subtick> }

                  let found = Observation.query filter snapshot |> unwrap
                  Expect.hasLength found 1 "action lookup includes its recorded resolution"
                  Expect.equal found[0].Id (ObservationId 2UL) "original identity survives query"

                  Expect.isError
                      (Observation.query { filter with UntilTick = -1<subtick> } snapshot)
                      "invalid range rejected"

                  use json = System.Text.Json.JsonDocument.Parse(Observation.exportJson snapshot)
                  Expect.equal (json.RootElement.GetProperty("Evicted").GetUInt64()) 1UL "export retains truncation"
                  let row = json.RootElement.GetProperty("Entries")[0]
                  let cause = row.GetProperty("Causes")[0]
                  Expect.equal (cause.GetProperty("Kind").GetString()) "action" "typed source category"
                  Expect.equal (cause.GetProperty("Id").GetUInt64()) 7UL "source identity"

                  Expect.isTrue
                      (row.GetProperty("Explanation").GetString().Contains("not measured physical contact"))
                      "export does not invent contact")
              testCase "delta publication supports slow consumers without unbounded history" (fun _ ->
                  let buffer = ObservationBuffer(3)
                  buffer.Record(0<subtick>, ValueNone, CommandEffective(1UL, 1UL)) |> ignore
                  let first = buffer.SnapshotSince 0UL
                  buffer.Record(1<subtick>, ValueNone, CommandEffective(1UL, 2UL)) |> ignore
                  let second = buffer.SnapshotSince first.Total
                  Expect.hasLength second.Entries 1 "only new evidence is copied"
                  let merged = Observation.merge (Some first) second
                  Expect.hasLength merged.Entries 2 "consumer retains its own history"

                  for sequence in 3UL .. 8UL do
                      buffer.Record(int sequence * 1<subtick>, ValueNone, CommandEffective(1UL, sequence))
                      |> ignore

                  let delayed = buffer.SnapshotSince second.Total
                  let caughtUp = Observation.merge (Some merged) delayed
                  Expect.equal caughtUp (buffer.Snapshot()) "consumer catches up to bounded retained window"
                  Expect.equal caughtUp.Evicted 5UL "lost history is visible"
                  Expect.isEmpty (buffer.SnapshotSince delayed.Total).Entries "no duplicate transmission"

                  Expect.equal
                      (Observation.merge (Some caughtUp) first)
                      caughtUp
                      "stale publication cannot rewind history"

                  Expect.equal
                      (Observation.merge (Some caughtUp) delayed)
                      caughtUp
                      "duplicate publication is idempotent")
              testCase "kickoff evidence distinguishes match start period start and conceded goal" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()

                  for flow, expected in
                      [ MatchFlow.PreMatch, RestartCause.MatchStart
                        MatchFlow.HalfTime 0<tickDelta>, RestartCause.PeriodStart
                        MatchFlow.GoalPause(0<tickDelta>, ClubSide.Home, None), RestartCause.GoalConceded ClubSide.Home ] do
                      let world = TestBuilders.minimalWorld ()
                      world.State <- { world.State with Flow = flow }

                      EventApply.apply
                          ctx
                          world
                          { Tick = 0<subtick>
                            Data = EventData.SetPiece(SetPieceKind.KickOff, ClubSide.Away) }

                      let awards =
                          world.Observation.Snapshot().Entries
                          |> Array.choose (fun entry ->
                              match entry.Data with
                              | RestartCreated award -> Some award
                              | _ -> None)

                      Expect.hasLength awards 1 "one authoritative restart cause"
                      Expect.equal awards[0].Cause expected "evidence records the actual football source")
              testCase "referee response and restart retain an explicit incident source" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let ctx = TestBuilders.defaultCtx ()

                  let sctx =
                      { Ctx = ctx
                        Clock = SimulationClock.defaultClock }

                  let incident =
                      { Tick = 0<subtick>
                        Data =
                          EventData.Foul(
                              ctx.AwayLineup.Profiles[5].Id,
                              ctx.HomeLineup.Profiles[5].Id,
                              FoulSeverity.Minor,
                              CardDecision.NoCard,
                              (-10.0<meter>, 0.0<meter>)
                          ) }

                  let buffer =
                      { Events = ResizeArray()
                        Applied = ResizeArray([ incident ])
                        Flags = EventFlags.ofKind EventKind.Foul }

                  RefereeFouls.step sctx (Rng.ofSeed 8UL) world buffer |> ignore

                  let restart =
                      buffer.Events
                      |> Seq.find (fun event ->
                          match event.Data with
                          | EventData.SetPieceAt _ -> true
                          | _ -> false)

                  let assessment =
                      world.Observation.Snapshot().Entries
                      |> Array.find (fun entry ->
                          match entry.Data with
                          | RefereeAssessed _ -> true
                          | _ -> false)

                  Expect.equal
                      assessment.Cause
                      (ValueSome(EventCause(MatchEvent.id incident)))
                      "assessment retains its original incident"

                  Expect.equal
                      (world.Observation.ConsumeCause restart)
                      (ValueSome(ResolutionCause assessment.Id))
                      "restart response retains the actual referee assessment rather than bypassing it"

                  EventApply.apply ctx world restart

                  let awards =
                      world.Observation.Snapshot().Entries
                      |> Array.choose (fun entry ->
                          match entry.Data with
                          | RestartCreated _ -> Some entry
                          | _ -> None)

                  Expect.hasLength awards 1 "one restart evidence row"

                  Expect.equal
                      awards[0].Cause
                      (ValueSome(EventCause(MatchEvent.id restart)))
                      "restart points at its actual source event"

                  Expect.equal world.Observation.CurrentEvent ValueNone "reducer scope never leaks into another event")
              testCase "recalled advantage retains the original foul and the actual restart trigger" (fun _ ->
                  let ctx = TestBuilders.defaultCtx ()

                  let foul =
                      { Tick = 5<subtick>
                        Data =
                          EventData.Foul(
                              PlayerId 101,
                              PlayerId 2,
                              FoulSeverity.Minor,
                              NoCard,
                              (0.0<meter>, 3.0<meter>)
                          ) }

                  let incident =
                      { Source = MatchEvent.id foul
                        Tick = foul.Tick
                        Side = ClubSide.Home
                        Fouler = PlayerId 101
                        Fouled = PlayerId 2
                        X = 0.0<meter>
                        Y = 3.0<meter> }

                  for data in
                      [ EventData.AdvantageRecalled ClubSide.Home
                        EventData.AdvantageExpired ClubSide.Home
                        EventData.Possession(ClubSide.Away, PlayerId 101) ] do
                      let world = TestBuilders.minimalWorld ()

                      let start =
                          { Tick = 6<subtick>
                            Data = EventData.AdvantageStarted(incident, 20<subtick>) }

                      EventApply.apply ctx world start

                      Expect.equal
                          world.State.Advantage
                          (AdvantageState.AdvantagePending(incident, 20<subtick>))
                          "pending state owns immutable original incident context"

                      world.Observation.Record(foul.Tick, ValueNone, EventCommitted foul) |> ignore
                      let trigger = { Tick = 20<subtick>; Data = data }
                      EventApply.apply ctx world trigger

                      world.Observation.Record(trigger.Tick, ValueNone, EventCommitted trigger)
                      |> ignore

                      let snapshot = world.Observation.Snapshot()

                      let restart =
                          snapshot.Entries
                          |> Array.find (fun entry ->
                              match entry.Data with
                              | RestartCreated _ -> true
                              | _ -> false)

                      Expect.equal
                          restart.Cause
                          (ValueSome(PairedEvents(incident.Source, MatchEvent.id trigger)))
                          "original incident and actual trigger are separate explicit causes"

                      match restart.Data with
                      | RestartCreated award ->
                          Expect.equal
                              award.Cause
                              (RestartCause.Foul(Some incident.Fouler, Some incident.Fouled))
                              "participants survive recall instead of becoming unknown"
                      | _ -> failtest "expected restart"

                      let chain = Observation.chain restart.Id snapshot |> unwrap
                      Expect.hasLength chain.Entries 3 "restart links both original foul and later trigger"
                      Expect.isEmpty chain.MissingCauses "all fixture causes are retained"

                      let query =
                          { Actor = None
                            Action = None
                            Event = Some incident.Source
                            FromTick = trigger.Tick
                            UntilTick = trigger.Tick }

                      Expect.equal
                          (Observation.query query snapshot |> unwrap |> Array.map _.Id)
                          [| restart.Id |]
                          "event queries follow the original incident branch even when the immediate trigger is different"

                      use json =
                          System.Text.Json.JsonDocument.Parse(
                              Observation.exportJson
                                  { snapshot with
                                      Entries = [| restart |] }
                          )

                      let row = json.RootElement.GetProperty("Entries")[0]
                      let links = row.GetProperty("Causes")

                      Expect.equal
                          (links.GetArrayLength())
                          2
                          "JSON exposes separate typed links rather than a comma-separated identity"

                      Expect.equal
                          (links[0].GetProperty("Role").GetString())
                          "incident"
                          "original incident role is explicit"

                      Expect.equal
                          (links[1].GetProperty("Role").GetString())
                          "trigger"
                          "later trigger role is explicit"

                      let evicted =
                          { snapshot with
                              Entries =
                                  snapshot.Entries
                                  |> Array.filter (fun entry -> entry.Data <> EventCommitted foul) }

                      Expect.equal
                          (Observation.chain restart.Id evicted |> unwrap).MissingCauses
                          [| EventCause incident.Source |]
                          "one retained branch cannot hide another missing branch"

                      let repeated =
                          { Tick = 21<subtick>
                            Data = EventData.AdvantageRecalled ClubSide.Home }

                      EventApply.apply ctx world repeated

                      let awards =
                          world.Observation.Snapshot().Entries
                          |> Array.filter (fun entry ->
                              match entry.Data with
                              | RestartCreated _ -> true
                              | _ -> false)

                      Expect.hasLength awards 1 "consumed advantage cannot produce another restart")
              testCase "advantage response for the wrong side cannot consume the incident" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let ctx = TestBuilders.defaultCtx ()

                  let incident =
                      { Source = EventId 7UL
                        Tick = 1<subtick>
                        Side = ClubSide.Home
                        Fouler = PlayerId 101
                        Fouled = PlayerId 2
                        X = 0.0<meter>
                        Y = 3.0<meter> }

                  EventApply.apply
                      ctx
                      world
                      { Tick = 2<subtick>
                        Data = EventData.AdvantageStarted(incident, 20<subtick>) }

                  let pending = world.State.Advantage

                  for data in
                      [ EventData.AdvantageRecalled ClubSide.Away
                        EventData.AdvantageExpired ClubSide.Away
                        EventData.AdvantageRealized ClubSide.Away ] do
                      EventApply.apply ctx world { Tick = 3<subtick>; Data = data }

                      Expect.equal
                          world.State.Advantage
                          pending
                          "mismatched response does not overwrite another team's advantage"

                  Expect.isEmpty
                      (world.Observation.Snapshot().Entries
                       |> Array.filter (fun entry ->
                           match entry.Data with
                           | RestartCreated _ -> true
                           | _ -> false))
                      "invalid response never awards a restart")
              testCase "missing retained cause is not invented after eviction" (fun _ ->
                  let buffer = ObservationBuffer(1)
                  let cause = buffer.Record(0<subtick>, ValueNone, CommandEffective(1UL, 1UL))

                  match cause with
                  | ValueSome id ->
                      buffer.Record(1<subtick>, ValueSome(DecisionCause id), CommandEffective(1UL, 2UL))
                      |> ignore

                      let snapshot = buffer.Snapshot()

                      Expect.isEmpty
                          (Observation.causes snapshot snapshot.Entries[0])
                          "unavailable context remains unavailable"

                      Expect.equal snapshot.Evicted 1UL "consumer can display incomplete history"
                  | ValueNone -> failtest "enabled capture must assign an occurrence ID") ]
