module FootballEngine.Tests.Infrastructure.Runners

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

let defaultClock = SimulationClock.defaultClock

let advanceTicks (n: int) (ctx: MatchContext) (state: SimState) : SimState * MatchEvent list =
    let allEvents = ResizeArray<MatchEvent>()
    let mutable s = state
    let mutable i = 0
    while i < n && s.Flow <> MatchEnded do
        let result = MatchStepper.updateOne ctx defaultClock [||] s
        s <- result.State
        allEvents.AddRange(result.Events)
        i <- i + 1
    s, allEvents |> Seq.toList

let runUntilEvent
    (predicate: MatchEvent -> bool) (maxTicks: int) (ctx: MatchContext) (state: SimState)
    : (SimState * MatchEvent list) option =
    let allEvents = ResizeArray<MatchEvent>()
    let mutable s = state
    let mutable found = false
    let mutable i = 0
    while i < maxTicks && not found && s.Flow <> MatchEnded do
        let result = MatchStepper.updateOne ctx defaultClock [||] s
        s <- result.State
        allEvents.AddRange(result.Events)
        if result.Events |> Seq.exists predicate then found <- true
        i <- i + 1
    if found then Some(s, allEvents |> Seq.toList) else None

let runUntilFlow
    (predicate: MatchFlow -> bool) (maxTicks: int) (ctx: MatchContext) (state: SimState)
    : (SimState * MatchEvent list) option =
    let allEvents = ResizeArray<MatchEvent>()
    let mutable s = state
    let mutable found = false
    let mutable i = 0
    while i < maxTicks && not found && s.Flow <> MatchEnded do
        let result = MatchStepper.updateOne ctx defaultClock [||] s
        s <- result.State
        allEvents.AddRange(result.Events)
        if predicate s.Flow then found <- true
        i <- i + 1
    if found then Some(s, allEvents |> Seq.toList) else None

let applyOneDomainEvent (domainEvent: DomainEvent) : SimState * MatchEvent list =
    let state = SimState()
    state.Config <- BalanceConfig.defaultConfig
    let n = 1
    let homeFrame = TeamFrame()
    homeFrame.Physics <- PhysicsFrame.init n [| { X = 0.0<meter>; Y = 0.0<meter>; Z = 0.0<meter>; Vx = 0.0<meter / second>; Vy = 0.0<meter / second>; Vz = 0.0<meter / second> } |]
    state.Home <- TeamSimState()
    state.Home.Frame <- homeFrame
    state.Home.ShapeTargetX <- Array.zeroCreate homeFrame.SlotCount
    state.Home.ShapeTargetY <- Array.zeroCreate homeFrame.SlotCount
    let awayFrame = TeamFrame()
    awayFrame.Physics <- PhysicsFrame.init n [| { X = 0.0<meter>; Y = 0.0<meter>; Z = 0.0<meter>; Vx = 0.0<meter / second>; Vy = 0.0<meter / second>; Vz = 0.0<meter / second> } |]
    state.Away <- TeamSimState()
    state.Away.Frame <- awayFrame
    state.Away.ShapeTargetX <- Array.zeroCreate awayFrame.SlotCount
    state.Away.ShapeTargetY <- Array.zeroCreate awayFrame.SlotCount
    let events = ResizeArray<MatchEvent>()
    MatchStepper.applyDomainEvent state events domainEvent
    state, events |> Seq.toList
