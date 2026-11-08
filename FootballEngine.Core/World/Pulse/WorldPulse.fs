namespace FootballEngine.World.Pulse

open System
open FootballEngine.Domain

module WorldPulse =

    /// `weekly` is supplied by the scheduler's own weekly cadence so the pulse never second-guesses it.
    let runWithCadence
        (detectors: PulseDetector list)
        (weekly: bool)
        (config: WorldPulseConfig)
        (date: DateTime)
        (gs: GameState)
        : GameState =
        // The ledger runs first so streak detectors read the result of the very match they report.
        let gs = ResultsLedger.apply config date gs
        let fresh = MatchFacts.newlyPlayed date gs
        // Pressure shares the ledger's fresh set, so a fixture moves confidence exactly once.
        let gs, pressureNews = Pressure.apply config date fresh gs

        let gs, milestoneNews = PlayerStories.milestones config date gs
        // Transfers settle before the weekly pass so a player who just moved is not judged on his old club.
        let gs, marketNews = MarketStories.apply config date gs

        let gs, weeklyNews =
            if weekly then
                PlayerStories.weekly config date gs
            else
                gs, []

        let candidates =
            pressureNews
            @ (detectors |> List.collect (fun detect -> detect gs date))
            @ milestoneNews
            @ marketNews
            @ weeklyNews

        NewsDesk.publish config date candidates gs |> MatchFacts.markProcessed fresh

    let runWith (detectors: PulseDetector list) (config: WorldPulseConfig) (date: DateTime) (gs: GameState) =
        runWithCadence detectors false config date gs

    let runOn (weekly: bool) (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : GameState =
        runWithCadence (MatchStories.detectors config) weekly config date gs

    let run (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : GameState =
        runOn false config date gs
