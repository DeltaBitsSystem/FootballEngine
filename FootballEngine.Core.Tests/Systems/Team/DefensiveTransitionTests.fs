namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module DefensiveTransitionTests =
    let private decide ctx world =
        TeamTransition.step (DefensiveTransitionFixtures.systemContext ctx) (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore

    let private episode (world: World) = world.Home.Defense.Episode |> Option.get

    let private response ctx world =
        let context = DefensiveTransitionFixtures.systemContext ctx
        let buffer = DefensiveTransitionFixtures.buffer ()
        for step in [ TeamPerceive.step; TeamTransition.step; TeamPosition.step; PlayerIntent.step ] do
            step context (Rng.ofSeed 1UL) world buffer |> ignore

    let private invariants (world: World) side =
        let team = MatchWorld.team world side
        let frame = team.Frame
        Expect.isLessThanOrEqual (frame.DefensiveRoles |> Array.filter ((=) DefensiveRole.FirstDefender) |> Array.length) 1 "single primary"
        for slot in 0 .. frame.SlotCount - 1 do
            if not (MatchWorld.isActive frame slot) then
                Expect.equal frame.DefensiveRoles[slot] DefensiveRole.Unassigned "inactive slot has no role"
                Expect.isNone team.Defense.Tasks[slot] "inactive slot has no task"
            match team.Defense.Tasks[slot] with
            | Some task ->
                Expect.isTrue (TransitionGeometry.outfielder frame slot) "only active outfielders participate"
                Expect.equal frame.DefensiveRoles[slot] task.Role "one responsibility"
                Expect.equal frame.Intent.TargetX[slot] (float32 task.Target.X) "task reaches movement"
                Expect.equal frame.Intent.TargetY[slot] (float32 task.Target.Y) "task reaches movement"
                Expect.isLessThanOrEqual (abs (float task.Target.X)) (float Pitch.HalfLength - 2.0) "legal X"
                Expect.isLessThanOrEqual (abs (float task.Target.Y)) (float Pitch.HalfWidth - 2.0) "legal Y"
                Expect.isFalse (frame.RunLookup.ContainsKey slot) "no orphaned attacking assignment"
            | None -> ()

    let private unwrap = function Ok value -> value | Error error -> failtestf "unexpected contract error: %A" error

    let private matchInput () : MatchInput =
        { MatchId = 941UL; Seed = 941UL; Context = TestBuilders.formationCtx ()
          EffectiveConfig = EffectiveConfig.defaultConfig; EngineVersion = "defensive-transition-corpus" }

    let private inTransition (session: MatchSession) =
        [ session.World.Home; session.World.Away ] |> List.exists (fun team ->
            team.Defense.Episode |> Option.exists (fun episode -> episode.EndedAt.IsNone && episode.Mode <> Assessing))

    let private transitionSession () =
        let session = MatchSession.create Scheduler.systems (matchInput ()) MatchRunMode.Fast |> unwrap
        // The horizon only bounds the search for a natural episode; controlled receptions keep the opening
        // possession longer, so this seed's first transition now starts after ~13.5k subticks.
        while not (inTransition session) && session.World.State.Tick < 24000<subtick> do
            MatchSession.advanceTo (session.World.State.Tick + 1<subtick>) session |> unwrap |> ignore
        Expect.isTrue (inTransition session) "the real sporting pipeline must produce an episode"
        session

    let private fingerprint (session: MatchSession) =
        session.World.State, session.Rng, Array.ofSeq session.Events,
        DefensiveCheckpoint.snapshot session.World.Home, DefensiveCheckpoint.snapshot session.World.Away,
        Array.copy session.World.Home.Frame.PosX, Array.copy session.World.Home.Frame.PosY,
        Array.copy session.World.Away.Frame.PosX, Array.copy session.World.Away.Frame.PosY,
        Array.copy session.World.Home.Frame.Condition, Array.copy session.World.Away.Frame.Condition

    let tests =
        testList "DefensiveTransitions"
            [ testCase "organized defense assigns coherent movement on both sides" (fun _ ->
                  for side in [ ClubSide.Home; ClubSide.Away ] do
                      let ctx, world = DefensiveTransitionFixtures.scenario side false 1.0f
                      let team = MatchWorld.team world side
                      let opponent = MatchWorld.team world (MatchWorld.oppOf side)
                      team.Defense.Episode <- None
                      team.Tactics <- { team.Tactics with PressIntensity = 1.0f; PressTriggerZone = 1.0f }
                      world.State <-
                          { world.State with
                              Ball = { world.State.Ball with Control = BallControl.Controlled(MatchWorld.oppOf side, opponent.RosterIds[9]) } }
                      let system = DefensiveTransitionFixtures.systemContext ctx
                      TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                      PlayerIntent.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                      invariants world side
                      let snapshot = DefensiveCheckpoint.snapshot team
                      use stream = new System.IO.MemoryStream()
                      use writer = new System.IO.BinaryWriter(stream)
                      DefensiveCheckpoint.write writer snapshot
                      writer.Flush()
                      stream.Position <- 0L
                      use reader = new System.IO.BinaryReader(stream)
                      Expect.equal (DefensiveCheckpoint.read reader) snapshot "organized tasks survive checkpoint encoding"
                      let primary = MatchWorld.activeSlots team.Frame |> Array.filter (fun slot -> team.Frame.DefensiveRoles[slot] = DefensiveRole.FirstDefender)
                      Expect.equal primary.Length 1 "one primary engages a controlled ball"
                      Expect.equal team.Frame.Intent.Kind[primary[0]] IntentCode.Press "the assigned player presses"
                      Expect.equal (team.Frame.DefensiveRoles |> Array.filter ((=) DefensiveRole.Cover) |> Array.length) 1 "one player covers"
                      let initial = primary[0]
                      team.Frame.PosY[initial] <- team.Frame.PosY[initial] + 0.01f
                      TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                      Expect.equal team.Frame.DefensiveRoles[initial] DefensiveRole.FirstDefender "small perturbation keeps responsibility"
                      opponent.Frame.PosX[8] <- float32 (-45.0 * MatchMath.goalDirection side)
                      opponent.Frame.PosY[8] <- 0.0f
                      TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                      Expect.isFalse (team.Frame.DefensiveRoles |> Array.contains DefensiveRole.FirstDefender) "deep threat takes priority over pressure")
              testCase "organized defense respects tactical restraint and clears after recovery" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  let team = world.Home
                  team.Defense.Episode <- None
                  team.Tactics <- { team.Tactics with PressIntensity = 0.0f }
                  world.State <-
                      { world.State with
                          Ball = { world.State.Ball with Control = BallControl.Controlled(ClubSide.Away, world.Away.RosterIds[9]) } }
                  let system = DefensiveTransitionFixtures.systemContext ctx
                  TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                  Expect.isFalse (team.Frame.DefensiveRoles |> Array.contains DefensiveRole.FirstDefender) "low press holds shape"
                  world.State <-
                      { world.State with
                          Ball = { world.State.Ball with Control = BallControl.Controlled(ClubSide.Home, team.RosterIds[9]) } }
                  TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                  Expect.isTrue (team.Defense.Tasks |> Array.forall Option.isNone) "recovery clears defensive tasks")
              testCase "organized defense records changed responsibilities without capture effects" (fun _ ->
                  let ctx, captured = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  let _, silent = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  for world, enabled in [ captured, true; silent, false ] do
                      world.Observation.Enabled <- enabled
                      world.Home.Defense.Episode <- None
                      world.Home.Tactics <- { world.Home.Tactics with PressIntensity = 1.0f; PressTriggerZone = 1.0f }
                      world.State <- { world.State with Ball = { world.State.Ball with Control = BallControl.Controlled(ClubSide.Away, world.Away.RosterIds[9]) } }
                      let system = DefensiveTransitionFixtures.systemContext ctx
                      TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                      TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                      world.Away.Frame.PosX[8] <- -45.0f
                      world.Away.Frame.PosY[8] <- 0.0f
                      TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                  let decisions =
                      captured.Observation.Snapshot().Entries
                      |> Array.choose (fun entry -> match entry.Data with OrganizedDefenseEvaluated decision when decision.Side = ClubSide.Home -> Some decision | _ -> None)
                  Expect.equal decisions.Length 2 "only initial assignment and threat-driven release produce evidence"
                  Expect.equal decisions[0].Reason OrganizedPressure "initial reason"
                  Expect.equal decisions[1].Reason OrganizedDepthThreat "release reason"
                  Expect.equal captured.Home.Defense.Tasks silent.Home.Defense.Tasks "capture does not alter tasks"
                  Expect.equal captured.Home.Frame.DefensiveRoles silent.Home.Frame.DefensiveRoles "capture does not alter assignments")
              testCase "organized defense checkpoint restores a real session frontier" (fun _ ->
                  let input = matchInput ()
                  let session = MatchSession.create Scheduler.systems input MatchRunMode.Fast |> unwrap
                  let organized () =
                      [ ClubSide.Home; ClubSide.Away ] |> List.exists (fun side ->
                          let team = MatchWorld.team session.World side
                          (team.Defense.Episode |> Option.forall (fun episode -> episode.EndedAt.IsSome))
                          && (team.Defense.Tasks |> Array.exists Option.isSome)
                          && (match MatchWorld.ballCarrier input.Context session.World with
                              | Some (owner, _, _) -> owner <> side
                              | None -> false))
                  while not (organized ()) && session.World.State.Tick < 40000<subtick> do
                      MatchSession.advanceTo (session.World.State.Tick + 8<subtick>) session |> unwrap |> ignore
                  Expect.isTrue (organized ()) "production scheduler reaches an organized defensive frontier"
                  let checkpoint = MatchSession.checkpoint session
                  let restored = checkpoint |> MatchSession.serializeCheckpoint |> MatchSession.deserializeCheckpoint |> unwrap
                                 |> MatchSession.restore Scheduler.systems input |> unwrap
                  Expect.equal (fingerprint restored) (fingerprint session) "exact tasks and sporting state at checkpoint"
                  let target = session.World.State.Tick + 400<subtick>
                  MatchSession.advanceTo target session |> unwrap |> ignore
                  while restored.World.State.Tick < target do
                      MatchSession.advanceTo (min target (restored.World.State.Tick + 7<subtick>)) restored |> unwrap |> ignore
                  Expect.equal (fingerprint restored) (fingerprint session) "continuation is chunk-independent")
              testCase "organized defense capture leaves a real session deterministic" (fun _ ->
                  let input = matchInput ()
                  let captured = MatchSession.create Scheduler.systems input MatchRunMode.Fast |> unwrap
                  let silent = MatchSession.create Scheduler.systems input MatchRunMode.Fast |> unwrap
                  captured.World.Observation.Enabled <- true
                  silent.World.Observation.Enabled <- false
                  MatchSession.advanceTo 12000<subtick> captured |> unwrap |> ignore
                  MatchSession.advanceTo 12000<subtick> silent |> unwrap |> ignore
                  Expect.isTrue (captured.World.Observation.Snapshot().Entries |> Array.exists (fun entry -> match entry.Data with OrganizedDefenseEvaluated _ -> true | _ -> false)) "the measured path was reached"
                  Expect.equal (fingerprint captured) (fingerprint silent) "capture cannot change tasks events state or RNG")
              testCase "scheduler physically advances an organized primary toward its target" (fun _ ->
                  for side in [ ClubSide.Home; ClubSide.Away ] do
                      let ctx, geometry = DefensiveTransitionFixtures.scenario side false 1.0f
                      let world = MatchFactory.create ctx
                      for currentSide in [ ClubSide.Home; ClubSide.Away ] do
                          let source = (MatchWorld.team geometry currentSide).Frame
                          let destination = (MatchWorld.team world currentSide).Frame
                          System.Array.Copy(source.PosX, destination.PosX, source.SlotCount)
                          System.Array.Copy(source.PosY, destination.PosY, source.SlotCount)
                          System.Array.Copy(source.Condition, destination.Condition, source.SlotCount)
                      let team = MatchWorld.team world side
                      let opponentSide = MatchWorld.oppOf side
                      let opponent = MatchWorld.team world opponentSide
                      let dir = MatchMath.goalDirection side
                      team.Frame.PosX[9] <- float32 (-2.0 * dir)
                      team.Frame.PosX[5] <- float32 (-4.0 * dir)
                      team.Tactics <- { team.Tactics with PressIntensity = 1.0f; PressTriggerZone = 1.0f }
                      world.State <- { world.State with Flow = MatchFlow.Live; Ball = TestBuilders.emptyBall }
                      let buffer = DefensiveTransitionFixtures.buffer ()
                      buffer.Events.Add { Tick = world.State.Tick; Data = EventData.Possession(opponentSide, opponent.RosterIds[9]) }
                      let schedule = ScheduleCompiler.compile Scheduler.systems
                      let context = DefensiveTransitionFixtures.systemContext ctx
                      let output = Array.zeroCreate<MatchEvent> 256
                      let initialRng, _, _ = Stepper.stepInto schedule context (Rng.ofSeed 1UL) world buffer output
                      let primary = MatchWorld.activeSlots team.Frame |> Array.find (fun slot -> team.Frame.DefensiveRoles[slot] = DefensiveRole.FirstDefender)
                      Expect.equal (team.Frame.DefensiveRoles |> Array.filter ((=) DefensiveRole.Cover) |> Array.length) 1 "one cover supports the initial press"
                      Expect.equal (team.Frame.DefensiveRoles |> Array.filter ((=) DefensiveRole.ProtectDepth) |> Array.length) 2 "two players protect depth"
                      let target = team.Defense.Tasks[primary].Value.Target
                      let distance () = MatchMath.dist (MatchWorld.posX team.Frame primary) (MatchWorld.posY team.Frame primary) target.X target.Y
                      let initialDistance = distance ()
                      let mutable minimum = initialDistance
                      let mutable rng = initialRng
                      let mutable tracking = true
                      for _ in 1 .. 20 do
                          let next, _, _ = Stepper.stepInto schedule context rng world buffer output
                          rng <- next
                          if team.Frame.DefensiveRoles[primary] <> DefensiveRole.FirstDefender then tracking <- false
                          if tracking then minimum <- min minimum (distance ())
                      Expect.isLessThan minimum initialDistance "real scheduler physics closes distance while the same primary is assigned")
              testCase "organized defense releases duties on loose ball restart and tactics" (fun _ ->
                  for interruption in [ 0; 1; 2 ] do
                      let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                      world.Home.Defense.Episode <- None
                      world.Home.Tactics <- { world.Home.Tactics with PressIntensity = 1.0f; PressTriggerZone = 1.0f }
                      world.State <- { world.State with Ball = { world.State.Ball with Control = BallControl.Controlled(ClubSide.Away, world.Away.RosterIds[9]) } }
                      let system = DefensiveTransitionFixtures.systemContext ctx
                      TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                      Expect.isTrue (world.Home.Frame.DefensiveRoles |> Array.contains DefensiveRole.FirstDefender) "fixture begins in pressure"
                      if interruption = 0 then
                          world.State <- { world.State with Ball = { world.State.Ball with Control = BallControl.Free } }
                          TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                          Expect.isTrue (world.Home.Defense.Tasks |> Array.forall Option.isNone) "loose-ball roles replace controlled-ball tasks"
                      elif interruption = 1 then
                          EventApply.apply ctx world { Tick = 402<subtick>; Data = EventData.SetPiece(SetPieceKind.ThrowIn, ClubSide.Home) }
                          Expect.isTrue (world.Home.Defense.Tasks |> Array.forall Option.isNone) "restart reducer clears duties immediately"
                      else
                          world.Home.Tactics <- { world.Home.Tactics with PressIntensity = 0.0f }
                          TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                          Expect.isFalse (world.Home.Frame.DefensiveRoles |> Array.contains DefensiveRole.FirstDefender) "new tactical instruction releases pressure")
              testCase "organized defense removes departed primary and reassigns" (fun _ ->
                  for injury in [ false; true ] do
                      let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                      world.Home.Defense.Episode <- None
                      world.Home.Tactics <- { world.Home.Tactics with PressIntensity = 1.0f; PressTriggerZone = 1.0f }
                      world.State <- { world.State with Ball = { world.State.Ball with Control = BallControl.Controlled(ClubSide.Away, world.Away.RosterIds[9]) } }
                      let system = DefensiveTransitionFixtures.systemContext ctx
                      TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                      let old = MatchWorld.activeSlots world.Home.Frame |> Array.find (fun slot -> world.Home.Frame.DefensiveRoles[slot] = DefensiveRole.FirstDefender)
                      let data = if injury then EventData.Injury(world.Home.RosterIds[old], 1) else EventData.RedCardIssued world.Home.RosterIds[old]
                      EventApply.apply ctx world { Tick = 402<subtick>; Data = data }
                      Expect.isNone world.Home.Defense.Tasks[old] "reducer releases the departed slot immediately"
                      TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                      Expect.equal world.Home.Frame.DefensiveRoles[old] DefensiveRole.Unassigned "departed player cannot regain a duty")
              testCase "organized defense releases substituted identity" (fun _ ->
                  let original = TestBuilders.formationCtx ()
                  let lineup = original.HomeLineup
                  let lineup = { lineup with Profiles = Array.append lineup.Profiles [| TestBuilders.makeProfile 500 SlotRole.Striker |]
                                             BasePositions = Array.append lineup.BasePositions [| lineup.BasePositions[9] |]; SlotCount = 12 }
                  let ctx = { original with HomeLineup = lineup; HomeParticipants = MatchLineupBoundary.fromLineup lineup }
                  let ctx, world = DefensiveTransitionFixtures.scenarioWithContext ctx ClubSide.Home false 1.0f
                  world.Home.Defense.Episode <- None
                  world.Home.Tactics <- { world.Home.Tactics with PressIntensity = 1.0f; PressTriggerZone = 1.0f }
                  world.State <- { world.State with Ball = { world.State.Ball with Control = BallControl.Controlled(ClubSide.Away, world.Away.RosterIds[9]) } }
                  let system = DefensiveTransitionFixtures.systemContext ctx
                  TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                  let old = MatchWorld.activeSlots world.Home.Frame |> Array.find (fun slot -> world.Home.Frame.DefensiveRoles[slot] = DefensiveRole.FirstDefender)
                  EventApply.apply ctx world { Tick = 402<subtick>; Data = EventData.Substitution(world.Home.RosterIds[old], PlayerId 500) }
                  Expect.isNone world.Home.Defense.Tasks[old] "old identity is released immediately"
                  TeamPosition.step system (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                  Expect.equal world.Home.Frame.DefensiveRoles[old] DefensiveRole.Unassigned "outgoing slot cannot regain a duty"
                  Expect.isTrue (MatchWorld.isActive world.Home.Frame 11) "incoming slot participates by its own identity")
              testCase "unchanged possession cannot manufacture transition episodes" (fun _ ->
                  let ctx = TestBuilders.formationCtx ()
                  let world = MatchFactory.create ctx
                  world.State <- { world.State with Flow = MatchFlow.Live; Tick = 400<subtick> }
                  EventApply.apply ctx world { Tick = 400<subtick>; Data = EventData.Possession(ClubSide.Home, world.Home.RosterIds[9]) }
                  for tick in [ 400; 404; 644; 648; 888; 892 ] do
                      world.State <- { world.State with Tick = tick * 1<subtick> }
                      TeamPerceive.step (DefensiveTransitionFixtures.systemContext ctx) (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                  Expect.equal world.Home.Blackboard.OurPhase PhaseKind.Attacking "no loss/recovery happened")
              testCase "real loss is a transition on its first shared evaluation" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  TeamPerceive.step (DefensiveTransitionFixtures.systemContext ctx) (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                  Expect.equal world.Home.Blackboard.OurPhase PhaseKind.TransitionDefend "loss fact, not next blackboard refresh")
              testCase "departed players cannot retain defensive responsibilities" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  world.Home.Frame.DefensiveRoles[6] <- DefensiveRole.Cover
                  EventApply.apply ctx world { Tick = 402<subtick>; Data = EventData.RedCardIssued world.Home.RosterIds[6] }
                  TeamPosition.step (DefensiveTransitionFixtures.systemContext ctx) (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                  Expect.equal world.Home.Frame.DefensiveRoles[6] DefensiveRole.Unassigned "participation invalidates ownership")
              testCase "contested ball and same-side touches preserve one loss" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  let episode = world.Home.Defense.Episode
                  for tick in 402 .. 406 do
                      EventApply.apply ctx world { Tick = tick * 1<subtick>; Data = EventData.BallLoose(0.0<meter>, 0.0<meter>) }
                      EventApply.apply ctx world { Tick = tick * 1<subtick>; Data = EventData.Possession(ClubSide.Away, world.Away.RosterIds[9]) }
                  Expect.equal world.Home.Defense.Episode episode "a touch is not another loss")
              testCase "recovery and a new loss are separate factual episodes" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  let first = world.Home.Defense.Episode |> Option.get
                  EventApply.apply ctx world { Tick = 402<subtick>; Data = EventData.Possession(ClubSide.Home, world.Home.RosterIds[6]) }
                  Expect.equal (world.Home.Defense.Episode |> Option.bind _.Outcome) (Some TransitionEnd.Recovered) "real recovery"
                  EventApply.apply ctx world { Tick = 403<subtick>; Data = EventData.Possession(ClubSide.Away, world.Away.RosterIds[9]) }
                  let second = world.Home.Defense.Episode |> Option.get
                  Expect.notEqual first.Id second.Id "distinct sporting identity"
                  Expect.equal second.LostAt 403<subtick> "no phase timer"
                  Expect.equal (world.Away.Defense.Episode |> Option.bind _.Outcome) (Some TransitionEnd.Recovered) "other side recovers")
              testCase "restart interrupts and the first acquisition is not another loss" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  EventApply.apply ctx world { Tick = 402<subtick>; Data = EventData.SetPiece(SetPieceKind.ThrowIn, ClubSide.Home) }
                  Expect.equal (world.Home.Defense.Episode |> Option.bind _.Outcome) (Some TransitionEnd.Interrupted) "dead ball ends the race"
                  world.State <- { world.State with Flow = MatchFlow.Live }
                  EventApply.apply ctx world { Tick = 403<subtick>; Data = EventData.Possession(ClubSide.Home, world.Home.RosterIds[6]) }
                  Expect.isNone world.Away.Defense.Episode "restart control is acquisition")
              testCase "defensive checkpoint owns exact episode payload" (fun _ ->
                  let _, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  let snapshot = DefensiveCheckpoint.snapshot world.Home
                  use stream = new System.IO.MemoryStream()
                  use writer = new System.IO.BinaryWriter(stream)
                  DefensiveCheckpoint.write writer snapshot
                  writer.Flush()
                  stream.Position <- 0L
                  use reader = new System.IO.BinaryReader(stream)
                  Expect.equal (DefensiveCheckpoint.read reader) snapshot "no reconstructed phase or commitment"
                  world.Home.Defense.Tasks[5] <- Some { Role = DefensiveRole.Cover; Target = Vec2.create 1.0<meter> 2.0<meter>; Opponent = None }
                  Expect.isNone snapshot.Tasks[5] "checkpoint arrays are caller-independent")
              testCase "local superiority and cover make counterpress viable" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  decide ctx world
                  Expect.equal (episode world).Mode Counterpress "prepared local unit"
                  Expect.equal (episode world).Reason LocalAdvantage "measured viability"
                  Expect.isSome (episode world).Cover "pressure is backed up")
              testCase "open team with deep runner retreats instead of chasing" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home true 1.0f
                  decide ctx world
                  Expect.equal (episode world).Mode Retreat "protect the goal first"
                  Expect.equal (episode world).Reason DepthThreat "unprotected runner is causal"
                  Expect.isNone (episode world).Primary "retreat is not a slower chase")
              testCase "same exhausted unit cannot commit like a fresh unit" (fun _ ->
                  let ctx, fresh = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  let _, tired = DefensiveTransitionFixtures.scenario ClubSide.Home false 0.3f
                  decide ctx fresh
                  decide ctx tired
                  Expect.equal (episode fresh).Mode Counterpress "fresh support"
                  Expect.equal (episode tired).Mode Retreat "insufficient repeat-sprint capacity"
                  let target = Vec2.create (-8.0<meter>) 0.0<meter>
                  Expect.isGreaterThan (TransitionGeometry.arrivalTime ctx tired.Home.Frame 9 target)
                      (TransitionGeometry.arrivalTime ctx fresh.Home.Frame 9 target) "actual fatigue speed is used")
              testCase "equal candidates choose one deterministic primary and keep it" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  world.Home.Frame.PosX[9] <- 20.0f
                  decide ctx world
                  let first = (episode world).Primary
                  Expect.equal first (Some 6) "slot breaks a true time tie"
                  for tick in 402 .. 440 do
                      world.State <- { world.State with Tick = tick * 1<subtick> }
                      decide ctx world
                      Expect.equal (episode world).Primary first "no geometric cause for a handoff")
              testCase "tactical restraint overrides a commitment without oscillation" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  decide ctx world
                  world.Home.Tactics <- { world.Home.Tactics with PressIntensity = 0.1f }
                  decide ctx world
                  Expect.equal (episode world).Mode Retreat "effective instruction takes precedence"
                  Expect.equal (episode world).Reason TacticalRestraint "instruction explains the switch"
                  world.Home.Tactics <- { world.Home.Tactics with PressIntensity = 1.0f }
                  decide ctx world
                  Expect.equal (episode world).Mode Retreat "a new loss is required to start a new counterpress")
              testCase "a possession fact wakes the whole response off cadence" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  world.Home.Defense.Episode <- None
                  world.State <- { world.State with Tick = 403<subtick>; Possession = { world.State.Possession with LastGainer = Some ClubSide.Home } }
                  let buffer = DefensiveTransitionFixtures.buffer ()
                  buffer.Events.Add { Tick = 403<subtick>; Data = EventData.Possession(ClubSide.Away, world.Away.RosterIds[9]) }
                  Stepper.step Scheduler.systems (DefensiveTransitionFixtures.systemContext ctx) (Rng.ofSeed 1UL) world buffer |> ignore
                  Expect.equal (episode world).Mode Counterpress "not deferred until a minute or fourth tick"
                  Expect.equal (episode world).EvaluatedAt 403<subtick> "same entry drain")
              testCase "transition configuration rejects non-finite and inconsistent commitments" (fun _ ->
                  let config = BalanceConfig.defaultConfig
                  for transition in [ { config.DefensiveTransition with BaseArrivalWindow = nan * 1.0<second> }
                                      { config.DefensiveTransition with ReevaluateTicks = 0<tickDelta> }
                                      { config.DefensiveTransition with Commitment = 2000<subtick> } ] do
                      Expect.isError (ConfigValidation.validateAll { config with DefensiveTransition = transition }) "invalid tactical contract")
              testCase "coordinated lanes protection and pressure all reach movement" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  world.Away.Frame.PosX[6] <- 6.0f; world.Away.Frame.PosY[6] <- -8.0f
                  world.Away.Frame.PosX[7] <- 6.0f; world.Away.Frame.PosY[7] <- 8.0f
                  response ctx world
                  invariants world ClubSide.Home
                  let frame = world.Home.Frame
                  Expect.equal (episode world).Mode Counterpress "a four-player unit contains three opponents"
                  Expect.equal (frame.Intent.Kind |> Array.filter ((=) IntentCode.Press) |> Array.length) 1 "not a swarm"
                  Expect.equal (frame.DefensiveRoles |> Array.filter ((=) DefensiveRole.Screen) |> Array.length) 2 "two reachable outlets are screened"
                  Expect.equal (frame.DefensiveRoles |> Array.filter ((=) DefensiveRole.ProtectDepth) |> Array.length) 2 "depth is reserved"
                  let before = Array.copy frame.PosX
                  let movementContext = DefensiveTransitionFixtures.systemContext ctx
                  let movementEvents = DefensiveTransitionFixtures.buffer ()
                  CognitionLatch.step movementContext (Rng.ofSeed 0UL) world movementEvents |> ignore
                  PlayerSteering.step movementContext (Rng.ofSeed 1UL) world movementEvents |> ignore
                  ContactDynamics.step movementContext (Rng.ofSeed 1UL) world movementEvents |> ignore
                  Expect.notEqual frame.PosX before "targets produce real motion")
              testCase "retreat protects the centre and has an attacking opportunity cost" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home true 1.0f
                  response ctx world
                  invariants world ClubSide.Home
                  Expect.isFalse (world.Home.Frame.Intent.Kind |> Array.exists ((=) IntentCode.Press)) "nobody chases"
                  for slot in MatchWorld.activeSlots world.Home.Frame do
                      if slot <> 0 then Expect.isLessThan world.Home.Frame.Intent.TargetX[slot] 0.0f "forward outlet is surrendered"
                  Expect.equal world.Home.Frame.Intent.Kind[0] IntentCode.HoldPosition "keeper still owns goal positioning"
                  let before = world.Home.Frame.PosX[0]
                  let movementContext = DefensiveTransitionFixtures.systemContext ctx
                  let movementEvents = DefensiveTransitionFixtures.buffer ()
                  CognitionLatch.step movementContext (Rng.ofSeed 0UL) world movementEvents |> ignore
                  PlayerSteering.step movementContext (Rng.ofSeed 1UL) world movementEvents |> ignore
                  ContactDynamics.step movementContext (Rng.ofSeed 1UL) world movementEvents |> ignore
                  Expect.notEqual world.Home.Frame.PosX[0] before "keeper is not frozen")
              testCase "beaten primary hands off to cover without exposing the depth pair" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  response ctx world
                  let first, cover = (episode world).Primary.Value, (episode world).Cover.Value
                  world.State <- { world.State with Tick = 404<subtick>; Ball = { world.State.Ball with Position = { world.State.Ball.Position with X = -4.0<meter> } } }
                  world.Home.Frame.PosX[6] <- -8.0f
                  world.Home.Frame.PosX[7] <- -10.0f
                  response ctx world
                  Expect.notEqual (episode world).Primary (Some first) "beaten player is not reappointed"
                  Expect.equal (episode world).Primary (Some cover) "the available cover takes the contact"
                  Expect.isSome (episode world).Cover "new cover backs the handoff"
                  invariants world ClubSide.Home)
              testCase "a broken pressure exits into retreat and reports failure not recovery" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  response ctx world
                  world.State <- { world.State with Tick = 405<subtick>; Ball = { world.State.Ball with Position = { world.State.Ball.Position with X = -15.0<meter> } } }
                  response ctx world
                  Expect.equal (episode world).Mode Retreat "ball escaped the local unit"
                  Expect.equal (episode world).Reason PressureBroken "sporting failure"
                  Expect.isNone (episode world).Primary "chase is abandoned"
                  invariants world ClubSide.Home)
              testCase "injury and dismissal invalidate the primary before another task is consumed" (fun _ ->
                  for injury in [ false; true ] do
                      let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                      response ctx world
                      let old = (episode world).Primary.Value
                      let data = if injury then EventData.Injury(world.Home.RosterIds[old], 1) else EventData.RedCardIssued world.Home.RosterIds[old]
                      EventApply.apply ctx world { Tick = 402<subtick>; Data = data }
                      response ctx world
                      Expect.notEqual (episode world).Primary (Some old) "availability is a fact, not a timer"
                      invariants world ClubSide.Home)
              testCase "a substitute takes an available responsibility with its own identity" (fun _ ->
                  let original = TestBuilders.formationCtx ()
                  let lineup = original.HomeLineup
                  let lineup = { lineup with Profiles = Array.append lineup.Profiles [| TestBuilders.makeProfile 500 SlotRole.Striker |]
                                             BasePositions = Array.append lineup.BasePositions [| lineup.BasePositions[9] |]; SlotCount = 12 }
                  let ctx = { original with HomeLineup = lineup; HomeParticipants = MatchLineupBoundary.fromLineup lineup }
                  let ctx, world = DefensiveTransitionFixtures.scenarioWithContext ctx ClubSide.Home false 1.0f
                  response ctx world
                  let old = (episode world).Primary.Value
                  EventApply.apply ctx world { Tick = 402<subtick>; Data = EventData.Substitution(world.Home.RosterIds[old], PlayerId 500) }
                  response ctx world
                  Expect.equal (episode world).Primary (Some 11) "incoming player is not confused with the outgoing slot"
                  invariants world ClubSide.Home)
              testCase "mirrored sides preserve the local decision and contact coverage geometry" (fun _ ->
                  let ctx, home = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  let _, away = DefensiveTransitionFixtures.scenario ClubSide.Away false 1.0f
                  away.Away.Tactics <- home.Home.Tactics
                  response ctx home; response ctx away
                  let h, a = home.Home.Defense.Episode.Value, away.Away.Defense.Episode.Value
                  Expect.equal (h.Mode, h.Primary, h.Cover) (a.Mode, a.Primary, a.Cover) "same football in the other attacking frame"
                  for slot in [ h.Primary.Value; h.Cover.Value; 2; 3 ] do
                      Expect.equal home.Home.Defense.Tasks[slot].Value.Target.X (-away.Away.Defense.Tasks[slot].Value.Target.X) "mirrored X"
                  invariants home ClubSide.Home; invariants away ClubSide.Away)
              testCase "an organised retreat ends only after actual stable arrival" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 0.3f
                  response ctx world
                  for slot in MatchWorld.activeSlots world.Home.Frame do
                      match world.Home.Defense.Tasks[slot] with
                      | Some task -> world.Home.Frame.PosX[slot] <- float32 task.Target.X; world.Home.Frame.PosY[slot] <- float32 task.Target.Y
                      | None -> ()
                  decide ctx world
                  Expect.isNone (episode world).Outcome "one settled observation is insufficient"
                  world.State <- { world.State with Tick = 401<subtick> + ctx.Config.DefensiveTransition.StabilizationHold }
                  decide ctx world
                  Expect.equal (episode world).Outcome (Some Stabilized) "organisation, not window expiry"
                  Expect.isTrue (world.Home.Defense.Tasks |> Array.forall Option.isNone) "finished task namespace is empty")
              testCase "unchanged geometry produces neither event nor observation spam" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  let context = DefensiveTransitionFixtures.systemContext ctx
                  let buffer = DefensiveTransitionFixtures.buffer ()
                  TeamTransition.step context (Rng.ofSeed 1UL) world buffer |> ignore
                  let events, observations = buffer.Events.Count, world.Observation.Total
                  for tick in 402 .. 440 do
                      world.State <- { world.State with Tick = tick * 1<subtick> }
                      TeamTransition.step context (Rng.ofSeed 1UL) world buffer |> ignore
                  Expect.equal buffer.Events.Count events "only mode or responsibility changes are semantic"
                  Expect.equal world.Observation.Total observations "no per-player or per-tick capture"
                  let evidence = world.Observation.Snapshot().Entries |> Array.choose (fun entry -> match entry.Data with TransitionEvaluated value -> Some value | _ -> None)
                  Expect.equal evidence.Length 1 "one shared assessment"
                  Expect.equal evidence[0].Reason LocalAdvantage "observable rationale")
              testCase "checkpoint during a real transition restores commitment and equivalent continuation" (fun _ ->
                  let session = transitionSession ()
                  let checkpoint = MatchSession.checkpoint session
                  let restored = checkpoint |> MatchSession.serializeCheckpoint |> MatchSession.deserializeCheckpoint |> unwrap
                                 |> MatchSession.restore Scheduler.systems (matchInput ()) |> unwrap
                  Expect.equal (fingerprint restored) (fingerprint session) "frontier includes exact roles and targets"
                  let target = session.World.State.Tick + 320<subtick>
                  MatchSession.advanceTo target session |> unwrap |> ignore
                  while restored.World.State.Tick < target do
                      MatchSession.advanceTo (min target (restored.World.State.Tick + 7<subtick>)) restored |> unwrap |> ignore
                  Expect.equal (fingerprint restored) (fingerprint session) "continuous sporting state and RNG"
                  let corrupt = { checkpoint with HomeDefense = { checkpoint.HomeDefense with ControlChangedAt = Some 9999<subtick> } }
                  Expect.isError (MatchSession.restore Scheduler.systems (matchInput ()) corrupt) "commitment identity is verified, not guessed")
              testCase "transition evidence links decisions and exits to the real possession cause" (fun _ ->
                  let session = transitionSession ()
                  MatchSession.advanceTo (session.World.State.Tick + 320<subtick>) session |> unwrap |> ignore
                  let snapshot = MatchSession.observations session
                  let decisions = snapshot.Entries |> Array.filter (fun entry -> match entry.Data with TransitionEvaluated _ -> true | _ -> false)
                  Expect.isNonEmpty decisions "the tactical owner emits bounded evidence"
                  for entry in decisions do
                      let chain = Observation.chain entry.Id snapshot |> unwrap
                      Expect.isEmpty chain.FutureCauses "future facts never become causes"
                      Expect.isEmpty chain.Cycles "no cyclic explanation"
                      Expect.isEmpty chain.MissingCauses "short retained corpus has complete ancestry"
                      Expect.isTrue (chain.Entries |> Array.exists (fun source -> match source.Data with EventCommitted { Data = EventData.Possession _ } -> true | _ -> false)) "real possession commit is the root")
              testCase "capture policy cannot alter a causal response or consume sporting RNG" (fun _ ->
                  let ctx, captured = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  let _, disabled = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  disabled.Observation.Enabled <- false
                  let initial = Rng.ofSeed 42UL
                  let context = DefensiveTransitionFixtures.systemContext ctx
                  let buffer = DefensiveTransitionFixtures.buffer ()
                  Expect.equal (TeamTransition.step context initial captured buffer) initial "collective decision uses no draw"
                  response ctx captured; response ctx disabled
                  Expect.equal (DefensiveCheckpoint.snapshot captured.Home) (DefensiveCheckpoint.snapshot disabled.Home) "observation is not authority"
                  Expect.equal captured.State.Possession.LastGainer (Some ClubSide.Away) "a plan cannot fabricate a recovery")
              testCase "same full match survives different advance sizes and presentation policies" (fun _ ->
                  let run mode chunk =
                      let session = MatchSession.create Scheduler.systems (matchInput ()) mode |> unwrap
                      while session.World.State.Flow <> MatchFlow.MatchEnded do
                          MatchSession.advanceTo (min MatchSession.maxTicks (session.World.State.Tick + chunk)) session |> unwrap |> ignore
                      fingerprint session
                  let fast = run MatchRunMode.Fast 4000<subtick>
                  let replay = run (MatchRunMode.Replay 17<tickDelta>) 137<subtick>
                  let live = run (MatchRunMode.Live { LiveExecutionPolicy.defaultPolicy with PoseEvery = 7<tickDelta>; SemanticEvery = 31<tickDelta>; BufferCapacity = 16 }) 233<subtick>
                  Expect.equal replay fast "snapshot interval cannot decide football"
                  Expect.equal live fast "publication and worker slices cannot decide football")
              testCase "a legal near-boundary approach remains inside physical contact reach" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  world.State <- { world.State with Ball = { world.State.Ball with Position = { world.State.Ball.Position with X = 51.8<meter> } } }
                  for slot, x, y in [ 5, 46.0f, 0.0f; 6, 50.0f, -4.0f; 7, 50.0f, 4.0f; 9, 52.0f, 0.0f ] do
                      world.Home.Frame.PosX[slot] <- x; world.Home.Frame.PosY[slot] <- y
                  world.Away.Frame.PosX[9] <- 51.8f
                  response ctx world
                  let primary = (episode world).Primary.Value
                  let target = world.Home.Defense.Tasks[primary].Value.Target
                  Expect.isLessThanOrEqual (abs (float target.X)) (float Pitch.HalfLength) "legal standing target"
                  Expect.isLessThanOrEqual (MatchMath.dist target.X target.Y world.State.Ball.Position.X world.State.Ball.Position.Y) (float ctx.Config.Ball.FootReach) "shape inset must not make the ball unreachable")
              testCase "exhaustion reduces actual retreat execution on identical targets" (fun _ ->
                  let ctx, fresh = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  let _, tired = DefensiveTransitionFixtures.scenario ClubSide.Home false 0.3f
                  for world in [ fresh; tired ] do
                      world.Home.Tactics <- { world.Home.Tactics with PressIntensity = 0.1f }
                      response ctx world
                  Expect.equal fresh.Home.Frame.Intent.TargetX[6] tired.Home.Frame.Intent.TargetX[6] "same protective objective"
                  let targetX = float fresh.Home.Frame.Intent.TargetX[6]
                  for world in [ fresh; tired ] do
                      world.Home.Frame.PosX[6] <- float32 (targetX - 8.0)
                  let initial = fresh.Home.Frame.PosX[6]
                  for _ in 1 .. 80 do
                      for world in [ fresh; tired ] do
                          let movementContext = DefensiveTransitionFixtures.systemContext ctx
                          let movementEvents = DefensiveTransitionFixtures.buffer ()
                          CognitionLatch.step movementContext (Rng.ofSeed 0UL) world movementEvents |> ignore
                          PlayerSteering.step movementContext (Rng.ofSeed 1UL) world movementEvents |> ignore
                          ContactDynamics.step movementContext (Rng.ofSeed 1UL) world movementEvents |> ignore
                  Expect.isGreaterThan
                      (abs (fresh.Home.Frame.PosX[6] - initial))
                      (abs (tired.Home.Frame.PosX[6] - initial))
                      (sprintf
                          "capacity is movement, not a decision-only label (condition %.2f/%.2f, target velocity %.2f/%.2f, x %.2f/%.2f, target %.2f)"
                          fresh.Home.Frame.Condition[6]
                          tired.Home.Frame.Condition[6]
                          fresh.Home.Frame.TargetVelX[6]
                          tired.Home.Frame.TargetVelX[6]
                          fresh.Home.Frame.PosX[6]
                          tired.Home.Frame.PosX[6]
                          fresh.Home.Frame.Intent.TargetX[6]))
              testCase "configured geometry cadence reaches the consumers in the same pass" (fun _ ->
                  let original, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  let ctx = { original with Config = { original.Config with DefensiveTransition = { original.Config.DefensiveTransition with ReevaluateTicks = 3<tickDelta> } } }
                  response ctx world
                  world.State <- { world.State with Tick = 405<subtick>; Ball = { world.State.Ball with Position = { world.State.Ball.Position with X = -15.0<meter> } } }
                  Stepper.step Scheduler.systems (DefensiveTransitionFixtures.systemContext ctx) (Rng.ofSeed 1UL) world (DefensiveTransitionFixtures.buffer ()) |> ignore
                  Expect.equal (episode world).Mode Retreat "geometry changed on the configured pass"
                  Expect.equal (episode world).EvaluatedAt 405<subtick> "not the old four-tick cadence"
                  Expect.isFalse (world.Home.Frame.Intent.Kind |> Array.exists ((=) IntentCode.Press)) "mode publication and execution cannot diverge"
                  invariants world ClubSide.Home)
              testCase "repeated factual episodes keep a fixed task and observation footprint" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  let tasks = world.Home.Defense.Tasks
                  for tick in 402 .. 2601 do
                      world.State <- { world.State with Tick = tick * 1<subtick> }
                      let side = if tick % 2 = 0 then ClubSide.Home else ClubSide.Away
                      let team = MatchWorld.team world side
                      EventApply.apply ctx world { Tick = world.State.Tick; Data = EventData.Possession(side, team.RosterIds[9]) }
                      decide ctx world
                  Expect.isTrue (obj.ReferenceEquals(tasks, world.Home.Defense.Tasks)) "no per-episode task arrays or histories"
                  Expect.equal tasks.Length world.Home.Frame.SlotCount "slot-bounded producer state"
                  Expect.equal world.Observation.Count world.Observation.Capacity "retention saturates rather than grows"
                  Expect.isGreaterThan world.Observation.Total (uint64 world.Observation.Capacity) "enough occurrences to exercise eviction")
              testCase "a commitment cannot survive the physical loss of every eligible cover" (fun _ ->
                  let ctx, world = DefensiveTransitionFixtures.scenario ClubSide.Home false 1.0f
                  response ctx world
                  let primary = (episode world).Primary.Value
                  for slot in MatchWorld.activeSlots world.Home.Frame do
                      if slot <> primary then world.Home.Frame.Condition[slot] <- 0.3f
                  world.State <- { world.State with Tick = 402<subtick> }
                  response ctx world
                  Expect.equal (episode world).Mode Retreat "hysteresis cannot keep an impossible responsibility"
                  Expect.isNone (episode world).Cover "no one is physically eligible"
                  Expect.isFalse (world.Home.Frame.Intent.Kind |> Array.exists ((=) IntentCode.Press)) "pressure stops before commitment expires"
                  invariants world ClubSide.Home) ]
