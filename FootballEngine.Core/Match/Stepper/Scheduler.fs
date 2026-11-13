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

/// Trigger representation used by the tick loop. Lists are resolved once when
/// a match session is created, so the hot path only walks compact arrays.
type CompiledTrigger =
    | EveryTick
    | EveryN of int<tickDelta>
    | EveryMinute of period: int<matchMin> * offset: int<matchMin>
    | OnEvent of EventFlags
    | OnEventOrEveryN of EventFlags * int<tickDelta>
    | OnTransitionOrGeometry of EventFlags
    | WhenLive of CompiledTrigger
    | WhenMovementAllowed of CompiledTrigger
    | WhenBallDead of CompiledTrigger

type CompiledSystem =
    { Name: string
      Side: SystemSide
      /// Keys the system's own random stream; derived from the name so adding or reordering systems does not
      /// reassign another system's numbers.
      StreamKey: uint64
      Triggers: CompiledTrigger[]
      Step: SystemStep }

[<Struct>]
type CompiledSchedule =
    { Systems: CompiledSystem[]
      Cognition: CompiledSystem[]
      Body: CompiledSystem[] }

module ScheduleCompiler =
    let rec private compileTrigger trigger : CompiledTrigger =
        match trigger with
        | Trigger.EveryTick -> CompiledTrigger.EveryTick
        | Trigger.EveryN period -> CompiledTrigger.EveryN period
        | Trigger.EveryMinute(period, offset) -> CompiledTrigger.EveryMinute(period, offset)
        | Trigger.OnEvent flags -> CompiledTrigger.OnEvent flags
        | Trigger.OnEventOrEveryN(flags, period) -> CompiledTrigger.OnEventOrEveryN(flags, period)
        | Trigger.OnTransitionOrGeometry flags -> CompiledTrigger.OnTransitionOrGeometry flags
        | Trigger.WhenLive nested -> CompiledTrigger.WhenLive(compileTrigger nested)
        | Trigger.WhenMovementAllowed nested -> CompiledTrigger.WhenMovementAllowed(compileTrigger nested)
        | Trigger.WhenBallDead nested -> CompiledTrigger.WhenBallDead(compileTrigger nested)

    /// Compilation is cold; the tick loop receives only arrays and precomputed
    /// trigger values.
    let compile (systems: SystemSpec list) : CompiledSchedule =
        let compiled =
            systems
            |> List.map (fun system ->
                { Name = system.Name
                  Side = system.Side
                  StreamKey = Rng.keyOf system.Name
                  Triggers = system.Triggers |> List.map compileTrigger |> Array.ofList
                  Step = system.Step })
            |> Array.ofList

        { Systems = compiled
          Cognition = compiled |> Array.filter (fun system -> system.Side = SystemSide.Cognition)
          Body = compiled |> Array.filter (fun system -> system.Side = SystemSide.Body) }
