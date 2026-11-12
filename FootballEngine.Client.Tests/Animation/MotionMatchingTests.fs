namespace FootballEngine.Client.Tests.Animation

open System
open System.Diagnostics
open System.IO
open System.Numerics
open Expecto
open FootballEngine.Client.Animation
open FootballEngine.Client.Match3D
open FootballEngine.Core.Match

/// Motion matching is judged on the shipped capture and rig, against Core roots whose motion is known exactly.
module MotionMatchingTests =
    let private assetDirectory =
        Path.Combine(AppContext.BaseDirectory, "Assets", "MatchViewer")

    let private animation = AnimationConfig.defaultConfig

    let private loaded =
        lazy
            (let character =
                match CharacterAsset.load (Path.Combine(assetDirectory, "player.glb")) with
                | Ok value -> value
                | Error error -> failtestf "character failed validation: %s" error

             match MotionDatabase.load (Path.Combine(assetDirectory, "motion-db.bin")) with
             | Ok db -> character.Rig, db
             | Error error -> failtestf "motion database failed validation: %A" error)

    let private matcher () =
        let rig, db = loaded.Force()

        match MotionMatcher.Create(db, rig, animation, MatchingConfig.defaultConfig) with
        | Ok matcher -> matcher
        | Error error -> failtestf "matcher rejected the shipped assets: %s" error

    /// Root path integrated per tick from a speed (m/s) and turn-rate (rad/s) profile over seconds.
    /// The stride clock follows Core's own cadence law, shifted by `phaseOffset` cycles.
    type private Path(ticks: int, speed: float32 -> float32, turn: float32 -> float32, origin: Vector2, phaseOffset: float32) =
        let dt = animation.TickSeconds
        let positions = Array.zeroCreate<Vector2> ticks
        let headings = Array.zeroCreate<float32> ticks
        let phases = Array.zeroCreate<float32> ticks

        do
            let mutable position = origin
            let mutable heading = 0.0f
            let mutable phase = float phaseOffset

            for tick = 0 to ticks - 1 do
                positions[tick] <- position
                headings[tick] <- heading
                phases[tick] <- float32 (Gait.wrap phase)
                let seconds = float32 tick * dt

                phase <-
                    phase
                    + Gait.cyclesPerSecond BalanceConfig.defaultGait PhysicalMorphology.standard (float (speed seconds))
                      * float dt
                heading <- heading + turn seconds * dt
                position <- position + Vector2(MathF.Cos heading, MathF.Sin heading) * (speed seconds * dt)

        new(ticks, speed, turn, origin) = Path(ticks, speed, turn, origin, 0.0f)

        member _.Root(actor, tick) : MotionRootSample =
            let tick = Math.Clamp(tick, 0, ticks - 1)
            let seconds = float32 tick * dt
            let heading = headings[tick]
            let velocity = Vector2(MathF.Cos heading, MathF.Sin heading) * speed seconds

            { Actor = actor
              Generation = 0
              Position = Vector3(positions[tick], 0.0f)
              Velocity = Vector3(velocity, 0.0f)
              Facing = heading
              AngularVelocity = turn seconds
              Condition = 1.0f
              GaitPhase = phases[tick]
              AttentionTarget = ValueNone
              Morphology = PhysicalMorphology.standard
              Active = true }

        member _.Ticks = ticks

        /// Render-time root, interpolated between ticks as the presentation stream interpolates frames.
        member this.RenderRoot(actor, time: float) =
            let tick = int (floor time)
            let a = this.Root(actor, tick)
            let b = this.Root(actor, tick + 1)
            let u = float32 (time - floor time)
            let turn = b.Facing - a.Facing
            Vector3.Lerp(MotionInput.vector a.Position, MotionInput.vector b.Position, u), MotionInput.facing (a.Facing + turn * u)

        /// Plans as the live runtime publishes them: past its frontier it answers with the latest plan it has.
        member this.Source(frontier: int) =
            { new IMotionPlanSource with
                member _.TryCopyPlan(actor, time, plan) =
                    let tick = min frontier time.Tick

                    if tick < 0 then
                        false
                    else
                        plan <- SupportPlanner.initialize animation tick (this.Root(actor, tick))
                        true }

    let private evaluate (matcher: MotionMatcher) (path: Path) (source: IMotionPlanSource) actor (time: float) (scratch: PoseScratch) (palette: Matrix4x4[]) =
        let motion = MotionTime.ofTicks time
        let mutable plan = SupportPlanner.initialize animation 0 (path.Root(actor, 0))

        if not (source.TryCopyPlan(actor, motion, &plan)) then
            failtest "the plan source had no plan"

        let rootPosition, facing = path.RenderRoot(actor, time)

        match matcher.Evaluate(source, plan, motion, rootPosition, facing, scratch, palette, 0) with
        | ValueSome pose -> pose, rootPosition, facing
        | ValueNone -> failtestf "matcher had no pose at tick %.2f" time

    let private soleWorld (rig: RigDefinition) (world: Matrix4x4[]) (leg: RigLimb) rootPosition facing =
        let local = world[leg.End].Translation + Vector3.TransformNormal(leg.Socket, world[leg.End])
        Vector3.Transform(local, Matrix4x4.CreateRotationY facing) + rootPosition

    let private actor = PlayerId 7

    let private steady speed =
        Path(400, (fun _ -> speed), (fun _ -> 0.0f), Vector2.Zero)

    /// Accelerate, turn, slow, stand and sprint: every kind of transition a footballer's root makes.
    let private varied =
        Path(
            480,
            (fun t ->
                if t < 2.0f then 3.0f * t
                elif t < 4.0f then 6.0f
                elif t < 6.0f then 6.0f - 2.25f * (t - 4.0f)
                elif t < 8.0f then max 0.0f (1.5f - 1.5f * (t - 6.0f))
                else min 7.0f (3.5f * (t - 8.0f))),
            (fun t -> if t >= 2.0f && t < 4.0f then 1.0f else 0.0f),
            Vector2.Zero
        )

    let private capturedSpeed (matcher: MotionMatcher) (pose: MatchedPose) = matcher.Features.Speed[int pose.Frame]

    let tests =
        testList
            "MotionMatching"
            [ testCase "a steady run lands on captured running near the same speed" (fun _ ->
                  let rig, db = loaded.Force()
                  let matcher = matcher ()
                  let path = steady 6.0f
                  let source = path.Source(path.Ticks - 1)
                  let scratch = PoseScratch rig
                  let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity
                  let mutable speeds = []

                  for tick in 120..4..300 do
                      let pose, _, _ = evaluate matcher path source actor (float tick) scratch palette
                      // Stride cadence is the capture's speed time-warped by the playback rate.
                      speeds <- capturedSpeed matcher pose * pose.PlaybackRate :: speeds
                      Expect.notEqual db.Clips[pose.Clip].Family "walking" $"a 6 m/s run is not a walk (tick {tick})"

                  let mean = List.average speeds
                  Expect.isTrue (mean > 5.1f && mean < 6.9f) $"played speed {mean:F2} m/s for a 6 m/s root")
              testCase "a player standing still is matched to near-still capture" (fun _ ->
                  let rig, _ = loaded.Force()
                  let matcher = matcher ()
                  let path = steady 0.0f
                  let source = path.Source(path.Ticks - 1)
                  let scratch = PoseScratch rig
                  let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity

                  for tick in 120..8..300 do
                      let pose, _, _ = evaluate matcher path source actor (float tick) scratch palette
                      let speed = capturedSpeed matcher pose
                      Expect.isLessThan speed 0.8f $"captured speed {speed:F2} m/s while the root stands (tick {tick})")
              testCase "a cold evaluation reproduces the palette of sequential playback exactly" (fun _ ->
                  let rig, _ = loaded.Force()
                  let warm = matcher ()
                  let source = varied.Source(varied.Ticks - 1)
                  let scratch = PoseScratch rig
                  let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity
                  let mutable time = 0.0

                  while time < 300.0 do
                      evaluate warm varied source actor time scratch palette |> ignore
                      time <- time + 0.37

                  for probe in [ 300.0; 301.5; 333.25 ] do
                      evaluate warm varied source actor probe scratch palette |> ignore
                      let cold = matcher ()
                      let coldScratch = PoseScratch rig
                      let coldPalette = Array.create rig.JointNodes.Length Matrix4x4.Identity
                      evaluate cold varied source actor probe coldScratch coldPalette |> ignore
                      Expect.sequenceEqual coldPalette palette $"palette at tick {probe} depends on playback history")
              testCase "switches and chain hand-overs never pop a joint" (fun _ ->
                  let rig, _ = loaded.Force()
                  let matcher = matcher ()
                  let source = varied.Source(varied.Ticks - 1)
                  let scratch = PoseScratch rig
                  let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity
                  let older = Array.create rig.Bind.Length Vector3.Zero
                  let previous = Array.create rig.Bind.Length Vector3.Zero
                  let mutable switches = 0
                  let mutable fastest = 0.0f
                  let mutable sharpest = 0.0f
                  let mutable sharpestAt = ""
                  let mutable last = None
                  let mutable samples = 0
                  let mutable time = 8.0

                  while time < 460.0 do
                      let pose, rootPosition, facing = evaluate matcher varied source actor time scratch palette

                      if pose.Switched then
                          switches <- switches + 1

                      for node = 0 to rig.Bind.Length - 1 do
                          let world = Vector3.Transform(scratch.World[node].Translation, Matrix4x4.CreateRotationY facing) + rootPosition

                          if samples >= 2 then
                              fastest <- max fastest (Vector3.Distance(world, previous[node]))
                              let jerk = (world - previous[node] - (previous[node] - older[node])).Length()

                              if jerk > sharpest then
                                  sharpest <- jerk
                                  sharpestAt <- $"{rig.Names[node]} at tick {time:F2}: {last} -> {pose}"

                          older[node] <- previous[node]
                          previous[node] <- world

                      samples <- samples + 1
                      last <- Some pose
                      time <- time + 0.25

                  Expect.isGreaterThan switches 0 "the varied path must exercise switches"
                  // A quarter tick is 1/160 s. 0.125 m in it is 20 m/s, beyond any limb of a sprinting footballer.
                  Expect.isLessThan fastest 0.125f $"largest joint step {fastest:F3} m"
                  // A pop is a discontinuity, not speed: 0.03 m of second difference is ~1200 m/s², several times a
                  // sprinting foot's real peak acceleration, while a visible snap of a few centimetres exceeds it.
                  Expect.isLessThan sharpest 0.03f $"sharpest joint change {sharpest:F3} m, {sharpestAt}")
              testCase "a planted sole stays where it landed" (fun _ ->
                  let rig, _ = loaded.Force()
                  let matcher = matcher ()
                  let path = steady 4.0f
                  let source = path.Source(path.Ticks - 1)
                  let scratch = PoseScratch rig
                  let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity
                  let landed = Array.create 2 (ValueNone: struct (Vector3 * float) voption)
                  let mutable worst = 0.0f
                  let mutable worstAt = ""
                  let mutable locked = 0
                  let mutable time = 80.0

                  while time < 360.0 do
                      let pose, rootPosition, facing = evaluate matcher path source actor time scratch palette

                      for foot, weight, leg in [ 0, pose.LeftLock, rig.LeftLeg; 1, pose.RightLock, rig.RightLeg ] do
                          if weight >= 1.0f then
                              let sole = soleWorld rig scratch.World leg rootPosition facing
                              locked <- locked + 1

                              match landed[foot] with
                              | ValueSome(struct (first, since)) ->
                                  let drift = Vector3.Distance(first, sole)

                                  if drift > worst then
                                      worst <- drift
                                      worstAt <- $"foot {foot} planted at {since:F2}, now {time:F2}: {pose}"
                              | ValueNone -> landed[foot] <- ValueSome(struct (sole, time))
                          else
                              landed[foot] <- ValueNone

                      time <- time + 0.25

                  Expect.isGreaterThan locked 100 "the run must plant its feet"
                  Expect.isLessThan worst 0.01f $"planted sole drifted {worst:F4} m, {worstAt}")
              // Timing is only meaningful without the rest of the suite competing for the same cores.
              testCase "a run follows Core's stride clock, so the support foot is planted when Core touches the ball" (fun _ ->
                  let _, db = loaded.Force()

                  // Both clock offsets must be followed: a pose that happened to agree with one would fail the other.
                  for offset in [ 0.0f; 0.5f ] do
                      let matcher = matcher ()
                      let path = Path(400, (fun _ -> 4.5f), (fun _ -> 0.0f), Vector2.Zero, offset)
                      let source = path.Source(path.Ticks - 1)
                      let rig, _ = loaded.Force()
                      let scratch = PoseScratch rig
                      let palette = Array.create rig.Bind.Length Matrix4x4.Identity
                      let errors = ResizeArray<float32>()
                      let mutable supported, touches = 0, 0

                      for tick in 120..380 do
                          let pose, _, _ = evaluate matcher path source actor (float tick) scratch palette
                          let captured = matcher.Features.Phase[int pose.Frame]
                          Expect.isFalse (Single.IsNaN captured) $"tick {tick}: a run is matched to striding capture"
                          let core = path.Root(actor, tick).GaitPhase
                          let gap = core - captured
                          errors.Add(abs (gap - MathF.Round gap))

                          // Core's touch point is CarryTouchPhase after a strike; the striking foot must be planted.
                          let sinceStrike = core - MathF.Floor(core * 2.0f) * 0.5f
                          let touch = float32 BalanceConfig.defaultGait.CarryTouchPhase

                          if abs (sinceStrike - touch) < 0.04f then
                              touches <- touches + 1
                              let bit = if core < 0.5f then 1uy else 2uy
                              if db.Contacts[int pose.Frame] &&& bit <> 0uy then supported <- supported + 1

                      let mean = Seq.average errors
                      Expect.isLessThan mean 0.08f $"offset {offset}: mean phase gap {mean:F3} cycles"
                      Expect.isLessThan (Seq.max errors) 0.2f $"offset {offset}: worst phase gap"
                      Expect.isGreaterThan touches 10 "the run passes many touch points"
                      Expect.isGreaterThanOrEqual (float supported / float touches) 0.9 $"offset {offset}: support foot planted at {supported}/{touches} touch points")
              testSequenced <| testCase "twenty-two players are matched within the frame budget" (fun _ ->
                  let rig, _ = loaded.Force()
                  let matcher = matcher ()
                  let paths = Array.init 22 (fun i -> Path(1200, (fun t -> 2.0f + 4.0f * MathF.Sin(t * 0.7f + float32 i)), (fun t -> 0.6f * MathF.Sin(t * 0.4f + float32 i)), Vector2(float32 i * 3.0f, 0.0f)))
                  let sources = paths |> Array.map (fun path -> path.Source(path.Ticks - 1))
                  let scratch = PoseScratch rig
                  let palette = Array.create rig.JointNodes.Length Matrix4x4.Identity
                  let frame = 40.0 / 60.0
                  let run first last =
                      let mutable time = first

                      while time < last do
                          for i = 0 to 21 do
                              evaluate matcher paths[i] sources[i] (PlayerId(i + 1)) time scratch palette |> ignore

                          time <- time + frame

                  run 0.0 200.0

                  // Other suites run in parallel and preemption only ever adds time, so the fastest of several
                  // equal windows measures the matcher; one long window failed the same code on a busy machine.
                  let window = 160.0

                  let perFrame =
                      [ for start in 200.0 .. window .. 1000.0 - window ->
                            let clock = Stopwatch.StartNew()
                            run start (start + window)
                            clock.Elapsed.TotalMilliseconds / (window / frame) ]
                      |> List.min

                  Expect.isLessThan perFrame 1.0 $"{perFrame:F3} ms per 60 Hz frame for 22 players") ]
