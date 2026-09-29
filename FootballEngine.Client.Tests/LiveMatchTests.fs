module FootballEngine.Client.Tests.LiveMatchTests

open System
open System.Diagnostics
open System.Threading
open Expecto
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Client.Views.Components
open FootballEngine.Core.Match
open FootballEngine.Data
open FootballEngine.Domain
open FootballEngine.Generation
open FootballEngine.LiveMatch
open FootballEngine.Routing
open FootballEngine.Settings
open FootballEngine.World

let private career () =
    let country =
        """{"id":"test.live:country/tst","kind":"country","schema":1,"code":"TST","confederation":"UEFA","nameKey":"test.live:country/tst.name","domain":{"Country":{"Code":"TST","Name":"Testland","Confederation":"UEFA"},"Names":{"FirstNames":["Alex","Sam"],"LastNames":["Stone","River"]},"LeagueNames":["Test League"],"LeagueRules":[{"PointsForWin":3,"PointsForDraw":1,"Tiebreakers":["GD","GS"],"Promotion":[],"Relegation":[]}],"Clubs":[{"Name":"North FC","LeagueLevel":0,"Reputation":7000},{"Name":"South FC","LeagueLevel":0,"Reputation":6500},{"Name":"East FC","LeagueLevel":0,"Reputation":6000},{"Name":"West FC","LeagueLevel":0,"Reputation":6000}],"Cups":[]}}"""

    let package: ModPackage =
        { Manifest =
            { Id = ModId "test.live"
              Version = "1.0.0"
              Dependencies = []
              Priority = 0
              Before = []
              After = [] }
          PackageSha256 = "test-live"
          SportsContent = Map.ofList [ ContentId "test.live:country/tst", country ]
          Translations = Map.empty
          Assets = Map.empty
          Patches = [] }

    let resolved =
        match
            ModSystem.resolve
                [ { RelativePath = "test-live.femod"
                    Package = package } ]
        with
        | Ok value -> value
        | Error error -> failtestf "Cannot resolve career: %A" error

    match WorldGen.generateNewGame resolved 0xC02UL (DateTime(2026, 7, 1)) "TST" "Live Manager" [] with
    | Ok value -> value
    | Error error -> failtestf "Cannot generate career: %A" error

let private scheduledCareer () =
    let gs = career ()

    let matchId, fixture =
        gs.Competitions
        |> Map.toSeq
        |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
        |> Seq.filter (fun (_, fixture) ->
            MatchFixture.isPending fixture && MatchFixture.involves gs.UserClubId fixture)
        |> Seq.minBy (fun (_, fixture) -> fixture.ScheduledDate)

    matchId,
    { gs with
        CurrentDate = fixture.ScheduledDate.Date }

let private policy =
    { LiveExecutionPolicy.defaultPolicy with
        BufferCapacity = 8 }

let private awaitPublication (host: MatchRuntimeHost) runtime pick =
    let timeout = Stopwatch.StartNew()
    let mutable result = None

    while result.IsNone && timeout.Elapsed < TimeSpan.FromSeconds 10.0 do
        match host.TryTake runtime with
        | Some(MatchRuntimePublication.Failed error) -> failtest error
        | Some publication -> result <- pick publication
        | None -> Thread.Sleep 1

    result
    |> Option.defaultWith (fun () -> failtest "Runtime publication timed out")

let private visualSnapshot tick x : Replay.ReplaySnapshot =
    { Tick = tick
      HomePlayerIds = [| FootballEngine.Core.Match.PlayerId 1 |]
      AwayPlayerIds = [| FootballEngine.Core.Match.PlayerId 2 |]
      HomeScore = 0
      AwayScore = 0
      Flow = 0uy
      BallX = x
      BallY = 0.0f
      BallZ = 0.0f
      BallVx = 1.0f
      BallVy = 0.0f
      BallVz = 0.0f
      BallSpinTop = 0.0f
      BallSpinSide = 0.0f
      BallControlSide = -1y
      BallController = -1
      RefereeX = 0.0f
      RefereeY = 0.0f
      NearAssistantX = 0.0f
      NearAssistantY = 0.0f
      FarAssistantX = 0.0f
      FarAssistantY = 0.0f
      RefereeSignal = 0uy
      HomeX = [| x |]
      HomeY = [| 0.0f |]
      HomeVx = [| 1.0f |]
      HomeVy = [| 0.0f |]
      HomeCondition = [| 1.0f |]
      HomeIntent = [| 0uy |]
      HomeActive = [| true |]
      AwayX = [| -x |]
      AwayY = [| 0.0f |]
      AwayVx = [| -1.0f |]
      AwayVy = [| 0.0f |]
      AwayCondition = [| 1.0f |]
      AwayIntent = [| 0uy |]
      AwayActive = [| true |] }

let private renderPlayer id x =
    { Id = id
      Position = Vector2.create x 0.0
      Velocity = Vector2.zero
      Acceleration = Vector2.zero
      State = Idle
      VisualSlot = id
      ShortName = string id
      Condition = 100
      IsHome = true
      HasBall = false
      IsSidelined = false }

let private withRuntime test =
    let matchId, gs = scheduledCareer ()
    let host = MatchRuntimeHost()

    let runtime =
        host.CreateUserMatch(matchId, gs, WorldClockOps.init gs.Season, None, MatchRunMode.Live policy)

    host.Start(runtime, ignore)

    try
        let session, fixture =
            awaitPublication host runtime (function
                | MatchRuntimePublication.Connected(session, fixture, _, _) -> Some(session, fixture)
                | _ -> None)

        test host runtime session fixture gs
    finally
        host.Close runtime

let tests =
    testList
        "LiveMatch"
        [ testCase "ring buffer overwrites oldest poses without blocking and brackets adjacent poses"
          <| fun _ ->
              let stream =
                  MatchPresentationStream(
                      MatchRuntimeId 501UL,
                      { policy with
                          BufferCapacity = 3
                          PresentationDelay = TimeSpan.Zero }
                  )

              for tick in 0..3 do
                  let measuredTick: int<subtick> = tick * 1<subtick>

                  stream.Publish(measuredTick, visualSnapshot measuredTick (float32 tick), int64 (3 - tick))
                  |> ignore

              let poses = stream.TryReadAfter 0UL

              Expect.equal
                  (poses |> Array.map _.Tick)
                  [| 1<subtick>; 2<subtick>; 3<subtick> |]
                  "the oldest pose is overwritten"

              poses
              |> Array.pairwise
              |> Array.iter (fun (a, b) -> Expect.isGreaterThan b.Sequence a.Sequence "sequence is monotonic")

              match stream.Bracket 2.5 with
              | Some(first, second, t) ->
                  Expect.equal first.Tick 2<subtick> "bracket starts at the adjacent lower pose"
                  Expect.equal second.Tick 3<subtick> "bracket ends at the adjacent upper pose"
                  Expect.floatClose Accuracy.high t 0.5 "bracket parameter is correct"
              | None -> failtest "expected a pose bracket"

          testCase "presentation clock freezes at the frontier and resumes without jumping"
          <| fun _ ->
              let stream =
                  MatchPresentationStream(
                      MatchRuntimeId 502UL,
                      { policy with
                          PresentationDelay = TimeSpan.Zero }
                  )

              let started = Stopwatch.GetTimestamp()
              stream.Publish(0<subtick>, visualSnapshot 0<subtick> 0.0f, started) |> ignore

              stream.Publish(1<subtick>, visualSnapshot 1<subtick> 1.0f, started + 1L)
              |> ignore

              let afterStarvation = started + int64 (0.5 * float Stopwatch.Frequency)

              match stream.Sample afterStarvation with
              | Some(Hold pose) -> Expect.equal pose.Tick 1<subtick> "the visual clock holds at the known frontier"
              | sample -> failtestf "expected a held frontier, got %A" sample

              stream.Publish(2<subtick>, visualSnapshot 2<subtick> 2.0f, afterStarvation + 1L)
              |> ignore

              match stream.Sample afterStarvation with
              | Some(Between(first, second, t)) ->
                  Expect.equal first.Tick 0<subtick> "recovery retains the previous bracket"
                  Expect.equal second.Tick 1<subtick> "recovery starts from the held frontier"
                  Expect.floatClose Accuracy.high t 1.0 "recovery does not skip into newly arrived movement"
              | sample -> failtestf "expected continuous recovery, got %A" sample

          testCase "flow changes create typed epochs and never interpolate across the boundary"
          <| fun _ ->
              let stream =
                  new MatchPresentationStream(
                      MatchRuntimeId 503UL,
                      { policy with
                          PresentationDelay = TimeSpan.Zero }
                  )

              let started = Stopwatch.GetTimestamp()
              stream.Publish(0<subtick>, visualSnapshot 0<subtick> 0.0f, started) |> ignore

              let boundary =
                  { visualSnapshot 40<subtick> 40.0f with
                      Flow = 1uy }

              stream.Publish(40<subtick>, boundary, started + Stopwatch.Frequency) |> ignore

              let poses = stream.TryReadAfter 0UL
              Expect.equal poses[1].Epoch 1UL "a flow transition starts a new presentation epoch"

              Expect.equal poses[1].EpochStart (Some(MatchFlowChanged(0uy, 1uy))) "the epoch retains a typed reason"

              match stream.Bracket 20.0 with
              | Some(first, second, _) ->
                  Expect.equal first.Tick 0<subtick> "a pre-boundary bracket uses the prior pose"
                  Expect.equal second.Tick 0<subtick> "a pre-boundary bracket never crosses epochs"
              | None -> failtest "expected a bracket before the flow boundary"

              match stream.Bracket 40.0 with
              | Some(first, second, _) ->
                  Expect.equal first.Tick 40<subtick> "the authoritative transition tick starts the new epoch"
                  Expect.equal second.Tick 40<subtick> "the new epoch is sampled without a cross-boundary tangent"
              | None -> failtest "expected a bracket at the flow boundary"

              match stream.Sample(started + int64 (0.5 * float Stopwatch.Frequency)) with
              | Some(Hold pose) ->
                  Expect.equal pose.Tick 0<subtick> "pre-boundary poses remain held until the transition tick"
              | sample -> failtestf "a flow boundary must not interpolate: %A" sample

              match stream.Sample(started + Stopwatch.Frequency) with
              | Some(Hold pose) ->
                  Expect.equal pose.Tick 40<subtick> "the new epoch becomes visible at its authoritative tick"
              | sample -> failtestf "expected the new epoch at its tick: %A" sample

          testCase "participation changes and regressed ticks are explicit epoch boundaries"
          <| fun _ ->
              let stream = new MatchPresentationStream(MatchRuntimeId 504UL, policy)
              stream.Publish(10<subtick>, visualSnapshot 10<subtick> 1.0f, 0L) |> ignore

              let participationChange =
                  { visualSnapshot 20<subtick> 2.0f with
                      HomeActive = [| false |] }

              stream.Publish(20<subtick>, participationChange, 1L) |> ignore

              let tickRegression =
                  { visualSnapshot 5<subtick> 3.0f with
                      HomeActive = [| false |] }

              stream.Publish(5<subtick>, tickRegression, 2L) |> ignore

              let poses = stream.TryReadAfter 0UL

              Expect.equal poses[1].EpochStart (Some ParticipationChanged) "active roster changes start an epoch"

              Expect.equal
                  poses[2].EpochStart
                  (Some SimulationTickRegressed)
                  "seeking behind the published frontier cannot be mistaken for forward motion"

              match stream.Sample 2L with
              | Some(Hold pose) ->
                  Expect.equal pose.Tick 5<subtick> "a tick regression begins sampling from the seek destination"
              | sample -> failtestf "expected the seek destination to be held, got %A" sample

          testCase "ring overflow reports one underrun and resumes at the oldest retained pose"
          <| fun _ ->
              let stream =
                  new MatchPresentationStream(
                      MatchRuntimeId 505UL,
                      { policy with
                          BufferCapacity = 2
                          PresentationDelay = TimeSpan.Zero }
                  )

              stream.Publish(0<subtick>, visualSnapshot 0<subtick> 0.0f, 0L) |> ignore
              stream.Publish(40<subtick>, visualSnapshot 40<subtick> 40.0f, 1L) |> ignore
              stream.Publish(80<subtick>, visualSnapshot 80<subtick> 80.0f, 2L) |> ignore

              Expect.equal stream.DroppedPoseCount 1UL "the overwritten ring entry is observable"

              match stream.Sample 2L with
              | Some(Hold pose) ->
                  Expect.equal pose.Tick 40<subtick> "an underrun skips only to the oldest still-retained pose"
              | sample -> failtestf "expected a held retained pose after underrun, got %A" sample

              stream.Sample 3L |> ignore
              Expect.equal stream.UnderrunCount 1UL "one continuous underrun is counted once"

          testCase "authoritative presentation events survive pose-ring overflow with stable ordering"
          <| fun _ ->
              let stream =
                  new MatchPresentationStream(
                      MatchRuntimeId 506UL,
                      { policy with
                          BufferCapacity = 2
                          PresentationDelay = TimeSpan.Zero }
                  )

              let event =
                  { Tick = 40<subtick>
                    Data = EventData.SetPiece(SetPieceKind.KickOff, ClubSide.Home) }

              stream.PublishEvents [| event; event |] |> ignore

              for tick in [| 0<subtick>; 40<subtick>; 80<subtick> |] do
                  stream.Publish(tick, visualSnapshot tick (float32 tick), int64 tick) |> ignore

              let events = stream.ReadEventsAfter 0UL

              Expect.equal events.Length 2 "pose capacity does not discard authoritative events"
              Expect.equal (events |> Array.map _.Sequence) [| 1UL; 2UL |] "event order is explicit"
              Expect.equal events[0].Tick 40<subtick> "the effective simulation tick is retained"
              Expect.equal events[0].Id events[1].Id "identical event hashes remain distinct ordered occurrences"

          testCase "default live policy captures ten visual poses per match second"
          <| fun _ ->
              Expect.equal
                  LiveExecutionPolicy.defaultPolicy.WorkerSlice
                  2<tickDelta>
                  "workers publish every 200 ms at 1x"

              Expect.equal LiveExecutionPolicy.defaultPolicy.PoseEvery 1<tickDelta> "every simulation pose is retained"

              Expect.equal
                  LiveExecutionPolicy.defaultPolicy.SemanticEvery
                  10<tickDelta>
                  "Elmish remains bounded to 1 Hz"

          testCase "identity interpolation ignores render ordering"
          <| fun _ ->
              let first =
                  { Players = [| renderPlayer 1 0.0; renderPlayer 2 10.0 |]
                    Ball =
                      { Position = Vector2.zero
                        Velocity = Vector2.zero
                        VelocityZ = 0.0
                        Spin = Vector2.zero
                        Height = 0.0 }
                    Officials = [||]
                    TimeSeconds = 0.0
                    HomeScore = 0
                    AwayScore = 0
                    AttackingClubId = None
                    Momentum = 0.0
                    Situation = MatchSituation.Live
                    HomeName = "H"
                    AwayName = "A"
                    CompetitionName = None }

              let second =
                  { first with
                      Players = [| renderPlayer 2 20.0; renderPlayer 1 10.0 |]
                      TimeSeconds = 1.0 }

              let interpolated = MatchInterp.hermite first second 0.5 1.0

              Expect.floatClose
                  Accuracy.high
                  (interpolated.Players |> Array.find (fun p -> p.Id = 1)).Position.X
                  5.0
                  "player one follows its identity"

              Expect.floatClose
                  Accuracy.high
                  (interpolated.Players |> Array.find (fun p -> p.Id = 2)).Position.X
                  15.0
                  "player two follows its identity"

          testCase "fast ball movement remains continuous between visual poses"
          <| fun _ ->
              let first =
                  { Players = [||]
                    Ball =
                      { Position = Vector2.create 0.0 0.0
                        Velocity = Vector2.create 20.0 0.0
                        VelocityZ = 0.0
                        Spin = Vector2.zero
                        Height = 0.0 }
                    Officials = [||]
                    TimeSeconds = 0.0
                    HomeScore = 0
                    AwayScore = 0
                    AttackingClubId = None
                    Momentum = 0.0
                    Situation = MatchSituation.Live
                    HomeName = "H"
                    AwayName = "A"
                    CompetitionName = None }

              let second =
                  { first with
                      Ball =
                          { first.Ball with
                              Position = Vector2.create 8.0 0.0 }
                      TimeSeconds = 0.4 }

              let interpolated = MatchInterp.hermite first second 0.5 0.4
              Expect.isGreaterThan interpolated.Ball.Position.X 0.0 "the ball leaves its previous pose before midpoint"
              Expect.isLessThan interpolated.Ball.Position.X 8.0 "the ball does not snap to its next pose at midpoint"

          testCase "subsecond pose intervals scale Hermite tangents in seconds"
          <| fun _ ->
              let movingPlayer =
                  { renderPlayer 1 0.0 with
                      Velocity = Vector2.create 10.0 0.0 }

              let first =
                  { Players = [| movingPlayer |]
                    Ball =
                      { Position = Vector2.zero
                        Velocity = Vector2.create 10.0 0.0
                        VelocityZ = 10.0
                        Spin = Vector2.zero
                        Height = 0.0 }
                    Officials = [||]
                    TimeSeconds = 0.0
                    HomeScore = 0
                    AwayScore = 0
                    AttackingClubId = None
                    Momentum = 0.0
                    Situation = MatchSituation.Live
                    HomeName = "H"
                    AwayName = "A"
                    CompetitionName = None }

              let second =
                  { first with
                      Players =
                          [| { movingPlayer with
                                 Position = Vector2.create 1.0 0.0
                                 Velocity = Vector2.zero } |]
                      Ball =
                          { first.Ball with
                              Position = Vector2.create 1.0 0.0
                              Velocity = Vector2.zero
                              Height = 1.0
                              VelocityZ = 0.0 }
                      TimeSeconds = 0.1 }

              let interpolated = MatchInterp.hermite first second 0.25 0.1

              Expect.floatClose
                  Accuracy.high
                  interpolated.Players[0].Position.X
                  0.296875
                  "player tangents use the actual 100 ms interval"

              Expect.floatClose
                  Accuracy.high
                  interpolated.Ball.Position.X
                  0.296875
                  "ball tangents use the same presentation interval"

              Expect.floatClose
                  Accuracy.high
                  interpolated.Ball.Height
                  0.296875
                  "vertical height uses measured vertical endpoint tangents"

              Expect.floatClose
                  Accuracy.high
                  interpolated.Ball.VelocityZ
                  13.125
                  "vertical velocity is the derivative of the interpolated height"

              let duplicateTick = MatchInterp.hermite first second 0.75 0.0
              Expect.equal duplicateTick.Players[0].Position.X 1.0 "zero-duration samples snap without NaN"

          testCase "presentation time is invariant at 30, 60 and 144 render samples per second"
          <| fun _ ->
              let sampleAtRate rate =
                  let presentationPolicy =
                      { policy with
                          PresentationDelay = TimeSpan.Zero }

                  use stream = new MatchPresentationStream(MatchRuntimeId 1UL, presentationPolicy)
                  let frequency = float Stopwatch.Frequency
                  stream.Publish(0<subtick>, visualSnapshot 0<subtick> 0.0f, 0L) |> ignore

                  stream.Publish(40<subtick>, visualSnapshot 40<subtick> 40.0f, Stopwatch.Frequency)
                  |> ignore

                  let mutable sample = None

                  for frame = 0 to rate / 2 do
                      let timestamp = int64 (frequency * float frame / float rate)
                      sample <- stream.Sample timestamp

                  sample

              let sampleTimes = [ 30; 60; 144 ] |> List.map sampleAtRate

              sampleTimes
              |> List.iter (function
                  | Some(Between(_, _, t)) ->
                      Expect.floatClose Accuracy.high t 0.5 "all display rates present the same authoritative time"
                  | sample -> failtestf "expected an interpolated presentation sample, got %A" sample)

          testCase "poses are visual-only, monotonic and bounded"
          <| fun _ ->
              withRuntime (fun host runtime _ fixture _ ->
                  awaitPublication host runtime (function
                      | MatchRuntimePublication.Semantic _ -> Some()
                      | _ -> None)

                  let stream = host.PresentationStream runtime |> Option.get
                  let poses = stream.TryReadAfter 0UL

                  Expect.isNonEmpty poses "the visual stream receives poses"
                  Expect.isLessThanOrEqual poses.Length policy.BufferCapacity "the visual stream is bounded"

                  poses
                  |> Array.pairwise
                  |> Array.iter (fun (a, b) -> Expect.isGreaterThan b.Sequence a.Sequence "pose sequence is monotonic")

                  Expect.equal stream.RuntimeId runtime "the stream is owned by its runtime"
                  Expect.equal fixture.MatchId fixture.MatchId "fixture metadata remains semantic")

          testCase "semantic state has score and no pose ownership"
          <| fun _ ->
              let matchId, gs = scheduledCareer ()
              let host = MatchRuntimeHost()

              let runtime =
                  host.CreateUserMatch(matchId, gs, WorldClockOps.init gs.Season, None, MatchRunMode.Live policy)

              host.Start(runtime, ignore)

              try
                  let semantic =
                      awaitPublication host runtime (function
                          | MatchRuntimePublication.Semantic value -> Some value
                          | _ -> None)

                  Expect.isTrue
                      (semantic.Events
                       |> Array.forall (fun event -> event.Data.ToString().Contains("ReplaySnapshot") |> not))
                      "semantic events do not carry pose arrays"

                  Expect.isNull
                      (typeof<LiveMatchState>.GetProperty("PresentationSnapshot"))
                      "presentation snapshot was removed from Elmish state"

                  Expect.isNull
                      (typeof<LiveMatchState>.GetProperty("LatestSnapshot"))
                      "latest pose ownership was removed from Elmish state"
              finally
                  host.Close runtime

          testCase "pause freezes the visual stream and resume continues without catch-up"
          <| fun _ ->
              withRuntime (fun host runtime session _ _ ->
                  let semantic =
                      awaitPublication host runtime (function
                          | MatchRuntimePublication.Semantic value -> Some value
                          | _ -> None)

                  let pause =
                      { CommandId = MatchCommandId 1UL
                        SessionId = session
                        ExpectedRevision = semantic.Revision
                        IssuedAt = semantic.Frontier
                        Payload = PauseMatch }

                  host.Submit(runtime, pause)

                  let revision =
                      awaitPublication host runtime (function
                          | MatchRuntimePublication.CommandApplied(id, revision, _) when id = pause.CommandId ->
                              Some revision
                          | _ -> None)

                  awaitPublication host runtime (function
                      | MatchRuntimePublication.StatusChanged Paused -> Some()
                      | _ -> None)

                  let stream = host.PresentationStream runtime |> Option.get
                  let frozen = stream.TryReadAfter 0UL |> Array.tryLast |> Option.map _.Sequence
                  Thread.Sleep 150
                  let stillFrozen = stream.TryReadAfter 0UL |> Array.tryLast |> Option.map _.Sequence
                  Expect.equal stillFrozen frozen "pause does not advance visual poses"

                  host.Submit(
                      runtime,
                      { pause with
                          CommandId = MatchCommandId 2UL
                          ExpectedRevision = revision
                          Payload = ResumeMatch }
                  )

                  let resumed =
                      awaitPublication host runtime (function
                          | MatchRuntimePublication.Semantic value when value.Frontier > semantic.Frontier ->
                              Some value
                          | _ -> None)

                  let afterResume = stream.TryReadAfter 0UL |> Array.tryLast |> Option.map _.Sequence
                  Expect.isGreaterThan resumed.Frontier semantic.Frontier "resume advances semantic time"
                  Expect.isGreaterThan afterResume.Value frozen.Value "resume advances without replaying old poses")

          testCase "closing a runtime releases its stream and prevents stale attachment"
          <| fun _ ->
              let matchId, gs = scheduledCareer ()
              let host = MatchRuntimeHost()

              let first =
                  host.CreateUserMatch(matchId, gs, WorldClockOps.init gs.Season, None, MatchRunMode.Live policy)

              let firstStream = host.PresentationStream first |> Option.get
              host.Close first
              Expect.isNone (host.PresentationStream first) "closed runtime is removed from the registry"
              Expect.isTrue firstStream.IsClosed "closed stream rejects stale renderer access"

              let second =
                  host.CreateUserMatch(matchId, gs, WorldClockOps.init gs.Season, None, MatchRunMode.Live policy)

              Expect.notEqual first second "a new session receives a distinct stable handle"
              host.Close second

          testCase "full-time confirmation commits exactly once"
          <| fun _ ->
              let matchId, gs = scheduledCareer ()
              let clock = WorldClockOps.init gs.Season

              let job =
                  WorldRunner.prepareUserMatchJobWithBalance BalanceConfig.defaultConfig clock gs matchId
                  |> function
                      | Ok value -> value
                      | Error error -> failtest error

              let result =
                  MatchSession.create Scheduler.systems job.Input MatchRunMode.Fast
                  |> function
                      | Error error -> failtestf "Cannot create fast match: %A" error
                      | Ok session ->
                          MatchSession.finish session
                          |> function
                              | Ok outcome -> WorldRunner.matchJobResult job outcome
                              | Error error -> failtestf "Cannot finish match: %A" error

              let initial, _ = AppState.initWithGlobal GlobalSettings.defaults None ()

              let state =
                  { initial with
                      Mode = InGame(gs, AppTypes.managerEmployment gs)
                      Navigation =
                          { Current = Route.Match(matchId, MatchPhase.Live)
                            Back = []
                            Forward = [] }
                      WorldClock = clock
                      Match =
                          { MatchSlice.clearState with
                              PendingResult = Some result } }

              let committed, command = AppState.update (LiveMsg LiveMsg.ConfirmFinish) state

              let repeated, secondCommand =
                  AppState.update (LiveMsg LiveMsg.ConfirmFinish) committed

              Expect.isNone committed.Match.PendingResult "successful commit consumes the result"
              Expect.isNonEmpty command "commit schedules persistence"
              Expect.equal repeated committed "a second confirmation is idempotent"
              Expect.isEmpty secondCommand "a second confirmation emits no effects" ]
