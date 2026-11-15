namespace FootballEngine.Core.Tests.Systems.Team

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module OffsideLineTests =

    let private ctx = TestBuilders.formationCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private setPosX (frame: TeamFrame) (start: int) (xs: float32[]) : unit =
        for i in 0 .. xs.Length - 1 do
            frame.PosX.[start + i] <- xs.[i]

    let offsideLineTests =
        testList
            "FootballEngine.Core.Systems.Team.OffsideLineTests"
            [ testCase "defensive line is the 2nd deepest outfield player" (fun _ ->
                  let world = TestBuilders.formationWorld ()

                  setPosX
                      world.Home.Frame
                      1
                      [| 5.0f; -20.0f; -15.0f; -10.0f; -5.0f; 0.0f; 10.0f; 15.0f; 20.0f; 25.0f |]

                  Expect.equal
                      (DefenseLine.secondDeepest ctx world ClubSide.Home)
                      -15.0<meter>
                      "Home defensive line is the 2nd smallest X"

                  setPosX world.Away.Frame 1 [| 30.0f; 40.0f; 45.0f; 35.0f; 25.0f; 15.0f; 10.0f; 5.0f; 0.0f; -5.0f |]

                  Expect.equal
                      (DefenseLine.secondDeepest ctx world ClubSide.Away)
                      40.0<meter>
                      "Away defensive line is the 2nd largest X")

              testCase "defensive line ignores GK" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.Home.Frame.PosX.[0] <- -45.0f

                  setPosX
                      world.Home.Frame
                      1
                      [| 5.0f; -20.0f; -15.0f; -10.0f; -5.0f; 0.0f; 10.0f; 15.0f; 20.0f; 25.0f |]

                  Expect.equal
                      (DefenseLine.secondDeepest ctx world ClubSide.Home)
                      -15.0<meter>
                      "the keeper deep in own goal is excluded from the line")

              testCase "fallback to own goal when fewer than 2 outfielders" (fun _ ->
                  let world = TestBuilders.formationWorld ()

                  for i in 2 .. world.Home.Frame.SlotCount - 1 do
                      world.Home.Frame.Occupancy.[i] <-
                          OccupancyKind.Sidelined
                              { Id = PlayerId i
                                Reason = PlayerOutReason.Substituted }

                  Expect.equal
                      (DefenseLine.secondDeepest ctx world ClubSide.Home)
                      (MatchMath.ownGoalX ClubSide.Home)
                      "single active outfielder -> own goal X")

              testCase "the offside line is the last outfield defender, whichever way the attack goes" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  setPosX world.Away.Frame 1 [| 30.0f; 40.0f; 45.0f; 35.0f; 25.0f; 15.0f; 10.0f; 5.0f; 0.0f; -5.0f |]

                  setPosX
                      world.Home.Frame
                      1
                      [| -30.0f; -40.0f; -45.0f; -35.0f; -25.0f; -15.0f; -10.0f; -5.0f; 0.0f; 5.0f |]

                  Expect.equal
                      (MatchWorld.offsideLine ctx world ClubSide.Home)
                      45.0<meter>
                      "home attack: the deepest away outfielder"

                  Expect.equal
                      (MatchWorld.offsideLine ctx world ClubSide.Away)
                      -45.0<meter>
                      "away attack: the deepest home outfielder")

              testCase "the offside line never enters the attacker's own half" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  setPosX world.Away.Frame 1 (Array.create 10 -20.0f)
                  setPosX world.Home.Frame 1 (Array.create 10 20.0f)
                  Expect.equal (MatchWorld.offsideLine ctx world ClubSide.Home) 0.0<meter> "home attack: halfway"
                  Expect.equal (MatchWorld.offsideLine ctx world ClubSide.Away) 0.0<meter> "away attack: halfway")

              testCase "resting targets clamped behind offside line in defense" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.Home.Blackboard.OurPhase <- PhaseKind.Defending

                  setPosX world.Away.Frame 1 [| 10.0f; 11.0f; 12.0f; 13.0f; 14.0f; 15.0f; 15.0f; 15.0f; 15.0f; 15.0f |]

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 30.0<meter> } } }

                  TeamPosition.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  let offLine = MatchWorld.offsideLine ctx world ClubSide.Home

                  for i in MatchWorld.activeSlots world.Home.Frame do
                      if i <> 0 then
                          Expect.isLessThanOrEqual
                              (float world.Home.Shape.TargetsX.[i])
                              (float offLine - 1.0)
                              (sprintf "slot %d target stays behind the offside line" i)

                  let clamped =
                      MatchWorld.activeSlots world.Home.Frame
                      |> Array.exists (fun i ->
                          i <> 0
                          && abs (float world.Home.Shape.TargetsX.[i] - (float offLine - 1.0)) < 0.01)

                  Expect.isTrue clamped "at least one resting target is clamped exactly to the line")

              testCase "targets not clamped when attacking" (fun _ ->
                  let world = TestBuilders.formationWorld ()
                  world.Home.Blackboard.OurPhase <- PhaseKind.Attacking

                  setPosX world.Away.Frame 1 [| 10.0f; 11.0f; 12.0f; 13.0f; 14.0f; 15.0f; 15.0f; 15.0f; 15.0f; 15.0f |]

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 30.0<meter> } } }

                  TeamPosition.step sctx (Rng.ofSeed 42UL) world (buffer ()) |> ignore

                  let offLine = MatchWorld.offsideLine ctx world ClubSide.Home

                  let strikerSlot =
                      ctx.HomeLineup.Profiles |> Array.findIndex (fun p -> p.Role = SlotRole.Striker)

                  Expect.isGreaterThan
                      (float world.Home.Shape.TargetsX.[strikerSlot])
                      (float offLine - 1.0)
                      "attacking striker target may exceed the offside line")

              testCase "deterministic" (fun _ ->
                  let run (w: World) : World =
                      w.Home.Blackboard.OurPhase <- PhaseKind.Defending
                      setPosX w.Away.Frame 1 [| 12.0f; 12.0f; 12.0f; 12.0f; 12.0f; 12.0f; 12.0f; 12.0f; 12.0f; 12.0f |]
                      TeamPosition.step sctx (Rng.ofSeed 42UL) w (buffer ()) |> ignore
                      w

                  let a = run (TestBuilders.formationWorld ())
                  let b = run (TestBuilders.formationWorld ())

                  Expect.sequenceEqual a.Home.Shape.TargetsX b.Home.Shape.TargetsX "Home X targets deterministic"
                  Expect.sequenceEqual a.Home.Shape.TargetsY b.Home.Shape.TargetsY "Home Y targets deterministic"
                  Expect.sequenceEqual a.Away.Shape.TargetsX b.Away.Shape.TargetsX "Away X targets deterministic") ]
