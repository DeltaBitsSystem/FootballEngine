namespace FootballEngine.Core.Tests.Systems.Ball

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module OutcomeTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private shooter = ctx.HomeLineup.Profiles.[9].Id
    let private awayOutfield = ctx.AwayLineup.Profiles.[5].Id
    let private homeOutfield = ctx.HomeLineup.Profiles.[5].Id

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    /// Callers place the centre 0.2 m past the line: the Laws need the whole ball (r = 0.11 m) over it.
    let private setShotBall (world: World) (x: float<meter>) (y: float<meter>) : unit =
        world.State <-
            { world.State with
                Ball =
                    { world.State.Ball with
                        Position =
                            { X = x
                              Y = y
                              Z = 0.0<meter>
                              Vx = 20.0<meterPerSecond>
                              Vy = 0.0<meterPerSecond>
                              Vz = 0.0<meterPerSecond> }
                        ArrivalGate = Some 0<subtick>
                        Trajectory = Some(TrajectoryIntent.Struck(shooter, 20.0<meterPerSecond>, 0.0, 0.5)) } }

    let private lastTouch (world: World) (pid: PlayerId) : unit =
        world.State <-
            { world.State with
                Ball =
                    { world.State.Ball with
                        LastTouch = Some pid } }

    let outcomeTests =
        testList
            "FootballEngine.Core.Systems.Ball.OutcomeTests"
            [ testCase "Shot inside the frame scores unless physical contact stopped it" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  let impulse =
                      BallPhysics.createImpulse
                          shooter
                          { X = Pitch.awayGoalX - 0.5<meter>
                            Y = 0.5<meter>
                            Z = 0.0<meter> }
                          { X = 20.0<meterPerSecond>
                            Y = 0.0<meterPerSecond>
                            Z = 0.0<meterPerSecond> }
                          { Top = 0.0<radianPerSecond>
                            Side = 0.0<radianPerSecond>
                            Roll = 0.0<radianPerSecond> }
                          None
                          StrikeKind.Shot
                          (SimTick.ofSubTicks 0<subtick>)
                          ExecutionContext.OpenPlay

                  BallPhysics.applyImpulse
                      world
                      impulse
                      (TrajectoryIntent.Struck(shooter, 20.0<meterPerSecond>, 0.0, 0.5))
                      (Some 0<subtick>)

                  setShotBall world (Pitch.awayGoalX + 0.2<meter>) 0.5<meter>
                  lastTouch world homeOutfield

                  let buf = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 5UL) world buf |> ignore

                  let consequence =
                      buf.Events
                      |> Seq.find (fun event -> match event.Data with EventData.Goal _ -> true | _ -> false)

                  let cause = world.Observation.ConsumeCause consequence

                  Expect.equal
                      cause
                      (ValueSome(ActionCause impulse.ActionId))
                      "the goal is linked directly to the committed strike, not a probability result"

                  world.Observation.Record(consequence.Tick, cause, EventCommitted consequence)
                  |> ignore

                  let filter =
                      { Actor = Some shooter
                        Action = Some impulse.ActionId
                        Event = None
                        FromTick = 0<subtick>
                        UntilTick = world.State.Tick }

                  match Observation.query filter (world.Observation.Snapshot()) with
                  | Ok rows ->
                      Expect.isTrue
                          (rows |> Array.exists (fun entry -> entry.Data = EventCommitted consequence))
                          "actor/action query reaches the actual crossing consequence"
                  | Error error -> failtest error

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.Shot(_, ShotResult.OnTarget _, _, _, _, _) -> true
                          | _ -> false)
                      "shot on target emitted"

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                           | EventData.Goal _ -> true
                           | _ -> false)
                      "the unobstructed in-frame crossing is a goal"

                  Expect.isFalse
                      (buf.Events |> Seq.exists (fun event -> match event.Data with EventData.Save _ -> true | _ -> false))
                      "goalkeeper attributes cannot create a save without physical contact")

              testCase "Shot wide of the post is off target" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  setShotBall world (Pitch.awayGoalX + 0.2<meter>) 6.0<meter>
                  lastTouch world homeOutfield

                  let buf = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 5UL) world buf |> ignore

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.Shot(_, ShotResult.OffTarget, _, _, _, _) -> true
                          | _ -> false)
                      "off target shot emitted")

              testCase "Shot beyond the goal line with last touch Home is a goal kick for Away" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  setShotBall world 60.0<meter> 5.0<meter>
                  lastTouch world homeOutfield

                  let buf = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 5UL) world buf |> ignore

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.SetPiece(SetPieceKind.GoalKick, ClubSide.Away) -> true
                          | _ -> false)
                      "goal kick for away")

              testCase "Last touch by the defending side over the goal line is a corner" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  setShotBall world 60.0<meter> 5.0<meter>
                  lastTouch world awayOutfield

                  let buf = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 5UL) world buf |> ignore

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.SetPiece(SetPieceKind.Corner, ClubSide.Home) -> true
                          | _ -> false)
                      "corner for home")

              testCase "Ball past the touchline is a throw-in against the last toucher" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  setShotBall world 5.0<meter> 40.0<meter>
                  lastTouch world homeOutfield

                  let buf = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 5UL) world buf |> ignore

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.SetPiece(SetPieceKind.ThrowIn, ClubSide.Away) -> true
                          | _ -> false)
                      "throw-in for away")

              testCase "Ball inside the pitch emits nothing" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  setShotBall world 10.0<meter> 2.0<meter>
                  lastTouch world homeOutfield

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = None
                                  Trajectory = None } }

                  let buf = buffer ()
                  BallOutcome.step sctx (Rng.ofSeed 5UL) world buf |> ignore

                  Expect.isEmpty buf.Events "no events") ]
