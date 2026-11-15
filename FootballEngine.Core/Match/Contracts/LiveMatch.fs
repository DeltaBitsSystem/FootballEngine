namespace FootballEngine.LiveMatch

open FootballEngine.Domain
open FootballEngine.Core.Match

[<Struct>]
type LiveMatchSessionId = LiveMatchSessionId of uint64

[<Struct>]
type MatchCommandId = MatchCommandId of uint64

[<Struct>]
type MatchEventId = MatchEventId of uint64

type LiveSpeed =
    | Normal
    | Fast
    | VeryFast

/// Why the pacing stopped the match for the manager; the strings are already-resolved names, never ids.
type KeyMomentReason =
    | GoalScored of scorerTeamIsUser: bool
    | RedCardShown of playerName: string * userTeam: bool
    | PlayerTiring of playerName: string * condition: int
    | RivalChange of description: string
    /// An autonomous rival coach changed the plan; the shift says what he did so the banner can say it too.
    | RivalShift of ManagerShift

type LiveMatchStatus =
    | Connecting
    | Preparing
    | Live of speed: LiveSpeed
    /// Fast-forwarding through quiet play; status rather than a presentation-only flag so the HUD needs no new channel.
    | Cruising
    | Paused
    | KeyMomentStop of KeyMomentReason
    | AwaitingDecision of DecisionPrompt
    | HalfTime
    | Finished
    | Failed of message: string

and DecisionPrompt =
    | InjuryDecision of playerId: FootballEngine.Domain.PlayerId
    | DismissalDecision of playerId: FootballEngine.Domain.PlayerId
    | SubstitutionRequired of reason: string

type LiveMatchSource =
    | LiveSession
    | RecordedReplay

type MatchPeriod =
    | FirstHalf
    | HalfTimeInterval
    | SecondHalf
    | ExtraTimeFirstHalf
    | ExtraTimeInterval
    | ExtraTimeSecondHalf
    | PenaltyShootout
    | FullTime

type LiveClock =
    { Elapsed: int<subtick>
      DisplayMinute: int
      StoppageMinute: int option
      Period: MatchPeriod }

type ClubMatchIdentity =
    { ClubId: ClubId
      Name: string
      ShortName: string
      /// 0xFFRRGGBB, or zero when the club has no authored kit and presentation must choose a fallback.
      PrimaryColor: uint32
      SecondaryColor: uint32
      /// The home ground is generated from these, so a club's stadium is the same every match.
      Reputation: int
      /// Zero-based national division; None for clubs outside any league.
      LeagueLevel: int option }

type MatchPlayerIdentity =
    { Id: FootballEngine.Domain.PlayerId
      Name: string
      IsGoalkeeper: bool }

type MatchTeamIdentity =
    { Players: MatchPlayerIdentity array
      StarterCount: int
      InitialTactics: TacticsConfig }

type FixtureContext =
    { MatchId: MatchId
      CompetitionId: CompetitionId
      CompetitionName: string
      BroadcastIdentityKey: string
      Home: ClubMatchIdentity
      Away: ClubMatchIdentity
      HomeLineup: MatchTeamIdentity
      AwayLineup: MatchTeamIdentity
      UserClubId: ClubId
      BroadcastIdentity: BroadcastIdentity
      PlayerNames: Map<FootballEngine.Core.Match.PlayerId, string>
      Round: Round option
      Leg: int option
      AggregateScore: (int * int) option
      SubstitutionRules: CompetitionSubstitutionRules }

type TacticalPhase =
    | InPossession
    | Transition
    | OutOfPossession

type Mentality =
    | Defensive
    | Cautious
    | Balanced
    | Positive
    | Attacking

type TacticalInstruction =
    | PlayOutFromBack
    | CounterPress
    | CounterAttack
    | HoldShape
    | OverlapLeft
    | OverlapRight
    | LowBlock
    | HighLine

type PlayerRole =
    | GoalkeeperRole
    | FullBackRole
    | CentreBackRole
    | DefensiveMidfielderRole
    | CentralMidfielderRole
    | AttackingMidfielderRole
    | WingerRole
    | StrikerRole

type PlayerDuty =
    | DefendDuty
    | SupportDuty
    | AttackDuty

type PlayerInstruction =
    | StayWider
    | RoamFromPosition
    | GetFurtherForward
    | HoldPosition
    | PressMore

type TouchlineInstruction =
    | DemandMore
    | CalmDown
    | FocusPlayLeft
    | FocusPlayRight
    | Encourage

type SubstitutionChange =
    { Outgoing: FootballEngine.Domain.PlayerId
      Incoming: FootballEngine.Domain.PlayerId
      Role: PlayerRole
      Duty: PlayerDuty }

type LiveMatchCommandPayload =
    | ChangeMentality of Mentality
    | ChangeFormation of Formation
    | SetTeamInstruction of phase: TacticalPhase * instruction: TacticalInstruction
    | RemoveTeamInstruction of phase: TacticalPhase * instruction: TacticalInstruction
    | SetPlayerRole of playerId: FootballEngine.Domain.PlayerId * role: PlayerRole * duty: PlayerDuty
    | SetPlayerInstruction of playerId: FootballEngine.Domain.PlayerId * instruction: PlayerInstruction
    | PrepareSubstitutions of changes: SubstitutionChange list
    | ConfirmSubstitutions of changes: SubstitutionChange list
    | IssueTouchlineInstruction of TouchlineInstruction
    | SetLiveSpeed of LiveSpeed
    /// Presentation-only: the worker advances unpaced up to the tick. The match is deterministic, so
    /// skipping the wall-clock wait cannot change a sporting outcome.
    | SkipAhead of untilTick: int<subtick>
    /// Presentation-only: the worker stops advancing at the tick until a later hold, so a capture
    /// can step the match frame by frame however long each frame takes to grab.
    | HoldAt of untilTick: int<subtick>
    | PauseMatch
    | ResumeMatch

type LiveMatchCommand =
    { CommandId: MatchCommandId
      SessionId: LiveMatchSessionId
      ExpectedRevision: uint64
      IssuedAt: int<subtick>
      Payload: LiveMatchCommandPayload }

type CommandRejection =
    | StaleRevision of expected: uint64 * actual: uint64
    | InvalidMatchState of LiveMatchStatus
    | IllegalSubstitution of reason: string
    | InvalidTacticalChange of reason: string
    | CommandNotPermitted of reason: string

type ApplicationPoint =
    | Immediate
    | NextCognitionBoundary
    | NextStoppage
    | BeforeNextKickOff

type CommandLifecycle =
    | Queued
    | Accepted of applicationPoint: ApplicationPoint
    | Applied of revision: uint64 * appliedAt: int<subtick>
    | Rejected of CommandRejection

type PendingCommand =
    { Command: LiveMatchCommand
      Lifecycle: CommandLifecycle }

type MatchDayWorkspace =
    | Overview
    | Tactics of TacticalPhase
    | Players
    | Substitutions
    | Touchline

type IntelligenceWorkspace =
    | Commentary
    | Statistics
    | Events
    | Assistant

type MatchDayRail =
    | ManagerRail
    | IntelligenceRail

type MatchDayUiState =
    { ManagerWorkspace: MatchDayWorkspace
      IntelligenceWorkspace: IntelligenceWorkspace
      SelectedPlayerId: FootballEngine.Domain.PlayerId option
      SelectedEventId: MatchEventId option
      EvidenceQuery: ObservationQuery
      DraftSubstitutions: SubstitutionChange list
      PinnedRail: MatchDayRail option }

type CommentaryCategory =
    | PlayByPlay
    | Officiating
    | TacticalObservation
    | MatchContext

type CommentaryImportance =
    | Routine
    | Notable
    | Critical

type CommentaryEntry =
    { Id: MatchEventId
      Tick: int<subtick>
      Category: CommentaryCategory
      Importance: CommentaryImportance
      Text: string
      LocalizationKey: string
      Arguments: Map<string, string>
      ActorIds: FootballEngine.Core.Match.PlayerId list
      EvidenceEventIds: MatchEventId list }

type LiveMetrics = { Home: TeamStats; Away: TeamStats }

type LiveMatchState =
    { SessionId: LiveMatchSessionId
      Source: LiveMatchSource
      Fixture: FixtureContext
      Revision: uint64
      Status: LiveMatchStatus
      Speed: LiveSpeed
      Clock: LiveClock
      Score: int * int
      Metrics: LiveMetrics
      Events: MatchEvent list
      Evidence: ObservationSnapshot option
      Commentary: CommentaryEntry list
      PendingCommands: Map<MatchCommandId, PendingCommand>
      NextCommandId: uint64
      Ui: MatchDayUiState
      Error: string option }

type LiveMatchMsg =
    | SessionConnected of LiveMatchSessionId * FixtureContext * revision: uint64 * LiveMatchSource
    | SemanticReceived of revision: uint64 * LiveClock * (int * int) * TeamStats * TeamStats * MatchEvent list
    | CommandSubmitted of LiveMatchCommand
    | CommandAccepted of MatchCommandId * ApplicationPoint
    | CommandApplied of MatchCommandId * revision: uint64 * appliedAt: int<subtick>
    | CommandRejected of MatchCommandId * CommandRejection
    | RuntimeStatusChanged of LiveMatchStatus
    | SelectManagerWorkspace of MatchDayWorkspace
    | SelectIntelligenceWorkspace of IntelligenceWorkspace
    | SelectPlayer of FootballEngine.Domain.PlayerId option
    | SetEvidenceQuery of ObservationQuery
    | SetDraftSubstitutions of SubstitutionChange list
    | SubmitDraftSubstitutions
    | DismissError

type LiveMatchEffect =
    | SendCommand of LiveMatchCommand
    | ConnectSession of MatchId
    | DisconnectSession of LiveMatchSessionId
    | NoEffect

module LiveMatchState =
    let private zeroStats () : TeamStats =
        { Shots = 0
          OnTarget = 0
          Passes = 0
          PassesCompleted = 0
          Fouls = 0
          Corners = 0
          Offsides = 0
          Touches = 0
          Dribbles = 0
          Crosses = 0
          XgSum = 0.0 }

    let private initialUi =
        { ManagerWorkspace = Overview
          IntelligenceWorkspace = Commentary
          SelectedPlayerId = None
          SelectedEventId = None
          EvidenceQuery =
            { Actor = None
              Action = None
              Event = None
              FromTick = 0<subtick>
              UntilTick = System.Int32.MaxValue * 1<subtick> }
          DraftSubstitutions = []
          PinnedRail = None }

    let create sessionId fixture revision source =
        { SessionId = sessionId
          Source = source
          Fixture = fixture
          Revision = revision
          Status = Preparing
          Evidence = None
          Speed = Normal
          Clock =
            { Elapsed = 0<subtick>
              DisplayMinute = 0
              StoppageMinute = None
              Period = FirstHalf }
          Score = (0, 0)
          Metrics =
            { Home = zeroStats ()
              Away = zeroStats () }
          Events = []
          Commentary = []
          PendingCommands = Map.empty
          NextCommandId = 1UL
          Ui = initialUi
          Error = None }

module LiveMatchCommand =
    let idValue (MatchCommandId value) = value

    let applicationPoint payload =
        match payload with
        | SetLiveSpeed _
        | SkipAhead _
        | HoldAt _
        | PauseMatch
        | ResumeMatch
        | IssueTouchlineInstruction _ -> Immediate
        | PrepareSubstitutions _
        | ConfirmSubstitutions _ -> NextStoppage
        | _ -> NextCognitionBoundary

module LiveMatchUpdate =
    let private bounded limit values =
        if List.length values <= limit then
            values
        else
            values |> List.skip (List.length values - limit)

    let private matchPeriod flow =
        match flow with
        | 5uy -> HalfTimeInterval
        | 6uy -> ExtraTimeInterval
        | 8uy -> PenaltyShootout
        | 9uy -> FullTime
        | _ -> FirstHalf

    let private updatePending commandId lifecycle state =
        match state.PendingCommands |> Map.tryFind commandId with
        | Some current ->
            { state with
                PendingCommands =
                    state.PendingCommands
                    |> Map.add commandId { current with Lifecycle = lifecycle } }
        | None -> state

    let private eventId event =
        MatchEventId(EventId.value (MatchEvent.id event))

    let private commentaryFor (_fixture: FixtureContext) (event: MatchEvent) =
        let playerName (playerId: FootballEngine.Core.Match.PlayerId) =
            _fixture.PlayerNames
            |> Map.tryFind playerId
            |> Option.defaultValue "Unknown player"

        let id = eventId event

        let text, category, importance, actors, key, args =
            match event.Data with
            | EventData.Goal(_, Some scorer, Some assist, _) ->
                sprintf "%s scores, assisted by %s." (playerName scorer) (playerName assist),
                PlayByPlay,
                Critical,
                [ scorer; assist ],
                "match.commentary.goal.assisted",
                Map.empty
            | EventData.Goal(_, Some scorer, None, _) ->
                sprintf "%s finishes the move." (playerName scorer),
                PlayByPlay,
                Critical,
                [ scorer ],
                "match.commentary.goal",
                Map.empty
            | EventData.Shot(shooter, ShotResult.OnTarget saved, xG, _, _, _) ->
                sprintf "%s tests the goalkeeper. xG %.2f." (playerName shooter) xG,
                PlayByPlay,
                Notable,
                [ shooter ],
                "match.commentary.shot.saved",
                Map.empty
            | EventData.Shot(shooter, _, xG, _, _, _) ->
                sprintf "%s gets a sight of goal. xG %.2f." (playerName shooter) xG,
                PlayByPlay,
                Notable,
                [ shooter ],
                "match.commentary.shot",
                Map.empty
            | EventData.Card(player, CardColor.Yellow) ->
                sprintf "%s is booked." (playerName player),
                Officiating,
                Notable,
                [ player ],
                "match.commentary.yellow",
                Map.empty
            | EventData.RedCardIssued player ->
                sprintf "%s is sent off." (playerName player),
                Officiating,
                Critical,
                [ player ],
                "match.commentary.red",
                Map.empty
            | EventData.Injury(player, severity) ->
                sprintf "%s needs attention after an injury (severity %d)." (playerName player) severity,
                Officiating,
                Critical,
                [ player ],
                "match.commentary.injury",
                Map.empty
            | EventData.SubstitutionApplied(outgoing, incoming) ->
                sprintf "%s replaces %s." (playerName incoming) (playerName outgoing),
                MatchContext,
                Notable,
                [ outgoing; incoming ],
                "match.commentary.substitution",
                Map.empty
            | EventData.VARDecision VARResult.GoalOverturned ->
                "The goal is overturned after a VAR review.",
                Officiating,
                Critical,
                [],
                "match.commentary.var.overturned",
                Map.empty
            | EventData.VARDecision VARResult.GoalStands ->
                "The goal stands after a VAR review.",
                Officiating,
                Critical,
                [],
                "match.commentary.var.stands",
                Map.empty
            | EventData.HalfTime ->
                "The referee brings the half to a close.",
                MatchContext,
                Notable,
                [],
                "match.commentary.halftime",
                Map.empty
            | EventData.FullTime ->
                "The final whistle goes.", MatchContext, Critical, [], "match.commentary.fulltime", Map.empty
            | EventData.Pass(passer, _, _, quality, completed, _, _, _, _) when completed ->
                sprintf "%s finds the next pass with %.0f%% quality." (playerName passer) (quality * 100.0),
                PlayByPlay,
                Routine,
                [ passer ],
                "match.commentary.pass.complete",
                Map.empty
            | EventData.Pass(passer, _, _, _, _, _, _, _, _) ->
                sprintf "%s cannot find the target." (playerName passer),
                PlayByPlay,
                Routine,
                [ passer ],
                "match.commentary.pass.failed",
                Map.empty
            | EventData.Foul(fouler, _, _, _, _) ->
                sprintf "%s concedes a foul." (playerName fouler),
                Officiating,
                Notable,
                [ fouler ],
                "match.commentary.foul",
                Map.empty
            | EventData.Offside player ->
                sprintf "%s is caught offside." (playerName player),
                Officiating,
                Notable,
                [ player ],
                "match.commentary.offside",
                Map.empty
            | _ -> "", MatchContext, Routine, [], "", Map.empty

        if System.String.IsNullOrEmpty key then
            None
        else
            Some
                { Id = id
                  Tick = event.Tick
                  Category = category
                  Importance = importance
                  Text = text
                  LocalizationKey = key
                  Arguments = args
                  ActorIds = actors
                  EvidenceEventIds = [ id ] }

    let private applyEvents (fixture: FixtureContext) (events: MatchEvent list) (state: LiveMatchState) =
        let events: MatchEvent list = events |> List.sortBy _.Tick
        let commentary: CommentaryEntry list = events |> List.choose (commentaryFor fixture)

        { state with
            Events = bounded 128 (state.Events @ events)
            Commentary = bounded 96 (state.Commentary @ commentary) }

    let update msg state : LiveMatchState option * LiveMatchEffect =
        match msg, state with
        | SessionConnected(sessionId, _, _, _), Some current when current.SessionId = sessionId ->
            Some current, NoEffect
        | SessionConnected(sessionId, fixture, revision, source), _ ->
            Some(LiveMatchState.create sessionId fixture revision source), NoEffect
        | _, None -> None, NoEffect
        | SemanticReceived(revision, clock, (homeScore, awayScore), homeStats, awayStats, events), Some current when
            revision >= current.Revision
            ->
            let next =
                { applyEvents current.Fixture events current with
                    Revision = revision
                    Status =
                        match current.Status with
                        | Paused -> Paused
                        | KeyMomentStop reason -> KeyMomentStop reason
                        | Cruising -> Cruising
                        | HalfTime -> HalfTime
                        | Finished -> Finished
                        | _ -> Live current.Speed
                    Clock = clock
                    Score = homeScore, awayScore
                    Metrics = { Home = homeStats; Away = awayStats }
                    Error = None }

            Some next, NoEffect
        | SemanticReceived _, Some current -> Some current, NoEffect
        | CommandSubmitted command, Some current ->
            let pending =
                { Command = command
                  Lifecycle = Queued }

            Some
                { current with
                    PendingCommands = current.PendingCommands |> Map.add command.CommandId pending
                    NextCommandId = max current.NextCommandId (LiveMatchCommand.idValue command.CommandId + 1UL) },
            SendCommand command
        | CommandAccepted(commandId, point), Some current ->
            Some(updatePending commandId (Accepted point) current), NoEffect
        | CommandApplied(commandId, revision, appliedAt), Some current ->
            Some(updatePending commandId (Applied(revision, appliedAt)) { current with Revision = revision }), NoEffect
        | CommandRejected(commandId, rejection), Some current ->
            Some(updatePending commandId (Rejected rejection) current), NoEffect
        | RuntimeStatusChanged status, Some current ->
            Some
                { current with
                    Status = status
                    Speed =
                        match status with
                        | Live speed -> speed
                        | _ -> current.Speed },
            NoEffect
        | SelectManagerWorkspace workspace, Some current ->
            Some
                { current with
                    Ui =
                        { current.Ui with
                            ManagerWorkspace = workspace } },
            NoEffect
        | SelectIntelligenceWorkspace workspace, Some current ->
            Some
                { current with
                    Ui =
                        { current.Ui with
                            IntelligenceWorkspace = workspace } },
            NoEffect
        | SelectPlayer playerId, Some current when playerId = current.Ui.SelectedPlayerId -> Some current, NoEffect
        | SelectPlayer playerId, Some current ->
            Some
                { current with
                    Ui =
                        { current.Ui with
                            SelectedPlayerId = playerId
                            EvidenceQuery =
                                { current.Ui.EvidenceQuery with
                                    Actor = playerId |> Option.map FootballEngine.Core.Match.PlayerId } } },
            NoEffect
        | SetEvidenceQuery query, Some current ->
            let query =
                { query with
                    Actor = current.Ui.SelectedPlayerId |> Option.map FootballEngine.Core.Match.PlayerId }

            if
                query.FromTick < 0<subtick>
                || query.UntilTick < query.FromTick
                || query = current.Ui.EvidenceQuery
            then
                Some current, NoEffect
            else
                Some
                    { current with
                        Ui =
                            { current.Ui with
                                EvidenceQuery = query } },
                NoEffect
        | SetDraftSubstitutions changes, Some current ->
            Some
                { current with
                    Ui =
                        { current.Ui with
                            DraftSubstitutions = changes } },
            NoEffect
        | SubmitDraftSubstitutions, Some current ->
            match current.Ui.DraftSubstitutions with
            | [] -> Some current, NoEffect
            | changes -> Some current, NoEffect
        | DismissError, Some current -> Some { current with Error = None }, NoEffect
