namespace FootballEngine

open FootballEngine.Simulation
open FootballEngine.AppTypes
open AppMsgs

module MatchSlice =

    let update (msg: MatchMsg) (state: MatchState) : MatchState =
        let total =
            state.ActiveReplay
            |> Option.map (fun r -> r.Snapshots.Length - 1)
            |> Option.defaultValue 0

        match msg with
        | MatchMsg.Step delta ->
            { state with
                Snapshot       = max 0 (min total (state.Snapshot + delta))
                Accumulator    = 0.0
                InterpolationT = 0.0 }

        | MatchMsg.Close ->
            { ActiveReplay = None; Snapshot = 0; IsPlaying = false; PlaybackSpeed = 20; Accumulator = 0.0; InterpolationT = 0.0 }

        | MatchMsg.TogglePlay ->
            { state with IsPlaying = not state.IsPlaying }

        | MatchMsg.SetSpeed s ->
            { state with PlaybackSpeed = s }

        | MatchMsg.Tick ->
            if not state.IsPlaying then state
            else
                let dt = 16.67
                let newAcc = state.Accumulator + dt
                let effectiveSpeed =
                    match state.ActiveReplay with
                    | Some replay when state.Snapshot < replay.Snapshots.Length ->
                        match replay.Snapshots[state.Snapshot].Flow with
                        | Types.MatchFlow.Live -> state.PlaybackSpeed
                        | _ -> max 1 (state.PlaybackSpeed / 4)
                    | _ -> state.PlaybackSpeed
                let snapInterval = 125.0 / float effectiveSpeed
                if newAcc >= snapInterval then
                    let newSnap  = min total (state.Snapshot + 1)
                    let finished = newSnap >= total
                    { state with
                        Accumulator    = newAcc - snapInterval
                        InterpolationT = 0.0
                        Snapshot       = newSnap
                        IsPlaying      = not finished }
                else
                    { state with
                        Accumulator    = newAcc
                        InterpolationT = newAcc / snapInterval }
