namespace FootballEngine.World

open FootballEngine.Domain

type AgentId =
    | ClubAgentId of ClubId
    | ManagerAgentId of ClubId
    | PlayerAgentId of PlayerId

type WorldEventType =
    | OfferReceived of PlayerId * ClubId
    | OfferExceedsThreshold of PlayerId * decimal
    | SeasonStarted
    | SeasonEnded
    | MatchDayArrived
    | FullSeasonRequested

type Trigger =
    | OnSchedule of WorldTick
    | OnEvent of WorldEventType
    | Both of WorldTick * WorldEventType

type AgentView =
    | ClubView of club: Club * budget: decimal * squad: Player list * incomingNegotiations: (Negotiation * Player) list
    | ManagerView of club: Club * targets: TransferTarget list
    | PlayerView of player: Player * offers: TransferOffer list

type Intent<'T> =
    { AgentId: AgentId
      Payload: 'T
      Priority: int }

type Agent<'Intent> =
    { Id: AgentId
      Trigger: Trigger
      Project: GameState -> AgentView
      Decide: AgentView -> Intent<'Intent> list }

type PhaseTag =
    | Finance
    | Match
    | Transfer
    | Development
    | Youth
    | Season

type WorldPhaseTag =
    | ExtractDueWork
    | ApplyEffectiveRecords
    | PrepareDay
    | ResolveFixtures
    | ConfirmResults
    | SettleObligations
    | ResolveDecisions
    | CloseDay

[<Struct>]
type ScheduledWorkId = ScheduledWorkId of uint64

type ScheduledWorkKind =
    | DueCommand of CommandId
    | TrainingSession
    | CompetitionClosure
    | ObligationSettlement

type ScheduledWork =
    { Id: ScheduledWorkId
      EffectiveDate: System.DateTime
      Phase: WorldPhaseTag
      Kind: ScheduledWorkKind
      TargetId: AgentId option }

type WorldPhaseTrace =
    { Phase: WorldPhaseTag
      Date: System.DateTime
      RevisionBefore: uint64
      RevisionAfter: uint64 }

type WorldPhase =
    { Tag: PhaseTag
      Frequency: WorldTick
      Run: WorldClock -> GameState -> GameState }

and WorldClock =
    { Current: WorldTime
      PendingEvents: PendingEvent list
      Revision: uint64
      ScheduledWork: ScheduledWork list
      ConsumedWork: Set<ScheduledWorkId>
      PhaseHistory: WorldPhaseTrace list }

and PendingEvent =
    { At: WorldTime
      Type: WorldEventType
      TargetId: AgentId option }

module AgentTypes =
    let mkPhase
        (agents: Agent<'Intent> list)
        (supervisor: GameState -> Intent<'Intent> list -> GameState)
        (tag: PhaseTag)
        (frequency: WorldTick)
        : WorldPhase =
        { Tag = tag
          Frequency = frequency
          Run =
            fun clock state ->
                agents
                |> List.filter (fun a ->
                    match a.Trigger with
                    | OnSchedule freq -> WorldTime.shouldRun freq clock.Current
                    | OnEvent evType ->
                        clock.PendingEvents
                        |> List.filter (fun e -> e.At.Day = clock.Current.Day)
                        |> List.exists (fun e -> e.Type = evType)
                    | Both(freq, evType) ->
                        WorldTime.shouldRun freq clock.Current
                        || clock.PendingEvents
                           |> List.filter (fun e -> e.At.Day = clock.Current.Day)
                           |> List.exists (fun e -> e.Type = evType))
                |> Array.ofList
                |> Array.Parallel.map (fun agent -> agent.Project state |> agent.Decide)
                |> Array.toList
                |> List.concat
                |> supervisor state }
