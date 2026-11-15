module FootballEngine.Client.Tests.Program

open Expecto
open FootballEngine.Client.Tests.ModEditorTests
open FootballEngine.Client.Tests.PersistenceTests
open FootballEngine.Client.Tests.RoutingTests
open FootballEngine.Client.Tests.TacticsDraftTests
open FootballEngine.Client.Tests

[<EntryPoint>]
let main argv =
    // The whole Client suite already fits the edit loop, so `--fast` selects everything.
    runTestsWithCLIArgs
        []
        (argv |> Array.filter ((<>) "--fast"))
        (testList
            "FootballEngine.Client"
            [ tests
              routingTests
              NewsPageTests.tests
              JobsPageTests.tests
              ViewerQualityTests.tests
              tacticsTests
              ModEditorTests.tests
              ModEditorPackageTests.tests
              LiveMatchTests.tests
              KeyMomentsTests.tests
              RivalCoachPresenterTests.tests
              CharacterAnimationTests.tests
              Animation.MotionRuntimeTests.tests
              Animation.MotionTimelineTests.tests
              Animation.MotionDatabaseTests.tests
              Animation.MotionMatchingTests.tests
              HdrLightingTests.tests
              ViewerPresentationTests.tests
              PitchMarkingsTests.tests
              MatchDiagnosticsTests.tests
              MatchHudTests.tests
              PresentationLoopTests.tests
              RenderWorkerTests.tests
              StadiumLayoutTests.tests
              Match3DFoundationTests.tests
              Match3DSceneTests.tests
              Match3DFrameTests.tests
              StadiumGeometryTests.tests ])
