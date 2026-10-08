namespace FootballEngine.Core.Tests.Systems.Players

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module SteeringTests =

    let private ctx = TestBuilders.formationCtx ()

    let private systemContext =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private stepIntent world =
        PlayerIntent.step systemContext (Rng.ofSeed 1UL) world (buffer ()) |> ignore

    let private stepSteering world =
        PlayerSteering.step systemContext (Rng.ofSeed 1UL) world (buffer ()) |> ignore

    let steeringTests =
        testList
            "FootballEngine.Core.Systems.Players.SteeringTests"
            [ testCase "continuous intent refreshes its target" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  let intent = world.Home.Frame.Intent
                  world.State <- { world.State with Tick = 100<subtick> }
                  intent.Kind[0] <- IntentCode.HoldPosition
                  intent.TargetX[0] <- 0.0f
                  intent.TargetY[0] <- 12.0f
                  intent.Phase[0] <- IntentPhase.Executing

                  TeamPosition.step systemContext (Rng.ofSeed 1UL) world (buffer ()) |> ignore
                  let expectedX = world.Home.Shape.TargetsX[0]
                  let expectedY = world.Home.Shape.TargetsY[0]
                  stepIntent world

                  Expect.equal intent.TargetX[0] expectedX "target follows role"
                  Expect.equal intent.TargetY[0] expectedY "stale target is replaced"
                  Expect.equal intent.CommittedAt[0] 100<subtick> "refresh is recorded")

              testCase "body flight emits one takeoff and landing without consuming randomness" (fun _ ->
                  let world = TestBuilders.formationWorld ()

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Live }

                  let frame = world.Home.Frame
                  let events = buffer ()
                  let rng = Rng.ofSeed 1UL
                  frame.JumpRequested[5] <- true
                  let result = PlayerBodyPhysics.step systemContext rng world events
                  Expect.equal result rng "body integration cannot affect random decisions"
                  Expect.isGreaterThan frame.PosZ[5] 0.0f "takeoff raises the body"
                  Expect.isFalse frame.JumpRequested[5] "request is consumed once"
                  frame.JumpRequested[5] <- true

                  for _ in 1..200 do
                      PlayerBodyPhysics.step systemContext rng world events |> ignore

                  Expect.equal frame.PosZ[5] 0.0f "landing never penetrates the pitch"
                  Expect.equal frame.VelZ[5] 0.0f "landing stops vertical motion"
                  Expect.equal frame.JumpSequence[5] 1UL "an airborne request cannot start a second jump"

                  Expect.equal
                      (events.Events |> Seq.map _.Data |> Seq.toList)
                      [ EventData.PlayerBodyFlight(world.Home.RosterIds[5], 1UL, true)
                        EventData.PlayerBodyFlight(world.Home.RosterIds[5], 1UL, false) ]
                      "takeoff and landing share a stable flight identity")

              testCase "continuous intent remains executing at its target" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  let frame = world.Home.Frame
                  let slot = 5
                  frame.Intent.Kind[slot] <- IntentCode.Support
                  frame.Intent.TargetX[slot] <- frame.PosX[slot]
                  frame.Intent.TargetY[slot] <- frame.PosY[slot]
                  frame.Intent.Phase[slot] <- IntentPhase.Executing

                  stepSteering world

                  Expect.equal
                      frame.Intent.Phase[slot]
                      IntentPhase.Executing
                      "arrival does not end continuous movement"

                  Expect.equal frame.Intent.ExitTrigger[slot] ExitTrigger.None "arrival does not fire an exit trigger")

              testCase "arrival clears stale velocity when separation is inactive" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  let frame = world.Home.Frame
                  let slot = 5
                  frame.PosX[slot] <- 0.0f
                  frame.PosY[slot] <- 0.0f
                  frame.VelX[slot] <- 6.0f
                  frame.VelY[slot] <- -3.0f
                  frame.Intent.Kind[slot] <- IntentCode.Support
                  frame.Intent.TargetX[slot] <- frame.PosX[slot]
                  frame.Intent.TargetY[slot] <- frame.PosY[slot]
                  frame.Intent.Phase[slot] <- IntentPhase.Executing

                  for teammate in 0 .. frame.PosX.Length - 1 do
                      if teammate <> slot then
                          frame.PosX[teammate] <- float32 (10 + teammate)
                          frame.PosY[teammate] <- float32 (10 + teammate)

                  stepSteering world

                  Expect.equal frame.VelX[slot] 0.0f "arrival clears stale x velocity"
                  Expect.equal frame.VelY[slot] 0.0f "arrival clears stale y velocity")

              testCase "overlapping stationary teammates separate" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  let frame = world.Home.Frame
                  let x = frame.PosX[1]
                  let y = frame.PosY[1]
                  frame.PosX[2] <- x
                  frame.PosY[2] <- y

                  for slot in [ 1; 2 ] do
                      frame.Intent.Kind[slot] <- IntentCode.HoldPosition
                      frame.Intent.TargetX[slot] <- x
                      frame.Intent.TargetY[slot] <- y
                      frame.Intent.Phase[slot] <- IntentPhase.Executing

                  stepSteering world

                  Expect.isTrue
                      (frame.PosX[1] <> frame.PosX[2] || frame.PosY[1] <> frame.PosY[2])
                      "players no longer share the same coordinate")

              testCase "steering cannot move a player beyond the pitch" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  let frame = world.Home.Frame
                  let slot = 5
                  frame.PosX[slot] <- float32 Pitch.HalfLength - 0.01f
                  frame.PosY[slot] <- float32 Pitch.HalfWidth - 0.01f
                  frame.Intent.Kind[slot] <- IntentCode.BallCarry
                  frame.Intent.TargetX[slot] <- 100.0f
                  frame.Intent.TargetY[slot] <- 100.0f
                  frame.Intent.Phase[slot] <- IntentPhase.Executing

                  stepSteering world

                  Expect.isLessThanOrEqual frame.PosX[slot] (float32 Pitch.HalfLength) "x remains in bounds"
                  Expect.isLessThanOrEqual frame.PosY[slot] (float32 Pitch.HalfWidth) "y remains in bounds")

              testCase "body facing turns with bounded angular speed and acceleration" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  let frame = world.Home.Frame
                  let slot = 5
                  frame.PosX[slot] <- 0.0f
                  frame.PosY[slot] <- 0.0f
                  frame.BodyFacing[slot] <- 1.5707964f
                  frame.Intent.Kind[slot] <- IntentCode.BallCarry
                  frame.Intent.TargetX[slot] <- 10.0f
                  frame.Intent.TargetY[slot] <- 0.0f
                  frame.Intent.Phase[slot] <- IntentPhase.Executing

                  for teammate in 0 .. frame.PosX.Length - 1 do
                      if teammate <> slot then
                          frame.PosX[teammate] <- float32 (20 + teammate)
                          frame.PosY[teammate] <- float32 (20 + teammate)

                  stepSteering world

                  Expect.isLessThan
                      frame.BodyFacing[slot]
                      1.5707964f
                      "facing begins rotating toward the movement target"

                  Expect.isLessThan
                      frame.BodyAngularVelocity[slot]
                      0.0f
                      "turn direction follows the shortest angular path"

                  Expect.isLessThanOrEqual
                      (abs frame.BodyAngularVelocity[slot])
                      (float32 ctx.Config.Movement.TurningRate)
                      "angular velocity respects its configured limit"

                  Expect.isLessThanOrEqual
                      (abs frame.BodyAngularAcceleration[slot])
                      (float32 ctx.Config.Movement.TurningAcceleration + 0.001f)
                      "angular acceleration respects its configured limit")

              testCase "a replay never freezes for a full match minute" (fun _ ->
                  let outcome =
                      Simulator.runMatch Scheduler.systems ctx 0x53484F5453UL (Simulator.RunMode.Replay 20<tickDelta>)
                      |> TestBuilders.requireMatchOutcome

                  let replay = outcome.Replay |> Option.get
                  let mutable previous = Replay.readFrame replay 0
                  let mutable frozenCount = 0
                  let mutable frozenStart = previous
                  let mutable longestCount = 0
                  let mutable longestStart = previous
                  let mutable longestEnd = previous
                  let mutable stationaryBallCount = 0
                  let mutable longestStationaryBallCount = 0

                  let samePositions (left: Replay.ReplaySnapshot) (right: Replay.ReplaySnapshot) =
                      left.BallX = right.BallX
                      && left.BallY = right.BallY
                      && left.HomeX = right.HomeX
                      && left.HomeY = right.HomeY
                      && left.AwayX = right.AwayX
                      && left.AwayY = right.AwayY

                  for index = 1 to replay.Count - 1 do
                      let current = Replay.readFrame replay index

                      if previous.Flow = 0uy && current.Flow = 0uy && samePositions previous current then
                          if frozenCount = 0 then
                              frozenStart <- previous

                          frozenCount <- frozenCount + 1

                          if frozenCount > longestCount then
                              longestCount <- frozenCount
                              longestStart <- frozenStart
                              longestEnd <- current
                      else
                          frozenCount <- 0

                      if
                          previous.Flow = 0uy
                          && current.Flow = 0uy
                          && previous.BallX = current.BallX
                          && previous.BallY = current.BallY
                      then
                          stationaryBallCount <- stationaryBallCount + 1
                          longestStationaryBallCount <- max longestStationaryBallCount stationaryBallCount
                      else
                          stationaryBallCount <- 0

                      previous <- current

                  Expect.isLessThan
                      longestCount
                      120
                      (sprintf
                          "world frozen for %d snapshots from tick %d (flow %d) to %d (flow %d)"
                          longestCount
                          (int longestStart.Tick)
                          longestStart.Flow
                          (int longestEnd.Tick)
                          longestEnd.Flow)

                  Expect.isLessThan
                      longestStationaryBallCount
                      120
                      (sprintf "ball stationary during live play for %d snapshots" longestStationaryBallCount)) ]
