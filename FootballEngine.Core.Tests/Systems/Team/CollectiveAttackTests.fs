namespace FootballEngine.Core.Tests.Systems.Team

open System.IO
open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure
open CollectiveAttackFixtures

module CollectiveAttackTests =
    let private connection ctx (world: World) side (passer: PlayerId) (receiver: PlayerId) tick =
        world.State <- { world.State with Tick = tick }
        EventApply.apply ctx world { Tick = tick; Data = EventData.Pass(passer, Some receiver, 10.0<meter>, 0.8, true, 0.0<meter>, 0.0<meter>, 6.0<meter>, -8.0<meter>) }
        EventApply.apply ctx world { Tick = tick; Data = EventData.Possession(side, receiver) }

    let tests = testList "CollectiveAttack" [
        testCase "fractional evidence caps remain exact bounds after saturation" (fun _ ->
            let cfg = {BalanceConfig.defaultConfig.CollectiveAttack with EvidenceCap = 0.7}
            let memory = Map.ofList [4, ({Samples = 0.2; RewardSum = 0.2}: AttackEstimate)]
            let updated = AttackLearning.add cfg 4 1.0 memory
            Expect.isLessThanOrEqual updated[4].Samples cfg.EvidenceCap "rounding must not make a valid learner invalid at its next input boundary"
            Expect.isLessThanOrEqual (abs updated[4].RewardSum) updated[4].Samples "bounded rewards remain a valid sufficient statistic")
        testCase "anticipation-off seed2 cannot let a stale advantage replace halftime" (fun _ ->
            let baseCtx = TestBuilders.formationCtx ()
            let cfg = {baseCtx.Config with Planner = {baseCtx.Config.Planner with Enabled = false}}
            let ctx = {baseCtx with Config = cfg; HomeClub = {baseCtx.HomeClub with BaseTactics = {baseCtx.HomeClub.BaseTactics with Directness = 0.2f}}; AwayClub = {baseCtx.AwayClub with BaseTactics = {baseCtx.AwayClub.BaseTactics with PressIntensity = 0.9f}}}
            let effective = EffectiveConfig.fromBalance cfg |> function Ok x -> x | Error e -> failtestf "%A" e
            let input = {MatchId = 2UL; Seed = 2UL; Context = ctx; EffectiveConfig = effective; EngineVersion = "collective-probe-v1"}
            let result = MatchAdapter.runOutcome Scheduler.systems input []
            match result with
            | Error e -> failtestf "unchanged safety limit and real execution must terminate: %A" e
            | Ok outcome -> Expect.equal outcome.Final.State.Flow MatchFlow.MatchEnded "second half must actually finish")
        testCase "two receptions in one drain preserve order and deviation permanently severs attribution" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            start ctx world |> ignore
            let e = episode world ClubSide.Home
            let b = buffer ()
            let fact passer receiver x y = { Tick = 410<subtick>; Data = EventData.Pass(passer, Some receiver, 10.0<meter>, 0.8, true, 0.0<meter>, 0.0<meter>, x, y) }
            AttackLifecycle.observe ctx world b (fact e.Carrier e.Link.Player 6.0<meter> -8.0<meter>)
            AttackLifecycle.observe ctx world b (fact e.Link.Player e.Runner.Player 13.0<meter> 8.0<meter>)
            Expect.equal (episode world ClubSide.Home).Connections 2 "neither fact overwrites its predecessor"
            Expect.equal (episode world ClubSide.Home).Owner e.Runner.Player "stage-specific factual owner"
            let frontier = (episode world ClubSide.Home).OwnedPoint
            world.State <- { world.State with Tick = 420<subtick>; Ball = { world.State.Ball with Position = { world.State.Ball.Position with X = 50.0<meter> } } }
            AttackLifecycle.observe ctx world b { Tick = 420<subtick>; Data = EventData.Pass(e.Runner.Player, Some world.Home.RosterIds[1], 10.0<meter>, 1.0, true, frontier.X, frontier.Y, 50.0<meter>, 0.0<meter>) }
            Expect.isNone world.Home.Attack.Episode "autonomous alternative is legitimate but no longer this combination"
            let reward = b.Events |> Seq.choose (fun event -> match event.Data with EventData.CollectiveAttack(_, _, Ended(Deviated, reward, _)) -> Some reward | _ -> None) |> Seq.exactlyOne
            let expected = MatchMath.clamp -1.0 1.0 (ctx.Config.CollectiveAttack.ProgressReward + 2.0 * ctx.Config.CollectiveAttack.ConnectionReward + ctx.Config.CollectiveAttack.SpaceReward * ((AttackGeometry.space ctx.Config.CollectiveAttack world.Away.Frame frontier) - e.BaselineSpace) - ctx.Config.CollectiveAttack.IdlePenalty)
            Expect.floatClose Accuracy.high reward expected "reward uses the last owned receipt, not unrelated 50m progress"
            let memory = world.Home.Attack.Estimates
            AttackLifecycle.observe ctx world b { Tick = 421<subtick>; Data = EventData.Shot(e.Runner.Player, ShotResult.OffTarget, 0.9, 20.0<meterPerSecond>, 0.0<meter>, 0.0<meter>) }
            Expect.equal world.Home.Attack.Estimates memory "late involved-player shot cannot revive the lineage")
        testCase "post-loss rival movement never changes an episode reward" (fun _ ->
            let ctx, first = scenario ClubSide.Home
            let second = scenarioWithContext ctx ClubSide.Home
            for world in [first; second] do
                start ctx world |> ignore
                let e = episode world ClubSide.Home
                connection ctx world ClubSide.Home e.Carrier e.Link.Player 410<subtick>
            for world, x in [first, -40.0<meter>; second, 50.0<meter>] do
                world.State <- { world.State with Tick = 420<subtick>; Ball = { world.State.Ball with Position = { world.State.Ball.Position with X = x }; Control = BallControl.Controlled(ClubSide.Away, world.Away.RosterIds[5]) } }
                start ctx world |> ignore
            Expect.equal first.Home.Attack.Evidence second.Home.Attack.Evidence "opponent territory and space are not causal reward")
        testCase "observed pressure separates bounded learning contexts and changed response does not inherit false certainty" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            let openChoices = TeamAttack.candidates ctx world ClubSide.Home 5
            world.Away.Frame.PosX[1] <- -3.0f; world.Away.Frame.PosY[1] <- 3.0f
            world.Away.Frame.PosX[2] <- -3.0f; world.Away.Frame.PosY[2] <- -3.0f
            let pressure = AttackGeometry.situation ctx.Config.CollectiveAttack ClubSide.Home (AttackGeometry.position world.Home.Frame 5) world.Away.Frame
            Expect.equal pressure 4 "same territory, actually observed high pressure"
            world.Home.Attack.Estimates <- AttackLearning.add ctx.Config.CollectiveAttack (CollectiveAttackState.key 1 ThirdMan) -1.0 Map.empty
            let pressured = TeamAttack.candidates ctx world ClubSide.Home 5
            Expect.isNonEmpty openChoices "open alternatives"
            Expect.isNonEmpty pressured "response has alternatives"
            for candidate in pressured do Expect.equal candidate.Learned 0.0 "open-context failure is not unseen-pressure evidence")
        testCase "saturated prior commits chronological samples rather than resurrecting old evidence" (fun _ ->
            let cfg = BalanceConfig.defaultConfig.CollectiveAttack
            let prior = Map.ofList [ 4, ({ Samples = cfg.EvidenceCap; RewardSum = cfg.EvidenceCap }: AttackEstimate) ]
            let events = [ for id in 1UL .. 100UL -> { Tick = int id * 1<subtick>; Data = EventData.CollectiveAttack(ClubSide.Home, id, Ended(LostBall, -1.0, Some 4)) } ]
            let online = events |> List.fold (fun memory _ -> AttackLearning.add cfg 4 -1.0 memory) prior
            let committed = AttackLearning.applyEvents cfg ClubSide.Home prior events
            Expect.equal committed online "persistent policy must be exactly the policy learned during play")
        testCase "depth duties remain complementary after legal clamping" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            world.Home.Frame.PosX[5] <- 37.5f
            world.State <- { world.State with Ball = { world.State.Ball with Position = { world.State.Ball.Position with X = 37.5<meter> } } }
            world.Home.Frame.PosX[6] <- 37.0f; world.Home.Frame.PosY[6] <- -8.0f
            world.Home.Frame.PosX[7] <- 37.0f; world.Home.Frame.PosY[7] <- 8.0f
            world.Home.Frame.PosX[4] <- 29.0f; world.Home.Frame.PosY[4] <- 8.0f
            let candidates = TeamAttack.candidates ctx world ClubSide.Home 5
            Expect.isNonEmpty candidates "reachable opportunities at the actual clamp boundary, not vacuous safety"
            Expect.isTrue (candidates |> Array.exists (fun c -> c.Pattern = ExploitDepth)) "depth remains a feasible complementary mechanism"
            for candidate in candidates do
                let duties = [ candidate.Link; candidate.Runner; candidate.Security ]
                for a in duties do
                    for b in duties do
                        if a.Player <> b.Player then
                            Expect.isGreaterThanOrEqual (MatchMath.dist a.Target.X a.Target.Y b.Target.X b.Target.Y) (float ctx.Config.CollectiveAttack.MinimumDutySeparation) "different identities do not legitimize collapsed targets")
        testCase "runner receipt cannot be countered by a self-target lane" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            start ctx world |> ignore
            let e = episode world ClubSide.Home
            connection ctx world ClubSide.Home e.Carrier e.Link.Player 410<subtick>
            start ctx world |> ignore
            connection ctx world ClubSide.Home e.Link.Player e.Runner.Player 420<subtick>
            let runnerPoint = AttackGeometry.position world.Home.Frame e.Runner.Slot
            world.State <- { world.State with Ball = { world.State.Ball with Position = { world.State.Ball.Position with X = runnerPoint.X; Y = runnerPoint.Y } } }
            start ctx world |> ignore
            world.State <- { world.State with Tick = world.State.Tick + ctx.Config.CollectiveAttack.CounterHold }
            let b = start ctx world
            Expect.isFalse (b.Events |> Seq.exists (fun event -> match event.Data with EventData.CollectiveAttack(_, _, Ended(Countered, _, _)) -> true | _ -> false)) "exploiting means the runner owns the ball, not a pass to himself")
        testCase "checkpoint decoder rejects overflow nonfinite threat chronology stages identities and tactics" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            start ctx world |> ignore
            let state = world.Home.Attack
            let rejects state =
                use stream = new MemoryStream()
                use writer = new BinaryWriter(stream)
                AttackCheckpoint.write writer state
                writer.Flush()
                stream.Position <- 0L
                use reader = new BinaryReader(stream)
                Expect.throws (fun () -> AttackCheckpoint.read reader |> ignore) "malformed sporting state must not deserialize"
            rejects { state with Estimates = Map.ofList [ 4, { Samples = 1001.0; RewardSum = 0.0 } ] }
            let e = state.Episode.Value
            for invalid in [
                { e with Threat = System.Double.NaN }
                { e with Threat = 0.5 }
                { e with OwnedSpace = System.Double.PositiveInfinity }
                { e with FactTick = e.StartedAt - 1<subtick> }
                { e with Stage = Connected; Connections = 0 }
                { e with Link = e.Runner }
                { e with Owner = PlayerId -1 }
                { e with Tactics = {e.Tactics with Width = System.Single.NaN} }
                { e with LastFact = Some(EventId 0UL) }
            ] do rejects { state with Episode = Some invalid })
        testCase "production actions and physics supply causal milestones learning and exact checkpoint continuation" (fun _ ->
            let unwrap = function Ok value -> value | Error error -> failtestf "%A" error
            let ctx = TestBuilders.formationCtx ()
            let input = { MatchId = 1UL; Seed = 1UL; Context = ctx; EffectiveConfig = EffectiveConfig.defaultConfig; EngineVersion = "collective-contract" }
            let session = MatchSession.create Scheduler.systems input MatchRunMode.Fast |> unwrap
            while session.World.Home.Attack.Episode.IsNone && session.World.State.Tick < 8000<subtick> do
                MatchSession.advanceTo (session.World.State.Tick + 1<subtick>) session |> unwrap |> ignore
            Expect.isSome session.World.Home.Attack.Episode "actual scheduler creates a live commitment"
            let checkpoint = MatchSession.checkpoint session
            let bytes = MatchSession.serializeCheckpoint checkpoint
            let restored = bytes |> MatchSession.deserializeCheckpoint |> unwrap |> MatchSession.restore Scheduler.systems input |> unwrap
            Expect.equal restored.World.Home.Attack session.World.Home.Attack "exact active duties, factual frontier and learner"
            for version in [4; 5] do
                let obsolete = Array.copy bytes
                System.BitConverter.GetBytes(version).CopyTo(obsolete, 4)
                Expect.isError (MatchSession.deserializeCheckpoint obsolete) "old formats are obsolete, not migrated"
            let corrupt = { checkpoint with HomeAttack = { checkpoint.HomeAttack with NextId = checkpoint.HomeAttack.NextId + 1UL } }
            Expect.isError (MatchSession.restore Scheduler.systems input corrupt) "replay verifies collective identity"
            let overConfiguredCap = {checkpoint with HomeAttack = {checkpoint.HomeAttack with Estimates = Map.ofList [4, {Samples = ctx.Config.CollectiveAttack.EvidenceCap + 1.0; RewardSum = 0.0}]}}
            let decoded = overConfiguredCap |> MatchSession.serializeCheckpoint |> MatchSession.deserializeCheckpoint |> unwrap
            Expect.isError (MatchSession.restore Scheduler.systems input decoded) "decoder structural limit does not replace input-specific evidence cap"
            let finish (world: MatchSession) capture chunk =
                world.World.Observation.Enabled <- capture
                while world.World.State.Flow <> MatchFlow.MatchEnded do
                    MatchSession.advanceTo (min MatchSession.maxTicks (world.World.State.Tick + chunk)) world |> unwrap |> ignore
                MatchSession.finish world |> unwrap
            let a = finish session true 4000<subtick>
            let b = finish restored false 137<subtick>
            Expect.equal a.EventsHash b.EventsHash "capture and chunk size cannot decide football"
            Expect.equal a.Final.Home.Attack b.Final.Home.Attack "terminal learning and retirement are deterministic"
            Expect.isNone a.Final.Home.Attack.Episode "full-time cannot retain stale sporting commitments"
            let actual = a.Events |> List.choose (fun e -> match e.Data with EventData.CollectiveAttack(_, _, Advanced(_, cause)) -> Some cause | _ -> None)
            Expect.isNonEmpty actual "actual actions and physical receptions progress sustained episodes"
            for cause in actual do
                Expect.isTrue (a.Events |> List.exists (fun e -> MatchEvent.id e = cause && (match e.Data with EventData.Pass(_, Some _, _, _, true, _, _, _, _) -> true | _ -> false))) "each milestone names a real completed delivery"
            let evidence = AttackLearning.fromEvents ctx.Config.CollectiveAttack ClubSide.Home a.Events
            Expect.equal evidence a.Final.Home.Attack.Evidence "result extraction matches observation-independent learner"
            Expect.isNonEmpty evidence "real negative and positive outcomes supply new samples")
        testCase "four football alternatives have complementary identity-owned legal duties" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            let choices = TeamAttack.candidates ctx world ClubSide.Home 5
            Expect.equal (choices |> Array.map _.Pattern |> Set.ofArray) (Set.ofList [ ThirdMan; AttractSwitch; ExploitDepth; SecureProgression ]) "all patterns are feasible in this open geometry"
            for choice in choices do
                let duties = [| choice.Link; choice.Runner; choice.Security |]
                Expect.equal (duties |> Array.distinctBy _.Player |> Array.length) 3 "one player cannot consume two complementary roles"
                for duty in duties do
                    Expect.notEqual duty.Slot 5 "carrier is autonomous"
                    Expect.isLessThan (float duty.Target.X) (float (MatchWorld.offsideLine ctx world ClubSide.Home)) "legal target"
                Expect.isLessThan (float choice.Security.Target.X) 0.0 "protection remains behind the ball")
        testCase "duties physically reach steering without teleporting" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            start ctx world |> ignore
            let e = episode world ClubSide.Home
            world.Home.Frame.PosX[e.Runner.Slot] <- world.Home.Frame.PosX[e.Runner.Slot] - 5.0f
            let before = world.Home.Frame.PosX[e.Runner.Slot]
            let initialDistance = MatchMath.dist (float before * 1.0<meter>) (float world.Home.Frame.PosY[e.Runner.Slot] * 1.0<meter>) e.Runner.Target.X e.Runner.Target.Y
            let b = buffer ()
            PlayerIntent.step (systemContext ctx) (Rng.ofSeed 1UL) world b |> ignore
            Expect.equal world.Home.Frame.Intent.Kind[e.Runner.Slot] IntentCode.Support "physical support consumer"
            Expect.equal world.Home.Frame.Intent.TargetY[e.Runner.Slot] (float32 e.Runner.Target.Y) "complementary target consumed"
            for index in 1 .. 40 do
                world.State <- { world.State with Tick = 400<subtick> + index * 1<subtick> }
                let movementContext = systemContext ctx
                PlayerSteering.step movementContext (Rng.ofSeed 1UL) world b |> ignore
                ContactDynamics.step movementContext (Rng.ofSeed 1UL) world b |> ignore
            let finalDistance = MatchMath.dist (MatchWorld.posX world.Home.Frame e.Runner.Slot) (MatchWorld.posY world.Home.Frame e.Runner.Slot) e.Runner.Target.X e.Runner.Target.Y
            Expect.isLessThan finalDistance initialDistance "actual physical motion approaches the assigned target"
            Expect.isLessThan (abs (world.Home.Frame.PosX[e.Runner.Slot] - before)) (float32 ctx.Config.Movement.SprintSpeed) "one second cannot exceed physical speed")
        testCase "active attack duties exclude independent fallback supports" (fun _ ->
            for side in [ ClubSide.Home; ClubSide.Away ] do
                let ctx, world = scenario side
                start ctx world |> ignore
                let e = episode world side
                let team = MatchWorld.team world side
                PlayerIntent.step (systemContext ctx) (Rng.ofSeed 1UL) world (buffer ()) |> ignore
                for slot in MatchWorld.activeSlots team.Frame do
                    if slot <> e.Link.Slot && slot <> e.Runner.Slot && slot <> e.Security.Slot then
                        Expect.notEqual team.Frame.Intent.Kind[slot] IntentCode.Support "nonparticipants preserve shape rather than duplicate the team plan")
        testCase "a real pass fact advances once and own flight preserves identity" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            start ctx world |> ignore
            let e = episode world ClubSide.Home
            let receiver = if e.Pattern = ExploitDepth then e.Runner.Player else e.Link.Player
            world.State <- { world.State with Ball = { world.State.Ball with Control = BallControl.Free; Trajectory = Some(TrajectoryIntent.Aimed(e.Carrier, receiver, 0.8)); ArrivalGate = Some 440<subtick> } }
            start ctx world |> ignore
            Expect.equal (episode world ClubSide.Home).Id e.Id "own airborne pass is not a possession loss"
            connection ctx world ClubSide.Home e.Carrier receiver 440<subtick>
            start ctx world |> ignore
            let progressed = episode world ClubSide.Home
            Expect.equal progressed.Connections 1 "actual intermediate reception"
            Expect.notEqual progressed.Stage Preparing "stage advanced"
            start ctx world |> ignore
            Expect.equal (episode world ClubSide.Home).Connections 1 "same geometry supplies no duplicate credit"
            Expect.equal world.Home.Attack.Evidence Map.empty "unfinished plan does not learn per tick")
        testCase "unrelated receptions cannot manufacture milestones" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            start ctx world |> ignore
            let e = episode world ClubSide.Home
            connection ctx world ClubSide.Home e.Carrier world.Home.RosterIds[1] 410<subtick>
            start ctx world |> ignore
            Expect.isNone world.Home.Attack.Episode "unrelated circulation explicitly abandons the lineage rather than later earning spurious credit")
        testCase "held rival lane closure supplies negative evidence exactly once" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            start ctx world |> ignore
            let e = episode world ClubSide.Home
            let target = if e.Pattern = ExploitDepth then e.Runner else e.Link
            world.Away.Frame.PosX[1] <- world.Home.Frame.PosX[target.Slot] / 2.0f
            world.Away.Frame.PosY[1] <- world.Home.Frame.PosY[target.Slot] / 2.0f
            start ctx world |> ignore
            world.State <- { world.State with Tick = world.State.Tick + ctx.Config.CollectiveAttack.CounterHold }
            let ended = start ctx world
            Expect.isNone world.Home.Attack.Episode "rival counterplay invalidates the route"
            let key = CollectiveAttackState.key e.Context e.Pattern
            Expect.isLessThan (AttackLearning.value ctx.Config.CollectiveAttack key world.Home.Attack.Estimates) 0.0 "failure changes the next learned estimate"
            Expect.equal world.Home.Attack.Evidence[key].Samples 1.0 "one terminal sample"
            Expect.isTrue (ended.Events |> Seq.exists (fun event -> match event.Data with EventData.CollectiveAttack(_, _, Ended(Countered, _, Some _)) -> true | _ -> false)) "semantic counterplay evidence"
            start ctx world |> ignore
            Expect.equal world.Home.Attack.Evidence[key].Samples 1.0 "unchanged geometry cannot learn twice")
        testCase "negative realized evidence changes the next choice in unchanged geometry" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            start ctx world |> ignore
            let old = episode world ClubSide.Home
            world.State <- { world.State with Ball = { world.State.Ball with Control = BallControl.Controlled(ClubSide.Away, world.Away.RosterIds[5]) } }
            start ctx world |> ignore
            world.State <- { world.State with Tick = world.State.Tick + ctx.Config.CollectiveAttack.Recovery; Ball = { world.State.Ball with Control = BallControl.Controlled(ClubSide.Home, old.Carrier) } }
            start ctx world |> ignore
            Expect.notEqual (episode world ClubSide.Home).Pattern old.Pattern "negative experience redirects the contextual selection")
        testCase "identity exits restart and tactical changes clear commitments without inventing learning" (fun _ ->
            for reason in [ ParticipantLeft; InstructionsChanged; AttackEnd.Stopped ] do
                let ctx, world = scenario ClubSide.Home
                start ctx world |> ignore
                let e = episode world ClubSide.Home
                match reason with
                | ParticipantLeft -> EventApply.apply ctx world { Tick = world.State.Tick; Data = EventData.RedCardIssued e.Link.Player }
                | InstructionsChanged -> world.Home.Tactics <- { world.Home.Tactics with Width = 0.2f }
                | _ -> EventApply.apply ctx world { Tick = world.State.Tick; Data = EventData.SetPiece(SetPieceKind.ThrowIn, ClubSide.Home) }
                start ctx world |> ignore
                Expect.isNone world.Home.Attack.Episode "no stale duties"
                Expect.equal world.Home.Attack.Evidence Map.empty "administrative exit supplies no football reward")
        testCase "unchanged geometry does not spam episode evidence" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            let first = start ctx world
            let count = world.Observation.Count
            for _ in 1 .. 50 do
                let repeated = start ctx world
                Expect.equal repeated.Events.Count 0 "no repeated creation or fake milestone"
            Expect.isGreaterThan first.Events.Count 0 "creation is observable"
            Expect.equal world.Observation.Count count "evaluation capture is commitment-bound")
        testCase "mirrored opportunities retain identical values and targets" (fun _ ->
            let ctx, home = scenario ClubSide.Home
            let mirroredCtx = { ctx with AwayClub = { ctx.AwayClub with BaseTactics = ctx.HomeClub.BaseTactics }; AwayLineup = { ctx.AwayLineup with Profiles = ctx.HomeLineup.Profiles |> Array.mapi (fun i p -> { p with Id = PlayerId(101 + i) }) } }
            let away = scenarioWithContext mirroredCtx ClubSide.Away
            let a = TeamAttack.candidates ctx home ClubSide.Home 5
            let b = TeamAttack.candidates mirroredCtx away ClubSide.Away 5
            Expect.equal a.Length b.Length "same opportunities"
            for x, y in Array.zip a b do
                Expect.equal x.Pattern y.Pattern "same mechanism"
                Expect.floatClose Accuracy.high x.Score y.Score "same football value"
                Expect.equal x.Runner.Target.X (-y.Runner.Target.X) "mirror longitudinal target"
                Expect.equal x.Runner.Target.Y y.Runner.Target.Y "same lateral complement")
        testCase "fatigue and anticipation reduce opportunity confidence and arrival" (fun _ ->
            let ctx, fresh = scenario ClubSide.Home
            let tired = scenarioWithContext ctx ClubSide.Home
            Array.fill tired.Home.Frame.Condition 0 tired.Home.Frame.SlotCount 0.31f
            Array.fill tired.Home.Frame.Cognitive 0 tired.Home.Frame.SlotCount 0.3f
            let a = TeamAttack.candidates ctx fresh ClubSide.Home 5
            let b = TeamAttack.candidates ctx tired ClubSide.Home 5
            Expect.isGreaterThan a.Length 0 "fresh feasible candidates"
            for candidate in b do
                let matching = a |> Array.find (fun x -> x.Pattern = candidate.Pattern)
                Expect.isLessThan candidate.Geometry matching.Geometry "cognitive and physical limitations are not ignored"
            Expect.isLessThan (Planner.project ctx tired ClubSide.Home 5).Confidence (Planner.project ctx fresh ClubSide.Home 5).Confidence "individual anticipation also deteriorates")
        testCase "capture disabled changes neither commitments learning nor RNG" (fun _ ->
            let ctx, a = scenario ClubSide.Home
            let b = scenarioWithContext ctx ClubSide.Home
            b.Observation.Enabled <- false
            let seed = Rng.ofSeed 42UL
            let ra = TeamAttack.step (systemContext ctx) seed a (buffer ())
            let rb = TeamAttack.step (systemContext ctx) seed b (buffer ())
            Expect.equal ra rb "no observation-dependent draws"
            Expect.equal a.Home.Attack b.Home.Attack "same sporting state"
            for world in [ a; b ] do
                world.State <- { world.State with Ball = { world.State.Ball with Control = BallControl.Controlled(ClubSide.Away, world.Away.RosterIds[5]) } }
                start ctx world |> ignore
            Expect.equal a.Home.Attack b.Home.Attack "same actual negative evidence")
        testCase "bounded memory forgets slowly and seasonal retention reduces confidence" (fun _ ->
            let cfg = BalanceConfig.defaultConfig.CollectiveAttack
            let mutable memory = Map.empty
            for i in 1 .. 10000 do memory <- AttackLearning.add cfg (i % 24) -0.5 memory
            Expect.equal memory.Count 24 "fixed territory-pressure contextual vocabulary"
            for KeyValue(_, estimate) in memory do Expect.isLessThanOrEqual estimate.Samples cfg.EvidenceCap "bounded sufficient statistics"
            let retained = AttackLearning.retain cfg memory
            Expect.isLessThan retained[0].Samples memory[0].Samples "season turnover weakens old certainty"
            Expect.floatClose Accuracy.high (FootballEngine.Domain.AttackEstimate.mean retained[0]) -0.5 "retention does not rewrite outcomes")
        testCase "checkpoint payload owns exact bounded collective state" (fun _ ->
            let ctx, world = scenario ClubSide.Home
            start ctx world |> ignore
            use stream = new MemoryStream()
            use writer = new BinaryWriter(stream)
            AttackCheckpoint.write writer world.Home.Attack
            writer.Flush()
            Expect.isLessThan stream.Length 2048L "bounded commitment payload"
            stream.Position <- 0L
            use reader = new BinaryReader(stream)
            Expect.equal (AttackCheckpoint.read reader) world.Home.Attack "exact learner and duties")
        testCase "obsolete planner contract and invalid collective numbers are rejected" (fun _ ->
            Expect.isError (WeightMerge.mergeWithDefaults """{"Planner":{"PlanTTL":480}}""" BalanceConfig.defaultConfig) "removed plan field is not a silent compatibility adapter"
            let cfg = BalanceConfig.defaultConfig
            Expect.isError (ConfigValidation.validateAll { cfg with CollectiveAttack = { cfg.CollectiveAttack with CooperationWeight = System.Double.NaN } }) "non-finite cooperation rejected")
    ]
