namespace FootballEngine.Core.Tests.Contracts

open Expecto
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
            [ testCase "Replay finish produces deterministic terminal state" (fun _ ->
                  let outcome = create (Simulator.RunMode.Replay 11<tickDelta>) 11 |> MatchSession.finish |> unwrap
                  Expect.isNone outcome.DumpJson "Replay mode does not retain a full dump"
                  Expect.equal outcome.Final.State.Flow MatchFlow.MatchEnded "Replay reaches the end"
                  Expect.notEqual outcome.EventsHash 0UL "event hash is present")
              testCase "finish is idempotent and completes replay once" (fun _ ->
                  let session = create (Simulator.RunMode.Replay(5 * 1<tickDelta>)) 11
                  let first = MatchSession.finish session |> unwrap
                  let second = MatchSession.finish session |> unwrap

                  Expect.isTrue (obj.ReferenceEquals(first, second)) "cached outcome is returned"
                  Expect.equal first.Replay second.Replay "replay data is stable")
              testCase "revision mismatch is rejected before queueing" (fun _ ->
                  let session = create Simulator.RunMode.Fast 11

                  let invalid =
                      command (session.Input.EffectiveConfig.Revision + 1UL) 0<subtick> 0UL 1UL (ChangeTactics tactics)

                  match MatchSession.receive invalid session with
                  | Error(InvalidCommandRevision _) -> Expect.isEmpty session.Pending "invalid revision is not queued"
                  | other -> failtestf "expected InvalidCommandRevision, got %A" other)
              testCase "commands earlier than the frontier are rejected" (fun _ ->
                  let session = create Simulator.RunMode.Fast 11
                  MatchSession.advanceTo 1<subtick> session |> unwrap |> ignore

                  let invalid =
                      command session.Input.EffectiveConfig.Revision 0<subtick> 0UL 1UL (ChangeTactics tactics)

                  match MatchSession.receive invalid session with
                  | Error(InvalidCommandTiming _) -> Expect.isEmpty session.Pending "stale command is not queued"
                  | other -> failtestf "expected InvalidCommandTiming, got %A" other)
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
                      (MatchSession.diagnostics restored)
                      (MatchSession.diagnostics session)
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
              testCase "diagnostic capture is deterministic and bounded" (fun _ ->
                  let first = create Simulator.RunMode.Fast 11
                  let second = create Simulator.RunMode.Fast 11
                  MatchSession.advanceTo 300<subtick> first |> unwrap |> ignore
                  MatchSession.advanceTo 300<subtick> second |> unwrap |> ignore
                  let firstTrace = MatchSession.diagnostics first
                  let secondTrace = MatchSession.diagnostics second

                  Expect.isTrue (List.length firstTrace <= 256) "diagnostic capture is bounded"
                  Expect.isNonEmpty firstTrace "diagnostic capture contains useful facts"
                  Expect.equal firstTrace secondTrace "diagnostic capture is deterministic")
              testCase "terminal output is independent of advance chunk size" (fun _ ->
                  let expected = runInChunks 1<subtick> |> outcomeFingerprint

                  for chunk in [ 2<subtick>; 20<subtick>; 200<subtick>; 2000<subtick> ] do
                      Expect.equal
                          (runInChunks chunk |> outcomeFingerprint)
                          expected
                          (sprintf "chunk size %d must preserve the terminal output" (int chunk))) ]
