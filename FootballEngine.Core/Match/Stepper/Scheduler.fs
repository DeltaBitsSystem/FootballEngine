namespace FootballEngine.Core.Match

type SystemContext =
    { Ctx: MatchContext
      Clock: SimulationClock }

/// All systems share this signature. No system mutates anything outside World/EventBuffer.
/// No system reads global state. No system calls another system.
type SystemStep = SystemContext -> Rng -> World -> EventBuffer -> Rng

type Trigger =
    | EveryTick
    | EveryN of int<tickDelta>
    | EveryMinute of period: int<matchMin> * offset: int<matchMin>
    | OnEvent of EventFlags
    | OnEventOrEveryN of EventFlags * int<tickDelta>
    | OnTransitionOrGeometry of EventFlags
    | WhenLive of Trigger
    | WhenMovementAllowed of Trigger
    /// Goal pauses and VAR reviews: play is stopped but a scored ball still travels into the net.
    | WhenBallDead of Trigger

/// Which half of the tick a system belongs to. Cognition reads the world as the tick began and writes only
/// what it owns; the body reads only what the latch handed it. The two halves can therefore run at once.
[<RequireQualifiedAccess>]
type SystemSide =
    | Cognition
    | Body

type SystemSpec =
    { Name: string
      Side: SystemSide
      Triggers: Trigger list
      Step: SystemStep }

/// What a compiled trigger tests once its flow gate has passed.
type TriggerKind =
    | Always = 0
    | EveryN = 1
    | EveryMinute = 2
    | OnEvent = 3
    | OnEventOrEveryN = 4
    /// The cadence comes from the defensive-transition config at run time, so Period is not fixed at compile time.
    | OnTransitionOrGeometry = 5

/// One flattened trigger: the nested When* gates are intersected into FlowMask (bit = MatchFlow case tag), so the
/// tick loop tests the flow with one AND and the leaf with one compare instead of walking a tree.
[<Struct>]
type CompiledTrigger =
    { FlowMask: int
      Kind: TriggerKind
      Flags: uint64
      Period: int
      Minutes: int<matchMin>
      Offset: int<matchMin> }

module FlowGate =
    let all = 0x7FF
    // Bit numbers are fixed by bitOf below; the three gates only name the flows the old When* wrappers allowed.
    let live = 1 <<< 1
    let movementAllowed = (1 <<< 1) ||| (1 <<< 5)
    let ballDead = (1 <<< 2) ||| (1 <<< 3)

    let bitOf (flow: MatchFlow) : int =
        match flow with
        | MatchFlow.PreMatch -> 1 <<< 0
        | MatchFlow.Live -> 1 <<< 1
        | MatchFlow.GoalPause _ -> 1 <<< 2
        | MatchFlow.VARReview _ -> 1 <<< 3
        | MatchFlow.InjuryPause _ -> 1 <<< 4
        | MatchFlow.Restart _ -> 1 <<< 5
        | MatchFlow.HalfTime _ -> 1 <<< 6
        | MatchFlow.ExtraTimeBreak _ -> 1 <<< 7
        | MatchFlow.FullTimeReview _ -> 1 <<< 8
        | MatchFlow.Shootout -> 1 <<< 9
        | MatchFlow.MatchEnded -> 1 <<< 10

type CompiledSystem =
    { Name: string
      Side: SystemSide
      /// Keys the system's own random stream; derived from the name so adding or reordering systems does not
      /// reassign another system's numbers.
      StreamKey: uint64
      Triggers: CompiledTrigger[]
      Step: SystemStep }

/// Per-tick values the two half loops read. One instance lives as long as the schedule so the loop closures are
/// built once instead of every tick; the tick owner fills the fields before it runs either half. Object fields
/// are options because there is no honest placeholder before the first tick.
[<AllowNullLiteral>]
type TickScratch(cognition: CompiledSystem[], body: CompiledSystem[]) as this =
    let mutable context: SystemContext voption = ValueNone
    let mutable world: World voption = ValueNone
    let mutable buffer: EventBuffer voption = ValueNone
    let mutable seen: World voption = ValueNone
    let mutable cognitionBuffer: EventBuffer voption = ValueNone
    let mutable stepKey = 0UL
    let mutable tick = 0
    let mutable playClock = PlayClock.ofSubTicks 0<subtick>
    let mutable flags = 0UL
    let mutable cadence = 1

    let fires (flowBit: int) (triggers: CompiledTrigger[]) : bool =
        let mutable index = 0
        let mutable matched = false

        while index < triggers.Length && not matched do
            let t = triggers[index]

            if t.FlowMask &&& flowBit <> 0 then
                matched <-
                    match t.Kind with
                    | TriggerKind.Always -> true
                    | TriggerKind.EveryN -> tick % t.Period = 0
                    | TriggerKind.EveryMinute ->
                        PlayClock.value playClock % SimulationClock.minuteSubticks = 0<subtick>
                        && PlayClock.toMatchMinute playClock % t.Minutes = t.Offset
                    | TriggerKind.OnEvent -> flags &&& t.Flags <> 0UL
                    | TriggerKind.OnEventOrEveryN -> flags &&& t.Flags <> 0UL || tick % t.Period = 0
                    | _ -> flags &&& t.Flags <> 0UL || tick % cadence = 0

            index <- index + 1

        matched

    // Each half runs its systems in table order; within a half a system may read what an earlier one wrote.
    let runCognition () =
        let ctx = context.Value
        let seenWorld = seen.Value
        let events = cognitionBuffer.Value

        for spec in cognition do
            // Read per system, as the unflattened loop did: a body system can change the flow mid-tick.
            if fires (FlowGate.bitOf seenWorld.State.Flow) spec.Triggers then
                spec.Step ctx (Rng.stream stepKey spec.StreamKey) seenWorld events |> ignore

    let runBody () =
        let ctx = context.Value
        let live = world.Value
        let events = buffer.Value

        for spec in body do
            if fires (FlowGate.bitOf live.State.Flow) spec.Triggers then
                spec.Step ctx (Rng.stream stepKey spec.StreamKey) live events |> ignore

    let cognitionAction: unit -> unit = runCognition
    let bodyAction: unit -> unit = runBody

    member _.Begin
        (
            ctx: SystemContext,
            key: uint64,
            simTick: int<subtick>,
            clock: PlayClock,
            eventFlags: uint64,
            transitionCadence: int,
            liveWorld: World,
            liveBuffer: EventBuffer,
            seenWorld: World,
            seenBuffer: EventBuffer
        ) =
        context <- ValueSome ctx
        stepKey <- key
        tick <- int simTick
        playClock <- clock
        flags <- eventFlags
        cadence <- transitionCadence
        world <- ValueSome liveWorld
        buffer <- ValueSome liveBuffer
        seen <- ValueSome seenWorld
        cognitionBuffer <- ValueSome seenBuffer

    /// Cognition has to run before the body on one thread and beside it on two; the caller picks the order.
    member _.CognitionAction = cognitionAction
    member _.BodyAction = bodyAction
    member _.RunCognition() = runCognition ()
    member _.RunBody() = runBody ()

[<Struct>]
type CompiledSchedule =
    { Systems: CompiledSystem[]
      Cognition: CompiledSystem[]
      Body: CompiledSystem[]
      Scratch: TickScratch }

module ScheduleCompiler =
    let private leaf mask kind flags period minutes offset =
        { FlowMask = mask
          Kind = kind
          Flags = flags
          Period = period
          Minutes = minutes
          Offset = offset }

    let rec private compileTrigger (mask: int) trigger : CompiledTrigger =
        match trigger with
        | Trigger.EveryTick -> leaf mask TriggerKind.Always 0UL 1 1<matchMin> 0<matchMin>
        | Trigger.EveryN period -> leaf mask TriggerKind.EveryN 0UL (int period) 1<matchMin> 0<matchMin>
        | Trigger.EveryMinute(period, offset) -> leaf mask TriggerKind.EveryMinute 0UL 1 period offset
        | Trigger.OnEvent flags -> leaf mask TriggerKind.OnEvent flags.Bits 1 1<matchMin> 0<matchMin>
        | Trigger.OnEventOrEveryN(flags, period) ->
            leaf mask TriggerKind.OnEventOrEveryN flags.Bits (int period) 1<matchMin> 0<matchMin>
        | Trigger.OnTransitionOrGeometry flags ->
            leaf mask TriggerKind.OnTransitionOrGeometry flags.Bits 1 1<matchMin> 0<matchMin>
        | Trigger.WhenLive nested -> compileTrigger (mask &&& FlowGate.live) nested
        | Trigger.WhenMovementAllowed nested -> compileTrigger (mask &&& FlowGate.movementAllowed) nested
        | Trigger.WhenBallDead nested -> compileTrigger (mask &&& FlowGate.ballDead) nested

    /// Compilation is cold; the tick loop receives only arrays and precomputed
    /// trigger values.
    let compile (systems: SystemSpec list) : CompiledSchedule =
        let compiled =
            systems
            |> List.map (fun system ->
                { Name = system.Name
                  Side = system.Side
                  StreamKey = Rng.keyOf system.Name
                  Triggers = system.Triggers |> List.map (compileTrigger FlowGate.all) |> Array.ofList
                  Step = system.Step })
            |> Array.ofList

        let cognition = compiled |> Array.filter (fun system -> system.Side = SystemSide.Cognition)
        let body = compiled |> Array.filter (fun system -> system.Side = SystemSide.Body)

        { Systems = compiled
          Cognition = cognition
          Body = body
          Scratch = TickScratch(cognition, body) }
