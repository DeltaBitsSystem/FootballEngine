namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module PositionTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private stepPositionAndIntent (world: World) : unit =
        TeamPosition.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore
        PlayerIntent.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

    let private stepSteering (world: World) : unit =
        CognitionLatch.step sctx (Rng.ofSeed 0UL) world (buffer ()) |> ignore
        PlayerSteering.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

    /// Places a team's frame so slot order is predictable; then forces a fresh
    /// directive so assignRoles runs.
    let private freshDirective (world: World) (side: ClubSide) (tick: int<subtick>) : unit =
        let team = MatchWorld.team world side
        team.Directive.Kind <- DirectiveKind.Attack
        team.Directive.SinceTick <- tick - 1<subtick>

    let positionTests =
        testList
            "FootballEngine.Core.Systems.Team.PositionTests"
            [ testCase "role assignment is deterministic and role-biased" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  world.State <- { world.State with Tick = 4<subtick> }
                  freshDirective world ClubSide.Home world.State.Tick

                  // Ball near slot 5; slot 6 far back.
                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { X = 10.0<meter>
                                        Y = 0.0<meter>
                                        Z = 0.0<meter>
                                        Vx = 0.0<meterPerSecond>
                                        Vy = 0.0<meterPerSecond>
                                        Vz = 0.0<meterPerSecond> } } }

                  world.Home.Frame.PosX.[5] <- 10.0f
                  world.Home.Frame.PosY.[5] <- 0.0f
                  world.Home.Frame.PosX.[6] <- -40.0f
                  world.Home.Frame.PosY.[6] <- 0.0f

                  TeamPosition.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.isGreaterThan
                      world.Home.Frame.RoleBias.[5]
                      world.Home.Frame.RoleBias.[6]
                      "the player nearest the ball gets the stronger press role")

              testCase "GK never gets the press role" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  world.State <- { world.State with Tick = 4<subtick> }
                  freshDirective world ClubSide.Home world.State.Tick

                  TeamPosition.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  let gkBias = world.Home.Frame.RoleBias.[0]

                  let maxOutfield =
                      [ for s in MatchWorld.activeSlots world.Home.Frame do
                            if s <> 0 then
                                world.Home.Frame.RoleBias.[s] ]
                      |> List.max

                  Expect.isLessThan gkBias maxOutfield "the keeper keeps a neutral role")

              testCase "role selection preserves sorted distance and slot ties for both sides" (fun _ ->
                  for sample in 0..31 do
                      let world = TestBuilders.minimalWorld ()

                      for side in [ ClubSide.Home; ClubSide.Away ] do
                          let frame = (MatchWorld.team world side).Frame

                          for slot in MatchWorld.activeSlots frame do
                              frame.PosX[slot] <- float32 ((slot * sample % 7) - 3) * 5.0f
                              frame.PosY[slot] <- float32 ((slot * sample % 3) - 1) * 4.0f

                      TeamPosition.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                      for side in [ ClubSide.Home; ClubSide.Away ] do
                          let frame = (MatchWorld.team world side).Frame
                          let active = MatchWorld.activeSlots frame
                          let nearest = MatchWorld.looseBallChaser world side |> Option.defaultValue -1

                          let candidates =
                              active
                              |> Array.filter (fun slot ->
                                  frame.SlotRoles[slot] <> SlotRole.Goalkeeper && slot <> nearest)

                          let rest =
                              candidates
                              |> Array.sortBy (fun slot ->
                                  MatchMath.dist
                                      (MatchMath.ownGoalX side)
                                      0.0<meter>
                                      (MatchWorld.posX frame slot)
                                      (MatchWorld.posY frame slot),
                                  slot)
                              |> Array.truncate 2

                          let ball = world.State.Ball.Position

                          let cover =
                              candidates
                              |> Array.filter (fun slot -> not (Array.contains slot rest))
                              |> Array.sortBy (fun slot ->
                                  MatchMath.dist
                                      ball.X
                                      ball.Y
                                      (MatchWorld.posX frame slot)
                                      (MatchWorld.posY frame slot),
                                  slot)
                              |> Array.tryHead

                          for slot in active do
                              let expected =
                                  if slot = nearest then DefensiveRole.FirstDefender
                                  elif Some slot = cover then DefensiveRole.Cover
                                  elif Array.contains slot rest then DefensiveRole.Marker
                                  else DefensiveRole.Unassigned

                              Expect.equal
                                  frame.DefensiveRoles[slot]
                                  expected
                                  "linear selection must agree with the sorted reference")

              testCase "loose ball assigns one chaser and defensive roles" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 10.0<meter>
                                          Y = 0.0<meter> } } }

                  world.Home.Frame.PosX.[5] <- 9.0f
                  world.Home.Frame.PosY.[5] <- 0.0f
                  world.Home.Frame.PosX.[6] <- -10.0f
                  world.Home.Frame.PosY.[6] <- 0.0f
                  stepPositionAndIntent world

                  let frame = world.Home.Frame

                  let pressers =
                      MatchWorld.activeSlots frame
                      |> Array.filter (fun slot -> frame.Intent.Kind.[slot] = IntentCode.Press)

                  Expect.equal pressers.Length 1 "exactly one player presses a loose ball"
                  Expect.notEqual pressers.[0] 0 "the goalkeeper is never the chaser"
                  Expect.equal frame.Intent.TargetX.[pressers.[0]] 10.0f "the chaser targets the ball"
                  Expect.equal frame.DefensiveRoles.[pressers.[0]] DefensiveRole.FirstDefender "chaser role"

                  Expect.equal
                      (MatchWorld.activeSlots frame
                       |> Array.filter (fun slot -> frame.DefensiveRoles.[slot] = DefensiveRole.Cover)
                       |> Array.length)
                      1
                      "one player covers"

                  Expect.equal
                      (MatchWorld.activeSlots frame
                       |> Array.filter (fun slot -> frame.DefensiveRoles.[slot] = DefensiveRole.Marker)
                       |> Array.length)
                      2
                      "two players protect rest defense")

              testCase "carrier gets two diagonal supports and a local carry target" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  let carrier = 5
                  let carrierId = ctx.HomeLineup.Profiles.[carrier].Id

                  for slot in MatchWorld.activeSlots world.Away.Frame do
                      world.Away.Frame.PosX.[slot] <- 0.0f
                      world.Away.Frame.PosY.[slot] <- 0.0f

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, carrierId)
                                  Position =
                                      { world.State.Ball.Position with
                                          X = -15.0<meter>
                                          Y = 0.0<meter> } } }

                  stepPositionAndIntent world

                  let frame = world.Home.Frame

                  let supportSlots =
                      MatchWorld.activeSlots frame
                      |> Array.filter (fun slot -> frame.Intent.Kind.[slot] = IntentCode.Support)

                  Expect.equal supportSlots.Length 2 "carrier selects at most two support runners"
                  Expect.equal frame.Intent.Kind.[carrier] IntentCode.BallCarry "carrier keeps the ball"
                  Expect.isGreaterThan frame.Intent.TargetX.[carrier] frame.PosX.[carrier] "carry target advances"

                  Expect.isLessThanOrEqual
                      (abs (float frame.Intent.TargetY.[carrier]))
                      (float Pitch.HalfWidth - 2.0)
                      "carry stays inside inset"

                  for slot in MatchWorld.activeSlots frame do
                      if slot <> carrier && not (Array.contains slot supportSlots) then
                          Expect.notEqual
                              frame.Intent.Kind.[slot]
                              IntentCode.Support
                              (sprintf "slot %d preserves shape" slot))

              testCase "carrier keeps an inward escape target near the touchline" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  let carrier = 5
                  let frame = world.Home.Frame
                  let carrierId = ctx.HomeLineup.Profiles.[carrier].Id
                  frame.PosX.[carrier] <- 50.0f
                  frame.PosY.[carrier] <- 25.0f
                  frame.Intent.Kind.[carrier] <- IntentCode.BallCarry
                  frame.Intent.TargetX.[carrier] <- 50.5f
                  frame.Intent.TargetY.[carrier] <- 18.0f
                  frame.Intent.Phase.[carrier] <- IntentPhase.Executing

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, carrierId)
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 50.0<meter>
                                          Y = 25.0<meter> } } }

                  PlayerIntent.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.isLessThan
                      frame.Intent.TargetY.[carrier]
                      frame.PosY.[carrier]
                      "an inward run is retained until the carrier clears the boundary zone"

                  Expect.isLessThan
                      frame.Intent.TargetX.[carrier]
                      frame.PosX.[carrier]
                      "a carrier at the goal line retreats to create a new action angle")

              testCase "goalkeeper stands on the goal-ball line and drops deeper as the ball approaches" (fun _ ->
                  let world = TestBuilders.formationWorld ()

                  for slot in MatchWorld.activeSlots world.Away.Frame do
                      world.Away.Frame.PosX.[slot] <- 0.0f
                      world.Away.Frame.PosY.[slot] <- 0.0f

                  let keeperFor x y =
                      world.State <-
                          { world.State with
                              Ball =
                                  { world.State.Ball with
                                      Position =
                                          { world.State.Ball.Position with
                                              X = x * 1.0<meter>
                                              Y = y * 1.0<meter>
                                              Vx = 0.0<meterPerSecond>
                                              Vy = 0.0<meterPerSecond> } } }

                      TeamPosition.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore
                      float world.Home.Shape.TargetsX.[0], float world.Home.Shape.TargetsY.[0]

                  let farX, farY = keeperFor 35.0 18.0
                  let nearX, nearY = keeperFor -45.0 18.0
                  let _, mirroredY = keeperFor -45.0 -18.0
                  let goalX = -float Pitch.HalfLength
                  let positioning = BalanceConfig.defaultConfig.Positioning
                  // Independent geometry: depth along the goal-centre-to-ball ray, capped by the maximum advance.
                  let farDistance = sqrt ((35.0 - goalX) ** 2.0 + 18.0 ** 2.0)
                  let farDepth = min (float positioning.GkMaxAdvance) (positioning.GkDepthRatio * farDistance)

                  Expect.floatClose Accuracy.medium farX (goalX + farDepth * (35.0 - goalX) / farDistance) "far ball: depth along the ray"
                  Expect.isGreaterThan farX nearX "keeper advances only as far as the ball's distance allows"
                  Expect.isGreaterThan farY 0.0 "keeper shades toward the ball's side to cover the angle"
                  Expect.isGreaterThan nearY 0.0 "near ball: still on the ball's side"
                  Expect.isLessThan mirroredY 0.0 "mirroring the ball mirrors the keeper"
                  Expect.isLessThanOrEqual (abs nearY) (float Pitch.GoalHalfWidth) "keeper never leaves the goal mouth laterally"

                  Expect.isLessThanOrEqual
                      (abs nearX)
                      (float Pitch.HalfLength - 2.0)
                      "near target respects inset")

              testCase "expired committed run completes with an exit trigger" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let frame = world.Home.Frame
                  world.State <- { world.State with Tick = 20<subtick> }
                  frame.Intent.Kind.[5] <- IntentCode.ExecuteRun
                  frame.Intent.TargetX.[5] <- 20.0f
                  frame.Intent.TargetY.[5] <- 0.0f
                  frame.Intent.Phase.[5] <- IntentPhase.Committed
                  frame.Intent.CommittedUntil.[5] <- 10<subtick>

                  stepSteering world
                  Expect.equal frame.Intent.Phase.[5] IntentPhase.Committed "the body never writes cognition's intention"
                  CognitionLatch.step sctx (Rng.ofSeed 0UL) world (buffer ()) |> ignore

                  Expect.equal frame.Intent.Phase.[5] IntentPhase.Completed "expired run completes"
                  Expect.equal frame.Intent.ExitTrigger.[5] ExitTrigger.Expired "expiry is observable")

              testCase "a body verdict on a replaced intention leaves the new one open" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let frame = world.Home.Frame
                  world.State <- { world.State with Tick = 20<subtick> }
                  frame.Intent.Kind.[5] <- IntentCode.ExecuteRun
                  frame.Intent.TargetX.[5] <- 20.0f
                  frame.Intent.TargetY.[5] <- 0.0f
                  frame.Intent.Phase.[5] <- IntentPhase.Committed
                  frame.Intent.CommittedUntil.[5] <- 10<subtick>

                  stepSteering world
                  frame.Intent.TargetX.[5] <- 25.0f
                  frame.Intent.CommittedUntil.[5] <- 60<subtick>
                  CognitionLatch.step sctx (Rng.ofSeed 0UL) world (buffer ()) |> ignore

                  Expect.equal frame.Intent.Phase.[5] IntentPhase.Committed "the new run is still committed"
                  Expect.equal frame.SteerExit.[5] ExitTrigger.None "the stale verdict is consumed")

              testCase "reassigns only after a directive change" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  world.State <- { world.State with Tick = 4<subtick> }
                  let team = MatchWorld.team world ClubSide.Home
                  team.Directive.SinceTick <- 0<subtick>
                  let before = team.Frame.RoleBias.[5]

                  TeamPosition.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  Expect.equal team.Frame.RoleBias.[5] before "no directive change -> no reassignment") ]
