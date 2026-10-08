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

        let previousX =
            if x >= defLine then
                defLine - 0.5<meter>
            else
                x - 0.5<meter>

        let previousPosition =
            { X = previousX
              Y = y
              Z = 0.0<meter>
              Vx = 10.0<meterPerSecond>
              Vy = 0.0<meterPerSecond>
              Vz = 0.0<meterPerSecond> }

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
                        PreviousPosition = ValueSome previousPosition
                        ArrivalGate = Some 0<subtick>
                        Trajectory = Some(TrajectoryIntent.Struck(shooter, 18.0<meterPerSecond>, 0.2, 0.8))
                        LastTouch = Some shooter } }

        let buf = buffer ()
        BallOutcome.step sctx (Rng.ofSeed seed) world buf |> ignore
        buf

    let private spatial (x: float<meter>) (y: float<meter>) (z: float<meter>) : Spatial =
        { X = x
          Y = y
          Z = z
          Vx = 0.0<meterPerSecond>
          Vy = 0.0<meterPerSecond>
          Vz = 0.0<meterPerSecond> }

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
                  let buf = resolveCrossing 0.5<meter> (defLine + 0.1<meter>) 5UL
                  Expect.isTrue (hasScoreEvent buf) "inside the posts the GK must react")

              testCase "wide of the posts is never a goal" (fun _ ->
                  let buf = resolveCrossing 6.0<meter> (defLine + 0.1<meter>) 5UL
                  Expect.isFalse (hasGoal buf) "wide of the posts can't score")

              testCase "back of the net is a goal" (fun _ ->
                  // A strong strike deep past the line still resolves as a shot.
                  let buf = resolveCrossing 0.5<meter> (defLine + 0.5<meter>) 5UL
                  Expect.isTrue (hasScoreEvent buf) "over the line the GK still reacts")

              testCase "high crossing is not a goal" (fun _ ->
                  let previous = spatial (defLine - 0.5<meter>) 0.5<meter> 3.0<meter>
                  let current = spatial (defLine + 0.5<meter>) 0.5<meter> 3.0<meter>

                  Expect.isFalse
                      (BallPhysics.goalCrossing previous current
                       |> Option.exists (fun crossing -> BallPhysics.withinGoalFrame crossing.Position))
                      "crossbar height rejects the shot")

              testCase "crossing direction identifies both goal lines" (fun _ ->
                  let towardAway = spatial (Pitch.awayGoalX - 1.0<meter>) 0.0<meter> 0.0<meter>
                  let pastAway = spatial (Pitch.awayGoalX + 1.0<meter>) 0.0<meter> 0.0<meter>
                  let towardHome = spatial (Pitch.homeGoalX + 1.0<meter>) 0.0<meter> 0.0<meter>
                  let pastHome = spatial (Pitch.homeGoalX - 1.0<meter>) 0.0<meter> 0.0<meter>

                  match BallPhysics.goalCrossing towardAway pastAway, BallPhysics.goalCrossing towardHome pastHome with
                  | Some away, Some home ->
                      Expect.equal away.Side ClubSide.Home "away goal belongs to home scorer"
                      Expect.equal home.Side ClubSide.Away "home goal belongs to away scorer"
                  | other -> failtestf "expected both crossings, got %A" other) ]
