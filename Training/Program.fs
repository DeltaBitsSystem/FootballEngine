namespace Training

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open FootballEngine.Core.Match

/// CLI:
///   validate [n]        — run n matches with defaultConfig, print vs targets
///   tune [sweeps] [n]   — coordinate-descent auto-calibration, then validate
///   dump <path> [n]     — tune then dump the best knobs as JSON for the game
module Program =

    let seedBase = 42UL

    let private defaultArgs (args: string list) (def: string list) = if args.IsEmpty then def else args

    let private validateCmd (n: int) (cfg: BalanceConfig) : int =
        let metrics = Validator.validate cfg seedBase n
        Validator.print "default config" metrics
        0

    let private tuneCmd (sweeps: int) (n: int) : BalanceConfig =
        let start = BalanceConfig.defaultConfig
        let startErr = SimulatorRunner.evalStable start seedBase n
        printfn "start error (n=%d): %.3f" n startErr

        let eval =
            if n <= 1 then
                fun cfg -> SimulatorRunner.evalQuick cfg seedBase
            else
                fun cfg -> SimulatorRunner.evalStable cfg seedBase n

        let sw = Diagnostics.Stopwatch.StartNew()
        let result = AutoTuner.tune eval start sweeps
        sw.Stop()

        printfn
            "tuned in %d evals, %.1fs (error %.3f -> %.3f)"
            result.Evaluations
            sw.Elapsed.TotalSeconds
            result.StartError
            result.Error

        for name, delta in AutoTuner.deltas start result.Best do
            printfn "  %-18s %+0.4f" name delta

        let metrics = Validator.validate result.Best seedBase n
        Validator.print "tuned config" metrics
        result.Best

    let private dumpCmd (path: string) (n: int) : unit =
        let best = tuneCmd 3 n

        let values = AutoTuner.knobs |> List.map (fun p -> p.Name, p.Get best) |> dict

        let json =
            JsonSerializer.Serialize(values, JsonSerializerOptions(WriteIndented = true))

        File.WriteAllText(path, json)
        printfn "dumped %d knobs to %s" values.Count path

    /// Where fouls, corners and offsides come from: counts per cause, averaged over `n` matches.
    let diagnoseCmd (n: int) (cfg: BalanceConfig) =
        let counts = System.Collections.Concurrent.ConcurrentDictionary<string, int>()
        let add key = counts.AddOrUpdate(key, 1, fun _ v -> v + 1) |> ignore

        [| for i in 0 .. n - 1 -> 42UL + uint64 i * 1013UL |]
        |> Array.Parallel.iter (fun seed ->
            let mutable lastContact = "none"
            let mutable lastBall = "none"

            SimulatorRunner.runObserved cfg seed (fun world events ->
                for event in events do
                    match event.Data with
                    | EventData.PhysicalContact record ->
                        let kind = record.ActionKind |> Option.map string |> Option.defaultValue "noAction"
                        lastContact <- $"{kind} {record.SurfaceA}->{record.SurfaceB} ball={record.BallTouched}"
                        add $"contact {lastContact}"
                        if record.BallTouched then
                            let speed =
                                sqrt (float record.RelativeVelocityX ** 2.0 + float record.RelativeVelocityY ** 2.0 + float record.RelativeVelocityZ ** 2.0)
                            let line = float (Pitch.HalfLength - abs record.PointX)
                            lastBall <- $"contact {kind}"
                            if kind = "ContactTrap" then
                                lastBall <- $"contact ContactTrap speed>{5.0 * floor (speed / 5.0)} line<{5.0 * ceil (line / 5.0)}m z>{0.5 * floor (float record.PointZ / 0.5)}"
                    | EventData.Foul(_, _, severity, _, _) -> add $"foul {severity} after {lastContact}"
                    | EventData.Card(_, card) -> add $"card {card}"
                    | EventData.FootContact(actor, _, kind, x, _, _) ->
                        let team = if Array.contains actor world.Home.RosterIds then "home" else "away"
                        lastBall <- $"touch {kind} by {team} line<{5.0 * ceil (float (Pitch.HalfLength - abs x) / 5.0)}m"
                    | EventData.Shot _ ->
                        lastBall <- "shot"
                        add "keeper: shot"
                    | EventData.KeeperDive _ -> add "keeper: dive"
                    | EventData.Save _ ->
                        lastBall <- "save"
                        add "keeper: save"
                    | EventData.Goal _ -> add $"keeper: goal after {lastBall}"
                    | EventData.CrossAttempt _ -> lastBall <- "cross"
                    | EventData.Woodwork _ -> lastBall <- "woodwork"
                    | EventData.Interception _ -> lastBall <- "interception"
                    | EventData.SetPiece(kind, side) -> add $"setpiece {kind} for {side} after {lastBall}"
                    | EventData.Offside _ -> add "offside"
                    | _ -> ())
            |> ignore)

        // A few concrete cases of a team passing the ball out over its own goal line, with the receiver's state.
        let examples = System.Collections.Concurrent.ConcurrentBag<string>()

        [| 42UL; 1055UL |]
        |> Array.Parallel.iter (fun seed ->
            let mutable pass = ""
            let mutable passer = None
            let mutable receiverId = None
            let mutable closest = System.Double.MaxValue
            let mutable closestAt = ""
            let mutable gateAt = -1
            let mutable atGate = ""

            SimulatorRunner.runObserved cfg seed (fun world events ->
                let describe (receiver: PlayerId) =
                    let side = if Array.contains receiver world.Home.RosterIds then ClubSide.Home else ClubSide.Away
                    let team = MatchWorld.team world side
                    let slot = Array.findIndex ((=) receiver) team.RosterIds
                    let f = team.Frame
                    $"recv {side} slot {slot} at ({f.PosX[slot]:F1},{f.PosY[slot]:F1}) v ({f.VelX[slot]:F1},{f.VelY[slot]:F1}) intent {f.Intent.Kind[slot]}"

                match receiverId with
                | Some (r: PlayerId) when passer.IsSome ->
                    let side = if Array.contains r world.Home.RosterIds then ClubSide.Home else ClubSide.Away
                    let team = MatchWorld.team world side
                    let slot = Array.findIndex ((=) r) team.RosterIds
                    let b = world.State.Ball.Position
                    let d = sqrt ((float b.X - float team.Frame.PosX[slot]) ** 2.0 + (float b.Y - float team.Frame.PosY[slot]) ** 2.0)
                    if int world.State.Tick = gateAt then
                        let f = team.Frame
                        atGate <- $"at gate ball ({b.X:F1},{b.Y:F1}) recv ({f.PosX[slot]:F1},{f.PosY[slot]:F1}) v ({f.VelX[slot]:F1},{f.VelY[slot]:F1}) intent {f.Intent.Kind[slot]} -> ({f.Intent.TargetX[slot]:F1},{f.Intent.TargetY[slot]:F1})"
                    if d < closest then
                        closest <- d
                        closestAt <- $"closest {d:F2} m at t{world.State.Tick} ball z {b.Z:F2} speed {sqrt (float b.Vx ** 2.0 + float b.Vy ** 2.0):F1} control {world.State.Ball.Control} action {team.Frame.ContactActions[slot] |> Option.map (fun a -> a.Kind, a.Phase)} status {team.Frame.ContactState[slot].Status}"
                | _ -> ()

                for event in events do
                    match event.Data with
                    | EventData.FootContact(actor, _, FootTouchKind.Kick StrikeKind.Pass, x, y, _) ->
                        let ball = world.State.Ball
                        passer <- Some actor
                        closest <- System.Double.MaxValue
                        gateAt <- (match ball.ArrivalGate with Some g -> int g | None -> -1)
                        atGate <- ""
                        let target = ball.Impulse |> Option.bind _.IntendedTarget |> Option.map (fun t -> $"({t.X:F1},{t.Y:F1})")
                        pass <- $"target {target} gate +{gateAt - int event.Tick} "
                        receiverId <-
                            match ball.Trajectory with
                            | Some(TrajectoryIntent.Aimed(_, receiver, _)) -> Some receiver
                            | _ -> None
                        pass <-
                            pass +
                            match ball.Trajectory with
                            | Some(TrajectoryIntent.Aimed(_, receiver, _)) ->
                                $"t{event.Tick} pass from ({x:F1},{y:F1}) v ({ball.Position.Vx:F1},{ball.Position.Vy:F1}) {describe receiver}"
                            | other -> $"t{event.Tick} pass from ({x:F1},{y:F1}) trajectory {other}"
                    | EventData.FootContact _
                    | EventData.PhysicalContact _ -> passer <- None
                    | EventData.SetPiece(SetPieceKind.Corner, side) when passer.IsSome ->
                        let ball = world.State.Ball
                        examples.Add $"seed {seed} corner t{event.Tick} for {side} | {pass} | {closestAt} | {atGate}"
                        passer <- None
                    | _ -> ())
            |> ignore)

        for line in examples |> Seq.truncate 12 do
            printfn "%s" line

        for KeyValue(key, value) in counts |> Seq.sortBy (fun kv -> kv.Key) do
            printfn "%7.2f  %s" (float value / float n) key

    [<EntryPoint>]
    let main argv =
        let args = argv |> Array.toList

        match args with
        | "validate" :: rest ->
            let n = defaultArgs rest [ "2" ] |> List.head |> int
            validateCmd n BalanceConfig.defaultConfig
        | [ "heldout" ] ->
            // Seeds 1-4 are never used for tuning, so they judge whether a change generalises beyond the tuning seeds.
            [ 1UL .. 4UL ]
            |> List.toArray
            |> Array.Parallel.map (fun seed -> SimulatorRunner.runOne BalanceConfig.defaultConfig seed)
            |> Array.toList
            |> List.map Metrics.ofWorld
            |> Validator.print "held-out seeds 1-4"
            0
        | "tune" :: rest ->
            let parts = defaultArgs rest [ "6"; "2" ]
            let sweeps = parts.[0] |> int
            let n = if parts.Length > 1 then parts.[1] |> int else 1
            tuneCmd sweeps n |> ignore
            0
        | "diagnose" :: rest ->
            let n = defaultArgs rest [ "4" ] |> List.head |> int
            diagnoseCmd n BalanceConfig.defaultConfig
            0
        | "dump" :: path :: rest ->
            let n = defaultArgs rest [ "1" ] |> List.head |> int
            dumpCmd path n
            0
        | _ ->
            printfn "usage: validate [n] | heldout | tune [sweeps] [n] | dump <path> [n] | diagnose [n]"
            0
