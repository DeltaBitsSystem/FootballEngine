namespace FootballEngine.Core.Tests.Contracts

open System
open Expecto
open FootballEngine.Domain
open FootballEngine.Core.Tests.Infrastructure
open FootballEngine.World
open FootballEngine.Core.Match

module FinanceTests =
    let private obligation id amount due =
        { Id = id
          Counterparty = "supplier"
          Description = "Facility work"
          DueDate = due
          Amount = amount
          Currency = EUR
          Condition = "Approved work"
          State = ObligationCommitted
          AccruedAmount = 0m
          PaidAmount = 0m }

    let tests =
        testList
            "FootballEngine.Core.Contracts.Finance"
            [ test "ledger entries remain balanced and preserve causal fields" {
                  let finance = Finance.initial 1_000_000m

                  let result =
                      Finance.recordRevenue
                          (DateTime(2026, 9, 1))
                          "gate-1"
                          "Ticket sales"
                          "Tickets"
                          25_000m
                          (Some "supporters")
                          finance

                  match result with
                  | Error error -> failtest error
                  | Ok next ->
                      Expect.equal next.Cash 1_025_000m "cash follows the receipt"
                      Expect.equal next.Ledger.Length 1 "one operation creates one entry"
                      let entry = next.Ledger.Head
                      Expect.equal entry.OperationId "gate-1" "operation remains traceable"

                      Expect.equal
                          (entry.Debits |> List.sumBy _.Amount)
                          (entry.Credits |> List.sumBy _.Amount)
                          "debits equal credits"
              }
              test "reservations reject overcommit and release capacity" {
                  let finance = Finance.initial 100_000m

                  let first =
                      Finance.reserveTransfer (DateTime(2026, 9, 1)) "offer-1" "player offer" 60_000m finance

                  let second =
                      first
                      |> Result.bind (Finance.reserveTransfer (DateTime(2026, 9, 1)) "offer-2" "another offer" 10_000m)

                  Expect.isError second "capacity is not overcommitted"

                  let released =
                      first
                      |> Result.map (Finance.releaseReservation "offer-1")
                      |> Result.defaultValue finance

                  Expect.equal
                      (Finance.availableForTransfers released)
                      finance.TransferBudget
                      "cancelled offers free capacity"
              }
              test "settlement is idempotent and distinguishes obligation states" {
                  let date = DateTime(2026, 9, 1)

                  let result =
                      Finance.initial 100_000m
                      |> Finance.commitObligation date "op-1" "Facility approved" (obligation "due-1" 10_000m date)
                      |> Result.bind (Finance.settleObligation date "op-2" "Facility paid" "due-1")
                      |> Result.bind (Finance.settleObligation date "op-2" "Facility paid" "due-1")

                  match result with
                  | Error error -> failtest error
                  | Ok finance ->
                      Expect.equal finance.Cash 90_000m "payment affects cash once"
                      Expect.equal (finance.Ledger.Length) 2 "accrual and payment are separate entries"
                      Expect.equal finance.Obligations.Head.State ObligationPaid "obligation reaches paid"
                      Expect.isOk (Finance.validate finance) "ledger invariant holds"
              }
              test "forecast exposes outstanding obligations as risk" {
                  let date = DateTime(2026, 9, 1)

                  let finance =
                      Finance.initial 100_000m
                      |> Finance.commitObligation
                          date
                          "op-1"
                          "Maintenance approved"
                          (obligation "due-1" 20_000m (date.AddDays 10.0))
                      |> Result.defaultValue (Finance.initial 100_000m)

                  let forecast = Finance.forecast finance
                  Expect.equal forecast.ExpectedCash 80_000m "forecast reserves future cost"
                  Expect.equal forecast.ExpectedCosts 20_000m "forecast reports the risk amount"
              }
              test "world day settlement reaches the single finance phase" {
                  let club =
                      { Id = 1
                        Name = "Test Club"
                        Nationality = "ARG"
                        Reputation = 100
                        PlayerIds = []
                        StaffIds = []
                        Finance = Finance.initial 100_000m
                        Morale = 60
                        BoardObjective = LeagueObjective MidTable
                        CoordinationMemory = CoordinationMemory.defaultMemory
                        ScoutingMemory = Map.empty
                        Kit = None }

                  let state =
                      { CurrentDate = DateTime(2026, 9, 1)
                        Season = 2026
                        TrainingWeeksApplied = 0
                        Clubs = Map [ 1, club ]
                        Players = Map.empty
                        Staff = Map.empty
                        Competitions = Map.empty
                        Countries = Map.empty
                        DataLock = TestDataLock.valid
                        UserClubId = 1
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
                        MatchSeeds = Map.empty }

                  let result =
                      WorldScheduler.executeDayWithBalance (
                          BalanceConfig.defaultConfig,
                          RunThroughMatches,
                          WorldClockOps.init 2026,
                          state
                      )

                  match result with
                  | Error failure -> failtest failure.Message
                  | Ok day ->
                      Expect.equal day.State.CurrentDate (DateTime(2026, 9, 2)) "finance runs inside the day kernel"
                      Expect.equal day.Phases.Length 8 "finance remains part of the ordered phases"
              } ]
