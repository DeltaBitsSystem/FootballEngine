namespace FootballEngine.Core.Match

type SystemContext =
    { Ctx: MatchContext
      Clock: SimulationClock }

type EventBuffer =
    { mutable Events: ResizeArray<MatchEvent>
      mutable Flags: EventFlags
      mutable Applied: ResizeArray<MatchEvent> }

/// All systems share this signature. No system mutates anything outside World/EventBuffer.
/// No system reads global state. No system calls another system.
type SystemStep = SystemContext -> Rng -> World -> EventBuffer -> Rng

type Trigger =
    | EveryTick
    | EveryN of int<tickDelta>
    | EveryMinute of period: int<matchMin> * offset: int<matchMin>
    | OnEvent of EventFlags
    | OnEventOrEveryN of EventFlags * int<tickDelta>
    | WhenLive of Trigger

type SystemSpec =
    { Name: string
      Triggers: Trigger list
      Step: SystemStep }
