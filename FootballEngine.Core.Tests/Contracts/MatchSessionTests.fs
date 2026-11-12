namespace FootballEngine.Core.Tests.Contracts

open Expecto
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module MatchSessionTests =
    let private unwrap result =
        match result with
        | Ok value -> value
        | Error error -> failtestf "unexpected error: %A" error

    let private input lineupSize =
        let context =
            { TestBuilders.defaultCtx () with
                HomeLineup = TestBuilders.makeLineup 0 lineupSize }

        { MatchId = 8UL
          Context = context
          Seed = 8UL
          EffectiveConfig = EffectiveConfig.defaultConfig
          EngineVersion = "tests" }

    let private command revision tick sequence manager action =
        { EffectiveTick = tick
          Sequence = sequence
          Revision = revision
          Manager = manager
          Action = action }

    let private tactics =
        { Formation = "4-3-3"
          DefensiveLine = 0.5f
          PressIntensity = 0.5f
          Width = 0.5f
          Tempo = 0.5f
          Directness = 0.5f
          Mentality = 0.5f
          PressTriggerZone = 0.5f }

    let private create mode lineupSize =
        MatchSession.create Scheduler.systems (input lineupSize) mode |> unwrap

    let private terminalSystems: SystemSpec list =
        [ { Name = "TerminalTest"
            Triggers = [ Trigger.EveryTick ]
            Step =
              fun _ rng world _ ->
                  if world.State.Tick >= 96<subtick> then
                      world.State <-
                          { world.State with
                              Flow = MatchFlow.MatchEnded }

                  rng } ]

    let private createTerminalSession () =
        MatchSession.create terminalSystems (input 11) MatchRunMode.Fast |> unwrap

    let private runInChunks (chunk: int<subtick>) =
        let session = createTerminalSession ()

        while session.World.State.Flow <> MatchFlow.MatchEnded do
            MatchSession.advanceTo (session.World.State.Tick + chunk) session
            |> unwrap
            |> ignore

        MatchSession.finish session |> unwrap

    let private outcomeFingerprint (outcome: MatchOutcome) =
        outcome.EventsHash,
        outcome.Events,
        outcome.Final.State,
        outcome.Final.Home.Frame.PosX,
        outcome.Final.Home.Frame.PosY,
        outcome.Final.Away.Frame.PosX,
        outcome.Final.Away.Frame.PosY,
        outcome.Final.Home.Stats,
        outcome.Final.Away.Stats

    let tests =
        testList
            "FootballEngine.Core.Contracts.MatchSession"
            [ // The live worker cuts advances wherever commands and wall-clock pacing fall, so the real
              // systems must produce the same match for any cut; otherwise one seed gives many results.
              testCase "real systems produce the same match however the advance is cut" (fun _ ->
                  let run (cuts: int list) =
                      let session =
                          MatchSession.create
                              Scheduler.systems
                              { input 11 with Context = TestBuilders.defaultCtx () }
                              (MatchRunMode.Live LiveExecutionPolicy.defaultPolicy)
                          |> unwrap

                      let mutable index = 0

                      while session.World.State.Flow <> MatchFlow.MatchEnded do
                          let cut = cuts[index % cuts.Length] * 1<subtick>
                          index <- index + 1
                          MatchSession.advanceTo (session.World.State.Tick + cut) session |> unwrap |> ignore

                      MatchSession.finish session |> unwrap

                  let whole = run [ int MatchSession.maxTicks ]
                  let irregular = run [ 2; 400; 7; 2; 2; 133; 41 ]

                  Expect.equal
                      (irregular.EventsHash, irregular.Final.State.HomeScore, irregular.Final.State.AwayScore)
                      (whole.EventsHash, whole.Final.State.HomeScore, whole.Final.State.AwayScore)
                      "advance boundaries cannot change the sporting result"

                  // The result hash covers the sporting record only; the physics must not depend on the cut either.
                  Expect.sequenceEqual
                      (irregular.Events |> List.map MatchEvent.id)
                      (whole.Events |> List.map MatchEvent.id)
                      "advance boundaries cannot change any event, physical ones included")
              testCase "a fixture keeps the sporting record and its hash, not the physics" (fun _ ->
                  let outcome = create Simulator.RunMode.Fast 11 |> MatchSession.finish |> unwrap
                  let record = MatchEvent.record outcome.Events

                  let physical =
                      record
                      |> List.filter (fun ev ->
                          match ev.Data with
                          | EventData.PhysicalContact _
                          | EventData.FootContact _
                          | EventData.RestartCommandIssued _ -> true
                          | _ -> false)

                  Expect.isEmpty physical "contacts, foot strikes and restart commands stay with the live run"

                  Expect.isLessThan
                      (float record.Length)
                      (0.25 * float outcome.Events.Length)
                      "physics is most of a match's events, so dropping it shrinks what a save keeps"

                  Expect.equal (Simulator.eventsHash record) outcome.EventsHash "the stored record verifies against the result hash"

                  let goals events =
                      events |> List.filter (fun ev -> match ev.Data with EventData.Goal _ -> true | _ -> false)

                  Expect.equal (goals record) (goals outcome.Events) "every goal is kept"
                  Expect.equal (List.length (goals record)) (outcome.Final.State.HomeScore + outcome.Final.State.AwayScore) "the record still explains the score")
              testCase "Replay finish produces deterministic terminal state" (fun _ ->
                  let outcome =
                      create (Simulator.RunMode.Replay 11<tickDelta>) 11
                      |> MatchSession.finish
                      |> unwrap

                  Expect.equal outcome.Final.State.Flow MatchFlow.MatchEnded "Replay reaches the end"
                  Expect.notEqual outcome.EventsHash 0UL "event hash is present")
              testCase "explicit dump contains typed events context stats and VAR-corrected score" (fun _ ->
                  let session = createTerminalSession ()
                  let outcome = MatchSession.finish session |> unwrap

                  let events =
                      [ { Tick = 1<subtick>
                          Data = EventData.Goal(ClubSide.Home, Some(PlayerId 1), None, 0.5) }
                        { Tick = 2<subtick>
                          Data = EventData.VARDecision VARResult.GoalOverturned }
                        { Tick = 3<subtick>
                          Data = EventData.Goal(ClubSide.Away, Some(PlayerId 101), None, 0.3) }
                        { Tick = 4<subtick>
                          Data = EventData.TeamInstruction ClubSide.Home } ]

                  let json =
                      MatchDump.export session.Input.Context events outcome.Final.Home.Stats outcome.Final.Away.Stats
                      |> unwrap

                  use document = JsonDocument.Parse json
                  let root = document.RootElement

                  Expect.equal
                      (root.GetProperty("schemaVersion").GetInt32())
                      2
                      "explicit schema replaces the empty dump"

                  Expect.equal
                      (root.GetProperty("finalScore").GetProperty("home").GetInt32())
                      0
                      "overturned goal is removed"

                  Expect.equal
                      (root.GetProperty("finalScore").GetProperty("away").GetInt32())
                      1
                      "confirmed goal remains"

                  Expect.equal
                      (root.GetProperty("context").GetProperty("homeClub").GetProperty("name").GetString())
                      "Home FC"
                      "context is exported"

                  Expect.isTrue
                      (root.GetProperty("homeStats").EnumerateObject() |> Seq.isEmpty |> not)
                      "stats are not an empty private DTO"

                  let options = JsonSerializerOptions()
                  options.Converters.Add(JsonFSharpConverter())

                  let restored =
                      root.GetProperty("events").EnumerateArray()
                      |> Seq.map (fun item ->
                          JsonSerializer.Deserialize<EventData>(item.GetProperty("data").GetRawText(), options))
                      |> List.ofSeq

                  Expect.equal restored (events |> List.map _.Data) "event payloads roundtrip without diagnostic text"
                  use stream = new MemoryStream()

                  MatchDump.write stream session.Input.Context events outcome.Final.Home.Stats outcome.Final.Away.Stats
                  |> unwrap

                  Expect.isTrue stream.CanWrite "the caller retains ownership of the destination"

                  Expect.equal
                      (System.Text.Encoding.UTF8.GetString(stream.ToArray()))
                      json
                      "streaming and string export agree")
              testCase "explicit dump reports an unwritable destination" (fun _ ->
                  let session = createTerminalSession ()
                  let outcome = MatchSession.finish session |> unwrap
                  use stream = new MemoryStream([| 0uy |], false)

                  match
                      MatchDump.write stream session.Input.Context [] outcome.Final.Home.Stats outcome.Final.Away.Stats
                  with
                  | Error _ -> ()
                  | Ok() -> failtest "writing to a read-only stream must report failure")
              testCase "finish is idempotent and completes replay once" (fun _ ->
                  let session = create (Simulator.RunMode.Replay(5 * 1<tickDelta>)) 11
                  let first = MatchSession.finish session |> unwrap
                  let second = MatchSession.finish session |> unwrap

                  Expect.isTrue (obj.ReferenceEquals(first, second)) "cached outcome is returned"
                  Expect.equal first.Replay second.Replay "replay data is stable")
              testCase "batch finish matches published advance and preserves replay archives" (fun _ ->
                  for mode in
                      [ MatchRunMode.Fast
                        MatchRunMode.Live LiveExecutionPolicy.defaultPolicy
                        MatchRunMode.Replay 5<tickDelta> ] do
                      let batch = MatchSession.create terminalSystems (input 11) mode |> unwrap
                      let published = MatchSession.create terminalSystems (input 11) mode |> unwrap
                      let output = MatchSession.advanceTo MatchSession.maxTicks published |> unwrap
                      Expect.isGreaterThan output.Motion.Length 0 "an explicit advance still publishes motion"
                      let expected = MatchSession.finish published |> unwrap
                      let actual = MatchSession.finish batch |> unwrap

                      Expect.equal
                          (outcomeFingerprint actual)
                          (outcomeFingerprint expected)
                          "discarding unrequested presentation cannot change sport"

                      Expect.equal
                          actual.Replay
                          expected.Replay
                          "replay snapshots and motion sections remain byte-identical"

                      Expect.equal
                          (MatchSession.checkpoint batch)
                          (MatchSession.checkpoint published)
                          "RNG events and checkpoint state agree")
              testCase "revision mismatch is rejected before queueing" (fun _ ->
                  let session = create Simulator.RunMode.Fast 11

                  let invalid =
                      command (session.Input.EffectiveConfig.Revision + 1UL) 0<subtick> 0UL 1UL (ChangeTactics tactics)

                  match MatchSession.receive invalid session with
                  | Error(InvalidCommandRevision _) -> Expect.isEmpty session.Pending "invalid revision is not queued"
                  | other -> failtestf "expected InvalidCommandRevision, got %A" other)
              testCase "terminal sessions reject commands without changing the journal or evidence" (fun _ ->
                  for finalize in [ false; true ] do
                      let session = createTerminalSession ()
                      MatchSession.advanceTo 100<subtick> session |> unwrap |> ignore

                      if finalize then
                          MatchSession.finish session |> unwrap |> ignore

                      let before = MatchSession.checkpoint session
                      let evidence = MatchSession.observations session

                      let requested =
                          command
                              session.Input.EffectiveConfig.Revision
                              session.World.State.Tick
                              0UL
                              1UL
                              (ChangeTactics tactics)

                      Expect.equal
                          (MatchSession.receive requested session)
                          (Error SessionAlreadyFinished)
                          "an ended match cannot accept effective instructions"

                      Expect.equal
                          (MatchSession.checkpoint session)
                          before
                          "rejection leaves checkpoint state unchanged"

                      Expect.equal
                          (MatchSession.observations session)
                          evidence
                          "rejection does not fabricate command evidence")
              testCase "commands at the safety frontier are rejected rather than retained forever" (fun _ ->
                  let session = create Simulator.RunMode.Fast 11

                  for tick in [ MatchSession.maxTicks; MatchSession.maxTicks + 1<subtick> ] do
                      let requested =
                          command session.Input.EffectiveConfig.Revision tick 0UL 1UL (ChangeTactics tactics)

                      match MatchSession.receive requested session with
                      | Error(InvalidCommandTiming _) -> ()
                      | other -> failtestf "expected InvalidCommandTiming, got %A" other

                  Expect.isEmpty session.Commands "unexecutable commands do not enter the replay journal"
                  Expect.isEmpty session.Pending "unexecutable commands do not enter the pending queue")
              testCase "commands earlier than the frontier are rejected" (fun _ ->
                  let session = create Simulator.RunMode.Fast 11
                  MatchSession.advanceTo 1<subtick> session |> unwrap |> ignore

                  let invalid =
                      command session.Input.EffectiveConfig.Revision 0<subtick> 0UL 1UL (ChangeTactics tactics)

                  match MatchSession.receive invalid session with
                  | Error(InvalidCommandTiming _) -> Expect.isEmpty session.Pending "stale command is not queued"
                  | other -> failtestf "expected InvalidCommandTiming, got %A" other)
              testCase "command journal capacity is explicit and matches checkpoint retention" (fun _ ->
                  let session = create Simulator.RunMode.Fast 11

                  let requested =
                      command session.Input.EffectiveConfig.Revision 0<subtick> 0UL 1UL (ChangeTactics tactics)

                  session.Commands <- List.replicate MatchSession.maxCommands requested
                  let retained = session.Commands
                  let evidence = MatchSession.observations session

                  Expect.equal
                      (MatchSession.receive requested session)
                      (Error(CommandLimitExceeded MatchSession.maxCommands))
                      "a full journal rejects capture explicitly"

                  Expect.isTrue
                      (obj.ReferenceEquals(session.Commands, retained))
                      "rejection cannot replace retained commands"

                  Expect.isEmpty session.Pending "overflow does not partially queue a command"

                  Expect.equal
                      (MatchSession.observations session)
                      evidence
                      "overflow cannot invent an effective command")
              testCase "pending commands must be supplied in effective order" (fun _ ->
                  let session = create Simulator.RunMode.Fast 11
                  let revision = session.Input.EffectiveConfig.Revision

                  MatchSession.receive (command revision 2<subtick> 0UL 1UL (ChangeTactics tactics)) session
                  |> unwrap

                  match MatchSession.receive (command revision 1<subtick> 0UL 1UL (ChangeTactics tactics)) session with
                  | Error(InvalidCommandOrder _) ->
                      Expect.hasLength session.Pending 1 "only the ordered command remains"
                  | other -> failtestf "expected InvalidCommandOrder, got %A" other)
              testCase "valid tactics are accepted and empty formation is rejected" (fun _ ->
                  let session = create Simulator.RunMode.Fast 11
                  let revision = session.Input.EffectiveConfig.Revision

                  Expect.isOk
                      (MatchSession.receive (command revision 0<subtick> 0UL 1UL (ChangeTactics tactics)) session)
                      "valid tactics"

                  match
                      MatchSession.receive
                          (command revision 0<subtick> 1UL 1UL (ChangeTactics { tactics with Formation = " " }))
                          session
                  with
                  | Error(InvalidCommandRules _) -> Expect.hasLength session.Pending 1 "invalid tactics are not queued"
                  | other -> failtestf "expected InvalidCommandRules, got %A" other)
              testCase "effective tactics expose applied fields without claiming unimplemented effects" (fun _ ->
                  let session = create Simulator.RunMode.Fast 11

                  let applied =
                      { tactics with
                          DefensiveLine = 44.0f
                          Width = 0.7f
                          Tempo = 0.8f
                          Directness = 0.8f }

                  MatchSession.receive
                      (command session.Input.EffectiveConfig.Revision 0<subtick> 1UL 1UL (ChangeTactics applied))
                      session
                  |> unwrap

                  MatchSession.advanceTo 1<subtick> session |> unwrap |> ignore

                  let rows =
                      (MatchSession.observations session).Entries
                      |> Array.filter (fun entry ->
                          match entry.Data with
                          | TacticsEffective(1UL, 1UL, _) -> true
                          | _ -> false)

                  Expect.hasLength rows 1 "effective command occurrence recorded once"

                  Expect.equal
                      session.World.Home.Tactics
                      applied
                      "effective instructions persist independently of the initial input"

                  Expect.equal
                      session.World.Home.Directive.Params.LineHeight
                      applied.DefensiveLine
                      "line instruction stays in engine metres"

                  let restored =
                      MatchSession.checkpoint session
                      |> MatchSession.restore Scheduler.systems session.Input
                      |> unwrap

                  Expect.equal
                      restored.World.Home.Tactics
                      applied
                      "checkpoint reconstruction restores effective instructions from the command history"

                  Expect.equal restored.Rng session.Rng "restoring applied tactics preserves RNG"

                  TeamDecide.step
                      { Ctx = session.Input.Context
                        Clock = SimulationClock.defaultClock }
                      session.Rng
                      session.World
                      session.Buffer
                  |> ignore

                  Expect.equal
                      session.World.Home.Directive.Params.LineHeight
                      applied.DefensiveLine
                      "team decision must not overwrite the command with kickoff tactics"

                  Expect.equal
                      session.World.Home.Directive.Params.Width
                      applied.Width
                      "team decision consumes effective width"

                  Expect.isTrue
                      ((Observation.explain rows[0]).Contains("does not yet apply all instruction fields"))
                      "accepted instruction does not imply complete execution")
              testCase "invalid tactical values never enter the authoritative command queue" (fun _ ->
                  for invalid in
                      [ { tactics with
                            Width = System.Single.NaN }
                        { tactics with Tempo = 1.1f }
                        { tactics with
                            DefensiveLine = System.Single.PositiveInfinity }
                        { tactics with DefensiveLine = -1.0f } ] do
                      let session = create Simulator.RunMode.Fast 11

                      Expect.isError
                          (MatchSession.receive
                              (command
                                  session.Input.EffectiveConfig.Revision
                                  0<subtick>
                                  1UL
                                  1UL
                                  (ChangeTactics invalid))
                              session)
                          "nonfinite or out-of-range tactics are rejected"

                      Expect.isEmpty session.Pending "rejection cannot partially apply instructions")
              testCase "valid substitution is accepted and an invalid pair is rejected" (fun _ ->
                  let session = create Simulator.RunMode.Fast 16
                  let revision = session.Input.EffectiveConfig.Revision

                  Expect.isOk
                      (MatchSession.receive
                          (command revision 0<subtick> 0UL 1UL (RequestSubstitution(PlayerId 1, PlayerId 12)))
                          session)
                      "valid substitution"

                  match
                      MatchSession.receive
                          (command revision 0<subtick> 1UL 1UL (RequestSubstitution(PlayerId 1, PlayerId 2)))
                          session
                  with
                  | Error(InvalidCommandPlayers _) -> Expect.hasLength session.Pending 1 "invalid pair is not queued"
                  | other -> failtestf "expected InvalidCommandPlayers, got %A" other)
              testCase "checkpoint roundtrip restores frontier, pending commands, RNG and event hash" (fun _ ->
                  let session = create (Simulator.RunMode.Replay(5 * 1<tickDelta>)) 11
                  let revision = session.Input.EffectiveConfig.Revision
                  let future = command revision 25<subtick> 1UL 1UL (ChangeTactics tactics)

                  MatchSession.receive (command revision 5<subtick> 0UL 1UL (ChangeTactics tactics)) session
                  |> unwrap

                  MatchSession.receive future session |> unwrap
                  MatchSession.advanceTo 20<subtick> session |> unwrap |> ignore

                  let original = MatchSession.checkpoint session
                  let encoded = MatchSession.serializeCheckpoint original
                  let decoded = MatchSession.deserializeCheckpoint encoded |> unwrap
                  let restored = MatchSession.restore Scheduler.systems (input 11) decoded |> unwrap

                  Expect.equal restored.World.State.Tick session.World.State.Tick "frontier is restored"
                  Expect.equal restored.Pending session.Pending "pending commands survive the checkpoint"
                  Expect.equal restored.Rng session.Rng "RNG state survives the checkpoint"

                  Expect.equal
                      (Simulator.eventsHash (List.ofSeq restored.Events))
                      original.EventsHash
                      "event hash survives replay"

                  Expect.equal
                      (MatchSession.replayCommands restored)
                      (MatchSession.replayCommands session)
                      "command journal is replayable"

                  Expect.equal
                      (MatchSession.observations restored)
                      (MatchSession.observations session)
                      "diagnostic trace is deterministic")
              testCase "checkpoint rejects trailing bytes and foreign input" (fun _ ->
                  let session = create Simulator.RunMode.Fast 11
                  MatchSession.advanceTo 4<subtick> session |> unwrap |> ignore
                  let checkpoint = MatchSession.checkpoint session
                  let encoded = MatchSession.serializeCheckpoint checkpoint
                  let corrupted = Array.append encoded [| 0uy |]

                  match MatchSession.deserializeCheckpoint corrupted with
                  | Error(InvalidCheckpoint _) -> ()
                  | other -> failtestf "expected InvalidCheckpoint, got %A" other

                  let foreignInput = { input 11 with MatchId = 99UL }

                  match MatchSession.restore Scheduler.systems foreignInput checkpoint with
                  | Error(InvalidCheckpoint _) -> ()
                  | other -> failtestf "expected foreign-input rejection, got %A" other)
              testCase
                  "input identity includes football rules tactics profiles and positions before any event"
                  (fun _ ->
                      let original = input 11

                      let checkpoint =
                          MatchSession.create Scheduler.systems original MatchRunMode.Fast
                          |> unwrap
                          |> MatchSession.checkpoint

                      let ctx = original.Context
                      let profiles = Array.copy ctx.HomeLineup.Profiles

                      profiles[1] <-
                          { profiles[1] with
                              Pace = profiles[1].Pace + 1.0f }

                      let positions = Array.copy ctx.HomeLineup.BasePositions

                      positions[1] <-
                          { positions[1] with
                              X = positions[1].X + 1.0<meter> }

                      let changedContexts =
                          [ { ctx with
                                TieResolution = MatchTieResolution.PenaltiesAfterRegulation }
                            { ctx with
                                HomeClub =
                                    { ctx.HomeClub with
                                        BaseTactics =
                                            { ctx.HomeClub.BaseTactics with
                                                Width = 0.9f } } }
                            { ctx with
                                HomeLineup =
                                    { ctx.HomeLineup with
                                        Profiles = profiles } }
                            { ctx with
                                HomeLineup =
                                    { ctx.HomeLineup with
                                        BasePositions = positions } }
                            { ctx with
                                SubstitutionRules =
                                    { ctx.SubstitutionRules with
                                        MaxSubstitutions = ctx.SubstitutionRules.MaxSubstitutions + 1 } } ]

                      for changed in changedContexts do
                          let modified = { original with Context = changed }

                          Expect.notEqual
                              (MatchJob.inputHash modified)
                              (MatchJob.inputHash original)
                              "resolved football change affects input identity"

                          Expect.isError
                              (MatchSession.restore Scheduler.systems modified checkpoint)
                              "a kickoff checkpoint must not accept different football input"

                      Expect.equal
                          (MatchJob.inputHash (input 11))
                          checkpoint.InputHash
                          "independently built equivalent input has the same identity")
              testCase "session input owns every array crossing the caller boundary" (fun _ ->
                  let supplied = input 16

                  let session =
                      MatchSession.create Scheduler.systems supplied MatchRunMode.Fast |> unwrap

                  let baseline =
                      MatchSession.create Scheduler.systems supplied MatchRunMode.Fast |> unwrap

                  let before = MatchSession.checkpoint session

                  for lineup in [ supplied.Context.HomeLineup; supplied.Context.AwayLineup ] do
                      lineup.Profiles[1] <- { lineup.Profiles[1] with Pace = 99.0f }

                      lineup.BasePositions[1] <-
                          { lineup.BasePositions[1] with
                              X = 999.0<meter> }

                  for participants in [ supplied.Context.HomeParticipants; supplied.Context.AwayParticipants ] do
                      participants.Starters[1] <-
                          { participants.Starters[1] with
                              Eligible = false
                              InitialCondition = 0.0f }

                      if participants.Substitutes.Length > 0 then
                          participants.Substitutes[0] <-
                              { participants.Substitutes[0] with
                                  Eligible = false }

                  supplied.Context.SubstitutionRules.WindowStarts[0] <- 999<matchMin>

                  Expect.equal
                      (MatchSession.checkpoint session)
                      before
                      "caller mutation cannot change a checkpoint input identity"

                  MatchSession.advanceTo 300<subtick> session |> unwrap |> ignore
                  MatchSession.advanceTo 300<subtick> baseline |> unwrap |> ignore
                  Expect.equal session.Rng baseline.Rng "caller mutation cannot change sporting randomness"

                  Expect.equal
                      (MatchSession.checkpoint session)
                      (MatchSession.checkpoint baseline)
                      "event journal and authoritative frontier remain identical"

                  Expect.equal
                      (Replay.capture session.World)
                      (Replay.capture baseline.World)
                      "player and ball physics use owned input")
              testCase "scheduled jobs capture their input before a caller can mutate it" (fun _ ->
                  let supplied = input 11
                  let kickoff = System.DateTime(2026, 10, 5)
                  let job = MatchJob.create 8 kickoff 1UL supplied
                  let originalHash = MatchJob.inputHash job.Input

                  supplied.Context.HomeLineup.Profiles[1] <-
                      { supplied.Context.HomeLineup.Profiles[1] with
                          Passing = 99.0f }

                  Expect.equal
                      (MatchJob.inputHash job.Input)
                      originalHash
                      "job football input stays fixed while waiting for a worker"

                  Expect.notEqual
                      (MatchJob.inputHash supplied)
                      originalHash
                      "fixture actually modified the caller input"

                  Expect.equal
                      (MatchJob.create 8 kickoff 1UL job.Input).InputHash
                      job.InputHash
                      "job identity agrees with its owned input")
              testCase "diagnostic capture is deterministic and bounded" (fun _ ->
                  let first = create Simulator.RunMode.Fast 11
                  let second = create Simulator.RunMode.Fast 11
                  MatchSession.advanceTo 300<subtick> first |> unwrap |> ignore
                  MatchSession.advanceTo 300<subtick> second |> unwrap |> ignore
                  let firstTrace = MatchSession.observations first
                  let secondTrace = MatchSession.observations second

                  Expect.isTrue
                      (firstTrace.Entries.Length <= first.World.Observation.Capacity)
                      "diagnostic capture is bounded"

                  Expect.isNonEmpty firstTrace.Entries "diagnostic capture contains useful facts"
                  Expect.equal firstTrace secondTrace "diagnostic capture is deterministic")
              testCase "invalid slot geometry and sporting identities return errors before world construction" (fun _ ->
                  let original = input 11
                  let context = original.Context
                  let home = context.HomeLineup
                  let duplicate = Array.copy home.Profiles
                  duplicate[1] <- duplicate[0]
                  let shared = Array.copy context.AwayLineup.Profiles

                  shared[0] <-
                      { shared[0] with
                          Id = home.Profiles[0].Id }

                  let nonfinite = Array.copy home.BasePositions

                  nonfinite[0] <-
                      { nonfinite[0] with
                          Vx = System.Double.NaN * 1.0<meterPerSecond> }

                  let invalidInputs =
                      [ { original with EngineVersion = " " }
                        { original with
                            Context =
                                { context with
                                    Config =
                                        { context.Config with
                                            Manager =
                                                { context.Config.Manager with
                                                    MaxSubs = context.Config.Manager.MaxSubs + 1 } } } }
                        { original with
                            Context =
                                { context with
                                    AwayClub =
                                        { context.AwayClub with
                                            Id = context.HomeClub.Id } } }
                        { original with
                            Context =
                                { context with
                                    HomeLineup =
                                        { home with
                                            SlotCount = 0
                                            StarterCount = 0
                                            Profiles = [||]
                                            BasePositions = [||] } } }
                        { original with
                            Context =
                                { context with
                                    HomeLineup =
                                        { home with
                                            BasePositions = home.BasePositions[..9] } } }
                        { original with
                            Context =
                                { context with
                                    HomeLineup = { home with Profiles = duplicate } } }
                        { original with
                            Context =
                                { context with
                                    AwayLineup =
                                        { context.AwayLineup with
                                            Profiles = shared } } }
                        { original with
                            Context =
                                { context with
                                    HomeLineup = { home with BasePositions = nonfinite } } } ]

                  Expect.isOk (MatchInput.validate original) "the valid resolved input remains accepted"

                  for invalid in invalidInputs do
                      match MatchSession.create Scheduler.systems invalid MatchRunMode.Fast with
                      | Error(InvalidMatchInput errors) ->
                          Expect.isNonEmpty errors "rejection has a repairable contract reason"
                      | other -> failtestf "expected InvalidMatchInput, got %A" other)
              testCase "terminal output is independent of advance chunk size" (fun _ ->
                  let expected = runInChunks 1<subtick> |> outcomeFingerprint

                  for chunk in [ 2<subtick>; 20<subtick>; 200<subtick>; 2000<subtick> ] do
                      Expect.equal
                          (runInChunks chunk |> outcomeFingerprint)
                          expected
                          (sprintf "chunk size %d must preserve the terminal output" (int chunk))) ]
