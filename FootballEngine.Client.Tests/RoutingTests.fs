module FootballEngine.Client.Tests.RoutingTests

open System
open Expecto
open FootballEngine
open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Client.Views.Components
open FootballEngine.Core.Match
open FootballEngine.Domain
open FootballEngine.Routing
open FootballEngine.Settings
open FootballEngine.World
open FootballEngine.World.WorldRunner

let private roundTrip route =
    testCase (Route.toPath route)
    <| fun _ ->
        let path = Route.toPath route
        Expect.equal (Route.fromPath path) (Some route) $"route round-trips through {path}"

let routeTests =
    testList
        "Route paths"
        [ roundTrip Route.Home
          roundTrip (Route.Inbox(InboxRoute(Some 4)))
          roundTrip (Route.Squad(SquadRoute.Detail 12))
          roundTrip (Route.Transfers TransferRoute.IncomingOffers)
          roundTrip (Route.Transfers TransferRoute.Negotiations)
          roundTrip (Route.Transfers(TransferRoute.Negotiation 8))
          roundTrip (Route.Finances(FinanceRoute.Operation "winter/fees"))
          roundTrip (Route.Settings(SettingsRoute.Category SettingsCategory.Simulation))
          roundTrip (Route.Match(3, MatchPhase.Replay)) ]

let setupLoadTests =
    testCase "opening load game lists slots without entering a career"
    <| fun _ ->
        let initial, _ = AppState.initWithGlobal GlobalSettings.defaults None ()
        let ready, _ = AppState.update StartupReady initial
        let state, _ = AppState.update (SetupMsg OpenLoadGame) ready

        Expect.equal state.Mode NoSave "load stays outside a career"
        Expect.equal state.Navigation.Current Route.Setup "load stays on the setup route"
        Expect.equal state.Setup.Step LoadGame "load screen opens"

        match state.Setup.LoadState with
        | LoadingSlots requestId -> Expect.isGreaterThan requestId 0UL "slot listing has a request id"
        | other -> failtestf "expected slot loading, got %A" other

let setupDraftTests =
    testCase "setup selections remain a draft until career creation"
    <| fun _ ->
        let initial, _ = AppState.initWithGlobal GlobalSettings.defaults None ()
        let ready, _ = AppState.update StartupReady initial
        let country, _ = AppState.update (SetupMsg(GoToStep CountrySelection)) ready
        let selected, _ = AppState.update (SetupMsg(SelectPrimaryCountry "ARG")) country
        let named, _ = AppState.update (SetupMsg(UpdateManagerName "Ada Manager")) selected
        let styled, _ = AppState.update (SetupMsg(SelectManagementStyle EmPossession)) named
        let club, _ = AppState.update (SetupMsg(SelectClub "River Plate")) styled
        let summary, _ = AppState.update (SetupMsg(GoToStep Summary)) club
        let canceled, _ = AppState.update (SetupMsg CancelSetup) summary

        Expect.equal summary.Mode NoSave "draft stays outside a career"
        Expect.equal summary.Setup.Step Summary "draft navigation is retained"
        Expect.equal summary.Setup.SelectedCountry (Some "ARG") "country is retained"
        Expect.equal summary.Setup.ManagerName "Ada Manager" "manager is retained"
        Expect.equal summary.Setup.ManagementStyle (Some EmPossession) "style is retained"
        Expect.equal summary.Setup.SelectedClubName (Some "River Plate") "club is retained"
        Expect.equal canceled.Setup initSetupState "cancel clears the draft"
        Expect.equal canceled.ActiveSlot None "cancel leaves no active save"

let modEditorBackTests =
    testCase "mod editor can return to the setup menu without a career"
    <| fun _ ->
        let initial, _ = AppState.initWithGlobal GlobalSettings.defaults None ()
        let ready, _ = AppState.update StartupReady initial
        let editor, _ = AppState.update (Navigate Route.ModEditor) ready
        let returned, _ = AppState.update GoBack editor

        Expect.equal editor.Navigation.Current Route.ModEditor "mod editor route opens"
        Expect.equal returned.Navigation.Current Route.Setup "back returns to setup"
        Expect.equal returned.Mode NoSave "back does not create a career"

let employmentGateTests =
    testCase "employment-gated routes remain unavailable without a contract"
    <| fun _ ->
        Expect.isTrue (Route.canNavigate false Route.Home) "home is always available"
        Expect.isFalse (Route.canNavigate false (Route.Training TrainingRoute.Overview)) "training requires employment"

let matchNavigationTests =
    testList
        "Match viewer routing"
        [ testCase "a user match opens prematch before simulation starts"
          <| fun _ ->
              let date = DateTime(2026, 8, 1)

              let fixture =
                  { Id = 42
                    CompetitionId = 1
                    Round = None
                    HomeClubId = 7
                    AwayClubId = 8
                    ScheduledDate = date
                    Played = false
                    HomeScore = None
                    AwayScore = None
                    Events = [] }

              let competition =
                  { Id = 1
                    Name = "Test League"
                    BroadcastIdentity = BroadcastIdentity.generated "competition:1"
                    Type =
                      NationalLeague(
                          LeagueLevel 0,
                          { PointsForWin = 3
                            PointsForDraw = 1
                            Tiebreakers = []
                            Promotion = []
                            Relegation = [] }
                      )
                    Country = Some "TST"
                    Season = 2026
                    ClubIds = [ 7; 8 ]
                    Fixtures = Map.ofList [ fixture.Id, fixture ]
                    Standings = Map.empty
                    KnockoutTies = Map.empty }

              let gameState =
                  { CurrentDate = date
                    Season = 2026
                    TrainingWeeksApplied = 0
                    Clubs = Map.empty
                    Players = Map.empty
                    Staff = Map.empty
                    Competitions = Map.ofList [ competition.Id, competition ]
                    Countries = Map.empty
                    DataLock = Unchecked.defaultof<_>
                    UserClubId = fixture.HomeClubId
                    UserStaffId = 0
                    PrimaryCountry = "TST"
                    Inbox = []
                    NextInboxId = 1
                    PendingNegotiations = Map.empty
                    NextNegotiationId = 1
                    ProfileCache = Map.empty
                    PlayerExperiences = Map.empty
                    TeamEmphasis = EmPossession
                    SeasonHistory = []
                    CareerHistory = []
                    ExecutionSeed = 1UL
                    MatchSeeds = Map.empty
                    News = []
                    NextNewsId = 1
                    PulseProcessed = Set.empty
                    Market = MarketState.empty }

              let initial =
                  { AppTypes.initialState () with
                      Mode = InGame(gameState, NotEmployed)
                      Navigation =
                          { Current = Route.Home
                            Back = []
                            Forward = [] }
                      IsProcessing = true }

              let result: DayResult =
                  { GameState = gameState
                    WorldClock = WorldClockOps.init gameState.Season
                    PlayedMatches = []
                    Logs = []
                    SeasonComplete = false }

              let _, command = UpdateSim.handle (AdvanceDone result) initial
              let mutable emitted = None

              command
              |> List.head
              |> fun effect -> effect (fun message -> emitted <- Some message)

              match emitted with
              | Some(Navigate(Route.Match(matchId, MatchPhase.Prematch))) ->
                  Expect.equal matchId fixture.Id "the current user match opens"
              | message -> failtestf "expected prematch navigation first, got %A" message

          testCase "replay v11 preserves body orientation and remaps active substitutes"
          <| fun _ ->
              let snapshot: Replay.ReplaySnapshot =
                  { Tick = 0<subtick>
                    HomePlayerIds =
                      [| FootballEngine.Core.Match.PlayerId 1
                         FootballEngine.Core.Match.PlayerId 2
                         FootballEngine.Core.Match.PlayerId 3 |]
                    AwayPlayerIds =
                      [| FootballEngine.Core.Match.PlayerId 4
                         FootballEngine.Core.Match.PlayerId 5
                         FootballEngine.Core.Match.PlayerId 6 |]
                    HomeScore = 0
                    AwayScore = 0
                    Flow = 0uy
                    BallX = 0.0f
                    BallY = 0.0f
                    BallZ = 0.0f
                    BallVx = 8.0f
                    BallVy = -2.0f
                    BallVz = 1.0f
                    BallSpinX = 0.0f
                    BallSpinY = 3.0f
                    BallSpinZ = 0.5f
                    BallRadius = 0.11f
                    BallOrientation = System.Numerics.Quaternion.Identity
                    FootMotion = None
                    BallControlSide = 0y
                    BallController = 1
                    RefereeX = 2.0f
                    RefereeY = 3.0f
                    NearAssistantX = -4.0f
                    NearAssistantY = -34.0f
                    FarAssistantX = 4.0f
                    FarAssistantY = 34.0f
                    RefereeSignal = 1uy
                    HomeX = [| -10.0f; 0.0f; 0.0f |]
                    HomeY = [| 0.0f; 5.0f; 0.0f |]
                    HomeZ = [| 0.35f; 0.0f; 0.0f |]
                    HomeVz = [| -1.25f; 0.0f; 0.0f |]
                    HomeVx = [| 4.0f; 2.0f; 0.0f |]
                    HomeVy = [| 1.0f; 0.0f; 0.0f |]
                    HomeFacing = [| 0.5f; -1.0f; 0.0f |]
                    HomeAngularVelocity = [| 0.2f; -0.3f; 0.0f |]
                    HomeAngularAcceleration = [| 0.1f; 0.0f; 0.0f |]
                    HomeTravelDistance = [| 3.5f; 1.25f; 0.0f |]
                    HomeCondition = [| 0.9f; 0.7f; 0.5f |]
                    HomeIntent = [| 1uy; 2uy; 0uy |]
                    HomeActive = [| true; true; false |]
                    AwayX = [| 10.0f; 0.0f; 0.0f |]
                    AwayY = [| 0.0f; -5.0f; 0.0f |]
                    AwayZ = [| 0.0f; 0.42f; 0.0f |]
                    AwayVz = [| 0.0f; 1.5f; 0.0f |]
                    AwayVx = [| -3.0f; -1.0f; 0.0f |]
                    AwayVy = [| 0.0f; 1.0f; 0.0f |]
                    AwayFacing = [| -0.5f; 1.0f; 0.0f |]
                    AwayAngularVelocity = [| 0.0f; 0.4f; 0.0f |]
                    AwayAngularAcceleration = [| -0.1f; 0.0f; 0.0f |]
                    AwayTravelDistance = [| 0.5f; 2.0f; 0.0f |]
                    AwayCondition = [| 0.8f; 0.6f; 0.5f |]
                    AwayIntent = [| 1uy; 2uy; 0uy |]
                    AwayActive = [| true; true; false |] }

              let replay = Replay.write [ snapshot ]
              let restored = Replay.read replay |> Seq.exactlyOne
              Expect.equal restored.HomePlayerIds snapshot.HomePlayerIds "home roster identity survives serialization"
              Expect.equal restored.AwayPlayerIds snapshot.AwayPlayerIds "away roster identity survives serialization"
              Expect.equal restored.HomeFacing snapshot.HomeFacing "home body facing survives serialization"
              Expect.equal restored.HomeZ snapshot.HomeZ "home body elevation survives serialization"
              Expect.equal restored.HomeVz snapshot.HomeVz "home vertical velocity survives serialization"
              Expect.equal restored.AwayZ snapshot.AwayZ "away body elevation survives serialization"
              Expect.equal restored.AwayVz snapshot.AwayVz "away vertical velocity survives serialization"

              Expect.equal
                  restored.AwayAngularVelocity
                  snapshot.AwayAngularVelocity
                  "away angular velocity survives serialization"

              let frame =
                  MatchProjection.project
                      [| "A"; "B"; "C" |]
                      [| "D"; "E"; "F" |]
                      [| FootballEngine.Core.Match.PlayerId 1
                         FootballEngine.Core.Match.PlayerId 2
                         FootballEngine.Core.Match.PlayerId 3 |]
                      [| FootballEngine.Core.Match.PlayerId 4
                         FootballEngine.Core.Match.PlayerId 5
                         FootballEngine.Core.Match.PlayerId 6 |]
                      2
                      2
                      7
                      8
                      "Home"
                      "Away"
                      None
                      restored
                      0.0

              Expect.equal replay.Meta.Version 11 "visual replay schema is explicit"
              Expect.equal restored.HomeTravelDistance snapshot.HomeTravelDistance "travel distance survives replay"
              Expect.equal frame.Players.Length 4 "only active players are rendered"
              Expect.equal frame.Officials.Length 3 "the referee team is projected"
              Expect.equal frame.Officials[0].Signal OfficialSignal.Whistle "the referee signal survives replay"
              Expect.equal frame.Ball.Velocity.X 8.0 "ball velocity survives replay"
              let homeCarrier = frame.Players |> Array.find (fun player -> player.Id = 1)
              Expect.equal homeCarrier.TravelDistance 3.5 "projection preserves distance-driven gait"
              Expect.equal homeCarrier.Height (float snapshot.HomeZ[0]) "projection preserves authoritative body height"
              Expect.equal homeCarrier.VelocityZ -1.25 "projection preserves descending motion"

              let airborne =
                  { homeCarrier with
                      Height = 0.0
                      VelocityZ = 4.0 }

              let descending =
                  { homeCarrier with
                      Height = 0.0
                      VelocityZ = -4.0 }

              let start = { frame with Players = [| airborne |] }

              let finish =
                  { frame with
                      Players = [| descending |] }

              let apex = MatchInterp.hermite start finish 0.5 0.8
              Expect.floatClose Accuracy.medium apex.Players[0].Height 0.8 "vertical tangents preserve the jump apex"
              Expect.floatClose Accuracy.medium apex.Players[0].VelocityZ 0.0 "vertical velocity vanishes at the apex"

              Expect.equal
                  (MatchInterp.hermite start finish 0.0 0.8).Players[0].Height
                  0.0
                  "takeoff starts on the ground"

              Expect.equal
                  (MatchInterp.hermite start finish 1.0 0.8).Players[0].Height
                  0.0
                  "landing returns to the ground"

              Expect.equal
                  homeCarrier.BodyFacing
                  (float snapshot.HomeFacing[0])
                  "projection uses authoritative body facing"

              let oldFormat = Array.copy replay.Snapshots

              BitConverter.GetBytes(6)
              |> fun version -> Array.blit version 0 oldFormat 4 version.Length

              let oldReplayRejected =
                  try
                      Replay.readFrame { replay with Snapshots = oldFormat } 0 |> ignore
                      false
                  with :? ArgumentException ->
                      true

              Expect.isTrue oldReplayRejected "replay readers reject incompatible formats explicitly"

              Expect.isTrue
                  (frame.Players |> Array.exists (fun player -> player.Id = 1 && player.HasBall))
                  "ball control survives replay"

              Expect.isFalse (frame.Players |> Array.exists (fun player -> player.Id = 3)) "bench slots stay hidden"

              let substitutionSnapshot =
                  { restored with
                      HomeActive = [| true; false; true |] }

              let afterSubstitution =
                  MatchProjection.project
                      [| "A"; "B"; "C" |]
                      [| "D"; "E"; "F" |]
                      [| FootballEngine.Core.Match.PlayerId 1
                         FootballEngine.Core.Match.PlayerId 2
                         FootballEngine.Core.Match.PlayerId 3 |]
                      [| FootballEngine.Core.Match.PlayerId 4
                         FootballEngine.Core.Match.PlayerId 5
                         FootballEngine.Core.Match.PlayerId 6 |]
                      2
                      2
                      7
                      8
                      "Home"
                      "Away"
                      None
                      substitutionSnapshot
                      1.0

              let substitute =
                  afterSubstitution.Players |> Array.find (fun player -> player.Id = 3)

              Expect.equal substitute.VisualSlot 1 "a substitute reuses the vacant starter model"

              Expect.isFalse
                  (afterSubstitution.Players |> Array.exists (fun player -> player.Id = 2))
                  "the outgoing player is not rendered"

              let mismatchedRoster =
                  { restored with
                      HomePlayerIds =
                          [| FootballEngine.Core.Match.PlayerId 1
                             FootballEngine.Core.Match.PlayerId 22
                             FootballEngine.Core.Match.PlayerId 3 |] }

              let mismatchedRosterRejected =
                  try
                      MatchProjection.project
                          [| "A"; "B"; "C" |]
                          [| "D"; "E"; "F" |]
                          [| FootballEngine.Core.Match.PlayerId 1
                             FootballEngine.Core.Match.PlayerId 2
                             FootballEngine.Core.Match.PlayerId 3 |]
                          [| FootballEngine.Core.Match.PlayerId 4
                             FootballEngine.Core.Match.PlayerId 5
                             FootballEngine.Core.Match.PlayerId 6 |]
                          2
                          2
                          7
                          8
                          "Home"
                          "Away"
                          None
                          mismatchedRoster
                          1.0
                      |> ignore

                      false
                  with :? ArgumentException ->
                      true

              Expect.isTrue mismatchedRosterRejected "a replay cannot be projected against another fixture"

          testCase "restart projection names the awarded set piece"
          <| fun _ ->
              [ RestartVisual.KickOff, "KICK-OFF"
                RestartVisual.ThrowIn, "THROW-IN"
                RestartVisual.Corner, "CORNER"
                RestartVisual.GoalKick, "GOAL KICK"
                RestartVisual.FreeKick, "FREE KICK"
                RestartVisual.Penalty, "PENALTY" ]
              |> List.iter (fun (restart, expected) ->
                  Expect.equal
                      (MatchProjection.restartDescription restart)
                      expected
                      "the replay signal retains its football meaning") ]

let matchRuntimeLifecycleTests =
    testCase "leaving the match route clears the live runtime"
    <| fun _ ->
        let initial, _ = AppState.initWithGlobal GlobalSettings.defaults None ()

        let state =
            { initial with
                Mode = InGame(Unchecked.defaultof<GameState>, NotEmployed)
                Navigation =
                    { Current = Route.Match(42, MatchPhase.Live)
                      Back = []
                      Forward = [] }
                Match =
                    { initial.Match with
                        RuntimeId = Some(MatchRuntimeId 99UL) } }

        let next, _ = AppState.update (Navigate Route.Home) state

        Expect.equal next.Navigation.Current Route.Home "navigation leaves the match route"
        Expect.equal next.Match.RuntimeId None "the runtime id is released"
        Expect.equal next.Match.Live None "the live session is released"

let routingTests =
    testList
        "Routing"
        [ routeTests
          setupLoadTests
          setupDraftTests
          modEditorBackTests
          employmentGateTests
          matchNavigationTests
          matchRuntimeLifecycleTests ]
