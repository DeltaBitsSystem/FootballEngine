namespace FootballEngine.Core.Tests.Contracts

open System
open Expecto
open FootballEngine.Core.Match
open FootballEngine.Domain
open FootballEngine.Core.Tests.Infrastructure

module BoundaryTests =

    let tests =
        testList
            "FootballEngine.Core.Contracts.Boundaries"
            [ test "effective config applies mods by stable id" {
                  let mods =
                      [ "z-last", """{ "World": { "Economy": { "BaseCurrency": "GBP" } } }"""
                        "a-first", """{ "World": { "Economy": { "BaseCurrency": "USD" } } }""" ]

                  match EffectiveConfig.resolve mods None with
                  | Error error -> failtestf "resolution failed: %A" error
                  | Ok resolved ->
                      Expect.equal resolved.Balance.World.Economy.BaseCurrency "GBP" "later stable mod wins"
                      Expect.isNonEmpty resolved.Content "resolved content is stored"
                      Expect.notEqual resolved.Hash 0UL "resolved content has a fingerprint"
              }

              test "invalid career config is rejected and previous effective config survives" {
                  let previous = EffectiveConfig.defaultConfig

                  let resolved =
                      EffectiveConfig.resolveOrPrevious
                          (Some previous)
                          []
                          (Some """{ "World": { "Ai": { "DecisionCadenceDays": 0 } } }""")

                  Expect.equal resolved.Hash previous.Hash "invalid import keeps the previous content"
                  Expect.equal resolved.Revision previous.Revision "invalid import keeps the previous revision"
              }

              test "world command ordering is stable by date and id" {
                  let later =
                      { Id = CommandId 2UL
                        Issuer = System
                        EffectiveDate = DateTime(2026, 9, 2)
                        ExpectedRevision = 0UL
                        Action = AdvanceDays 1 }

                  let earlier =
                      { later with
                          Id = CommandId 1UL
                          EffectiveDate = DateTime(2026, 9, 1) }

                  Expect.isLessThan (WorldCommand.compare earlier later) 0 "earlier command sorts first"
                  Expect.isOk (WorldCommand.validate earlier) "valid command is accepted"
              }

              test "world config rejects invalid operational values" {
                  let invalid: WorldConfig =
                      { WorldConfig.defaults with
                          Ai =
                              { WorldConfig.defaults.Ai with
                                  MaxParallelAgents = 0 } }

                  Expect.isNonEmpty (WorldConfig.validate invalid) "invalid world config is explicit"
              }

              test "save envelope rejects missing version and config identity" {
                  let state: WorldState =
                      { CurrentDate = DateTime(2026, 9, 1)
                        Season = 2026
                        TrainingWeeksApplied = 0
                        Clubs = Map.empty
                        Players = Map.empty
                        Staff = Map.empty
                        Competitions = Map.empty
                        Countries = Map.empty
                        DataLock = TestDataLock.valid
                        UserClubId = 0
                        UserStaffId = 0
                        PrimaryCountry = ""
                        Inbox = []
                        NextInboxId = 1
                        PendingNegotiations = Map.empty
                        NextNegotiationId = 1
                        Market = MarketState.empty
                        ProfileCache = Map.empty
                        PlayerExperiences = Map.empty
                        TeamEmphasis = EmPossession
                        SeasonHistory = []
                        CareerHistory = []
                        ExecutionSeed = 1UL
                        MatchSeeds = Map.empty }

                  let invalid: SaveEnvelope =
                      { ConfirmedState = state
                        SchemaVersion = 0
                        EngineVersion = ""
                        CareerId = ""
                        ConfirmedRevision = 0UL
                        ConfirmedDate = DateTime.MinValue
                        ClockDay = 0
                        ClockWeek = 0
                        ClockMonth = 0
                        ClockSeason = 0
                        ConfigContent = ""
                        ConfigHash = 0UL
                        DataManifest = []
                        DataLock =
                          { FormatVersion = 0
                            EngineVersion = ""
                            ContentApi = 0
                            Packages = []
                            DependencyLockHash = ""
                            ResolvedSportsHash = ""
                            ResolvedPresentationHash = ""
                            RuntimeIdTable = []
                            Snapshot =
                              { SportsContent = ""
                                Translations = ""
                                Assets = "" }
                            RequiredAssets = [] }
                        ResolvedDataHash = ""
                        ResolvedDataContent = "" }

                  Expect.isError (SaveEnvelope.validate invalid) "invalid envelope is rejected"
              } ]
