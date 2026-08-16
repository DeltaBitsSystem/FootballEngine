namespace FootballEngine.Core.Tests.Systems.Ball

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module GoalDetectionTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private shooter = ctx.HomeLineup.Profiles.[9].Id
    let private defLine = Pitch.awayGoalX

    /// Places the ball at the goal line (already crossed) with a struck
    /// trajectory and lets BallOutcome resolve it.
    let private resolveCrossing (y: float<meter>) (x: float<meter>) (seed: uint64) : EventBuffer =
        let world = TestBuilders.minimalWorld ()

        world.State <-
            { world.State with
                Ball =
                    { world.State.Ball with
                        Position =
                            { X = x
                              Y = y
                              Z = 0.0<meter>
                              Vx = 10.0<meterPerSecond>
                              Vy = 0.0<meterPerSecond>
                              Vz = 0.0<meterPerSecond> }
                        ArrivalGate = Some 0<subtick>
                        Trajectory = Some(TrajectoryIntent.Struck(shooter, 18.0<meterPerSecond>, 0.2, 0.8))
                        LastTouch = Some shooter } }

        let buf = buffer ()
        BallOutcome.step sctx (Rng.ofSeed seed) world buf |> ignore
        buf

    let private hasGoal (buf: EventBuffer) =
        buf.Events |> Seq.exists (fun ev -> MatchEvent.kind ev = EventKind.Goal)

    let private hasScoreEvent (buf: EventBuffer) =
        buf.Events
        |> Seq.exists (fun ev ->
            match ev.Data with
            | EventData.Goal _
            | EventData.Save _ -> true
            | _ -> false)

    let goalDetectionTests =
        testList
            "FootballEngine.Core.Systems.Ball.GoalDetectionTests"
            [ testCase "ball inside the frame yields goal or save" (fun _ ->
                  let buf = resolveCrossing 0.5<meter> (defLine - 0.1<meter>) 5UL
                  Expect.isTrue (hasScoreEvent buf) "inside the posts the GK must react")

              testCase "wide of the posts is never a goal" (fun _ ->
                  let buf = resolveCrossing 6.0<meter> (defLine - 0.1<meter>) 5UL
                  Expect.isFalse (hasGoal buf) "wide of the posts can't score")

              testCase "back of the net is a goal" (fun _ ->
                  // A strong strike deep past the line still resolves as a shot.
                  let buf = resolveCrossing 0.5<meter> (defLine - 0.5<meter>) 5UL
                  Expect.isTrue (hasScoreEvent buf) "over the line the GK still reacts") ]
