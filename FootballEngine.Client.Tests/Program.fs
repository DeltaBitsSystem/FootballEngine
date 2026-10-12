module FootballEngine.Client.Tests.Program

open Expecto
open FootballEngine.Client.Tests.ModEditorTests
open FootballEngine.Client.Tests.PersistenceTests
open FootballEngine.Client.Tests.RoutingTests
open FootballEngine.Client.Tests.TacticsDraftTests

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs
        []
        argv
        (testList
            "FootballEngine.Client"
            [ tests
              routingTests
              tacticsTests
              ModEditorTests.tests
              LiveMatchTests.tests
              CharacterAnimationTests.tests
              Animation.MotionRuntimeTests.tests
              Animation.MotionTimelineTests.tests
              HdrLightingTests.tests
              PitchMarkingsTests.tests
              MatchDiagnosticsTests.tests
              MatchHudTests.tests
              PresentationLoopTests.tests
              RenderWorkerTests.tests ])
