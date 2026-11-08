namespace FootballEngine.World.Pulse

open System
open FootballEngine.Domain

module WorldPulse =

    let runWith (detectors: PulseDetector list) (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : GameState =
        // The ledger runs first so streak detectors read the result of the very match they report.
        let gs = ResultsLedger.apply config date gs
        let candidates = detectors |> List.collect (fun detect -> detect gs date)
        let fresh = MatchFacts.newlyPlayed date gs

        NewsDesk.publish config date candidates gs
        |> MatchFacts.markProcessed fresh

    let run (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : GameState =
        runWith (MatchStories.detectors config) config date gs
