namespace FootballEngine.Core.Tests.Systems.Players.Actions

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module PassTests =

    /// Neutral attacker (Passing/Vision exactly 60) so attribute terms vanish
    /// and the cognitive factor is what separates two quality reads.
    let private ctx =
        let home = TestBuilders.makeLineup 0 11

        let neutral =
            home.Profiles
            |> Array.mapi (fun i p ->
                if i = 5 || i = 7 || i = 9 then
                    { p with
                        Passing = 60.0f
                        Vision = 60.0f }
                else
                    p)

        { TestBuilders.defaultCtx () with
            HomeLineup = { home with Profiles = neutral } }

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private passQuality (world: World) (slot: int) (target: int) (seed: uint64) : float =
        Pass.launch sctx (Rng.ofSeed seed) world (buffer ()) ClubSide.Home slot target 0.0
        |> ignore

        match world.State.Ball.PendingStrike |> Option.map _.Trajectory with
        | Some(TrajectoryIntent.Aimed(_, _, quality)) -> quality
        | _ -> -1.0

    /// Puts Home slot/1 target on the pitch away from the offside trap: away
    /// defenders sit deep ahead of the target.
    let private standardLayout (world: World) : unit =
        world.Home.Frame.PosX.[5] <- 0.0f
        world.Home.Frame.PosY.[5] <- 0.0f
        world.Home.Frame.PosX.[7] <- 12.0f
        world.Home.Frame.PosY.[7] <- 0.0f
        // Away defenders well ahead so the target is onside.
        world.Away.Frame.PosX.[2] <- 25.0f
        world.Away.Frame.PosX.[3] <- 25.0f
        world.Away.Frame.PosX.[4] <- 25.0f

    let passTests =
        testList
            "FootballEngine.Core.Systems.Players.Actions.PassTests"
            [ testCase "tired receiver gets a softer delivery" (fun _ ->
                  let fresh = TestBuilders.minimalWorld ()
                  standardLayout fresh

                  fresh.State <-
                      { fresh.State with
                          Ball =
                              { fresh.State.Ball with
                                  Position =
                                      { X = 0.0<meter>
                                        Y = 0.0<meter>
                                        Z = 0.0<meter>
                                        Vx = 0.0<meterPerSecond>
                                        Vy = 0.0<meterPerSecond>
                                        Vz = 0.0<meterPerSecond> } } }

                  fresh.Home.Frame.SteerCognitive.[7] <- 1.0f
                  let qFresh = passQuality fresh 5 7 5UL

                  let tired = TestBuilders.minimalWorld ()
                  standardLayout tired

                  tired.State <-
                      { tired.State with
                          Ball =
                              { tired.State.Ball with
                                  Position =
                                      { X = 0.0<meter>
                                        Y = 0.0<meter>
                                        Z = 0.0<meter>
                                        Vx = 0.0<meterPerSecond>
                                        Vy = 0.0<meterPerSecond>
                                        Vz = 0.0<meterPerSecond> } } }

                  tired.Home.Frame.SteerCognitive.[7] <- 0.3f
                  let qTired = passQuality tired 5 7 5UL

                  Expect.isGreaterThan qFresh qTired "a fresh receiver is easier to feed")

              testCase "refused pass records the actual limit without applying an impulse or consuming RNG" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  standardLayout world
                  let actor = ctx.HomeLineup.Profiles[5].Id

                  let limited =
                      { sctx with
                          Ctx =
                              { ctx with
                                  Config =
                                      { ctx.Config with
                                          Actions =
                                              { ctx.Config.Actions with
                                                  MaxPassExecutionDistance = 10.0<meter> } } } }

                  let rng = Rng.ofSeed 19UL
                  let before = world.State.Ball
                  let after = Pass.launch limited rng world (buffer ()) ClubSide.Home 5 7 0.0
                  Expect.equal after rng "execution refusal has no random draw"
                  Expect.equal world.State.Ball before "execution refusal leaves the ball untouched"
                  let rows = world.Observation.Snapshot().Entries
                  Expect.hasLength rows 1 "one actual refusal captured"

                  Expect.equal
                      rows[0].Data
                      (ActionRejected(actor, PassDistanceLimit(12.0<meter>, 10.0<meter>)))
                      "measure and configured execution limit are retained"

                  Expect.isTrue
                      ((Observation.explain rows[0]).Contains("No impulse was applied"))
                      "explanation distinguishes refusal from physical failed contact"

                  Expect.isNonEmpty
                      (ConfigValidation.validateActions
                          { ctx.Config.Actions with
                              MaxPassExecutionDistance = System.Double.NaN * 1.0<meter> })
                      "nonfinite execution limit is rejected")

              testCase "arrival gate accounts for strike windup exactly once" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  standardLayout world

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Position =
                                      { world.State.Ball.Position with
                                          X = 0.0<meter>
                                          Y = 0.0<meter> } } }

                  Pass.launch sctx (Rng.ofSeed 29UL) world (buffer ()) ClubSide.Home 5 7 0.0
                  |> ignore

                  let pending =
                      world.State.Ball.PendingStrike
                      |> Option.defaultWith (fun () -> failtest "reachable passer prepares a physical strike")

                  let distance = 12.0

                  let seconds =
                      (BallAero.pass
                          ctx.Config.Ball
                          ctx.Config.Actions.PassArrivalSpeed
                          ctx.Config.Actions.PassArrivalSpeedGain
                          distance)
                          .Seconds

                  let expectedFlightTicks =
                      int (ceil (seconds * float sctx.Clock.SubTicksPerSecond)) * 1<subtick>

                  Expect.equal
                      pending.ArrivalGate
                      (Some(SimTick.value pending.Impulse.ContactTick + expectedFlightTicks))
                      "the post-strike flight gets one ceiling-rounded travel interval, without a second grace period")

              testCase "quality stays within bounds" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  standardLayout world
                  world.Home.Frame.SteerCognitive.[7] <- 1.0f

                  let q = passQuality world 5 7 3UL
                  Expect.isTrue (q >= 0.0 && q <= 1.0) "pass quality is a clamped probability") ]
