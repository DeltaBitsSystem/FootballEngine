namespace FootballEngine.Core.Match

open System.Collections.Generic

[<Struct>]
type ObservationId = ObservationId of uint64

type ObservationCause =
    | DecisionCause of ObservationId
    | ResolutionCause of ObservationId
    | ActionCause of ActionId
    | EventCause of EventId
    | RestartReference of RestartId
    | PairedEvents of incident: EventId * trigger: EventId

[<Struct>]
type DecisionEvidence =
    { Actor: PlayerId
      ActionCode: int
      TargetSlot: int
      Condition: float32
      Cognitive: float32
      Pressure: int
      OptionCount: int
      ChosenValue: float
      Temperature: float
      Sample: float }

[<Struct>]
type ObservationOption =
    { ActionCode: int
      TargetSlot: int
      Value: float }

type ObservationSelection =
    { Actor: PlayerId
      FromTick: int<subtick>
      UntilTick: int<subtick> }

type ObservationQuery =
    { Actor: PlayerId option
      Action: ActionId option
      Event: EventId option
      FromTick: int<subtick>
      UntilTick: int<subtick> }

type ProbabilityResolutionKind =
    | Reception

type ExecutionRejection =
    | PassDistanceLimit of distance: float<meter> * limit: float<meter>
    | NoCrossTarget
    | DuelCarrierUnavailable

[<Struct>]
type TransitionDecisionEvidence =
    { Side: ClubSide
      Episode: EventId
      Mode: TransitionMode
      Reason: TransitionReason
      Primary: PlayerId option
      Cover: PlayerId option
      Arrival: float<second>
      ArrivalWindow: float<second>
      CoverArrival: float<second>
      LocalPlayers: int
      Opponents: int
      DepthRunners: int }

type OrganizedDefenseReason =
    | OrganizedPressure
    | OrganizedTacticalRestraint
    | OrganizedOutsidePressZone
    | OrganizedDepthThreat
    | OrganizedUnavailable

[<Struct>]
type OrganizedDefenseEvidence =
    { Side: ClubSide
      Reason: OrganizedDefenseReason
      Primary: PlayerId option
      Cover: PlayerId option }

type ObservationData =
    | AttackEvaluated of ClubSide * uint64 * AttackPattern * context: int * geometry: float * learned: float * uncertainty: float
    | AttackResolved of ClubSide * uint64 * AttackEnd * reward: float * connections: int * progress: float * exposure: float
    | AttackCooperation of actor: PlayerId * episode: uint64 * target: int * bias: float
    | DecisionChosen of DecisionEvidence
    | DecisionAlternatives of actor: PlayerId * ObservationOption list
    | ImpulseApplied of BallImpulse
    | ActionRejected of actor: PlayerId * ExecutionRejection
    | ProbabilityResolved of
        kind: ProbabilityResolutionKind *
        action: ActionId option *
        probability: float *
        succeeded: bool
    | RefereeAssessed of incident: MatchEvent * penalty: bool * advantage: bool
    | RestartCreated of RestartAward
    | EventCommitted of MatchEvent
    | CommandEffective of manager: uint64 * sequence: uint64
    | TacticsEffective of manager: uint64 * sequence: uint64 * TacticsConfig
    | TransitionEvaluated of TransitionDecisionEvidence
    | TransitionEnded of ClubSide * EventId * TransitionEnd * TransitionReason
    | OrganizedDefenseEvaluated of OrganizedDefenseEvidence

[<Struct>]
type ObservationEntry =
    { Id: ObservationId
      Tick: int<subtick>
      Cause: ObservationCause voption
      Data: ObservationData }

type ObservationSnapshot =
    { Entries: ObservationEntry[]
      Capacity: int
      Evicted: uint64
      Unlinked: uint64
      Total: uint64
      Enabled: bool }

type ObservationChain =
    { Entries: ObservationEntry[]
      MissingCauses: ObservationCause[]
      Cycles: ObservationId[]
      FutureCauses: (ObservationId * ObservationId)[] }

type ObservationAudit =
    { DuplicateOccurrences: ObservationId[]
      MissingCauses: (ObservationId * ObservationCause)[]
      AmbiguousCauses: (ObservationId * ObservationCause)[]
      FutureCauses: (ObservationId * ObservationId)[]
      Uncaused: ObservationId[] }

/// What cognition records while it runs beside the body, replayed into the match record at the tick barrier.
type private DeferredObservation =
    | DeferredRecord of tick: int<subtick> * cause: ObservationCause voption * data: ObservationData * provisional: uint64
    | DeferredDecision of tick: int<subtick> * decision: DecisionEvidence
    | DeferredAlternatives of tick: int<subtick> * actor: PlayerId * options: ObservationOption[]
    | DeferredAssociate of event: MatchEvent * cause: ObservationCause
    | DeferredAssociateDefault of event: MatchEvent * cause: ObservationCause

/// Only the simulation owner writes; immutable snapshots cross the worker boundary. A buffer created with
/// `Deferred` is cognition's journal: it records nothing itself and replays into its target on `Flush`, so the
/// match record has one order whichever thread cognition ran on.
type ObservationBuffer private (capacity: int, target: ObservationBuffer option) =
    // Ids a journal hands out before replay; the high bit keeps them apart from any real id.
    static let provisionalBit = 0x8000000000000000UL
    let capacity = max 1 capacity
    let deferred = ResizeArray<DeferredObservation>()
    let assigned = System.Collections.Generic.Dictionary<uint64, ObservationId>()
    let mutable provisional = 0UL
    let entries: ObservationEntry voption[] = Array.create capacity ValueNone
    let pendingDecisions = Dictionary<PlayerId, ObservationId>()
    let eventCauses = Dictionary<EventId, ObservationCause>()
    let mutable preparedImpulse: (ActionId * ObservationCause voption) option = None
    let mutable total = 0UL
    let mutable count = 0
    let mutable unlinked = 0UL
    let mutable enabled = true
    let mutable actionCause = ValueNone
    let mutable currentEvent = ValueNone
    let mutable selection: ObservationSelection option = None

    new(capacity: int) = ObservationBuffer(capacity, None)

    /// A journal for `target`: it reads the target's switches but never writes it before `Flush`.
    static member Deferred(target: ObservationBuffer) = ObservationBuffer(1, Some target)

    member _.Enabled
        with get () =
            match target with
            | Some owner -> owner.Enabled
            | None -> enabled
        and set value =
            enabled <- value
            pendingDecisions.Clear()
            eventCauses.Clear()
            actionCause <- ValueNone
            currentEvent <- ValueNone
            preparedImpulse <- None

    member _.Count = count
    member _.Total = total
    member _.Capacity = capacity

    member _.SelectDetail(value: ObservationSelection option) =
        match value with
        | Some item when item.FromTick < 0<subtick> || item.UntilTick < item.FromTick ->
            Error "Observation interval must be non-negative and ordered."
        | _ ->
            selection <- value
            Ok()

    member _.WantsDetail(tick, actor) =
        match target with
        | Some owner -> owner.WantsDetail(tick, actor)
        | None ->
            enabled
            && match selection with
               | Some item -> item.Actor = actor && tick >= item.FromTick && tick <= item.UntilTick
               | None -> false

    member this.Alternatives(tick, actor, options: ObservationOption[]) =
        if target.IsSome then
            if this.WantsDetail(tick, actor) then
                deferred.Add(DeferredAlternatives(tick, actor, options))
        elif this.WantsDetail(tick, actor) then
            let cause =
                match pendingDecisions.TryGetValue actor with
                | true, id -> ValueSome(DecisionCause id)
                | _ -> ValueNone

            this.Record(tick, cause, DecisionAlternatives(actor, Array.toList options))
            |> ignore

    member this.Record(tick, cause, data) =
        if target.IsSome then
            if this.Enabled then
                provisional <- provisional + 1UL
                deferred.Add(DeferredRecord(tick, cause, data, provisional))
                ValueSome(ObservationId(provisionalBit ||| provisional))
            else
                ValueNone
        elif enabled then
            total <- total + 1UL
            let id = ObservationId total

            entries[int ((total - 1UL) % uint64 capacity)] <-
                ValueSome
                    { Id = id
                      Tick = tick
                      Cause = cause
                      Data = data }

            count <- min capacity (count + 1)
            ValueSome id
        else
            ValueNone

    member this.Decision(tick, decision: DecisionEvidence) =
        if target.IsSome then
            if this.Enabled then
                deferred.Add(DeferredDecision(tick, decision))
        elif enabled then
            match this.Record(tick, ValueNone, DecisionChosen decision) with
            | ValueSome id ->
                // Observation state cannot grow with duration or arbitrary external player IDs.
                if
                    pendingDecisions.Count >= capacity
                    && not (pendingDecisions.ContainsKey decision.Actor)
                then
                    unlinked <- unlinked + uint64 pendingDecisions.Count
                    pendingDecisions.Clear()

                pendingDecisions[decision.Actor] <- id
            | ValueNone -> ()

    member _.BeginAction actor =
        actionCause <-
            match pendingDecisions.TryGetValue actor with
            | true, id ->
                pendingDecisions.Remove actor |> ignore
                ValueSome(DecisionCause id)
            | _ -> ValueNone

    member _.EndAction() = actionCause <- ValueNone

    member this.RejectAction(tick, actor, rejection) =
        this.Record(tick, actionCause, ActionRejected(actor, rejection))

    member this.Execution(tick, data) = this.Record(tick, actionCause, data)

    member _.BeginEvent(event: MatchEvent) =
        if enabled then
            currentEvent <- ValueSome(EventCause(MatchEvent.id event))

    member _.EndEvent() = currentEvent <- ValueNone
    member _.CurrentEvent = currentEvent

    member this.Associate(event: MatchEvent, cause: ObservationCause) =
        if target.IsSome then
            if this.Enabled then
                deferred.Add(DeferredAssociate(event, cause))
        elif enabled then
            let id = MatchEvent.id event

            if eventCauses.Count >= capacity && not (eventCauses.ContainsKey id) then
                unlinked <- unlinked + uint64 eventCauses.Count
                eventCauses.Clear()

            eventCauses[id] <- cause

    member this.AssociateDefault(event: MatchEvent, cause: ObservationCause) =
        if target.IsSome then
            if this.Enabled then
                deferred.Add(DeferredAssociateDefault(event, cause))
        elif enabled && not (eventCauses.ContainsKey(MatchEvent.id event)) then
            this.Associate(event, cause)

    /// Replays a journal into its target in recording order, giving provisional ids their real ones.
    member _.Flush() =
        match target with
        | None -> ()
        | Some owner ->
            if deferred.Count > 0 then
                let remapId (ObservationId value as id) =
                    if value &&& provisionalBit = 0UL then
                        ValueSome id
                    else
                        match assigned.TryGetValue(value &&& ~~~provisionalBit) with
                        | true, id -> ValueSome id
                        | _ -> ValueNone

                // A cause pointing at a journal entry the owner refused to record has nothing left to point at.
                let remap cause =
                    match cause with
                    | DecisionCause id -> remapId id |> ValueOption.map DecisionCause
                    | ResolutionCause id -> remapId id |> ValueOption.map ResolutionCause
                    | other -> ValueSome other

                for entry in deferred do
                    match entry with
                    | DeferredRecord(tick, cause, data, key) ->
                        match owner.Record(tick, cause |> ValueOption.bind remap, data) with
                        | ValueSome id -> assigned[key] <- id
                        | ValueNone -> ()
                    | DeferredDecision(tick, decision) -> owner.Decision(tick, decision)
                    | DeferredAlternatives(tick, actor, options) -> owner.Alternatives(tick, actor, options)
                    | DeferredAssociate(event, cause) -> remap cause |> ValueOption.iter (fun c -> owner.Associate(event, c))
                    | DeferredAssociateDefault(event, cause) ->
                        remap cause |> ValueOption.iter (fun c -> owner.AssociateDefault(event, c))

                deferred.Clear()
                assigned.Clear()

            provisional <- 0UL

    member _.ConsumeCause(event: MatchEvent) =
        match eventCauses.TryGetValue(MatchEvent.id event) with
        | true, cause ->
            eventCauses.Remove(MatchEvent.id event) |> ignore
            ValueSome cause
        | _ -> ValueNone

    member _.PrepareImpulse(id) =
        if enabled then
            preparedImpulse <- Some(id, actionCause)

    member _.CancelImpulse() = preparedImpulse <- None

    member this.Impulse(tick, impulse) =
        if enabled then
            let cause =
                match impulse.RestartId with
                | Some id -> ValueSome(RestartReference id)
                | None ->
                    match preparedImpulse with
                    | Some(id, cause) when id = impulse.ActionId -> cause
                    | _ -> actionCause

            this.Record(tick, cause, ImpulseApplied impulse) |> ignore
            preparedImpulse <- None

    member _.SnapshotSince(after: uint64) =
        let retainedCount = min (uint64 count) (total - min after total) |> int
        let first = total - uint64 retainedCount
        let snapshot = Array.zeroCreate<ObservationEntry> retainedCount

        for index = 0 to retainedCount - 1 do
            match entries[int ((first + uint64 index) % uint64 capacity)] with
            | ValueSome entry -> snapshot[index] <- entry
            | ValueNone -> ()

        { Entries = snapshot
          Capacity = capacity
          Evicted = total - uint64 count
          Unlinked = unlinked
          Total = total
          Enabled = enabled }

    member this.Snapshot() = this.SnapshotSince 0UL

module Observation =
    let merge (previous: ObservationSnapshot option) (publication: ObservationSnapshot) =
        match previous with
        | Some retained when retained.Total <= publication.Total ->
            let oldest = publication.Total - min publication.Total (uint64 publication.Capacity)

            let entries =
                Array.append retained.Entries publication.Entries
                |> Array.filter (fun entry ->
                    let (ObservationId id) = entry.Id
                    id > oldest)
                |> Array.distinctBy _.Id

            { publication with Entries = entries }
        | Some retained -> retained
        | None -> publication

    /// Text is deliberately built by consumers, never by the physical tick.
    let explain (entry: ObservationEntry) =
        match entry.Data with
        | AttackEvaluated(side, episode, pattern, context, geometry, learned, uncertainty) ->
            sprintf "Attack %A #%d %A context %d: geometry %.3f, learned value %.3f, uncertainty %.3f. A feasible opportunity, not a promised pass." side episode pattern context geometry learned uncertainty
        | AttackResolved(side, episode, reason, reward, connections, progress, exposure) ->
            sprintf "Attack %A #%d ended %A: realized reward %.3f, connections %d, progress %.2fm, exposure %.3f. One bounded learning sample." side episode reason reward connections progress exposure
        | AttackCooperation(actor, episode, target, bias) ->
            sprintf "%A cooperates with attack #%d: feasible target slot %d receives bounded utility %.3f; autonomous alternatives remain." actor episode target bias
        | TransitionEvaluated decision ->
            sprintf "Transition %A (%A): %A because %A. Arrival %.2fs/window %.2fs; cover %.2fs; ready local players %d versus %d opponents; unprotected depth runners %d. First %A, cover %A. Viability is a commitment, not a promised recovery."
                decision.Episode decision.Side decision.Mode decision.Reason (float decision.Arrival) (float decision.ArrivalWindow)
                (float decision.CoverArrival) decision.LocalPlayers decision.Opponents decision.DepthRunners decision.Primary decision.Cover
        | TransitionEnded(side, episode, outcome, reason) ->
            sprintf "Transition %A (%A) finished: %A; last mode rationale %A. Recovery requires committed possession; stabilization requires actual goal-side arrival held over successive evaluations." episode side outcome reason
        | OrganizedDefenseEvaluated decision ->
            sprintf "Organized defense %A: %A; first %A, cover %A. Assigned movement is not an immediate recovery." decision.Side decision.Reason decision.Primary decision.Cover
        | DecisionChosen decision ->
            let action =
                match decision.ActionCode with
                | 1 -> "pass"
                | 2 -> "shot"
                | 3 -> "duel"
                | 4 -> "clearance"
                | 5 -> "cross"
                | _ -> "unknown action"

            if decision.OptionCount = 0 then
                sprintf
                    "%A had no evaluated option; current fallback selected %s. This is not an evaluated utility preference."
                    decision.Actor
                    action
            else
                sprintf
                    "%A chose %s (target slot %d) from %d options using utility-weighted sampling (value %.3f, temperature %.3f, draw %.3f). Pressure %d; condition %.2f, cognition %.2f. Choice is not a guarantee of execution."
                    decision.Actor
                    action
                    decision.TargetSlot
                    decision.OptionCount
                    decision.ChosenValue
                    decision.Temperature
                    decision.Sample
                    decision.Pressure
                    decision.Condition
                    decision.Cognitive
        | DecisionAlternatives(actor, options) ->
            sprintf
                "Selective capture for %A: evaluated options %A. Values are the decision model's estimates, not future outcomes."
                actor
                options
        | ImpulseApplied impulse ->
            sprintf
                "%A applied %A, action %A, from (%.2f, %.2f, %.2f)."
                impulse.ActorId
                impulse.StrikeKind
                impulse.ActionId
                (float impulse.Origin.X)
                (float impulse.Origin.Y)
                (float impulse.Origin.Z)
        | ActionRejected(actor, PassDistanceLimit(distance, limit)) ->
            sprintf
                "%A did not execute the pass: measured distance %.2f m exceeded the current execution limit %.2f m. No impulse was applied."
                actor
                (float distance)
                (float limit)
        | ActionRejected(actor, NoCrossTarget) ->
            sprintf
                "%A attempted a cross but the current target search found no eligible teammate in its target region. No impulse was applied; CrossAttempt is intention, not a consumed physical contact."
                actor
        | ActionRejected(actor, DuelCarrierUnavailable) ->
            sprintf
                "%A did not execute the duel: the actor did not actively control a reachable ball. No duel draw, physical contact or sporting consequence was produced."
                actor
        | ProbabilityResolved(kind, action, probability, succeeded) ->
            sprintf
                "Current model resolved %A with probability %.3f: success=%b, action=%A. This is a stochastic adjudication, not measured physical contact."
                kind
                probability
                succeeded
                action
        | RefereeAssessed(incident, penalty, advantage) ->
            sprintf
                "Referee assessed %s: penalty-area=%b, advantage=%b. These are the current model's assessments, not certification of legal correctness."
                (MatchEvent.describe incident)
                penalty
                advantage
        | RestartCreated award ->
            sprintf
                "%A awarded to %A at (%.2f, %.2f), restart %A. Recorded football cause: %A."
                award.Kind
                award.Side
                (float award.X)
                (float award.Y)
                award.Id
                award.Cause
        | EventCommitted event -> sprintf "%s. Recorded payload: %A" (MatchEvent.describe event) event.Data
        | CommandEffective(manager, sequence) ->
            sprintf
                "Manager %d command #%d became effective at tick %d; tactical consequences require subsequent evidence."
                manager
                sequence
                (int entry.Tick)
        | TacticsEffective(manager, sequence, tactics) ->
            sprintf
                "Manager %d effective instruction #%d: formation %s, mentality %.2f, line %.2f metres, press %.2f, width %.2f, tempo %.2f, directness %.2f. Effective tactics feed team and player decisions; the executor does not yet apply all instruction fields to formation roles. Player response requires subsequent evidence."
                manager
                sequence
                tactics.Formation
                tactics.Mentality
                tactics.DefensiveLine
                tactics.PressIntensity
                tactics.Width
                tactics.Tempo
                tactics.Directness

    let actor (entry: ObservationEntry) =
        match entry.Data with
        | AttackCooperation(actor, _, _, _) -> Some actor
        | DecisionChosen decision -> Some decision.Actor
        | DecisionAlternatives(actor, _) -> Some actor
        | ImpulseApplied impulse -> Some impulse.ActorId
        | ActionRejected(actor, _) -> Some actor
        | EventCommitted { Data = EventData.FootContact(actor, _, _, _, _, _) }
        | EventCommitted { Data = EventData.FootStrikeCancelled actor } -> Some actor
        | _ -> None

    let actionId (entry: ObservationEntry) =
        match entry.Data with
        | ImpulseApplied impulse -> Some impulse.ActionId
        | ProbabilityResolved(_, action, _, _) -> action
        | _ -> None

    let private references cause =
        match cause with
        | ValueSome(PairedEvents(incident, trigger)) -> [| EventCause incident; EventCause trigger |] |> Array.distinct
        | ValueSome source -> [| source |]
        | ValueNone -> [||]

    let private indexSources (snapshot: ObservationSnapshot) =
        let sources = Dictionary<ObservationCause, ResizeArray<ObservationEntry>>()

        let addSource key entry =
            match sources.TryGetValue key with
            | true, rows -> rows.Add entry
            | _ -> sources[key] <- ResizeArray([ entry ])

        for entry in snapshot.Entries do
            addSource (DecisionCause entry.Id) entry
            addSource (ResolutionCause entry.Id) entry

            match entry.Data with
            | ImpulseApplied impulse -> addSource (ActionCause impulse.ActionId) entry
            | EventCommitted event -> addSource (EventCause(MatchEvent.id event)) entry
            | RestartCreated award -> addSource (RestartReference award.Id) entry
            | _ -> ()

        sources

    let private resolve (sources: Dictionary<ObservationCause, ResizeArray<ObservationEntry>>) cause =
        match sources.TryGetValue cause with
        | true, rows -> rows.ToArray()
        | _ -> [||]

    let causes (snapshot: ObservationSnapshot) (entry: ObservationEntry) =
        let sources = indexSources snapshot

        references entry.Cause
        |> Array.collect (resolve sources)
        |> Array.distinctBy _.Id

    let query (filter: ObservationQuery) (snapshot: ObservationSnapshot) =
        if filter.FromTick < 0<subtick> || filter.UntilTick < filter.FromTick then
            Error "Observation query interval must be non-negative and ordered."
        else
            let sources =
                if filter.Actor.IsSome || filter.Action.IsSome || filter.Event.IsSome then
                    indexSources snapshot
                else
                    Dictionary<ObservationCause, ResizeArray<ObservationEntry>>()
            // Only retained explicit ancestry participates; an evicted link cannot establish identity.
            let ancestryMatches predicate entry =
                let pending = Stack<ObservationEntry>()
                let visited = HashSet<ObservationId>()
                pending.Push entry
                let mutable matched = false

                while pending.Count > 0 && not matched do
                    let current = pending.Pop()

                    if visited.Add current.Id then
                        matched <- predicate current

                        if not matched then
                            for cause in references current.Cause do
                                match sources.TryGetValue cause with
                                | true, rows ->
                                    for source in rows do
                                        if source.Tick <= current.Tick then
                                            pending.Push source
                                | _ -> ()

                matched

            snapshot.Entries
            |> Array.filter (fun entry ->
                let matchesActor =
                    filter.Actor
                    |> Option.forall (fun selected ->
                        ancestryMatches (fun candidate -> actor candidate = Some selected) entry)

                let matchesAction =
                    filter.Action
                    |> Option.forall (fun selected ->
                        ancestryMatches
                            (fun candidate ->
                                actionId candidate = Some selected
                                || candidate.Cause = ValueSome(ActionCause selected))
                            entry)

                let matchesEvent =
                    filter.Event
                    |> Option.forall (fun selected ->
                        ancestryMatches
                            (fun candidate ->
                                match candidate.Data with
                                | EventCommitted event -> MatchEvent.id event = selected
                                | RefereeAssessed(event, _, _) -> MatchEvent.id event = selected
                                | _ -> candidate.Cause = ValueSome(EventCause selected))
                            entry)

                entry.Tick >= filter.FromTick
                && entry.Tick <= filter.UntilTick
                && matchesActor
                && matchesAction
                && matchesEvent)
            |> Ok

    // Diagnostic readers must terminate on malformed archives without turning later evidence into a cause.
    let chain (id: ObservationId) (snapshot: ObservationSnapshot) =
        match snapshot.Entries |> Array.tryFind (fun entry -> entry.Id = id) with
        | None -> Error "Observation occurrence is outside the captured window."
        | Some root ->
            let sourceIndex = indexSources snapshot
            let pending = Stack<ObservationEntry * bool>()
            let active = HashSet<ObservationId>()
            let completed = HashSet<ObservationId>()
            let entries = ResizeArray<ObservationEntry>()
            let missing = ResizeArray<ObservationCause>()
            let cycles = ResizeArray<ObservationId>()
            let future = ResizeArray<ObservationId * ObservationId>()
            pending.Push(root, false)

            while pending.Count > 0 do
                let current, exiting = pending.Pop()

                if exiting then
                    active.Remove current.Id |> ignore
                    completed.Add current.Id |> ignore
                elif active.Contains current.Id then
                    cycles.Add current.Id
                elif not (completed.Contains current.Id) then
                    active.Add current.Id |> ignore
                    entries.Add current
                    pending.Push(current, true)

                    let sources =
                        references current.Cause
                        |> Array.collect (resolve sourceIndex)
                        |> Array.distinctBy _.Id

                    for cause in references current.Cause do
                        if not (sourceIndex.ContainsKey cause) then
                            missing.Add cause

                    for index = sources.Length - 1 downto 0 do
                        let source = sources[index]

                        if source.Tick > current.Tick then
                            future.Add(current.Id, source.Id)
                        else
                            pending.Push(source, false)

            Ok
                { Entries = entries.ToArray()
                  MissingCauses = missing.ToArray() |> Array.distinct
                  Cycles = cycles.ToArray() |> Array.distinct
                  FutureCauses = future.ToArray() |> Array.distinct }

    // Whole-window diagnostics expose defects that representative successful chains can hide.
    let audit (snapshot: ObservationSnapshot) =
        let sources = indexSources snapshot
        let missing = ResizeArray<ObservationId * ObservationCause>()
        let ambiguous = ResizeArray<ObservationId * ObservationCause>()
        let future = ResizeArray<ObservationId * ObservationId>()

        for entry in snapshot.Entries do
            for cause in references entry.Cause do
                match sources.TryGetValue cause with
                | false, _ -> missing.Add(entry.Id, cause)
                | true, rows ->
                    if rows.Count > 1 then
                        ambiguous.Add(entry.Id, cause)

                    for source in rows do
                        if source.Tick > entry.Tick then
                            future.Add(entry.Id, source.Id)

        { DuplicateOccurrences =
            snapshot.Entries
            |> Array.countBy _.Id
            |> Array.choose (fun (id, count) -> if count > 1 then Some id else None)
          MissingCauses = missing.ToArray() |> Array.distinct
          AmbiguousCauses = ambiguous.ToArray() |> Array.distinct
          FutureCauses = future.ToArray() |> Array.distinct
          Uncaused =
            snapshot.Entries
            |> Array.choose (fun entry -> if entry.Cause = ValueNone then Some entry.Id else None) }

    let private exportLinks cause =
        let link role kind id = {| Role = role; Kind = kind; Id = id |}

        match cause with
        | ValueNone -> [||]
        | ValueSome(DecisionCause(ObservationId id)) -> [| link "source" "decision" id |]
        | ValueSome(ResolutionCause(ObservationId id)) -> [| link "source" "resolution" id |]
        | ValueSome(ActionCause(ActionId id)) -> [| link "source" "action" id |]
        | ValueSome(EventCause(EventId id)) -> [| link "source" "event" id |]
        | ValueSome(RestartReference(RestartId id)) -> [| link "source" "restart" id |]
        | ValueSome(PairedEvents(incident, trigger)) ->
            [| link "incident" "event" (EventId.value incident)
               link "trigger" "event" (EventId.value trigger) |]

    let private exportRows (entries: ObservationEntry[]) =
        entries
        |> Array.map (fun entry ->
            let (ObservationId id) = entry.Id

            {| Id = id
               Tick = int entry.Tick
               Causes = exportLinks entry.Cause
               Explanation = explain entry |})

    /// This export carries explicit links and loss counters; it is an inspection format, not a checkpoint.
    let exportJson (snapshot: ObservationSnapshot) =
        System.Text.Json.JsonSerializer.Serialize(
            {| FormatVersion = 3
               Enabled = snapshot.Enabled
               Total = snapshot.Total
               Evicted = snapshot.Evicted
               Unlinked = snapshot.Unlinked
               Entries = exportRows snapshot.Entries |},
            System.Text.Json.JsonSerializerOptions(WriteIndented = true)
        )

    let exportChainJson (root: ObservationId) (snapshot: ObservationSnapshot) =
        chain root snapshot
        |> Result.map (fun result ->
            let (ObservationId rootId) = root
            let missing = result.MissingCauses |> Array.collect (ValueSome >> exportLinks)
            let cycles = result.Cycles |> Array.map (fun (ObservationId id) -> id)

            let future =
                result.FutureCauses
                |> Array.map (fun (ObservationId child, ObservationId source) -> {| Child = child; Source = source |})

            System.Text.Json.JsonSerializer.Serialize(
                {| FormatVersion = 3
                   Root = rootId
                   Enabled = snapshot.Enabled
                   Total = snapshot.Total
                   Evicted = snapshot.Evicted
                   Unlinked = snapshot.Unlinked
                   Entries = exportRows result.Entries
                   MissingCauses = missing
                   Cycles = cycles
                   FutureCauses = future |},
                System.Text.Json.JsonSerializerOptions(WriteIndented = true)
            ))

    let exportAuditJson (snapshot: ObservationSnapshot) =
        let result = audit snapshot

        let uncausedKinds =
            snapshot.Entries
            |> Array.filter (fun entry -> entry.Cause = ValueNone)
            |> Array.countBy (fun entry ->
                match entry.Data with
                | AttackEvaluated _ -> "attack-evaluation"
                | AttackResolved _ -> "attack-resolution"
                | AttackCooperation _ -> "attack-cooperation"
                | DecisionChosen _ -> "decision"
                | DecisionAlternatives _ -> "alternatives"
                | ImpulseApplied _ -> "impulse"
                | ActionRejected _ -> "rejection"
                | ProbabilityResolved _ -> "probability-resolution"
                | RefereeAssessed _ -> "referee-assessment"
                | RestartCreated _ -> "restart"
                | EventCommitted event -> "event:" + string (MatchEvent.kind event)
                | CommandEffective _ -> "command"
                | TacticsEffective _ -> "tactics"
                | TransitionEvaluated _ -> "transition-decision"
                | TransitionEnded _ -> "transition-end"
                | OrganizedDefenseEvaluated _ -> "organized-defense")
            |> Array.sortBy fst
            |> Array.map (fun (kind, count) -> {| Kind = kind; Count = count |})

        let ids entries =
            entries |> Array.map (fun (ObservationId id) -> id)

        let edges entries =
            entries
            |> Array.collect (fun (ObservationId child, cause) ->
                exportLinks (ValueSome cause)
                |> Array.map (fun source -> {| Child = child; Source = source |}))

        System.Text.Json.JsonSerializer.Serialize(
            {| FormatVersion = 3
               Enabled = snapshot.Enabled
               Total = snapshot.Total
               Retained = snapshot.Entries.Length
               Evicted = snapshot.Evicted
               Unlinked = snapshot.Unlinked
               DuplicateOccurrences = ids result.DuplicateOccurrences
               MissingCauses = edges result.MissingCauses
               AmbiguousCauses = edges result.AmbiguousCauses
               FutureCauses =
                result.FutureCauses
                |> Array.map (fun (ObservationId child, ObservationId source) -> {| Child = child; Source = source |})
               Uncaused = ids result.Uncaused
               UncausedKinds = uncausedKinds |},
            System.Text.Json.JsonSerializerOptions(WriteIndented = true)
        )
