namespace FootballEngine.Core.Tests.World

open System
open Expecto
open FootballEngine.Domain
open FootballEngine.Core.Tests.Infrastructure
open FootballEngine.World
open FootballEngine.Core.Match

module WorldKernelTests =

    let private balance = BalanceConfig.defaultConfig

    let private emptyState () : GameState =
        { CurrentDate = DateTime(2026, 7, 1)
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
          PrimaryCountry = "ARG"
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
          MatchSeeds = Map.empty
          News = []
          NextNewsId = 1
          PulseProcessed = Set.empty
          PulseQueues = PulseQueues.empty
          JobOffers = []
          NextJobOfferId = 1
          UserStudy = [] }

    let worldKernelTests =
        testList
            "WorldKernel"
            [ testCase "agenda is ordered and due work is consumed once"
              <| fun _ ->
                  let first =
                      { Id = ScheduledWorkId 2UL
                        EffectiveDate = DateTime(2026, 7, 2)
                        Phase = ResolveDecisions
                        Kind = TrainingSession
                        TargetId = None }

                  let second = { first with Id = ScheduledWorkId 1UL }

                  let clock =
                      WorldClockOps.init 2026
                      |> WorldClockOps.scheduleWork first
                      |> WorldClockOps.scheduleWork second

                  let due = WorldClockOps.dueWork (DateTime(2026, 7, 2)) ResolveDecisions clock
                  let consumed = WorldClockOps.consumeWork due clock

                  Expect.equal (due |> List.map _.Id) [ ScheduledWorkId 1UL; ScheduledWorkId 2UL ] "stable ID ordering"

                  Expect.isEmpty
                      (WorldClockOps.dueWork (DateTime(2026, 7, 2)) ResolveDecisions consumed)
                      "work is single-use"

              testCase "one advance records the eight phases and one revision per phase"
              <| fun _ ->
                  let state = emptyState ()
                  let clock = WorldClockOps.init state.Season

                  let result =
                      WorldScheduler.executeDayWithBalance (balance, RunThroughMatches, clock, state)

                  match result with
                  | Error failure -> failtest failure.Message
                  | Ok day ->
                      Expect.equal day.State.CurrentDate (state.CurrentDate.AddDays(1.0)) "date advances once"
                      Expect.equal day.Clock.Revision 8UL "one revision per phase"

                      Expect.equal
                          (day.Clock.PhaseHistory |> List.map _.Phase)
                          [ ExtractDueWork
                            ApplyEffectiveRecords
                            PrepareDay
                            ResolveFixtures
                            ConfirmResults
                            SettleObligations
                            ResolveDecisions
                            CloseDay ]
                          "phase order is stable"

              testCase "a day without fixtures advances exactly once and keeps phase dates aligned"
              <| fun _ ->
                  let state = emptyState ()
                  let clock = WorldClockOps.init state.Season

                  match WorldScheduler.executeDayWithBalance (balance, RunThroughMatches, clock, state) with
                  | Error failure -> failtest failure.Message
                  | Ok day ->
                      Expect.equal day.State.CurrentDate (state.CurrentDate.AddDays(1.0)) "empty day advances once"
                      Expect.isEmpty day.PlayedMatches "empty day has no matches"
                      Expect.isEmpty day.Events "empty day publishes no match events"

                      Expect.all
                          day.Phases
                          (fun phase -> phase.Date = day.State.CurrentDate)
                          "all phases use the confirmed date"

              testCase "advanceDaysWithBalance delegates date advancement to the scheduler once"
              <| fun _ ->
                  let state = emptyState ()
                  let clock = WorldClockOps.init state.Season
                  let result = WorldRunner.advanceDaysWithBalance balance 1 clock state

                  Expect.equal
                      result.GameState.CurrentDate
                      (state.CurrentDate.AddDays(1.0))
                      "runner and scheduler do not double advance"

                  Expect.equal result.WorldClock.Revision 8UL "one day has one revision per phase"

              testCase "cancellation returns the last confirmed state at the phase boundary"
              <| fun _ ->
                  let state = emptyState ()
                  let clock = WorldClockOps.init state.Season

                  let request =
                      { FromDate = state.CurrentDate
                        UntilDate = state.CurrentDate.AddDays(1.0)
                        PausePolicy = RunThroughMatches
                        ExpectedRevision = clock.Revision
                        CancellationRequested = true }

                  let result = WorldRunner.advanceWithBalance balance request clock state
                  Expect.equal result.StoppedBecause Cancelled "cancellation is explicit"
                  Expect.equal result.State state "cancelled candidate is rolled back"
                  Expect.equal result.ConfirmedRevision clock.Revision "confirmed revision is unchanged"
                  Expect.isEmpty result.Events "cancelled phase publishes no events"

              testCase "stale revisions are rejected without changing state"
              <| fun _ ->
                  let state = emptyState ()
                  let clock = WorldClockOps.init state.Season

                  let request =
                      { FromDate = state.CurrentDate
                        UntilDate = state.CurrentDate.AddDays(1.0)
                        PausePolicy = RunThroughMatches
                        ExpectedRevision = 4UL
                        CancellationRequested = false }

                  let result = WorldRunner.advanceWithBalance balance request clock state
                  Expect.equal result.StoppedBecause (InvalidRequest "World revision is stale") "revision guard"
                  Expect.equal result.State state "candidate is not committed" ]
