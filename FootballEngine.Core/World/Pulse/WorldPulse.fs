namespace FootballEngine.World.Pulse

open System
open FootballEngine.Domain

/// A detector reads confirmed facts and proposes stories; it never mutates state or decides visibility.
type PulseDetector = GameState -> DateTime -> NewsCandidate list

module WorldPulse =

    /// Later phases register detectors here; Phase 1 only wires the pipeline.
    let detectors: PulseDetector list = []

    let runWith (detectors: PulseDetector list) (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : GameState =
        let candidates = detectors |> List.collect (fun detect -> detect gs date)
        NewsDesk.publish config date candidates gs

    let run (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : GameState =
        runWith detectors config date gs
