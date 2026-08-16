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

    let contactTests =
        testList
            "FootballEngine.Core.Systems.Ball.ContactTests"
            [ testCase "Clean reception gives possession and completed pass" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  placeAllFar world

                  let passer = ctx.HomeLineup.Profiles.[4].Id
                  let receiver = ctx.HomeLineup.Profiles.[5].Id
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

                  Expect.exists
                      buf.Events
                      (fun ev ->
                          match ev.Data with
                          | EventData.Pass(_, Some r, _, _, true) -> r = receiver
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

                  Expect.isEmpty buf.Events "no events"
                  Expect.equal world.State.Ball.Control BallControl.Free "ball untouched")

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

                  place world ClubSide.Home 5 7.0<meter> 0.0<meter>
                  place world ClubSide.Away 3 4.0<meter> 0.0<meter>
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
                          | EventData.Pass(_, Some r, _, _, true) -> r = receiver
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

                  place world ClubSide.Home 5 12.0<meter> 0.0<meter>
                  place world ClubSide.Away 3 8.0<meter> 0.0<meter>
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
                          | EventData.Pass(_, Some r, _, _, true) -> r = receiver
                          | _ -> false)
                      "completed pass to the intended receiver"

                  match world.State.Ball.Control with
                  | BallControl.Controlled(ClubSide.Home, pid) -> Expect.equal pid receiver "receiver controlled"
                  | other -> failtestf "expected Controlled, got %A" other) ]
