namespace FootballEngine.Core.Match

open System.Text

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

    let eventsHash (events: MatchEvent list) : uint64 =
        let mutable hash = 1469598103934665603UL

        for ev in events do
            let payload = sprintf "%d|%A" (int ev.Tick) ev.Data

            for byteValue in Encoding.UTF8.GetBytes payload do
                hash <- fnv hash (uint64 byteValue)

        hash

    let runMatch
        (systems: SystemSpec list)
        (ctx: MatchContext)
        (matchSeed: uint64)
        (mode: RunMode)
        (weightsPath: string option)
        : MatchOutcome =
        // The caller supplies the resolved config. Reading a mutable file here
        // would make the same MatchInput produce different results.
        let _ = weightsPath

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
            | RunMode.Full -> None

        match snapshotEvery with
        | Some _ -> snaps.Add(Replay.capture world)
        | None -> ()

        // Safety valve: if the loop never reaches MatchEnded, force termination
        // instead of hanging the simulation.
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
          EventsHash = eventsHash (List.ofSeq allEvents) }
