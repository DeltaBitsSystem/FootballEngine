namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.SchedulingTypes

module Scheduler =

    let buildMatchTime (state: SimState) (clock: SimulationClock) : MatchTime =
        let rawMinute = SimulationClock.toMatchMin clock state.SubTick
        { Subtick = state.SubTick
          Minute  = rawMinute
          Flow    = state.Flow }

    let private flowMatches (matcher: FlowMatcher) (flow: MatchFlow) : bool =
        match matcher, flow with
        | IsLive, Live -> true
        | IsGoalPause, GoalPause _ -> true
        | IsHalfTime, HalfTimePause _ -> true
        | IsRestartDelay, RestartDelay _ -> true
        | IsAnyPause, (GoalPause _ | VARReview _ | InjuryPause _ | RestartDelay _ | HalfTimePause _) -> true
        | IsMatchEnded, (MatchEnded | FullTimeReview) -> true
        | _ -> false

    let private flowOk (matchers: FlowMatcher list) (flow: MatchFlow) : bool =
        matchers |> List.exists (fun m -> flowMatches m flow)

    let private hasEventKind (kinds: SemanticEventKind list) (events: ResizeArray<SemanticEvent>) : bool =
        kinds |> List.exists (fun k ->
            events |> Seq.exists (fun e -> SemanticEvent.kind e = k))

    let private evalInner (inner: SystemFrequency) (time: MatchTime) (events: ResizeArray<SemanticEvent>) : bool =
        match inner with
        | EverySubtick -> true
        | EveryN delta -> int time.Subtick % int delta = 0
        | EveryMinute(period, offset) -> int time.Minute % int period = int offset
        | OnEvent kinds -> hasEventKind kinds events
        | OnEventOrEveryN(kinds, delta) ->
            hasEventKind kinds events || int time.Subtick % int delta = 0
        | WhenFlow _ -> false

    let shouldRun (freq: SystemFrequency) (time: MatchTime) (events: ResizeArray<SemanticEvent>) : bool =
        match freq with
        | EverySubtick -> true
        | EveryN delta -> int time.Subtick % int delta = 0
        | EveryMinute(period, offset) -> int time.Minute % int period = int offset
        | OnEvent kinds -> hasEventKind kinds events
        | OnEventOrEveryN(kinds, delta) ->
            hasEventKind kinds events || int time.Subtick % int delta = 0
        | WhenFlow(matchers, inner) ->
            if flowOk matchers time.Flow then evalInner inner time events
            else false
