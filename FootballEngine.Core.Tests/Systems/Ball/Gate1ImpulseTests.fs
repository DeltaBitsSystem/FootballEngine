namespace FootballEngine.Core.Tests.Systems.Ball

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module Gate1ImpulseTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private setBall (world: World) (x: float<meter>) (y: float<meter>) : unit =
        world.State <-
            { world.State with
                Ball =
                    { world.State.Ball with
                        Position =
                            { world.State.Ball.Position with
                                X = x
                                Y = y
                                Z = 0.0<meter>
                                Vx = 0.0<meterPerSecond>
                                Vy = 0.0<meterPerSecond>
                                Vz = 0.0<meterPerSecond> } } }

    let private assertImpulse (kind: StrikeKind) (context: ExecutionContext) (world: World) : unit =
        match world.State.Ball.Impulse with
        | Some impulse ->
            Expect.equal impulse.StrikeKind kind "strike kind recorded"
            Expect.equal impulse.ExecutionContext context "execution context recorded"
            Expect.equal world.State.Ball.LastTouch (Some impulse.ActorId) "impulse updates last touch"
        | None -> failtestf "expected %A impulse" kind

    let private putAwayLineDeep (world: World) : unit =
        for slot in MatchWorld.activeSlots world.Away.Frame do
            if slot <> 0 then
                world.Away.Frame.PosX.[slot] <- 55.0f

    let gate1ImpulseTests =
        testList
            "FootballEngine.Core.Systems.Ball.Gate1ImpulseTests"
            [ testCase "impulse identity is unique across strike kinds" (fun _ ->
                  let actor = PlayerId 7

                  let origin: Vec3 =
                      { X = 0.0<meter>
                        Y = 0.0<meter>
                        Z = 0.0<meter> }

                  let velocity =
                      { X = 10.0<meterPerSecond>
                        Y = 1.0<meterPerSecond>
                        Z = 0.0<meterPerSecond> }

                  let angular =
                      { Top = 1.0<radianPerSecond>
                        Side = 2.0<radianPerSecond>
                        Roll = 0.0<radianPerSecond> }

                  let kinds =
                      [ StrikeKind.Pass
                        StrikeKind.Cross
                        StrikeKind.Clearance
                        StrikeKind.Shot
                        StrikeKind.Restart SetPieceKind.KickOff
                        StrikeKind.Restart SetPieceKind.ThrowIn
                        StrikeKind.Restart SetPieceKind.Corner
                        StrikeKind.Restart SetPieceKind.GoalKick
                        StrikeKind.Restart SetPieceKind.FreeKick
                        StrikeKind.Restart SetPieceKind.Penalty ]

                  let ids =
                      kinds
                      |> List.map (fun kind ->
                          BallPhysics.createImpulse
                              actor
                              origin
                              velocity
                              angular
                              None
                              kind
                              (SimTick.ofSubTicks 12<subtick>)
                              ExecutionContext.OpenPlay)
                      |> List.map (fun impulse -> ActionId.value impulse.ActionId)

                  Expect.equal (List.distinct ids).Length ids.Length "each strike has a causal identity")

              testCase "pass uses a low physical impulse for short and long delivery" (fun _ ->
                  let run targetX =
                      let world = TestBuilders.minimalWorld ()
                      putAwayLineDeep world
                      world.Home.Frame.PosX.[5] <- 0.0f
                      world.Home.Frame.PosY.[5] <- 0.0f
                      world.Home.Frame.PosX.[7] <- targetX
                      world.Home.Frame.PosY.[7] <- 0.0f
                      setBall world 0.0<meter> 0.0<meter>

                      Pass.launch sctx (Rng.ofSeed 4UL) world (buffer ()) ClubSide.Home 5 7 0.0
                      |> ignore

                      world

                  let shortPass = run 10.0f
                  let longPass = run 40.0f

                  assertImpulse StrikeKind.Pass ExecutionContext.OpenPlay shortPass
                  assertImpulse StrikeKind.Pass ExecutionContext.OpenPlay longPass

                  Expect.isLessThan
                      (float shortPass.State.Ball.Position.Vx)
                      (float longPass.State.Ball.Position.Vx)
                      "long pass has greater launch speed")

              testCase "cross uses an elevated impulse" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  world.Home.Frame.PosX.[8] <- 20.0f
                  world.Home.Frame.PosY.[8] <- 0.0f
                  world.Home.Frame.PosX.[9] <- 45.0f
                  world.Home.Frame.PosY.[9] <- 0.0f
                  setBall world 20.0<meter> 0.0<meter>

                  Cross.launch sctx (Rng.ofSeed 4UL) world (buffer ()) ClubSide.Home 8 |> ignore

                  assertImpulse StrikeKind.Cross ExecutionContext.OpenPlay world
                  Expect.isGreaterThan (float world.State.Ball.Position.Vz) 0.0 "cross starts above the ground")

              testCase "clearance uses the common impulse path" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let pid = ctx.HomeLineup.Profiles.[5].Id
                  setBall world 0.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Controlled(ClubSide.Home, pid) } }

                  world.Home.Frame.RunLookup.[5] <-
                      { Slot = 5
                        TargetX = 0.0f
                        TargetY = 4.0f
                        Phase = 200
                        ExpiresAt = 0<subtick> }

                  Actions.step sctx (Rng.ofSeed 4UL) world (buffer ()) |> ignore

                  assertImpulse StrikeKind.Clearance ExecutionContext.OpenPlay world
                  Expect.isGreaterThan (float world.State.Ball.Position.Vz) 0.0 "clearance lifts the ball")

              testCase "shot uses the common impulse path" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  world.Home.Frame.PosX.[9] <- 20.0f
                  world.Home.Frame.PosY.[9] <- 0.0f
                  setBall world 20.0<meter> 0.0<meter>

                  Shot.launch sctx (Rng.ofSeed 4UL) world (buffer ()) ClubSide.Home 9 |> ignore

                  assertImpulse StrikeKind.Shot ExecutionContext.OpenPlay world
                  Expect.isGreaterThan (abs (float world.State.Ball.Position.Vx)) 0.0 "shot has horizontal velocity")

               ]
