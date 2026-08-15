module FootballEngine.Core.Tests.Program

open Expecto
open FootballEngine.Core.Tests.Contracts.RngTests
open FootballEngine.Core.Tests.Contracts.ClockTests
open FootballEngine.Core.Tests.Contracts.EventFlagTests
open FootballEngine.Core.Tests.Contracts.ConfigTests
open FootballEngine.Core.Tests.Contracts.ConfigMigrationTests

[<EntryPoint>]
let main argv =
    let tests =
        testList "FootballEngine.Core" [ rngTests; clockTests; eventFlagTests; configTests; configMigrationTests ]

    runTestsWithCLIArgs [] argv tests
