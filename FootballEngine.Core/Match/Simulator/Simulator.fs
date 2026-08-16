namespace FootballEngine.Core.Match

/// The match loop. Deterministic: one Rng threaded through every step, fresh World per run.
module Simulator =

    type RunMode =
        | Fast
        | Replay of snapshotEvery: int<tickDelta>
        | Full

    type MatchOutcome =
        { Events: MatchEvent list
          Final: World
          Replay: Replay.ReplayData option
          DumpJson: string option
          EventsHash: uint64 }

    let private fnv (h: uint64) (v: uint64) : uint64 = (h ^^^ v) * 1099511628211UL

    let runMatch
        (systems: SystemSpec list)
        (ctx: MatchContext)
        (matchSeed: uint64)
        (mode: RunMode)
        (weightsPath: string option)
        : MatchOutcome =
        let ctx =
            match weightsPath with
            | Some path when System.IO.File.Exists path ->
                match WeightMerge.load path ctx.Config with
                | Ok merged -> { ctx with Config = merged }
                | Error _ -> ctx
            | _ -> ctx

        let world = MatchFactory.create ctx

        let systemCtx =
            { Ctx = ctx
              Clock = SimulationClock.defaultClock }

        let buffer =
            { Events = ResizeArray<MatchEvent>()
              Flags = { EventFlags.Bits = 0UL }
              Applied = ResizeArray<MatchEvent>() }

        let mutable rng = Rng.ofSeed matchSeed
        let allEvents = ResizeArray<MatchEvent>()
        let snaps = ResizeArray<Replay.ReplaySnapshot>()

        let snapshotEvery =
            match mode with
            | RunMode.Fast -> None
            | RunMode.Replay n -> Some n

        match snapshotEvery with
        | Some _ -> snaps.Add(Replay.capture world)
        | None -> ()

        let mutable guard = 0

        while world.State.Flow <> MatchFlow.MatchEnded do
            let rng', _, applied = Stepper.step systems systemCtx rng world buffer
            rng <- rng'
            allEvents.AddRange applied

            match snapshotEvery with
            | Some n when world.State.Tick % (int n * 1<subtick>) = 0<subtick> -> snaps.Add(Replay.capture world)
            | _ -> ()

            guard <- guard + 1

            if guard > 400000 then
                world.State <-
                    { world.State with
                        Flow = MatchFlow.MatchEnded }

        let mutable hash = 1469598103934665603UL

        for ev in allEvents do
            let bit =
                System.Numerics.BitOperations.TrailingZeroCount(uint (EventFlags.ofKind (MatchEvent.kind ev)).Bits)

            hash <- fnv hash (uint64 bit ||| (uint64 ev.Tick <<< 32))

        { Events = List.ofSeq allEvents
          Final = world
          Replay =
            (match mode with
             | RunMode.Fast
             | RunMode.Full -> None
             | RunMode.Replay _ -> Some(Replay.write (List.ofSeq snaps)))
          DumpJson =
            (match mode with
             | RunMode.Full -> Some(MatchDump.export ctx (List.ofSeq allEvents) world.Home.Stats world.Away.Stats)
             | _ -> None)
          EventsHash = hash }
