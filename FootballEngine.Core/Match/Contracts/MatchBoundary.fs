namespace FootballEngine.Core.Match

open System
open System.IO

type MatchAction =
    | ChangeTactics of TacticsConfig
    | RequestSubstitution of outgoing: PlayerId * incoming: PlayerId

type MatchCommand =
    { EffectiveTick: int<subtick>
      Sequence: uint64
      Revision: uint64
      Manager: uint64
      Action: MatchAction }

type MatchInput =
    { MatchId: uint64
      Context: MatchContext
      Seed: uint64
      EffectiveConfig: EffectiveConfig
      EngineVersion: string }

type MatchFacts =
    { HomeScore: int
      AwayScore: int
      EventsHash: uint64 }

type MatchLoad =
    { HomeEvents: int
      AwayEvents: int
      TotalTicks: int<subtick> }

type MatchDiscipline =
    { YellowCards: int
      RedCards: int
      Fouls: int }

type MatchTraceRef = { Version: int; Events: int }

type MatchResult =
    { MatchId: uint64
      EngineVersion: string
      Facts: MatchFacts
      Load: MatchLoad
      InjuredPlayers: PlayerId list
      Discipline: MatchDiscipline
      Metrics: TeamStats * TeamStats
      Trace: MatchTraceRef option }

type MatchContractError =
    | InvalidCommandOrder of string
    | InvalidCommandRevision of string
    | InvalidCommandManager of string
    | InvalidCommandTiming of string
    | InvalidCommandPlayers of string
    | InvalidCommandRules of string
    | InvalidAdvanceBoundary of string
    | SessionAlreadyFinished
    | InvalidRunMode of string
    | InvalidInputConfig of string list
    | InvalidCheckpoint of string

type DecisionTrace =
    { Tick: int<subtick>
      Actor: string
      Options: string list
      Chosen: string option
      Constraints: string list
      Facts: string list }

type MatchCheckpoint =
    { Version: int
      MatchId: uint64
      Seed: uint64
      EngineVersion: string
      Revision: uint64
      Mode: Simulator.RunMode
      Frontier: int<subtick>
      Rng: Rng
      Commands: MatchCommand list
      Pending: MatchCommand list
      EventsHash: uint64
      TraceCount: int }

type SessionAdvance =
    { ReachedTick: int<subtick>
      Flow: MatchFlow
      Events: MatchEvent list }

type MatchSession =
    { Systems: SystemSpec list
      Input: MatchInput
      Mode: Simulator.RunMode
      World: World
      Buffer: EventBuffer
      mutable Rng: Rng
      mutable Pending: MatchCommand list
      mutable Commands: MatchCommand list
      Events: ResizeArray<MatchEvent>
      Snapshots: ResizeArray<Replay.ReplaySnapshot>
      Diagnostics: ResizeArray<DecisionTrace> }

module MatchSession =

    let private checkpointVersion = 1
    let private diagnosticLimit = 256

    let private appendDiagnostic trace (session: MatchSession) =
        session.Diagnostics.Add trace

        if session.Diagnostics.Count > diagnosticLimit then
            session.Diagnostics.RemoveAt 0

    let private writeTactics (writer: BinaryWriter) tactics =
        writer.Write tactics.Formation
        writer.Write tactics.DefensiveLine
        writer.Write tactics.PressIntensity
        writer.Write tactics.Width
        writer.Write tactics.Tempo
        writer.Write tactics.Directness
        writer.Write tactics.Mentality
        writer.Write tactics.PressTriggerZone

    let private readTactics (reader: BinaryReader) =
        { Formation = reader.ReadString()
          DefensiveLine = reader.ReadSingle()
          PressIntensity = reader.ReadSingle()
          Width = reader.ReadSingle()
          Tempo = reader.ReadSingle()
          Directness = reader.ReadSingle()
          Mentality = reader.ReadSingle()
          PressTriggerZone = reader.ReadSingle() }

    let private writeCommand (writer: BinaryWriter) (command: MatchCommand) =
        writer.Write(int command.EffectiveTick)
        writer.Write command.Sequence
        writer.Write command.Revision
        writer.Write command.Manager

        match command.Action with
        | ChangeTactics tactics ->
            writer.Write 0
            writeTactics writer tactics
        | RequestSubstitution(PlayerId outgoing, PlayerId incoming) ->
            writer.Write 1
            writer.Write outgoing
            writer.Write incoming

    let private readCommand (reader: BinaryReader) =
        let tick = reader.ReadInt32() * 1<subtick>
        let sequence = reader.ReadUInt64()
        let revision = reader.ReadUInt64()
        let manager = reader.ReadUInt64()

        let action =
            match reader.ReadInt32() with
            | 0 -> ChangeTactics(readTactics reader)
            | 1 -> RequestSubstitution(PlayerId(reader.ReadInt32()), PlayerId(reader.ReadInt32()))
            | _ -> raise (InvalidDataException "checkpoint contains an unknown command action")

        { EffectiveTick = tick
          Sequence = sequence
          Revision = revision
          Manager = manager
          Action = action }

    let private writeCommands (writer: BinaryWriter) (commands: MatchCommand list) =
        writer.Write commands.Length
        commands |> List.iter (writeCommand writer)

    let private readCommands (reader: BinaryReader) : MatchCommand list =
        let count = reader.ReadInt32()

        if count < 0 || count > 100000 then
            raise (InvalidDataException "checkpoint command count is outside the supported range")

        [ for _ in 1..count -> readCommand reader ]

    let private writeRng (writer: BinaryWriter) rng =
        writer.Write rng.S0
        writer.Write rng.S1
        writer.Write rng.S2
        writer.Write rng.S3

    let private readRng (reader: BinaryReader) =
        { S0 = reader.ReadUInt64()
          S1 = reader.ReadUInt64()
          S2 = reader.ReadUInt64()
          S3 = reader.ReadUInt64() }

    let private writeMode (writer: BinaryWriter) mode =
        match mode with
        | Simulator.RunMode.Fast -> writer.Write 0
        | Simulator.RunMode.Replay interval ->
            writer.Write 1
            writer.Write(int interval)
        | Simulator.RunMode.Full -> writer.Write 2

    let private readMode (reader: BinaryReader) =
        match reader.ReadInt32() with
        | 0 -> Ok Simulator.RunMode.Fast
        | 1 ->
            let interval = reader.ReadInt32() * 1<tickDelta>

            if interval <= 0<tickDelta> then
                Error "checkpoint replay interval must be positive"
            else
                Ok(Simulator.RunMode.Replay interval)
        | 2 -> Ok Simulator.RunMode.Full
        | _ -> Error "checkpoint contains an unknown run mode"

    let serializeCheckpoint (checkpoint: MatchCheckpoint) : byte[] =
        use stream = new MemoryStream()
        use writer = new BinaryWriter(stream)
        writer.Write(0x46454348)
        writer.Write checkpoint.Version
        writer.Write checkpoint.MatchId
        writer.Write checkpoint.Seed
        writer.Write checkpoint.EngineVersion
        writer.Write checkpoint.Revision
        writeMode writer checkpoint.Mode
        writer.Write(int checkpoint.Frontier)
        writeRng writer checkpoint.Rng
        writeCommands writer checkpoint.Commands
        writeCommands writer checkpoint.Pending
        writer.Write checkpoint.EventsHash
        writer.Write checkpoint.TraceCount
        writer.Flush()
        stream.ToArray()

    let deserializeCheckpoint (data: byte[]) : Result<MatchCheckpoint, MatchContractError> =
        try
            use stream = new MemoryStream(data)
            use reader = new BinaryReader(stream)

            if reader.ReadInt32() <> 0x46454348 then
                Error(InvalidCheckpoint "checkpoint magic does not match")
            else
                let version = reader.ReadInt32()

                if version <> checkpointVersion then
                    Error(InvalidCheckpoint "checkpoint version is not supported")
                else
                    let matchId = reader.ReadUInt64()
                    let seed = reader.ReadUInt64()
                    let engineVersion = reader.ReadString()
                    let revision = reader.ReadUInt64()

                    match readMode reader with
                    | Error message -> Error(InvalidCheckpoint message)
                    | Ok mode ->
                        let checkpoint =
                            { Version = version
                              MatchId = matchId
                              Seed = seed
                              EngineVersion = engineVersion
                              Revision = revision
                              Mode = mode
                              Frontier = reader.ReadInt32() * 1<subtick>
                              Rng = readRng reader
                              Commands = readCommands reader
                              Pending = readCommands reader
                              EventsHash = reader.ReadUInt64()
                              TraceCount = reader.ReadInt32() }

                        if checkpoint.Frontier < 0<subtick> || checkpoint.TraceCount < 0 then
                            Error(InvalidCheckpoint "checkpoint contains a negative frontier or trace count")
                        elif stream.Position <> stream.Length then
                            Error(InvalidCheckpoint "checkpoint contains trailing data")
                        else
                            Ok checkpoint
        with
        | :? EndOfStreamException -> Error(InvalidCheckpoint "checkpoint is truncated")
        | :? InvalidDataException as error -> Error(InvalidCheckpoint error.Message)
        | :? IOException as error -> Error(InvalidCheckpoint error.Message)

    let checkpoint (session: MatchSession) : MatchCheckpoint =
        { Version = checkpointVersion
          MatchId = session.Input.MatchId
          Seed = session.Input.Seed
          EngineVersion = session.Input.EngineVersion
          Revision = session.Input.EffectiveConfig.Revision
          Mode = session.Mode
          Frontier = session.World.State.Tick
          Rng = session.Rng
          Commands = session.Commands
          Pending = session.Pending
          EventsHash = Simulator.eventsHash (List.ofSeq session.Events)
          TraceCount = session.Diagnostics.Count }

    let diagnostics (session: MatchSession) = List.ofSeq session.Diagnostics

    let replayCommands (session: MatchSession) = session.Commands

    let private orderedCommands commands =
        let ordered = commands |> List.sortBy (fun c -> c.EffectiveTick, c.Sequence)

        if ordered <> commands then
            Error(InvalidCommandOrder "MatchCommand must be supplied in effective tick and sequence order")
        elif
            commands
            |> List.pairwise
            |> List.exists (fun (a, b) -> a.EffectiveTick = b.EffectiveTick && a.Sequence >= b.Sequence)
        then
            Error(InvalidCommandOrder "MatchCommand sequence must increase within a tick")
        else
            Ok()

    let private sideForManager (input: MatchInput) manager =
        if manager = input.Context.HomeClub.Id then
            Some ClubSide.Home
        elif manager = input.Context.AwayClub.Id then
            Some ClubSide.Away
        else
            None

    let private lineupFor (ctx: MatchContext) side =
        if side = ClubSide.Home then
            ctx.HomeLineup
        else
            ctx.AwayLineup

    let private slotOfPlayer (ctx: MatchContext) side player =
        lineupFor ctx side
        |> fun l -> l.Profiles |> Array.tryFindIndex (fun p -> p.Id = player)

    let private isActive (team: TeamWorld) slot =
        match team.Frame.Occupancy.[slot] with
        | OccupancyKind.Active _ -> true
        | OccupancyKind.Sidelined _ -> false

    let private isAvailableSubstitute (lineup: Lineup) (team: TeamWorld) slot player =
        slot >= lineup.StarterCount
        && not (Set.contains player team.Substitution.UsedIncoming)
        && match team.Frame.Occupancy.[slot] with
           | OccupancyKind.Sidelined { Reason = PlayerOutReason.Substituted } -> true
           | _ -> false

    let private validateCommand (session: MatchSession) (command: MatchCommand) =
        if command.Revision <> session.Input.EffectiveConfig.Revision then
            Error(InvalidCommandRevision "MatchCommand revision does not match the session configuration")
        elif command.EffectiveTick < session.World.State.Tick then
            Error(InvalidCommandTiming "MatchCommand is earlier than the published frontier")
        else
            match sideForManager session.Input command.Manager with
            | None -> Error(InvalidCommandManager "manager does not own either team in this match")
            | Some side ->
                match command.Action with
                | ChangeTactics tactics ->
                    if String.IsNullOrWhiteSpace tactics.Formation then
                        Error(InvalidCommandRules "tactics formation must not be empty")
                    else
                        Ok()
                | RequestSubstitution(outgoing, incoming) ->
                    match
                        slotOfPlayer session.Input.Context side outgoing,
                        slotOfPlayer session.Input.Context side incoming
                    with
                    | Some outSlot, Some inSlot when outSlot <> inSlot ->
                        let team = MatchWorld.team session.World side

                        if
                            not (isActive team outSlot)
                            || not (isAvailableSubstitute (lineupFor session.Input.Context side) team inSlot incoming)
                        then
                            Error(
                                InvalidCommandPlayers "players are not an active outgoing and available incoming pair"
                            )
                        elif team.SubsUsed >= session.Input.Context.SubstitutionRules.MaxSubstitutions then
                            Error(InvalidCommandRules "the team's substitution limit has been reached")
                        else
                            Ok()
                    | _ -> Error(InvalidCommandPlayers "both substitution players must belong to the manager's team")

    let private applyTactics (team: TeamWorld) tick tactics =
        let kind =
            if tactics.Mentality >= 0.75f then
                DirectiveKind.Attack
            elif tactics.Mentality <= 0.25f then
                DirectiveKind.Defend
            elif tactics.PressIntensity >= 0.75f then
                DirectiveKind.Press
            else
                DirectiveKind.Balanced

        team.Directive.Kind <- kind
        team.Directive.SinceTick <- tick
        team.Directive.Params.LineHeight <- tactics.DefensiveLine * 100.0f
        team.Directive.Params.PressIntensity <- tactics.PressIntensity
        team.Directive.Params.Width <- tactics.Width

    let private applyCommand (session: MatchSession) (command: MatchCommand) =
        let side = sideForManager session.Input command.Manager |> Option.get

        match command.Action with
        | ChangeTactics tactics -> applyTactics (MatchWorld.team session.World side) session.World.State.Tick tactics
        | RequestSubstitution(outgoing, incoming) ->
            session.Buffer.Events.Add
                { Tick = session.World.State.Tick
                  Data = EventData.SubstitutionRequested(outgoing, incoming) }

    let private validateMode mode =
        match mode with
        | Simulator.RunMode.Replay interval when interval <= 0<tickDelta> ->
            Error(InvalidRunMode "Replay snapshot interval must be positive")
        | _ -> Ok()

    let create systems input mode : Result<MatchSession, MatchContractError> =
        match ConfigValidation.validateAll input.EffectiveConfig.Balance, validateMode mode with
        | Error errors, _ -> Error(InvalidInputConfig errors)
        | Ok(), Error error -> Error error
        | Ok(), Ok() ->
            let context =
                { input.Context with
                    Config = input.EffectiveConfig.Balance }

            let world = MatchFactory.create context

            let session =
                { Systems = systems
                  Input = { input with Context = context }
                  Mode = mode
                  World = world
                  Buffer =
                    { Events = ResizeArray<MatchEvent>()
                      Flags = { EventFlags.Bits = 0UL }
                      Applied = ResizeArray<MatchEvent>() }
                  Rng = Rng.ofSeed input.Seed
                  Pending = []
                  Commands = []
                  Events = ResizeArray<MatchEvent>()
                  Snapshots = ResizeArray<Replay.ReplaySnapshot>()
                  Diagnostics = ResizeArray<DecisionTrace>() }

            match mode with
            | Simulator.RunMode.Replay _ -> session.Snapshots.Add(Replay.capture world)
            | Simulator.RunMode.Fast
            | Simulator.RunMode.Full -> ()

            Ok session

    let receive command session : Result<unit, MatchContractError> =
        match validateCommand session command with
        | Error error -> Error error
        | Ok() ->
            match orderedCommands (session.Pending @ [ command ]) with
            | Error error -> Error error
            | Ok() ->
                session.Pending <- session.Pending @ [ command ]
                session.Commands <- session.Commands @ [ command ]

                appendDiagnostic
                    { Tick = session.World.State.Tick
                      Actor = "manager"
                      Options = [ "accept command" ]
                      Chosen = Some(sprintf "%A" command.Action)
                      Constraints = [ sprintf "effective-tick=%d" (int command.EffectiveTick) ]
                      Facts = [ sprintf "revision=%d" command.Revision ] }
                    session

                Ok()

    let advanceTo target session : Result<SessionAdvance, MatchContractError> =
        if session.World.State.Flow = MatchFlow.MatchEnded then
            Error SessionAlreadyFinished
        elif target < session.World.State.Tick then
            Error(InvalidAdvanceBoundary "target is earlier than the published frontier")
        else
            let produced = ResizeArray<MatchEvent>()
            let mutable failure = None

            while session.World.State.Tick < target
                  && session.World.State.Flow <> MatchFlow.MatchEnded
                  && failure.IsNone do
                let due, later =
                    session.Pending
                    |> List.partition (fun c -> c.EffectiveTick = session.World.State.Tick)

                session.Pending <- later

                for command in due do
                    match validateCommand session command with
                    | Error error -> failure <- Some error
                    | Ok() -> applyCommand session command

                match failure with
                | Some _ -> ()
                | None ->
                    let rng, _, applied =
                        Stepper.step
                            session.Systems
                            { Ctx = session.Input.Context
                              Clock = SimulationClock.defaultClock }
                            session.Rng
                            session.World
                            session.Buffer

                    session.Rng <- rng
                    session.Events.AddRange applied
                    produced.AddRange applied

                    appendDiagnostic
                        { Tick = session.World.State.Tick
                          Actor = "stepper"
                          Options = applied |> List.map (fun event -> sprintf "%A" (MatchEvent.kind event))
                          Chosen = None
                          Constraints = [ sprintf "flow=%A" session.World.State.Flow ]
                          Facts = [ sprintf "events=%d" applied.Length ] }
                        session

                    match session.Mode with
                    | Simulator.RunMode.Replay interval when
                        session.World.State.Tick % (int interval * 1<subtick>) = 0<subtick>
                        ->
                        session.Snapshots.Add(Replay.capture session.World)
                    | Simulator.RunMode.Fast
                    | Simulator.RunMode.Replay _
                    | Simulator.RunMode.Full -> ()

            match failure with
            | Some error -> Error error
            | None ->
                Ok
                    { ReachedTick = session.World.State.Tick
                      Flow = session.World.State.Flow
                      Events = List.ofSeq produced }

    let finish session : Result<Simulator.MatchOutcome, MatchContractError> =
        let rec loop () =
            if session.World.State.Flow = MatchFlow.MatchEnded then
                let events = List.ofSeq session.Events

                let outcome: Simulator.MatchOutcome =
                    { Events = events
                      Final = session.World
                      Replay =
                        match session.Mode with
                        | Simulator.RunMode.Replay _ -> Some(Replay.write (List.ofSeq session.Snapshots))
                        | Simulator.RunMode.Fast
                        | Simulator.RunMode.Full -> None
                      DumpJson =
                        match session.Mode with
                        | Simulator.RunMode.Full ->
                            Some(
                                MatchDump.export
                                    session.Input.Context
                                    events
                                    session.World.Home.Stats
                                    session.World.Away.Stats
                            )
                        | Simulator.RunMode.Fast
                        | Simulator.RunMode.Replay _ -> None
                      EventsHash = Simulator.eventsHash events }

                Ok outcome
            else
                match advanceTo (session.World.State.Tick + 1<subtick>) session with
                | Error error -> Error error
                | Ok _ -> loop ()

        loop ()

    let restore systems (input: MatchInput) (checkpoint: MatchCheckpoint) : Result<MatchSession, MatchContractError> =
        if checkpoint.Version <> checkpointVersion then
            Error(InvalidCheckpoint "checkpoint version is not supported")
        elif checkpoint.MatchId <> input.MatchId || checkpoint.Seed <> input.Seed then
            Error(InvalidCheckpoint "checkpoint does not belong to this match input")
        elif checkpoint.EngineVersion <> input.EngineVersion then
            Error(InvalidCheckpoint "checkpoint engine version does not match")
        elif checkpoint.Revision <> input.EffectiveConfig.Revision then
            Error(InvalidCheckpoint "checkpoint configuration revision does not match")
        else
            match create systems input checkpoint.Mode with
            | Error error -> Error error
            | Ok session ->
                let rec queue commands =
                    match commands with
                    | [] -> Ok()
                    | command :: rest ->
                        match receive command session with
                        | Error error -> Error error
                        | Ok() -> queue rest

                match queue checkpoint.Commands with
                | Error error -> Error(InvalidCheckpoint(sprintf "checkpoint command replay failed: %A" error))
                | Ok() ->
                    match advanceTo checkpoint.Frontier session with
                    | Error error -> Error(InvalidCheckpoint(sprintf "checkpoint frontier replay failed: %A" error))
                    | Ok advance when advance.ReachedTick <> checkpoint.Frontier ->
                        Error(InvalidCheckpoint "checkpoint frontier could not be reached")
                    | Ok _ ->
                        let actualHash = Simulator.eventsHash (List.ofSeq session.Events)

                        if actualHash <> checkpoint.EventsHash then
                            Error(InvalidCheckpoint "checkpoint event hash differs after replay")
                        elif session.Pending <> checkpoint.Pending then
                            Error(InvalidCheckpoint "checkpoint pending command queue differs after replay")
                        elif session.Rng <> checkpoint.Rng then
                            Error(InvalidCheckpoint "checkpoint RNG differs after replay")
                        else
                            Ok session

module MatchAdapter =

    let private resultOfOutcome (input: MatchInput) (outcome: Simulator.MatchOutcome) : MatchResult =
        let yellowCards, redCards, fouls =
            outcome.Events
            |> List.fold
                (fun (yellow, red, fouls) event ->
                    match event.Data with
                    | EventData.Card(_, CardColor.Yellow) -> yellow + 1, red, fouls
                    | EventData.Card(_, CardColor.Red)
                    | EventData.RedCardIssued _ -> yellow, red + 1, fouls
                    | EventData.Foul _ -> yellow, red, fouls + 1
                    | _ -> yellow, red, fouls)
                (0, 0, 0)

        let homeEvents, awayEvents =
            outcome.Events
            |> List.fold
                (fun (home, away) event ->
                    match event.Data with
                    | EventData.Goal(ClubSide.Home, _, _, _) -> home + 1, away
                    | EventData.Goal(ClubSide.Away, _, _, _) -> home, away + 1
                    | _ -> home, away)
                (0, 0)

        { MatchId = input.MatchId
          EngineVersion = input.EngineVersion
          Facts =
            { HomeScore = outcome.Final.State.HomeScore
              AwayScore = outcome.Final.State.AwayScore
              EventsHash = outcome.EventsHash }
          Load =
            { HomeEvents = homeEvents
              AwayEvents = awayEvents
              TotalTicks = outcome.Final.State.Tick }
          InjuredPlayers =
            outcome.Events
            |> List.choose (fun event ->
                match event.Data with
                | EventData.Injury(player, _) -> Some player
                | _ -> None)
          Discipline =
            { YellowCards = yellowCards
              RedCards = redCards
              Fouls = fouls }
          Metrics = outcome.Final.Home.Stats, outcome.Final.Away.Stats
          Trace = None }

    let runOutcome systems input commands : Result<Simulator.MatchOutcome, MatchContractError> =
        match MatchSession.create systems input Simulator.RunMode.Fast with
        | Error error -> Error error
        | Ok session ->
            let rec receiveAll rest =
                match rest with
                | [] -> MatchSession.finish session
                | command :: tail ->
                    match MatchSession.receive command session with
                    | Error error -> Error error
                    | Ok() -> receiveAll tail

            receiveAll commands

    let run systems input commands : Result<MatchResult, MatchContractError> =
        runOutcome systems input commands |> Result.map (resultOfOutcome input)
