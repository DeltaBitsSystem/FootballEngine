namespace Training

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open FootballEngine.Core.Match

/// CLI: see `usage`. Every command accepts `--set Path.Field=value` overrides and `--heldout` seeds.
module Program =

    let seedBase = 42UL

    let private defaultArgs (args: string list) (def: string list) = if args.IsEmpty then def else args

    /// Where fouls, corners and offsides come from: counts per cause, averaged over `n` matches.
    /// Rival-manager moves per match on the corpus, where every side is autonomous: how often and when coaches act.
    let shiftsCmd (n: int) (cfg: BalanceConfig) =
        let causes = System.Collections.Generic.List<string>()
        let rows =
            [| for i in 0 .. n - 1 -> 42UL + uint64 i * 1013UL |]
            |> Array.Parallel.map (fun seed ->
                let outcome = SimulatorRunner.runContext (SimulatorRunner.corpusContext cfg seed) seed

                let moves =
                    outcome.Events
                    |> List.choose (fun ev ->
                        match ev.Data with
                        | EventData.ManagerShift(side, shift) ->
                            Some(side, shift, int ev.Tick / int SimulationClock.minuteSubticks)
                        | _ -> None)

                let reds =
                    outcome.Events
                    |> List.choose (fun ev ->
                        match ev.Data with
                        | EventData.RedCardIssued player -> Some player
                        | _ -> None)

                let reds = reds.Length, (reds |> List.distinct |> List.length)

                // Severity of the foul behind each dismissal: the last foul the dismissed player committed before it.
                for red in outcome.Events do
                    match red.Data with
                    | EventData.RedCardIssued player ->
                        outcome.Events
                        |> List.filter (fun ev -> ev.Tick <= red.Tick)
                        |> List.choose (fun ev ->
                            match ev.Data with
                            | EventData.Foul(fouler, _, severity, _, _) when fouler = player -> Some(sprintf "%A" severity)
                            | _ -> None)
                        |> List.tryLast
                        |> Option.iter (fun severity -> lock causes (fun () -> causes.Add severity))
                    | _ -> ()

                seed, outcome.Final.State.HomeScore, outcome.Final.State.AwayScore, moves, reds)

        for seed, h, a, moves, _ in rows do
            let text =
                moves
                |> List.map (fun (side, shift, minute) -> sprintf "%A:%A@%d" side shift minute)
                |> String.concat " "

            printfn "seed %d  %d-%d  %s" seed h a text

        let all = rows |> Array.collect (fun (_, _, _, moves, _) -> Array.ofList moves)
        printfn "moves/match %.2f" (float all.Length / float n)
        printfn "red cards/match %.2f" (float (rows |> Array.sumBy (fun (_, _, _, _, (reds, _)) -> reds)) / float n)
        printfn "players sent off/match %.2f" (float (rows |> Array.sumBy (fun (_, _, _, _, (_, players)) -> players)) / float n)

        for severity, count in causes |> Seq.countBy id do
            printfn "  dismissal after %s foul: %d" severity count

        for shift, count in all |> Array.countBy (fun (_, shift, _) -> shift) do
            printfn "  %A %d" shift count

    let diagnoseCmd (n: int) (cfg: BalanceConfig) =
        let counts = System.Collections.Concurrent.ConcurrentDictionary<string, int>()
        let add key = counts.AddOrUpdate(key, 1, fun _ v -> v + 1) |> ignore

        [| for i in 0 .. n - 1 -> 42UL + uint64 i * 1013UL |]
        |> Array.Parallel.iter (fun seed ->
            let mutable lastContact = "none"
            let mutable lastBall = "none"
            let mutable dived = false
            let mutable shotTick = 0
            let mutable shotZone = ""
            let guards = System.Collections.Generic.HashSet<string>()
            let sctx = { Ctx = SimulatorRunner.corpusContext cfg seed; Clock = SimulationClock.defaultClock }

            SimulatorRunner.runObserved cfg seed (fun world events ->
                // The keeper's first decision once he has reacted to each shot, so a goal without a dive names
                // what kept him still.
                let ball = world.State.Ball

                match ball.Trajectory with
                | Some(TrajectoryIntent.Struck(shooter, _, _, _)) when guards.Count = 0 ->
                    let side = if Array.contains shooter world.Home.RosterIds then ClubSide.Away else ClubSide.Home

                    match KeeperSave.assess sctx world side with
                    | KeeperSave.NotAShotAtHim
                    | KeeperSave.StillReacting _ -> ()
                    | KeeperSave.OutOfReach(required, reach, seconds) ->
                        guards.Add $"out of reach by<{0.5 * ceil ((required - reach) / 0.5)}m left<{0.1 * ceil (seconds / 0.1)}s" |> ignore
                    | KeeperSave.CanReach _ -> guards.Add "can reach" |> ignore
                    | other -> guards.Add $"{other}" |> ignore
                | _ -> ()

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
                    | EventData.FootContact(actor, _, kind, x, y, _) ->
                        if kind = FootTouchKind.Kick StrikeKind.Shot then
                            dived <- false
                            shotTick <- int event.Tick
                            guards.Clear()
                            let distance = sqrt ((float Pitch.HalfLength - abs (float x)) ** 2.0 + float y ** 2.0)
                            let zone = if distance < 6.0 then "a <6m" elif distance < 11.0 then "b 6-11m" elif distance < 16.5 then "c 11-16.5m" elif distance < 25.0 then "d 16.5-25m" else "e 25m+"
                            add $"shot from {zone}"
                            shotZone <- zone

                        let team = if Array.contains actor world.Home.RosterIds then "home" else "away"
                        lastBall <- $"touch {kind} by {team} line<{5.0 * ceil (float (Pitch.HalfLength - abs x) / 5.0)}m"
                    | EventData.Shot _ ->
                        lastBall <- "shot"
                        add "keeper: shot"
                    | EventData.KeeperDive _ ->
                        dived <- true
                        add "keeper: dive"
                    | EventData.Save _ ->
                        lastBall <- "save"
                        add "keeper: save"
                    | EventData.Goal _ ->
                        let spin = abs (float world.State.Ball.Spin.Z)
                        let speed = sqrt (float world.State.Ball.Position.Vx ** 2.0 + float world.State.Ball.Position.Vy ** 2.0)
                        let flight = float (int event.Tick - shotTick) / 40.0
                        let keeperState =
                            match event.Data with
                            | EventData.Goal(scorer, _, _, _) ->
                                let defending = MatchWorld.team world (MatchWorld.oppOf scorer)
                                match defending.GoalkeeperSlot with
                                | Some gk ->
                                    let x = float (MatchWorld.posX defending.Frame gk)
                                    let line = abs (float (MatchMath.ownGoalX (MatchWorld.oppOf scorer)) - x)
                                    let y = float (MatchWorld.posY defending.Frame gk)
                                    let lateral = abs (float world.State.Ball.Position.Y - y)
                                    $"keeper {5.0 * floor (line / 5.0)}m off line lateral<{0.5 * ceil (lateral / 0.5)} z<{0.5 * ceil (float world.State.Ball.Position.Z / 0.5)}"
                                | None -> "no keeper"
                            | _ -> ""
                        add $"keeper: goal after {lastBall} dived={dived} flight<{0.25 * ceil (flight / 0.25)}s {keeperState}"
                        let seen = guards |> Seq.sort |> String.concat ", "
                        add $"keeper: goal guards {seen}"
                        add $"goal from {shotZone}"
                    | EventData.CrossAttempt _ -> lastBall <- "cross"
                    | EventData.Woodwork(_, part, x, _, z) ->
                        let behind = abs (float x) > float Pitch.HalfLength
                        add $"woodwork {part} after {lastBall} behind={behind} z<{0.5 * ceil (float z / 0.5)}"
                        lastBall <- "woodwork"
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

    /// Tuning seeds start at 42; held-out seeds 1..n are never tuned on, so they judge whether a change generalises.
    let private seedsFor heldOut n =
        if heldOut then [| for i in 1..n -> uint64 i |]
        else [| for i in 0 .. n - 1 -> seedBase + uint64 i * 1013UL |]

    let private sweepCmd (cfg: BalanceConfig) (path: string) (values: string list) (seeds: uint64[]) =
        printfn "sweep %s over %d matches per value" path seeds.Length
        printfn "  %-10s %6s %6s %6s %6s %6s %6s %6s %6s" "value" "goals" "shots" "conv%" "save%" "pass%" "fouls" "yellow" "corner"

        // All values run at once: a sweep is as slow as its slowest value, not the sum of them.
        let results =
            values
            |> List.toArray
            |> Array.Parallel.map (fun value ->
                let checkedVariant =
                    Overrides.apply cfg $"{path}={value}"
                    |> Result.bind (fun variant ->
                        ConfigValidation.validateAll variant
                        |> Result.map (fun () -> variant)
                        |> Result.mapError (fun errors -> "rejected: " + String.concat "; " errors))

                value, checkedVariant |> Result.map (fun variant -> Report.run variant seeds))

        for value, result in results do
            match result with
            | Error e -> printfn "  %-10s %s" value e
            | Ok runs ->
                let mean f = runs |> List.averageBy (fun (r: Report.MatchRun) -> f r)
                let count key (r: Report.MatchRun) = match r.Counts.TryGetValue key with | true, v -> v | _ -> 0.0
                let goals, shots = mean (count "goals"), mean (count "shots")
                let saves, onTarget = mean (count "saves"), mean (count "on target")

                printfn
                    "  %-10s %6.1f %6.1f %6.1f %6.1f %6.1f %6.1f %6.1f %6.1f"
                    value
                    goals
                    shots
                    (100.0 * goals / max 1.0 shots)
                    (100.0 * saves / max 1.0 onTarget)
                    (mean (fun r -> r.Metrics.PassPct))
                    (mean (fun r -> r.Metrics.Fouls))
                    (mean (fun r -> r.Metrics.Yellows))
                    (mean (fun r -> r.Metrics.Corners))

    /// Wall time per system over one match: where a slow balance loop actually spends its time. With cognition
    /// on its own thread the per-system times overlap, so only the wall time says what the match costs.
    let private profileCmd (ownThread: bool) (cfg: BalanceConfig) (seed: uint64) (budgetPath: string option) : int =
        let names = Scheduler.systems |> List.map _.Name |> Array.ofList
        // One slot per system: cognition's systems are timed on its own thread, so no slot is shared.
        let timeOf = Array.zeroCreate<int64> names.Length
        let callsOf = Array.zeroCreate<int> names.Length
        let bytesOf = Array.zeroCreate<int64> names.Length

        let timed =
            Scheduler.systems
            |> List.mapi (fun index spec ->
                { spec with
                    Step =
                        fun ctx rng world buffer ->
                            let allocated = System.GC.GetAllocatedBytesForCurrentThread()
                            let start = System.Diagnostics.Stopwatch.GetTimestamp()
                            let result = spec.Step ctx rng world buffer
                            bytesOf[index] <- bytesOf[index] + System.GC.GetAllocatedBytesForCurrentThread() - allocated
                            timeOf[index] <- timeOf[index] + System.Diagnostics.Stopwatch.GetTimestamp() - start
                            callsOf[index] <- callsOf[index] + 1
                            result })

        let pauseBefore = System.GC.GetTotalPauseDuration()
        let gen0, gen1, gen2 = System.GC.CollectionCount 0, System.GC.CollectionCount 1, System.GC.CollectionCount 2
        let total = System.Diagnostics.Stopwatch.StartNew()
        let ticks = SimulatorRunner.runWith ownThread timed cfg seed
        total.Stop()
        let timers = System.Collections.Generic.Dictionary<string, int64>()
        let calls = System.Collections.Generic.Dictionary<string, int>()
        let bytes = System.Collections.Generic.Dictionary<string, int64>()

        for index, name in Array.indexed names do
            timers[name] <- timeOf[index]
            calls[name] <- callsOf[index]
            bytes[name] <- bytesOf[index]

        printfn
            "gc: %.0f ms paused, collections gen0 %d gen1 %d gen2 %d"
            (System.GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds
            (System.GC.CollectionCount 0 - gen0)
            (System.GC.CollectionCount 1 - gen1)
            (System.GC.CollectionCount 2 - gen2)
        let ms (t: int64) = float t * 1000.0 / float System.Diagnostics.Stopwatch.Frequency
        let systemsMs = timers.Values |> Seq.sumBy ms
        printfn "one match: %.0f ms wall, %d ticks, %.1f us/tick; systems %.0f ms, stepper and rest %.0f ms" total.Elapsed.TotalMilliseconds ticks (total.Elapsed.TotalMilliseconds * 1000.0 / float ticks) systemsMs (total.Elapsed.TotalMilliseconds - systemsMs)

        for KeyValue(name, t) in timers |> Seq.sortByDescending (fun kv -> kv.Value) do
            printfn "  %-24s %8.0f ms %5.1f %%  %7d calls %8.0f MB" name (ms t) (100.0 * ms t / total.Elapsed.TotalMilliseconds) calls[name] (float bytes[name] / 1e6)

        match budgetPath with
        | None -> 0
        | Some path ->
            // Allocation is the same on every machine for one seed and one build, so it is gated per system;
            // wall time depends on the machine and its load, so only the whole match is held to a budget.
            let budget =
                System.IO.File.ReadAllLines path
                |> Array.map (fun line -> line.Trim())
                |> Array.filter (fun line -> line <> "" && not (line.StartsWith "#"))
                |> Array.map (fun line ->
                    let parts = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
                    parts[0], float parts[1])

            let violations =
                [ for name, limit in budget do
                      if name = "wall-ms" then
                          if total.Elapsed.TotalMilliseconds > limit then
                              yield sprintf "wall %.0f ms > %.0f ms" total.Elapsed.TotalMilliseconds limit
                      else
                          match bytes.TryGetValue name with
                          | true, allocated when float allocated / 1e6 > limit ->
                              yield sprintf "%s allocates %.1f MB > %.1f MB" name (float allocated / 1e6) limit
                          | true, _ -> ()
                          | false, _ -> yield sprintf "%s is in the budget but not in the schedule" name ]

            for violation in violations do
                printfn "OVER BUDGET: %s" violation

            if violations.IsEmpty then
                printfn "within budget (%s)" path
                0
            else
                1

    /// One FNV-1a hash over every applied event of each seed's match: an optimisation must leave it unchanged.
    let private hashCmd (ownThread: bool) (cfg: BalanceConfig) (seeds: uint64[]) =
        let digest seed =
            let mutable hash = 14695981039346656037UL
            let mutable count = 0

            let world =
                SimulatorRunner.runObservedOn ownThread cfg seed (fun _ events ->
                    for event in events do
                        count <- count + 1

                        for c in sprintf "%d %A" (int event.Tick) event.Data do
                            hash <- (hash ^^^ uint64 c) * 1099511628211UL)

            seed, hash, count, world.State.HomeScore, world.State.AwayScore

        let results = seeds |> Array.Parallel.map digest
        let mutable combined = 14695981039346656037UL

        for seed, hash, count, home, away in results do
            printfn "  seed %-6d %016x  %6d events  %d-%d" seed hash count home away
            combined <- (combined ^^^ hash) * 1099511628211UL

        printfn "combined %016x" combined

    let private usage () =
        printfn "usage (any command takes --set Path.Field=value, repeatable; --heldout uses seeds 1..n):"
        printfn "  validate [n=8]                       banded metrics with 95%% intervals and causal funnels"
        printfn "  sweep <Path.Field> <v1,v2,...> [n=8]  one metric row per value"
        printfn "  hash [n=4]                           event hash per seed; optimisations must keep it"
        printfn "  profile [--gate budget.txt]          time and allocation per system; --gate fails over budget"
        printfn "  diagnose [n=4]"
        1

    [<EntryPoint>]
    let main argv =
        match Overrides.parse (List.ofArray argv) with
        | Error e ->
            eprintfn "%s" e
            2
        | Ok(cfg, applied, args) ->
            let heldOut = List.contains "--heldout" args
            let ownThread = List.contains "--own-thread" args
            let args = args |> List.filter (fun arg -> arg <> "--heldout" && arg <> "--own-thread")
            let label = if applied.IsEmpty then "default config" else String.concat " " applied
            let label = if heldOut then $"{label}, held-out seeds" else label

            match args with
            | "hash" :: rest ->
                hashCmd ownThread cfg (seedsFor heldOut (defaultArgs rest [ "4" ] |> List.head |> int))
                0
            | "validate" :: rest ->
                let n = defaultArgs rest [ "8" ] |> List.head |> int
                Report.print label (Report.run cfg (seedsFor heldOut n))
                0
            | [ "sweep"; path; values ] -> sweepCmd cfg path (values.Split(',') |> List.ofArray) (seedsFor heldOut 8); 0
            | [ "sweep"; path; values; n ] -> sweepCmd cfg path (values.Split(',') |> List.ofArray) (seedsFor heldOut (int n)); 0
            | [ "profile" ] -> profileCmd ownThread cfg 42UL None
            | [ "profile"; "--gate"; path ] -> profileCmd ownThread cfg 42UL (Some path)
            | "quick-fit" :: rest ->
                let perPair = defaultArgs rest [ "6" ] |> List.head |> int
                printfn "%A" (QuickFit.fit cfg perPair)
                0
            | "contrast-groups" :: d :: rest ->
                QuickFit.contrastGroups cfg (float32 d) (defaultArgs rest [ "12" ] |> List.head |> int)
                0
            | "contrast" :: d :: rest ->
                QuickFit.contrast cfg (float32 d) (defaultArgs rest [ "12" ] |> List.head |> int)
                0
            | "side-flow" :: rest ->
                QuickFit.sideFlow cfg (defaultArgs rest [ "12" ] |> List.head |> int)
                0
            | "contrast-attributes" :: d :: n :: names ->
                QuickFit.contrastAttributes cfg (float32 d) (int n) names
                0
            | [ "turnovers"; d; n; name ] ->
                QuickFit.turnovers cfg (float32 d) (int n) name
                0
            | "contrast-flow" :: d :: rest ->
                QuickFit.contrastFlow cfg (float32 d) (defaultArgs rest [ "36" ] |> List.head |> int)
                0
            | "technical-flow" :: d :: rest ->
                QuickFit.technicalFlow cfg (float32 d) (defaultArgs rest [ "36" ] |> List.head |> int)
                0
            | "technical-counts" :: d :: rest ->
                QuickFit.technicalCounts cfg (float32 d) (defaultArgs rest [ "36" ] |> List.head |> int)
                0
            | "losses" :: rest ->
                QuickFit.losses cfg (defaultArgs rest [ "12" ] |> List.head |> int)
                0
            | "side-counts" :: rest ->
                QuickFit.sideCounts cfg (defaultArgs rest [ "12" ] |> List.head |> int)
                0
            | "quick-goals" :: rest ->
                QuickFit.traceGoals cfg (defaultArgs rest [ "3" ] |> List.head |> int)
                0
            | "quick-validate" :: rest ->
                let perPair = defaultArgs rest [ "4" ] |> List.head |> int
                QuickFit.validate cfg perPair
                0
            | "shifts" :: rest ->
                shiftsCmd (defaultArgs rest [ "16" ] |> List.head |> int) cfg
                0
            | "diagnose" :: rest ->
                let n = defaultArgs rest [ "4" ] |> List.head |> int
                diagnoseCmd n cfg
                0
            | _ -> usage ()
