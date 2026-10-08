namespace FootballEngine.Core.Tests.Systems.Ball

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module ContactTests =

    let private ctx = TestBuilders.defaultCtx ()

    let private sctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let private buffer () =
        { Events = ResizeArray()
          Flags = { EventFlags.Bits = 0UL }
          Applied = ResizeArray() }

    let private place (world: World) (side: ClubSide) (slot: int) (x: float<meter>) (y: float<meter>) : unit =
        let team = MatchWorld.team world side
        team.Frame.PosX.[slot] <- float32 x
        team.Frame.PosY.[slot] <- float32 y

    let private placeAllFar (world: World) : unit =
        for s in [ ClubSide.Home; ClubSide.Away ] do
            for i in MatchWorld.activeSlots (MatchWorld.team world s).Frame do
                place world s i -60.0<meter> 40.0<meter>

    let private setBall (world: World) (x: float<meter>) (y: float<meter>) : unit =
        world.State <-
            { world.State with
                Ball =
                    { world.State.Ball with
                        Position =
                            { world.State.Ball.Position with
                                X = x
                                Y = y } } }

    let private customProfile
        (slot: int)
        (pace: float32)
        (anticipation: float32)
        (positioning: float32)
        (c: MatchContext)
        : MatchContext =
        c.HomeLineup.Profiles.[slot] <-
            { c.HomeLineup.Profiles.[slot] with
                Pace = pace
                Anticipation = anticipation
                Positioning = positioning }

        c

    let private customAwayProfile
        (slot: int)
        (pace: float32)
        (anticipation: float32)
        (positioning: float32)
        (c: MatchContext)
        : MatchContext =
        c.AwayLineup.Profiles.[slot] <-
            { c.AwayLineup.Profiles.[slot] with
                Pace = pace
                Anticipation = anticipation
                Positioning = positioning }

        c

    let private hasEvent (buf: EventBuffer) (filter: EventData -> bool) : bool =
        buf.Events |> Seq.exists (fun ev -> filter ev.Data)

    let private applyEvents (c: MatchContext) (world: World) (buf: EventBuffer) : unit =
        for ev in buf.Events do
            EventApply.apply c world ev

    let contactTests =
        testList
            "FootballEngine.Core.Systems.Ball.ContactTests"
            [ testCase "Clean reception gives possession and completed pass" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world

                  let passer = ctx.HomeLineup.Profiles.[4].Id
                  let receiver = ctx.HomeLineup.Profiles.[5].Id

                  let impulse =
                      BallPhysics.createImpulse
                          passer
                          { X = 5.0<meter>
                            Y = 0.0<meter>
                            Z = 0.0<meter> }
                          { X = 0.0<meterPerSecond>
                            Y = 0.0<meterPerSecond>
                            Z = 0.0<meterPerSecond> }
                          { Top = 0.0<radianPerSecond>
                            Side = 0.0<radianPerSecond>
                            Roll = 0.0<radianPerSecond> }
                          None
                          StrikeKind.Pass
                          (SimTick.ofSubTicks 0<subtick>)
                          ExecutionContext.OpenPlay

                  BallPhysics.applyImpulse
                      world
                      impulse
                      (TrajectoryIntent.Aimed(passer, receiver, 1.0))
                      (Some 0<subtick>)

                  place world ClubSide.Home 5 5.0<meter> 0.0<meter>
                  setBall world 5.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = Some 0<subtick>
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, receiver, 1.0)) } }

                  let buf = buffer ()
                  BallContact.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents ctx world buf

                  let completed =
                      buf.Events
                      |> Seq.find (fun event ->
                          match event.Data with
                          | EventData.Pass _ -> true
                          | _ -> false)

                  match world.Observation.ConsumeCause completed with
                  | ValueSome(ResolutionCause id) ->
                      let snapshot = world.Observation.Snapshot()
                      let resolution = snapshot.Entries |> Array.find (fun entry -> entry.Id = id)

                      Expect.equal
                          resolution.Cause
                          (ValueSome(ActionCause impulse.ActionId))
                          "reception resolution points at the executed pass"

                      Expect.hasLength (Observation.causes snapshot resolution) 1 "pass execution is queryable"
                  | other -> failtestf "pass must reference its actual reception resolution, got %A" other

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.Pass(_, Some r, _, _, true, _, _, _, _) -> r = receiver
                          | _ -> false)
                      "completed pass emitted"

                  match world.State.Ball.Control with
                  | BallControl.Controlled(ClubSide.Home, pid) -> Expect.equal pid receiver "receiver got the ball"
                  | other -> failtestf "expected Controlled, got %A" other)

              testCase "Interception: closer defender beats a far receiver" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world

                  let passer = ctx.HomeLineup.Profiles.[4].Id
                  let receiver = ctx.HomeLineup.Profiles.[5].Id
                  let defender = ctx.AwayLineup.Profiles.[3].Id

                  place world ClubSide.Home 5 20.0<meter> 0.0<meter>
                  place world ClubSide.Away 3 5.0<meter> 0.0<meter>
                  setBall world 5.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = Some 0<subtick>
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, receiver, 0.6)) } }

                  let buf = buffer ()
                  BallContact.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents ctx world buf

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.Interception(i, p) -> i = defender && p = passer
                          | _ -> false)
                      "interception emitted"

                  match world.State.Ball.Control with
                  | BallControl.Controlled(ClubSide.Away, pid) -> Expect.equal pid defender "defender got the ball"
                  | other -> failtestf "expected Controlled Away, got %A" other)

              testCase "stationary aimed ball with no contact becomes loose" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  let passer = ctx.HomeLineup.Profiles.[4].Id
                  let receiver = ctx.HomeLineup.Profiles.[5].Id
                  setBall world 48.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Airborne
                                  StationarySince = Some 0<subtick>
                                  ArrivalGate = Some 0<subtick>
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, receiver, 1.0)) } }

                  let buf = buffer ()
                  BallContact.step sctx (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.isTrue
                      (hasEvent buf (fun ev ->
                          match ev with
                          | EventData.BallLoose _ -> true
                          | _ -> false))
                      "a missed stationary arrival is visible as a loose ball"

                  Expect.equal world.State.Ball.Control BallControl.Free "the ball can be claimed"
                  Expect.isNone world.State.Ball.Trajectory "the expired arrival no longer owns the ball")

              testCase "No arrival gate means nothing happens" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  setBall world 5.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = None
                                  Trajectory =
                                      Some(
                                          TrajectoryIntent.Aimed(
                                              ctx.HomeLineup.Profiles.[4].Id,
                                              ctx.HomeLineup.Profiles.[5].Id,
                                              0.8
                                          )
                                      ) } }

                  let buf = buffer ()
                  BallContact.step sctx (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents ctx world buf

                  Expect.isEmpty buf.Events "no events"
                  Expect.equal world.State.Ball.Control BallControl.Free "ball untouched")

              testCase "Expired arrival gate does not resolve a clearance" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  setBall world 0.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  Control = BallControl.Airborne
                                  ArrivalGate = Some 0<subtick>
                                  Trajectory = Some TrajectoryIntent.Cleared } }

                  let buf = buffer ()
                  BallContact.step sctx (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.isEmpty buf.Events "the gate alone cannot resolve the flight"

                  match world.State.Ball.Trajectory with
                  | Some TrajectoryIntent.Cleared -> ()
                  | other -> failtestf "expected clearance to remain in flight, got %A" other)

              testCase "Gate in the future is not resolved yet" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world
                  setBall world 5.0<meter> 0.0<meter>

                  world.State <-
                      { world.State with
                          Tick = 0<subtick>
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = Some 100<subtick>
                                  Trajectory =
                                      Some(
                                          TrajectoryIntent.Aimed(
                                              ctx.HomeLineup.Profiles.[4].Id,
                                              ctx.HomeLineup.Profiles.[5].Id,
                                              0.8
                                          )
                                      ) } }

                  let buf = buffer ()
                  BallContact.step sctx (Rng.ofSeed 3UL) world buf |> ignore

                  Expect.isEmpty buf.Events "no events")

              testCase "race won by time, not distance" (fun _ ->
                  let c =
                      TestBuilders.defaultCtx ()
                      |> customProfile 5 90.0f 20.0f 20.0f
                      |> customAwayProfile 3 40.0f 90.0f 90.0f

                  let sctx' =
                      { Ctx = c
                        Clock = SimulationClock.defaultClock }

                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world

                  let passer = c.HomeLineup.Profiles.[4].Id
                  let receiver = c.HomeLineup.Profiles.[5].Id

                  let _ = place world ClubSide.Home 5 2.4<meter> 0.0<meter>
                  let _ = place world ClubSide.Away 3 2.0<meter> 0.0<meter>
                  setBall world 0.0<meter> 0.0<meter>

                  (MatchWorld.team world ClubSide.Home).Frame.Intent.Kind.[5] <- IntentCode.ExecuteRun

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = Some 0<subtick>
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, receiver, 1.0)) } }

                  let buf = buffer ()
                  BallContact.step sctx' (Rng.ofSeed 3UL) world buf |> ignore
                  Expect.isEmpty buf.Events "arrival prediction cannot grant remote contact"
                  place world ClubSide.Home 5 0.7<meter> 0.0<meter> |> ignore
                  BallContact.step sctx' (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents c world buf

                  Expect.isFalse
                      (hasEvent buf (fun ev ->
                          match ev with
                          | EventData.Interception _ -> true
                          | _ -> false))
                      "no interception: fast receiver won by time despite being farther"

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.Pass(_, Some r, _, _, true, _, _, _, _) -> r = receiver
                          | _ -> false)
                      "completed pass to the fast receiver"

                  match world.State.Ball.Control with
                  | BallControl.Controlled(ClubSide.Home, pid) -> Expect.equal pid receiver "receiver controlled"
                  | other -> failtestf "expected Controlled, got %A" other)

              testCase "receiver with a free lane wins when they genuinely arrive first" (fun _ ->
                  let c =
                      TestBuilders.defaultCtx ()
                      |> customProfile 5 90.0f 20.0f 20.0f
                      |> customAwayProfile 3 25.0f 90.0f 90.0f

                  let sctx' =
                      { Ctx = c
                        Clock = SimulationClock.defaultClock }

                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world

                  let passer = c.HomeLineup.Profiles.[4].Id
                  let receiver = c.HomeLineup.Profiles.[5].Id

                  let _ = place world ClubSide.Home 5 2.4<meter> 0.0<meter>
                  let _ = place world ClubSide.Away 3 2.0<meter> 0.0<meter>
                  setBall world 0.0<meter> 0.0<meter>

                  (MatchWorld.team world ClubSide.Home).Frame.Intent.Kind.[5] <- IntentCode.ExecuteRun

                  world.State <-
                      { world.State with
                          Ball =
                              { world.State.Ball with
                                  ArrivalGate = Some 0<subtick>
                                  Trajectory = Some(TrajectoryIntent.Aimed(passer, receiver, 1.0)) } }

                  let buf = buffer ()
                  BallContact.step sctx' (Rng.ofSeed 3UL) world buf |> ignore
                  Expect.isEmpty buf.Events "a free lane is not a physical contact"
                  place world ClubSide.Home 5 0.7<meter> 0.0<meter> |> ignore
                  BallContact.step sctx' (Rng.ofSeed 3UL) world buf |> ignore
                  applyEvents c world buf

                  Expect.isFalse
                      (hasEvent buf (fun ev ->
                          match ev with
                          | EventData.Interception _ -> true
                          | _ -> false))
                      "no interception: the receiver arrived first"

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.Pass(_, Some r, _, _, true, _, _, _, _) -> r = receiver
                          | _ -> false)
                      "completed pass to the intended receiver"

                  match world.State.Ball.Control with
                  | BallControl.Controlled(ClubSide.Home, pid) -> Expect.equal pid receiver "receiver controlled"
                  | other -> failtestf "expected Controlled, got %A" other) ]
