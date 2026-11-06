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

    let private stepContact world =
        ContactDynamics.step systemContext (Rng.ofSeed 1UL) world (buffer ()) |> ignore

    let steeringTests =
        testList
            "FootballEngine.Core.Systems.Players.SteeringTests"
            [ testCase "settled players attend the ball instead of orbiting small shape corrections" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  let frame = world.Home.Frame
                  let slot = 5
                  frame.PosX[slot] <- 0.0f
                  frame.PosY[slot] <- 0.0f
                  frame.BodyFacing[slot] <- -1.0f
                  frame.Intent.Kind[slot] <- IntentCode.HoldPosition
                  frame.Intent.Phase[slot] <- IntentPhase.Executing

                  world.State <-
                      { world.State with
                          Flow = MatchFlow.Live
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 15.0<meter>
                                          Y = 0.0<meter> } } }

                  for teammate = 0 to frame.PosX.Length - 1 do
                      if teammate <> slot then
                          frame.PosX[teammate] <- float32 (10 + teammate)
                          frame.PosY[teammate] <- 15.0f

                  for opponent in MatchWorld.activeSlots world.Away.Frame do
                      world.Away.Frame.PosX[opponent] <- 30.0f + float32 opponent
                      world.Away.Frame.PosY[opponent] <- 20.0f

                  for tick = 0 to 159 do
                      let angle = float tick * 0.1
                      frame.Intent.TargetX[slot] <- frame.PosX[slot] + float32 (cos angle * 0.1)
                      frame.Intent.TargetY[slot] <- frame.PosY[slot] + float32 (sin angle * 0.1)
                      stepSteering world
                      stepContact world

                  let expected = atan2 (-float frame.PosY[slot]) (15.0 - float frame.PosX[slot])
                  let error = atan2 (sin (float frame.BodyFacing[slot] - expected)) (cos (float frame.BodyFacing[slot] - expected))

                  Expect.isLessThan
                       (abs error)
                       0.03
                       "body settles toward play, not the rotating micro target")
              testCase "continuous intent refreshes its target" (fun _ ->
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
                  let result = ContactDynamics.step systemContext rng world events
                  Expect.equal result rng "body integration cannot affect random decisions"
                  Expect.isGreaterThan frame.PosZ[5] 0.0f "takeoff raises the body"
                  Expect.isFalse frame.JumpRequested[5] "request is consumed once"
                  frame.JumpRequested[5] <- true

                  for _ in 1..200 do
                       ContactDynamics.step systemContext rng world events |> ignore

                  Expect.equal frame.PosZ[5] 0.0f "landing never penetrates the pitch"
                  Expect.equal frame.VelZ[5] 0.0f "landing stops vertical motion"
                  Expect.equal frame.JumpSequence[5] 1UL "an airborne request cannot start a second jump"

                  Expect.equal
                       (events.Events
                        |> Seq.map _.Data
                        |> Seq.filter (function EventData.PlayerBodyFlight _ -> true | _ -> false)
                        |> Seq.toList)
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

              testCase "arrival proposes a stop and physical integration removes stale velocity" (fun _ ->
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

                  Expect.equal frame.TargetVelX[slot] 0.0f "arrival proposes zero x velocity"
                  Expect.equal frame.TargetVelY[slot] 0.0f "arrival proposes zero y velocity"

                  for _ in 1..100 do
                       stepContact world

                  Expect.isLessThan (abs frame.VelX[slot]) 0.001f "body solver decelerates x"
                  Expect.isLessThan (abs frame.VelY[slot]) 0.001f "body solver decelerates y")

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

                  stepContact world

                  Expect.isTrue
                       (frame.PosX[1] <> frame.PosX[2] || frame.PosY[1] <> frame.PosY[2])
                       "static overlap receives a bounded positional correction")

              testCase "head-on body impact emits deterministic physical evidence" (fun _ ->
                   let world = TestBuilders.formationWorld ()
                   let events = buffer ()

                   for side, team in [ ClubSide.Home, world.Home; ClubSide.Away, world.Away ] do
                       for slot in MatchWorld.activeSlots team.Frame do
                           if not (side = ClubSide.Home && slot = 5) && not (side = ClubSide.Away && slot = 5) then
                               team.Frame.PosX[slot] <- if side = ClubSide.Home then -30.0f else 30.0f
                               team.Frame.PosY[slot] <- -20.0f + float32 slot * 4.0f

                   let home = world.Home.Frame
                   let away = world.Away.Frame
                   home.PosX[5] <- -0.22f
                   home.PosY[5] <- 0.0f
                   home.VelX[5] <- 8.0f
                   home.TargetVelX[5] <- 8.0f
                   away.PosX[5] <- 0.22f
                   away.PosY[5] <- 0.0f
                   away.VelX[5] <- -8.0f
                   away.TargetVelX[5] <- -8.0f
                   let distantBall =
                       { TestBuilders.emptyBall with
                           Position =
                               { TestBuilders.emptyBall.Position with
                                   X = 15.0<meter>
                                   Y = 20.0<meter>
                                   Z = 10.0<meter> } }

                   world.State <- { world.State with Flow = MatchFlow.Live; Ball = distantBall }

                   let rng = Rng.ofSeed 17UL
                   let next = ContactDynamics.step systemContext rng world events

                   Expect.equal next rng "physical resolution does not consume randomness"

                   let contact =
                       events.Events
                       |> Seq.tryPick (fun event ->
                           match event.Data with
                           | EventData.PhysicalContact contact -> Some contact
                           | _ -> None)
                       |> Option.defaultWith (fun () -> failtest "head-on collision emitted no physical record")

                   Expect.equal contact.Other (Some world.Away.RosterIds[5]) "the opposing participant is recorded"
                   Expect.isGreaterThan contact.NormalImpulse 0.0 "closing velocity produces a normal impulse"
                   Expect.equal contact.SupportBefore BodySupport.DoubleFootSupport "record preserves support before the impulse"
                   Expect.equal contact.SupportAfter BodySupport.NoBodySupport "falling response removes support"
                   Expect.isGreaterThan contact.Fraction 0.0 "impact occurs inside the swept subtick"
                   Expect.isLessThan contact.Fraction 1.0 "impact is not deferred past the subtick")

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
                  stepContact world

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
                  stepContact world

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
