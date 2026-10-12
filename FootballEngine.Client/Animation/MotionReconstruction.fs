namespace FootballEngine.Client.Animation

open System.Threading
open FootballEngine.Core.Match

module MotionReconstruction =
    /// Work is isolated from the published runtime; cancellation cannot partially replace live state.
    let rebuild
        config
        identity
        (checkpoint: MotionCheckpoint)
        targetTick
        (readBlock: int<subtick> -> Result<MotionBoundary[], string>)
        (cancellation: CancellationToken)
        =
        if
            targetTick < checkpoint.Tick
            || targetTick - checkpoint.Tick >= MotionTrack.checkpointInterval
        then
            Error "seek exceeds its preceding checkpoint interval"
        elif cancellation.IsCancellationRequested then
            Error "motion reconstruction cancelled"
        else
            let runtime = MotionRuntime(config, identity, historical = true)

            match runtime.Restore(checkpoint, runtime.Epoch) with
            | Error error -> Error(sprintf "%A" error)
            | Ok() when targetTick = checkpoint.Tick -> Ok runtime
            | Ok() ->
                match readBlock (targetTick * 1<subtick>) with
                | Error message -> Error message
                | Ok boundaries ->
                    let mutable error = None

                    for boundary in boundaries do
                        if
                            error.IsNone
                            && int boundary.Tick > checkpoint.Tick
                            && int boundary.Tick <= targetTick
                        then
                            if cancellation.IsCancellationRequested then
                                error <- Some "motion reconstruction cancelled"
                            else
                                match runtime.Ingest boundary with
                                | Ok() -> ()
                                | Error value -> error <- Some(sprintf "%A" value)

                    match error with
                    | Some message -> Error message
                    | None when runtime.Frontier <> targetTick ->
                        Error "motion archive cannot reconstruct the requested frontier"
                    | None -> Ok runtime
