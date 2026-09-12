namespace FootballEngine.Domain

open System

[<Struct>]
type CommandId = CommandId of uint64

[<Struct>]
type WorldEventId = WorldEventId of uint64

type CommandIssuer =
    | Manager of StaffId
    | System

type WorldCommandAction =
    | AdvanceDays of int
    | ConfirmMatch of MatchId
    | ChangeBalance of revision: uint64 * payload: string

type WorldCommand =
    { Id: CommandId
      Issuer: CommandIssuer
      EffectiveDate: DateTime
      ExpectedRevision: uint64
      Action: WorldCommandAction }

type DecisionReason =
    { Code: string
      FactReferences: string list
      Cost: decimal
      Uncertainty: float }

type WorldEventPayload =
    | WorldAdvanced of fromDate: DateTime * toDate: DateTime
    | MatchConfirmed of MatchId * homeScore: int * awayScore: int
    | RulesChanged of revision: uint64
    | FixtureRescheduled of MatchId * DateTime * DateTime
    | RoundCompleted of CompetitionId * Round
    | CompetitionWon of CompetitionId * ClubId
    | ClubPromoted of ClubId * CompetitionId
    | ClubRelegated of ClubId * CompetitionId
    | ObjectiveReviewed of ClubId * bool
    | ManagerHired of StaffId * ClubId
    | ManagerDismissed of StaffId * ClubId
    | PlayerCalledUp of PlayerId * CountryCode

type WorldEvent =
    { Id: WorldEventId
      EffectiveDate: DateTime
      Sequence: uint64
      Cause: CommandId option
      Entities: string list
      Payload: WorldEventPayload }

/// Employment is independent from match-team identity; unemployment is valid.
type ManagerEmployment =
    | EmployedBy of ClubId
    | Unemployed

type MatchTeamRef =
    | ClubTeam of ClubId
    | NationalTeam of CountryCode

/// GameState remains the sole world aggregate. This alias names its contract
/// without introducing a second state that can drift from persistence.
type WorldState = GameState

type SaveManifestEntry =
    { Id: string
      Version: string
      SchemaVersion: int
      Dependencies: string list
      Precedence: int
      ContentHash: string }

type SaveIds =
    { NextInboxId: int
      NextNegotiationId: int
      NextScheduledWorkId: uint64
      NextCommandId: uint64
      NextEventId: uint64 }

type SaveRngState =
    { Seed: uint64
      S0: uint64
      S1: uint64
      S2: uint64
      S3: uint64 }

type SavePhase =
    | BeforeMatch
    | AfterMatch

type LoadError =
    | Missing of slot: string
    | Incompatible of slot: string * expectedSchema: int * actualSchema: int
    | Corrupt of slot: string * message: string

type AdvanceStopReason =
    | ReachedLimit
    | PausedForMatch of MatchId
    | InvalidRequest of string
    | Cancelled
    | Failed of phase: string * message: string

type AdvanceRequest =
    { FromDate: DateTime
      UntilDate: DateTime
      PausePolicy: PausePolicy
      ExpectedRevision: uint64
      CancellationRequested: bool }

and PausePolicy =
    | RunThroughMatches
    | PauseBeforeUserMatch

type AdvanceResult =
    { State: WorldState
      ConfirmedRevision: uint64
      Events: WorldEvent list
      StoppedBecause: AdvanceStopReason }

type SaveEnvelope =
    { SchemaVersion: int
      EngineVersion: string
      CareerId: string
      ConfirmedRevision: uint64
      ConfirmedState: WorldState
      ConfirmedDate: DateTime
      Phase: SavePhase
      ClockDay: int
      ClockWeek: int
      ClockMonth: int
      ClockSeason: int
      Rng: SaveRngState
      NextIds: SaveIds
      PendingCommands: WorldCommand list
      PendingAgendaJson: string
      EntitiesJson: string
      ObligationsJson: string
      RelationsJson: string
      LearningJson: string
      KnowledgeJson: string
      ConfigContent: string
      ConfigHash: uint64
      DataManifest: SaveManifestEntry list
      DataLock: DataLock
      ResolvedDataHash: string
      ResolvedDataContent: string }

module SaveEnvelope =

    let validate (envelope: SaveEnvelope) : Result<unit, string> =
        let errors = ResizeArray<string>()

        let addIf condition message =
            if condition then
                errors.Add message

        addIf (String.IsNullOrWhiteSpace envelope.EngineVersion) "SaveEnvelope.EngineVersion must be non-empty"
        addIf (envelope.SchemaVersion < 1) "SaveEnvelope.SchemaVersion must be >= 1"
        addIf (String.IsNullOrWhiteSpace envelope.CareerId) "SaveEnvelope.CareerId must be non-empty"

        addIf
            (envelope.ClockDay < 1 || envelope.ClockWeek < 1 || envelope.ClockMonth < 1)
            "SaveEnvelope clock must be positive"

        addIf (String.IsNullOrWhiteSpace envelope.ConfigContent) "SaveEnvelope.ConfigContent must be non-empty"
        addIf (envelope.ConfigHash = 0UL) "SaveEnvelope.ConfigHash must be non-zero"
        addIf (String.IsNullOrWhiteSpace envelope.ResolvedDataHash) "SaveEnvelope.ResolvedDataHash must be non-empty"

        addIf
            (String.IsNullOrWhiteSpace envelope.ResolvedDataContent)
            "SaveEnvelope.ResolvedDataContent must be non-empty"

        if
            envelope.DataManifest
            |> List.exists (fun entry ->
                String.IsNullOrWhiteSpace entry.Id
                || String.IsNullOrWhiteSpace entry.Version
                || entry.SchemaVersion < 1
                || String.IsNullOrWhiteSpace entry.ContentHash)
        then
            errors.Add "SaveEnvelope.DataManifest entries must be named"

        match DataLock.validate envelope.DataLock with
        | Error message -> errors.Add("SaveEnvelope.DataLock: " + message)
        | Ok() ->
            let expectedManifest =
                envelope.DataLock.Packages
                |> List.map (fun package ->
                    { Id = package.ModId
                      Version = package.Version
                      SchemaVersion = package.SchemaVersion
                      Dependencies = package.Dependencies |> List.map fst
                      Precedence = package.Precedence
                      ContentHash = package.PackageSha256 })

            addIf
                (envelope.DataManifest <> expectedManifest)
                "SaveEnvelope.DataManifest must match SaveEnvelope.DataLock"

            addIf
                (envelope.ConfirmedState.DataLock <> envelope.DataLock)
                "SaveEnvelope.ConfirmedState.DataLock must match SaveEnvelope.DataLock"

        if errors.Count = 0 then
            Ok()
        else
            Error(String.concat "; " errors)

module WorldCommand =

    let validate (command: WorldCommand) : Result<unit, string> =
        [ if command.ExpectedRevision < 0UL then
              yield "WorldCommand.ExpectedRevision cannot be negative"
          match command.Action with
          | AdvanceDays days when days < 0 -> yield "WorldCommand.AdvanceDays must be >= 0"
          | ChangeBalance(_, payload) when String.IsNullOrWhiteSpace payload ->
              yield "WorldCommand.ChangeBalance payload must be non-empty"
          | _ -> () ]
        |> function
            | [] -> Ok()
            | errors -> Error(String.concat "; " errors)

    let compare (left: WorldCommand) (right: WorldCommand) =
        Microsoft.FSharp.Core.Operators.compare (left.EffectiveDate, left.Id) (right.EffectiveDate, right.Id)
