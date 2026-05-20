namespace FootballEngine.TeamOrchestrator

open FootballEngine
open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.ML

type PlanDeviation =
    | OnTrack
    | Drifting of magnitude: float
    | Critical of magnitude: float

module ReactiveLoop =

    let private intendedPressIntensity (state: SimState) (clubSide: ClubSide) : float =
        match SimStateOps.getDirective state clubSide with
        | TeamDirectiveState.Active d -> d.Params.Press.Intensity
        | TeamDirectiveState.Transitioning(_, d, _) -> d.Params.Press.Intensity
        | TeamDirectiveState.Suspended d -> d.Params.Press.Intensity

    let private deviationFor (state: SimState) (clubSide: ClubSide) =
        let w = EngineWeightDefaults.defaults.Collective.ReactiveLoop
        let emergent = SimStateOps.getEmergentState state clubSide
        let frame = SimStateOps.getFrame state clubSide

        let shapeDev = 1.0 - emergent.CompactnessLevel

        let intendedPress = intendedPressIntensity state clubSide
        let pressDev = abs (emergent.PressingIntensity - intendedPress)

        let mutable totalCond = 0
        let mutable active = 0
        for i = 0 to frame.SlotCount - 1 do
            match frame.Physics.Occupancy[i] with
            | OccupancyKind.Active _ ->
                totalCond <- totalCond + int frame.Condition[i]
                active <- active + 1
            | _ -> ()
        let avgCond = if active > 0 then float totalCond / float active else 50.0
        let fatigueDev = 1.0 - avgCond / 100.0

        shapeDev * w.ShapeDevWeight + pressDev * w.PressDevWeight + fatigueDev * w.FatigueDevWeight

    let run (ctx: MatchContext) (state: SimState) (clock: SimulationClock) : PlanDeviation =
        let w = EngineWeightDefaults.defaults.Collective.ReactiveLoop
        let homeDev = deviationFor state HomeClub
        let awayDev = deviationFor state AwayClub
        let totalDeviation = max homeDev awayDev

        if totalDeviation < w.OnTrackThreshold then OnTrack
        elif totalDeviation < w.DriftingThreshold then Drifting totalDeviation
        else Critical totalDeviation
