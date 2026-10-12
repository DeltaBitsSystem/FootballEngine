namespace FootballEngine.Client.Animation

open System.Threading
open FootballEngine.Core.Match

/// Archive ownership is separate from both the GPU and the canonical live planner.
type MotionReplaySource internal (config, identity, track: byte[], checkpoints: byte[]) =
    member _.Reconstruct(tick, cancellation: CancellationToken) =
        MotionCheckpointArchive.preceding config identity checkpoints tick
        |> Result.bind (fun checkpoint ->
            MotionReconstruction.rebuild config identity checkpoint tick (MotionTrack.readBlock track) cancellation)

module MotionReplay =
    let load config identity (replay: Replay.ReplayData) =
        if replay.Meta.Version <> Replay.formatVersion then
            Error "motion replay requires the current replay format"
        elif
            identity.PlannerVersion <> MotionIdentity.plannerVersion
            || identity.ConfigHash <> MotionIdentity.configHash config
        then
            Error "motion replay planner/config identity does not match the active implementation"
        elif
            replay.Sections.Length <> 2
            || (replay.Sections |> Array.exists (fun section -> section.Version <> 1))
        then
            Error "motion replay requires exactly one current track and checkpoint section"
        else
            let tracks = replay.Sections |> Array.filter (fun section -> section.Kind = 1u)
            let checkpoints = replay.Sections |> Array.filter (fun section -> section.Kind = 2u)

            if tracks.Length <> 1 || checkpoints.Length <> 1 then
                Error "motion replay section identities are missing or duplicated"
            else
                AnimationConfig.validate config
                |> Result.bind (fun () -> MotionTrack.tickDuration tracks[0].Payload)
                |> Result.bind (fun seconds ->
                    if seconds <> config.TickSeconds then
                        Error "motion track timebase is incompatible with the pinned planner"
                    else
                        MotionTrack.readBlock tracks[0].Payload 0<subtick> |> Result.map ignore)
                |> Result.bind (fun () ->
                    MotionCheckpointArchive.preceding config identity checkpoints[0].Payload 0
                    |> Result.map ignore)
                |> Result.map (fun () ->
                    MotionReplaySource(
                        config,
                        identity,
                        Array.copy tracks[0].Payload,
                        Array.copy checkpoints[0].Payload
                    ))
