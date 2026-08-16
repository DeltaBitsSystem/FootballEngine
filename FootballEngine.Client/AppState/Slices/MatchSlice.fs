namespace FootballEngine

open FootballEngine.Core.Match
open FootballEngine.AppTypes
open AppMsgs

module MatchSlice =

    let private totalOf (replay: Replay.ReplayData) : int = max 0 (replay.Count - 1)

    let update (msg: MatchMsg) (state: MatchState) : MatchState =
        let total = state.ActiveReplay |> Option.map totalOf |> Option.defaultValue 0

        match msg with
        | MatchMsg.Step delta ->
            { state with
                Snapshot = max 0 (min total (state.Snapshot + delta))
                Accumulator = 0.0
                InterpolationT = 0.0 }

        | MatchMsg.Close ->
            { ActiveReplay = None
              HomeClubId = None
              AwayClubId = None
              Snapshot = 0
              IsPlaying = false
              PlaybackSpeed = 20
              Accumulator = 0.0
              InterpolationT = 0.0 }

        | MatchMsg.TogglePlay ->
            { state with
                IsPlaying = not state.IsPlaying }

        | MatchMsg.SetSpeed s -> { state with PlaybackSpeed = s }

        | MatchMsg.Tick ->
            if not state.IsPlaying then
                state
            else
                let dt = 16.67
                let newAcc = state.Accumulator + dt

                let effectiveSpeed =
                    match state.ActiveReplay with
                    | Some replay when state.Snapshot < replay.Count ->
                        let snaps = Replay.read replay |> Seq.toArray

                        if state.Snapshot < snaps.Length then
                            match snaps[state.Snapshot].Flow with
                            | 0uy -> state.PlaybackSpeed
                            | _ -> max 1 (state.PlaybackSpeed / 4)
                        else
                            state.PlaybackSpeed
                    | _ -> state.PlaybackSpeed

                let snapInterval = 125.0 / float effectiveSpeed

                if newAcc >= snapInterval then
                    let newSnap = min total (state.Snapshot + 1)
                    let finished = newSnap >= total

                    { state with
                        Accumulator = newAcc - snapInterval
                        InterpolationT = 0.0
                        Snapshot = newSnap
                        IsPlaying = not finished }
                else
                    { state with
                        Accumulator = newAcc
                        InterpolationT = newAcc / snapInterval }
