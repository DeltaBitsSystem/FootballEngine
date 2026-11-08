namespace FootballEngine.World.Pulse

open System
open FootballEngine.Domain

module WorldPulse =

    let runWith
        (detectors: PulseDetector list)
        (config: WorldPulseConfig)
        (date: DateTime)
        (gs: GameState)
        : GameState =
        // The ledger runs first so streak detectors read the result of the very match they report.
        let gs = ResultsLedger.apply config date gs
        let fresh = MatchFacts.newlyPlayed date gs
        // Pressure shares the ledger's fresh set, so a fixture moves confidence exactly once.
        let gs, pressureNews = Pressure.apply config date fresh gs

        let candidates =
            pressureNews @ (detectors |> List.collect (fun detect -> detect gs date))

        NewsDesk.publish config date candidates gs |> MatchFacts.markProcessed fresh

    let run (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : GameState =
        runWith (MatchStories.detectors config) config date gs
