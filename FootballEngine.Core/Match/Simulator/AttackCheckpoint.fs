namespace FootballEngine.Core.Match

open System.IO
open Microsoft.FSharp.Reflection

/// The payload verifies replay restored learning and commitments, not just the scoreboard.
module AttackCheckpoint =
    let private option (w: BinaryWriter) write value =
        w.Write(Option.isSome value)
        value |> Option.iter write
    let private readOption (r: BinaryReader) read = if r.ReadBoolean() then Some(read ()) else None
    let private tag<'T> (w: BinaryWriter) (value: 'T) =
        let case, _ = FSharpValue.GetUnionFields(box value, typeof<'T>)
        w.Write(byte case.Tag)
    let private readTag<'T> (r: BinaryReader) : 'T =
        let index = int (r.ReadByte())
        match FSharpType.GetUnionCases typeof<'T> |> Array.tryFind (fun case -> case.Tag = index && case.GetFields().Length = 0) with
        | Some case -> FSharpValue.MakeUnion(case, [||]) :?> 'T
        | None -> raise (InvalidDataException "unknown attack checkpoint tag")
    let private writePoint (w: BinaryWriter) (p: Vec2) = w.Write(float p.X); w.Write(float p.Y)
    let private readPoint (r: BinaryReader) =
        let x, y = r.ReadDouble(), r.ReadDouble()
        if not (System.Double.IsFinite x && System.Double.IsFinite y) || abs x > float Pitch.HalfLength || abs y > float Pitch.HalfWidth then
            raise (InvalidDataException "attack checkpoint point is outside the pitch")
        Vec2.create (x * 1.0<meter>) (y * 1.0<meter>)
    let private writeDuty (w: BinaryWriter) (d: AttackDuty) =
        let (PlayerId id) = d.Player
        w.Write id
        w.Write d.Slot
        writePoint w d.Target
    let private readDuty (r: BinaryReader) : AttackDuty =
        let player, slot = PlayerId(r.ReadInt32()), r.ReadInt32()
        if slot < 0 || (let (PlayerId id) = player in id <= 0) then raise (InvalidDataException "invalid attack participant")
        { Player = player; Slot = slot; Target = readPoint r }
    let private writeEstimates (w: BinaryWriter) (memory: Map<int, AttackEstimate>) =
        w.Write memory.Count
        for KeyValue(key, estimate) in memory do
            w.Write key
            w.Write estimate.Samples
            w.Write estimate.RewardSum
    let private readEstimates (r: BinaryReader) : Map<int, AttackEstimate> =
        let count = r.ReadInt32()
        if count < 0 || count > 24 then raise (InvalidDataException "attack checkpoint exceeds contextual storage bound")
        let rows = Array.init count (fun _ ->
            let key, samples, reward = r.ReadInt32(), r.ReadDouble(), r.ReadDouble()
            if key < 0 || key >= 24 || not (System.Double.IsFinite samples && System.Double.IsFinite reward) || samples < 0.0 || samples > 1000.0 || abs reward > samples then
                raise (InvalidDataException "invalid attack checkpoint estimate")
            key, ({ Samples = samples; RewardSum = reward }: AttackEstimate))
        let result = Map.ofArray rows
        if result.Count <> count then raise (InvalidDataException "duplicate attack checkpoint key")
        result
    let write (w: BinaryWriter) (state: CollectiveAttackState) =
        let tick (value: int<subtick>) = w.Write(int value)
        let player (PlayerId id) = w.Write id
        let event id = w.Write(EventId.value id)
        w.Write state.NextId
        tick state.AvailableAt
        writeEstimates w state.Estimates
        writeEstimates w state.Evidence
        option w (fun (e: AttackEpisode) ->
            w.Write e.Id
            tag w e.Pattern
            w.Write e.Context
            tick e.StartedAt
            tick e.Deadline
            tag w e.Stage
            player e.Carrier
            player e.Owner
            writeDuty w e.Link
            writeDuty w e.Runner
            writeDuty w e.Security
            writePoint w e.Origin
            w.Write e.BaselineSpace
            w.Write e.Connections
            option w event e.LastFact
            w.Write e.Threat
            writePoint w e.OwnedPoint
            w.Write e.OwnedSpace
            w.Write e.OwnedExposure
            tick e.FactTick
            w.Write e.Tactics.Formation
            for value in [ e.Tactics.DefensiveLine; e.Tactics.PressIntensity; e.Tactics.Width; e.Tactics.Tempo; e.Tactics.Directness; e.Tactics.Mentality; e.Tactics.PressTriggerZone ] do w.Write value
            option w tick e.BlockedSince) state.Episode
    let read (r: BinaryReader) =
        let tick () = r.ReadInt32() * 1<subtick>
        let player () =
            let id = r.ReadInt32()
            if id <= 0 then raise (InvalidDataException "invalid attack player identity")
            PlayerId id
        let event () =
            let id = r.ReadUInt64()
            if id = 0UL then raise (InvalidDataException "invalid attack event identity")
            EventId id
        let next, available = r.ReadUInt64(), tick ()
        let estimates, evidence = readEstimates r, readEstimates r
        let episode = readOption r (fun () ->
            let id, pattern, context = r.ReadUInt64(), readTag<AttackPattern> r, r.ReadInt32()
            let start, deadline, stage = tick (), tick (), readTag<AttackStage> r
            let carrier = player ()
            let owner = player ()
            let link, runner, security = readDuty r, readDuty r, readDuty r
            let origin, baseline, connections = readPoint r, r.ReadDouble(), r.ReadInt32()
            let fact, threat = readOption r event, r.ReadDouble()
            let ownedPoint, ownedSpace, ownedExposure, factTick = readPoint r, r.ReadDouble(), r.ReadDouble(), tick ()
            let tactics =
                { Formation = r.ReadString(); DefensiveLine = r.ReadSingle(); PressIntensity = r.ReadSingle()
                  Width = r.ReadSingle(); Tempo = r.ReadSingle(); Directness = r.ReadSingle()
                  Mentality = r.ReadSingle(); PressTriggerZone = r.ReadSingle() }
            let blocked = readOption r tick
            let finiteUnit x = System.Double.IsFinite x && x >= 0.0 && x <= 1.0
            let tacticsValid =
                not (System.String.IsNullOrWhiteSpace tactics.Formation) &&
                System.Single.IsFinite tactics.DefensiveLine && tactics.DefensiveLine >= 0.0f && tactics.DefensiveLine <= float32 Pitch.Length &&
                [tactics.PressIntensity; tactics.Width; tactics.Tempo; tactics.Directness; tactics.PressTriggerZone; tactics.Mentality] |> List.forall (fun x -> System.Single.IsFinite x && x >= 0.0f && x <= 1.0f)
            let stageValid =
                match stage with
                | Preparing -> connections = 0 && owner = carrier && fact.IsNone
                | Connected -> pattern <> ExploitDepth && connections = 1 && owner = link.Player && fact.IsSome
                | Exploiting -> connections = (if pattern = ExploitDepth then 1 else 2) && owner = runner.Player && fact.IsSome
            if id = 0UL || id >= next || context < 0 || context > 5 || start < 0<subtick> || deadline <= start || connections < 0 || connections > 2
               || factTick < start || factTick >= deadline || not stageValid || not tacticsValid || not (System.Single.IsFinite tactics.Mentality) || abs tactics.Mentality > 1.0f
               || not (finiteUnit ownedSpace && finiteUnit ownedExposure)
               || Set.ofList [carrier; link.Player; runner.Player; security.Player] |> Set.count <> 4
               || Set.ofList [link.Slot; runner.Slot; security.Slot] |> Set.count <> 3
               || List.distinct [link.Target; runner.Target; security.Target] |> List.length <> 3
               || (blocked |> Option.exists (fun t -> t < start || t >= deadline))
               || not (System.Double.IsFinite baseline && System.Double.IsFinite threat) || baseline < 0.0 || baseline > 1.0 || threat <> 0.0 then
                raise (InvalidDataException "invalid attack checkpoint episode")
            { Id = id; Pattern = pattern; Context = context; StartedAt = start; Deadline = deadline; Stage = stage
              Carrier = carrier; Owner = owner; Link = link; Runner = runner; Security = security; Origin = origin
              BaselineSpace = baseline; Connections = connections; LastFact = fact; Threat = threat
              OwnedPoint = ownedPoint; OwnedSpace = ownedSpace; OwnedExposure = ownedExposure; FactTick = factTick
              Tactics = tactics; BlockedSince = blocked })
        if next = 0UL || available < 0<subtick> then raise (InvalidDataException "invalid attack checkpoint frontier")
        { Episode = episode; Estimates = estimates; Evidence = evidence; NextId = next; AvailableAt = available }

    let validFor (ctx: MatchContext) side tick (state: CollectiveAttackState) =
        let cfg = ctx.Config.CollectiveAttack
        let estimatesValid (memory: Map<int, AttackEstimate>) = memory |> Map.forall (fun key estimate ->
            key >= 0 && key < 24 && System.Double.IsFinite estimate.Samples && System.Double.IsFinite estimate.RewardSum &&
            estimate.Samples >= 0.0 && estimate.Samples <= cfg.EvidenceCap && abs estimate.RewardSum <= estimate.Samples)
        estimatesValid state.Estimates && estimatesValid state.Evidence &&
        (state.Episode |> Option.forall (fun e ->
            let identity duty = MatchWorld.slotOf ctx side duty.Player = Some duty.Slot
            e.StartedAt <= tick && e.FactTick <= tick && e.Deadline = e.StartedAt + cfg.Duration && e.Id < state.NextId &&
            (MatchWorld.slotOf ctx side e.Carrier |> Option.isSome) &&
            identity e.Link && identity e.Runner && identity e.Security &&
            AttackGeometry.complementary cfg [e.Link; e.Runner; e.Security]))
