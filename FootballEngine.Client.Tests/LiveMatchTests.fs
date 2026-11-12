module FootballEngine.Client.Tests.LiveMatchTests

open System
open System.Diagnostics
open System.Threading
open Expecto
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Client.Match3D
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
            ModResolution.resolve
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
      BallSpinX = 0.0f
      BallSpinY = 0.0f
      BallSpinZ = 0.0f
      BallRadius = 0.11f
      BallOrientation = System.Numerics.Quaternion.Identity
      FootMotion = None
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
      HomeZ = [| 0.0f |]
      HomeVz = [| 0.0f |]
      HomeVx = [| 1.0f |]
      HomeVy = [| 0.0f |]
      HomeFacing = [| 0.0f |]
      HomeAngularVelocity = [| 0.0f |]
      HomeAngularAcceleration = [| 0.0f |]
      HomeTravelDistance = [| 0.0f |]
      HomeCondition = [| 1.0f |]
      HomeIntent = [| 0uy |]
      HomeActive = [| true |]
      AwayX = [| -x |]
      AwayY = [| 0.0f |]
      AwayZ = [| 0.0f |]
      AwayVz = [| 0.0f |]
      AwayVx = [| -1.0f |]
      AwayVy = [| 0.0f |]
      AwayFacing = [| 0.0f |]
      AwayAngularVelocity = [| 0.0f |]
      AwayAngularAcceleration = [| 0.0f |]
      AwayTravelDistance = [| 0.0f |]
      AwayCondition = [| 1.0f |]
      AwayIntent = [| 0uy |]
      AwayActive = [| true |] }

let private renderPlayer id x =
    { Id = id
      Position = Vector2.create x 0.0
      Velocity = Vector2.zero
      Acceleration = Vector2.zero
      BodyFacing = 0.0
      AngularVelocity = 0.0
      TravelDistance = 0.0
      Height = 0.0
      VelocityZ = 0.0
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
        [ testCase "cancel stops chained advance without discarding the current day"
          <| fun _ ->
              let gs = career ()
              let state = { AppTypes.initialState () with Mode = InGame(gs, managerEmployment gs); IsProcessing = true; AdvanceTarget = Some(gs.CurrentDate.AddDays 20.0) }
              let cancelled, command = AppState.update (SimMsg CancelAdvance) state
              Expect.isNone cancelled.AdvanceTarget "no further day is scheduled"
              Expect.isTrue cancelled.IsProcessing "the in-flight day is allowed to finish coherently"
              Expect.equal cancelled.Mode state.Mode "cancel does not rewind or discard world state"
              Expect.isEmpty command "cancel starts no competing work"

          testCase "advance failure clears busy state and target and offers visible recovery"
          <| fun _ ->
              let gs = career ()
              let state = { AppTypes.initialState () with Mode = InGame(gs, managerEmployment gs); IsProcessing = true; AdvanceTarget = Some(gs.CurrentDate.AddDays 20.0) }
              let failed, command = AppState.update (SimMsg(AdvanceFailed "test failure")) state
              Expect.isFalse failed.IsProcessing "retry is available"
              Expect.isNone failed.AdvanceTarget "failed advance cannot keep chaining"
              Expect.equal failed.Mode state.Mode "the failed day is not applied"
              Expect.isFalse failed.Notifications.IsEmpty "the failure is visible"
              Expect.isEmpty command "failure does not silently retry"

          testCase "a real finished match survives navigation and commits once before the next journey step"
          <| fun _ ->
              let require =
                  function
                  | Ok value -> value
                  | Error error -> failtestf "Unexpected error: %A" error

              let id, gs = scheduledCareer ()
              let clock = WorldClockOps.init gs.Season

              let balance =
                  WeightMergeSettings.mergeWithOverridesSave (Some SaveSettings.defaults)

              let job = WorldRunner.prepareUserMatchJobWithBalance balance clock gs id |> require

              let outcome =
                  Simulator.runMatch Scheduler.systems job.Input.Context job.Input.Seed Simulator.RunMode.Fast
                  |> require

              let result = WorldRunner.matchJobResult job [] outcome
              let initial = AppTypes.initialState ()

              let state =
                  { initial with
                      Mode = InGame(gs, managerEmployment gs)
                      WorldClock = clock
                      IsProcessing = false
                      Navigation =
                          { Current = Route.Match(id, Postmatch)
                            Back = []
                            Forward = [] }
                      Match =
                          { initial.Match with
                              RequestedMatchId = Some id
                              PendingResult = Some result } }

              let home, _ = AppState.update (Navigate Route.Home) state
              Expect.isSome home.Match.PendingResult "leaving the result screen cannot discard a completed match"

              Expect.equal
                  (CareerFlow.continuation home)
                  (CareerFlow.ReturnToMatch(id, Postmatch))
                  "home leads back to confirmation"

              let _, blockedSave = AppState.update (PersistenceMsg SaveCurrent) home
              Expect.isEmpty blockedSave "a pre-match save cannot masquerade as a saved active match"
              let committed, _ = AppState.update (LiveMsg LiveMsg.ConfirmFinish) home

              let confirmed =
                  match committed.Mode with
                  | InGame(value, _) -> value
                  | _ -> failtest "expected career"

              let fixture =
                  confirmed.Competitions
                  |> Map.toSeq
                  |> Seq.pick (fun (_, competition) -> Map.tryFind id competition.Fixtures)

              Expect.isTrue fixture.Played "the actual result is registered"
              Expect.equal fixture.HomeScore (Some result.HomeScore) "home score is preserved"
              Expect.equal fixture.AwayScore (Some result.AwayScore) "away score is preserved"
              Expect.isNone committed.Match.PendingResult "confirmation consumes the pending result"

              Expect.isSome
                  (CareerFlow.shotFacts committed id fixture.Events)
                  "facts remain available from the recorded match input"

              let duplicate, command = AppState.update (LiveMsg LiveMsg.ConfirmFinish) committed
              Expect.equal duplicate.Mode committed.Mode "a second confirmation cannot apply consequences twice"
              Expect.isEmpty command "duplicate confirmation schedules nothing"

              match CareerFlow.continuation committed with
              | CareerFlow.AdvanceDay _ -> ()
              | other -> failtestf "expected next day after confirmation, got %A" other

          testCase "suggested lineup never selects suspended players"
          <| fun _ ->
              let gs = career ()
              let starter = (TacticsSlice.Query.currentLineup gs).Value.Slots |> List.pick _.PlayerId
              let gs = { gs with Players = gs.Players |> Map.change starter (Option.map (fun player -> { player with Status = Suspended 1 })) }
              let state = { AppTypes.initialState () with Mode = InGame(gs, managerEmployment gs); IsProcessing = false }
              let suggested, _ = AppState.update (TacticsMsg QuickPick) state
              let updated = match suggested.Mode with InGame(value, _) -> value | _ -> failtest "expected career"
              let slots = (TacticsSlice.Query.currentLineup updated).Value.Slots
              Expect.isFalse (slots |> List.exists (fun slot -> slot.PlayerId = Some starter)) "an unavailable starter must not be suggested"
              Expect.isTrue (CareerFlow.lineupReadiness updated).IsReady "an available squad produces a valid suggested eleven"

          testCase "navigation retains a valid match and returns to the same session"
          <| fun _ ->
              let id, gs = scheduledCareer ()
              let initial = AppTypes.initialState ()

              let state =
                  { initial with
                      Mode = InGame(gs, managerEmployment gs)
                      IsProcessing = false
                      Navigation =
                          { Current = Route.Match(id, FootballEngine.Routing.Live)
                            Back = []
                            Forward = [] }
                      Match =
                          { initial.Match with
                              RequestedMatchId = Some id
                              RuntimeId = Some(MatchRuntimeId 9999UL) } }

              let home, _ = AppState.update (Navigate Route.Home) state
              Expect.equal home.Match.RuntimeId state.Match.RuntimeId "navigation does not dispose the active match"

              Expect.equal
                  (CareerFlow.continuation home)
                  (CareerFlow.ReturnToMatch(id, FootballEngine.Routing.Live))
                  "continue returns to that match"

              let returned, _ = AppState.update GoBack home
              Expect.equal returned.Navigation.Current state.Navigation.Current "back restores the match route"
              Expect.equal returned.Match.RuntimeId state.Match.RuntimeId "the session identity is unchanged"

              let rejected, command =
                  AppState.update (Navigate(Route.Match(id + 1, FootballEngine.Routing.Live))) home

              Expect.equal rejected.Navigation.Current Route.Home "a second fixture cannot replace this session"
              Expect.isEmpty command "rejected navigation cannot launch another simulation"

          testCase "selecting an existing starter swaps positions and undo restores the exact lineup"
          <| fun _ ->
              let gs = career ()
              let lineup = TacticsSlice.Query.currentLineup gs |> Option.get
              let first, second = lineup.Slots[0], lineup.Slots[1]

              let state =
                  { AppTypes.initialState () with
                      Mode = InGame(gs, managerEmployment gs)
                      IsProcessing = false
                      SaveStatus = Saved DateTime.UtcNow }

              let selected, _ = AppState.update (TacticsMsg(SelectSlot(Some first.Index))) state

              let edited, _ =
                  AppState.update (TacticsMsg(DropPlayerInSlot(first.Index, second.PlayerId.Value))) selected

              let updated =
                  match edited.Mode with
                  | InGame(value, _) -> value
                  | _ -> failtest "expected career"

              let changed = TacticsSlice.Query.currentLineup updated |> Option.get

              Expect.equal
                  (changed.Slots |> List.find (fun slot -> slot.Index = second.Index) |> _.PlayerId)
                  first.PlayerId
                  "the displaced player swaps rather than vanishes"

              Expect.isTrue
                  (CareerFlow.lineupReadiness updated).IsReady
                  "the swap keeps eleven distinct available players"

              Expect.isNone edited.Tactics.SelectedSlot "selection closes after assignment"
              Expect.equal edited.SaveStatus SaveIdle "the UI no longer claims a previous save includes the edit"
              let undone, _ = AppState.update (TacticsMsg UndoTactics) edited

              let restored =
                  match undone.Mode with
                  | InGame(value, _) -> value
                  | _ -> failtest "expected career"

              Expect.equal (TacticsSlice.Query.currentLineup restored) (Some lineup) "undo restores all lineup data"

          testCase "career continuation prepares today's fixture without advancing or starting it"
          <| fun _ ->
              let id, gs = scheduledCareer ()

              let state =
                  { AppTypes.initialState () with
                      Mode = InGame(gs, managerEmployment gs)
                      IsProcessing = false }

              Expect.equal
                  (CareerFlow.continuation state)
                  (CareerFlow.PrepareMatch id)
                  "today has an explicit preparation step"

              let next, command = UpdateSim.handle (Advance 1) state
              let messages = ResizeArray<Msg>()
              command |> List.iter (fun effect -> effect messages.Add)

              let routes =
                  messages
                  |> Seq.choose (function
                      | Navigate route -> Some route
                      | _ -> None)
                  |> List.ofSeq

              Expect.equal routes [ Route.Match(id, Prematch) ] "raw day advance cannot skip today's game"
              Expect.equal messages.Count 1 "no simulation commands are emitted"
              Expect.equal next.Mode state.Mode "no date, lineup or execution seed changes"
              Expect.isFalse next.IsProcessing "no background simulation was launched"

          testCase "next event respects pending decisions instead of advancing time"
          <| fun _ ->
              let gs = career ()

              let message =
                  { Inbox.create gs.CurrentDate "Staff" "Review an offer" "Details" InboxMessageCategory.Transfer true with
                      Id = 91 }

              let gs = { gs with Inbox = [ message ] }

              let state =
                  { AppTypes.initialState () with
                      Mode = InGame(gs, managerEmployment gs)
                      IsProcessing = false }

              let next, command = UpdateSim.handle AdvanceToNextEvent state
              let messages = ResizeArray<Msg>()
              command |> List.iter (fun effect -> effect messages.Add)

              let routes =
                  messages
                  |> Seq.choose (function
                      | Navigate route -> Some route
                      | _ -> None)
                  |> List.ofSeq

              Expect.equal routes [ Route.Inbox(InboxRoute(Some 91)) ] "the real pending message opens"
              Expect.equal messages.Count 1 "only navigation is requested"
              Expect.isNone next.AdvanceTarget "no hidden multi-day advance is scheduled"

          testCase "career attention detects new decisions but ignores routine information"
          <| fun _ ->
              let gs = career ()

              let routine =
                  Inbox.create gs.CurrentDate "Staff" "Training report" "Details" InboxMessageCategory.Development false

              let action =
                  { routine with
                      Id = 92
                      RequiresAction = true }

              Expect.isFalse
                  (CareerFlow.newAttention gs { gs with Inbox = [ routine ] })
                  "routine news does not interrupt"

              Expect.isTrue (CareerFlow.newAttention gs { gs with Inbox = [ action ] }) "a new decision interrupts"

              Expect.isFalse
                  (CareerFlow.newAttention { gs with Inbox = [ action ] } { gs with Inbox = [ action ] })
                  "old decisions are not new events"

              Expect.isTrue (CareerFlow.lineupReadiness gs).IsReady "generated current lineup is usable"

          testCase "ring buffer overwrites oldest poses without blocking and brackets adjacent poses"
          <| fun _ ->
              let stream =
                  new MatchPresentationStream(
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
                  new MatchPresentationStream(
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
                        Orientation = System.Numerics.Quaternion.Identity
                        AngularVelocity = System.Numerics.Vector3.Zero
                        Radius = 0.11
                        FootMotion = None
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
                        Orientation = System.Numerics.Quaternion.Identity
                        AngularVelocity = System.Numerics.Vector3.Zero
                        Radius = 0.11
                        FootMotion = None
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

              let motion: FootballEngine.Core.Match.FootMotion =
                  { Actor = FootballEngine.Core.Match.PlayerId 1
                    Foot = FootballEngine.Core.Match.ContactFoot.Right
                    Kind = FootballEngine.Core.Match.FootTouchKind.Kick FootballEngine.Core.Match.StrikeKind.Pass
                    Surface = FootballEngine.Core.Match.StrikeSurface.Inside
                    StartedAt = 0<subtick>
                    ContactAt = 8<subtick>
                    EndsAt = 16<subtick>
                    Point =
                      { X = -0.11<meter>
                        Y = 0.0<meter>
                        Z = 0.11<meter> }
                    BallCenter =
                      { X = 0.0<meter>
                        Y = 0.0<meter>
                        Z = 0.11<meter> } }

              let prepared =
                  { first with
                      Ball =
                          { first.Ball with
                              Velocity = Vector2.zero
                              FootMotion = Some motion } }

              let kicked =
                  { second with
                      Ball =
                          { second.Ball with
                              Position = Vector2.create 4.0 0.0
                              FootMotion = Some motion } }

              Expect.equal
                  (MatchInterp.hermite prepared kicked 0.25 0.4).Ball.Position
                  Vector2.zero
                  "outgoing velocity cannot pull the ball backward before impact"

              Expect.equal
                  (MatchInterp.hermite prepared kicked 0.5 0.4).Ball.Position
                  Vector2.zero
                  "ball is at the authoritative contact point at impact"

              Expect.floatClose
                  Accuracy.high
                  (MatchInterp.hermite prepared kicked 0.75 0.4).Ball.Position.X
                  2.0
                  "post-contact motion starts at impact"

              let spinning =
                  { first with
                      Ball =
                          { first.Ball with
                              AngularVelocity = System.Numerics.Vector3(0.0f, 0.0f, 160.0f) } }

              let rotated =
                  { spinning with
                      TimeSeconds = 0.025
                      Ball =
                          { spinning.Ball with
                              Orientation =
                                  System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, 4.0f) } }

              let middle = MatchInterp.hermite spinning rotated 0.5 0.025

              let direction =
                  System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, middle.Ball.Orientation)

              Expect.floatClose
                  Accuracy.medium
                  (float direction.X)
                  (cos 2.0)
                  "fast rotation retains the physical turn rather than the shortest quaternion arc"

              Expect.floatClose
                  Accuracy.medium
                  (float direction.Y)
                  (sin 2.0)
                  "sidespin does not visually reverse between poses"

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
                        Orientation = System.Numerics.Quaternion.Identity
                        AngularVelocity = System.Numerics.Vector3.Zero
                        Radius = 0.11
                        FootMotion = None
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

          testCase "4x playback keeps HUD updates per watched second and wakes the UI once per undrained batch"
          <| fun _ ->
              let matchId, gs = scheduledCareer ()
              let host = MatchRuntimeHost()

              let runtime =
                  host.CreateUserMatch(matchId, gs, WorldClockOps.init gs.Season, None, MatchRunMode.Live policy)

              let mutable wakes = 0

              host.Start(
                  runtime,
                  fun message ->
                      match message with
                      | LiveMsg(LiveMsg.RuntimePublicationAvailable _) -> Interlocked.Increment &wakes |> ignore
                      | _ -> ()
              )

              try
                  let session, _ =
                      awaitPublication host runtime (function
                          | MatchRuntimePublication.Connected(session, fixture, _, _) -> Some(session, fixture)
                          | _ -> None)

                  // Nobody drains for a while: however much the worker publishes, at most one wake is in flight.
                  Volatile.Write(&wakes, 0)
                  Thread.Sleep 300
                  Expect.isLessThanOrEqual (Volatile.Read &wakes) 1 "an undrained UI is woken once"

                  // Counts the HUD updates seen over a wall-clock window and how far the match advanced.
                  let watch (window: TimeSpan) =
                      let clock = Stopwatch.StartNew()
                      let mutable semantics = 0
                      let mutable first = None
                      let mutable last = None

                      while clock.Elapsed < window do
                          match host.TryTake runtime with
                          | Some(MatchRuntimePublication.Failed error) -> failtest error
                          | Some(MatchRuntimePublication.Semantic value) ->
                              semantics <- semantics + 1
                              first <- first |> Option.orElse (Some value)
                              last <- Some value
                          | Some _ -> ()
                          | None -> Thread.Sleep 1

                      let advanced =
                          match first, last with
                          | Some a, Some b -> int (b.Frontier - a.Frontier)
                          | _ -> 0

                      semantics, advanced, last

                  let normalCount, normalAdvance, latest = watch (TimeSpan.FromSeconds 1.5)
                  let latest = latest |> Option.defaultWith (fun () -> failtest "no HUD update at 1x")

                  host.Submit(
                      runtime,
                      { CommandId = MatchCommandId 1UL
                        SessionId = session
                        ExpectedRevision = latest.Revision
                        IssuedAt = latest.Frontier
                        Payload = SetLiveSpeed VeryFast }
                  )

                  awaitPublication host runtime (function
                      | MatchRuntimePublication.StatusChanged(FootballEngine.LiveMatch.Live VeryFast) -> Some()
                      | _ -> None)

                  let fastCount, fastAdvance, _ = watch (TimeSpan.FromSeconds 1.5)

                  Expect.isGreaterThan
                      (float fastAdvance)
                      (2.5 * float normalAdvance)
                      $"4x plays the match faster (1x {normalAdvance} ticks, 4x {fastAdvance})"

                  Expect.isLessThanOrEqual
                      (float fastCount)
                      (1.5 * float normalCount + 1.0)
                      $"HUD updates stay per watched second (1x {normalCount}, 4x {fastCount})"
              finally
                  host.Close runtime

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

          testCase "inspector filters are presentation only and retain the selected player"
          <| fun _ ->
              withRuntime (fun _ _ session fixture _ ->
                  let initial = LiveMatchState.create session fixture 1UL LiveSession

                  let state =
                      { MatchSlice.clearState with
                          Live = Some initial }

                  let selected = MatchSlice.update (LiveMsg.SelectPlayer(Some 1)) state

                  let filter =
                      { Actor = Some(FootballEngine.Core.Match.PlayerId 999)
                        Action = Some(ActionId 7UL)
                        Event = Some(EventId 8UL)
                        FromTick = 10<subtick>
                        UntilTick = 20<subtick> }

                  let filtered = MatchSlice.update (LiveMsg.SetEvidenceQuery filter) selected
                  let live = filtered.Live.Value

                  let repeated =
                      MatchSlice.update (LiveMsg.SetEvidenceQuery live.Ui.EvidenceQuery) filtered

                  Expect.isTrue
                      (obj.ReferenceEquals(repeated, filtered))
                      "rebinding an unchanged query cannot enqueue another UI render"

                  let repeatedPlayer = MatchSlice.update (LiveMsg.SelectPlayer(Some 1)) filtered

                  Expect.isTrue
                      (obj.ReferenceEquals(repeatedPlayer, filtered))
                      "rebinding an unchanged player cannot enqueue another UI render"

                  Expect.equal
                      live.Ui.EvidenceQuery
                      { filter with
                          Actor = Some(FootballEngine.Core.Match.PlayerId 1) }
                      "actor comes from the existing player selection"

                  Expect.equal live.PendingCommands initial.PendingCommands "querying cannot submit a sporting command"
                  Expect.equal live.Revision initial.Revision "querying does not advance the authoritative revision"
                  Expect.equal live.Events initial.Events "querying does not modify the event timeline"

                  let invalid =
                      MatchSlice.update (LiveMsg.SetEvidenceQuery { filter with FromTick = 21<subtick> }) filtered

                  Expect.equal invalid filtered "an inverted interval is rejected without changing UI or match state"
                  let cleared = MatchSlice.update (LiveMsg.SelectPlayer None) filtered
                  Expect.isNone cleared.Live.Value.Ui.EvidenceQuery.Actor "all players resets the causal actor filter"

                  Expect.equal
                      cleared.Live.Value.Ui.EvidenceQuery.Action
                      filter.Action
                      "changing player preserves independent action selection")

          testCase "diagnostic archive reconstructs sporting frontier and pending commands"
          <| fun _ ->
              let matchId, gs = scheduledCareer ()
              let clock = WorldClockOps.init gs.Season

              let unwrap =
                  function
                  | Ok value -> value
                  | Error error -> failtestf "Unexpected diagnostic error: %A" error

              let job =
                  WorldRunner.prepareUserMatchJobWithBalance BalanceConfig.defaultConfig clock gs matchId
                  |> unwrap

              let session =
                  MatchSession.create Scheduler.systems job.Input MatchRunMode.Fast |> unwrap

              let command =
                  { EffectiveTick = 1200<subtick>
                    Sequence = 1UL
                    Revision = job.Input.EffectiveConfig.Revision
                    Manager = job.Input.Context.HomeClub.Id
                    Action = ChangeTactics job.Input.Context.HomeClub.BaseTactics }

              MatchSession.receive command session |> unwrap
              MatchSession.advanceTo 800<subtick> session |> unwrap |> ignore
              let archive = MatchObservationArchive.capture session

              let decoded =
                  archive
                  |> MatchObservationArchive.encode
                  |> MatchObservationArchive.decode
                  |> unwrap

              let restored = MatchObservationArchive.restore decoded |> unwrap
              Expect.equal restored.Input session.Input "input config lineups rules and seed restored"
              Expect.equal restored.Rng session.Rng "same RNG frontier"
              Expect.equal restored.World.State session.World.State "same sporting state"
              Expect.equal restored.Pending session.Pending "pending manager commands preserved"
              Expect.equal (List.ofSeq restored.Events) (List.ofSeq session.Events) "same authoritative events"
              Expect.equal decoded.Evidence archive.Evidence "original typed causal evidence preserved"
              Expect.isError (MatchObservationArchive.decode "{\"FormatVersion\":0}") "unsupported format rejected"
              let captured = MatchObservationArchive.snapshot session
              let original = MatchObservationArchive.materialize captured
              MatchSession.advanceTo 1400<subtick> session |> unwrap |> ignore
              let delayed = MatchObservationArchive.materialize captured
              Expect.equal delayed original "serialization after subsequent ticks preserves the captured frontier"

              Expect.equal
                  (MatchObservationArchive.restore delayed |> unwrap).Pending
                  restored.Pending
                  "later command application cannot mutate the captured command queue"

          testCase "diagnostic queue rejects overflow and resolves every retained request on close"
          <| fun _ ->
              let matchId, gs = scheduledCareer ()
              let host = MatchRuntimeHost()

              let runtime =
                  host.CreateUserMatch(matchId, gs, WorldClockOps.init gs.Season, None, MatchRunMode.Live policy)

              let retained = [| for _ in 1..4 -> host.CaptureObservation runtime |]
              let overflow = host.CaptureObservation runtime
              Expect.isTrue (overflow.Wait 1000) "overflow is rejected without waiting for the worker"
              Expect.isError overflow.Result "bounded diagnostic queue reports overflow"
              Expect.isError (host.SelectObservation(runtime, None)) "selection also reports the full shared queue"
              host.Close runtime

              for pending in retained do
                  Expect.isTrue (pending.Wait 1000) "close resolves every retained request"
                  Expect.isError pending.Result "closure is explicit, not silent loss"

          testCase "closing an unstarted runtime resolves queued diagnostic exports"
          <| fun _ ->
              let matchId, gs = scheduledCareer ()
              let host = MatchRuntimeHost()

              let runtime =
                  host.CreateUserMatch(matchId, gs, WorldClockOps.init gs.Season, None, MatchRunMode.Live policy)

              let pending = host.CaptureObservation runtime
              host.Close runtime
              Expect.isTrue (pending.Wait 1000) "no stranded export task"
              Expect.isError pending.Result "close reports unavailable capture"
              Expect.isError (host.SelectObservation(runtime, None)) "closed runtime rejects selection"

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
                              | Ok outcome -> WorldRunner.matchJobResult job session.Commands outcome
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
              Expect.isEmpty secondCommand "a second confirmation emits no effects"

          testCase "the match HUD displays the authoritative half under the score"
          <| fun _ ->
              Expect.equal (MatchHudRenderer.periodLabel MatchPeriod.FirstHalf) "1T" "first half"
              Expect.equal (MatchHudRenderer.periodLabel MatchPeriod.SecondHalf) "2T" "second half"
              Expect.equal (MatchHudRenderer.periodLabel MatchPeriod.HalfTimeInterval) "DESC" "half-time"
              Expect.equal (MatchHudRenderer.periodLabel MatchPeriod.ExtraTimeFirstHalf) "1TE" "extra time" ]
