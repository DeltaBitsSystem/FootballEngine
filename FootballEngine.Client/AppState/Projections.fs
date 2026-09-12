namespace FootballEngine

open System
open FootballEngine.AppTypes
open FootballEngine.Domain
open FootballEngine.Routing

module CareerProjections =
    type FixtureStatus =
        | Played
        | Scheduled
        | Unresolved

    type FixtureProjection =
        { MatchId: MatchId
          CompetitionId: CompetitionId
          CompetitionName: string
          Round: Round option
          Date: DateTime
          HomeClubId: ClubId
          AwayClubId: ClubId
          HomeName: string option
          AwayName: string option
          Status: FixtureStatus
          HomeScore: int option
          AwayScore: int option
          Events: FootballEngine.Core.Match.MatchEvent list }

    type ResultOutcome =
        | Won
        | Drew
        | Lost
        | ScoreUnavailable

    type ResultChange =
        | InjuryEvents of count: int
        | MatchEvents of count: int
        | NoRecordedChange

    type RecentResultProjection =
        { Fixture: FixtureProjection
          Outcome: ResultOutcome
          Change: ResultChange }

    type StandingProjection =
        { Position: int
          Total: int
          Standing: LeagueStanding }

    type ClubPulse =
        { Morale: int
          AvailablePlayers: int
          UnavailablePlayers: int
          TotalPlayers: int
          TransferBudget: decimal
          WageBudget: decimal
          WageCommitment: decimal }

    type DecisionSubject =
        | InboxDecision of InboxMessage
        | ObligationDecision of FinancialObligation
        | FixtureDecision of FixtureProjection

    type DecisionProjection =
        { Key: string
          Priority: int
          Subject: DecisionSubject
          Deadline: DateTime option
          Cost: decimal option
          Route: Route }

    type LedgerDirection =
        | Income
        | Expense
        | Commitment

    type LedgerProjection =
        { Entry: LedgerEntry
          Direction: LedgerDirection }

    type CashFlowProjection =
        { Date: DateTime
          Income: decimal
          Expenses: decimal
          Commitments: decimal }

    type FinanceHealth =
        | Healthy
        | Watch
        | ActionRequired

    type FinanceProjection =
        { Finance: ClubFinance
          AvailableForTransfers: decimal
          ActiveObligations: FinancialObligation list
          Forecast: CashForecast
          Health: FinanceHealth
          Ledger: LedgerProjection list
          Flow: CashFlowProjection list }

    type FinanceOperationProjection =
        | LedgerOperation of LedgerProjection
        | ObligationOperation of FinancialObligation
        | ReservationOperation of Reservation
        | OperationNotFound of string

    let private clubName (gs: GameState) (clubId: ClubId) : string option =
        gs.Clubs |> Map.tryFind clubId |> Option.map _.Name

    let private fixtureStatus (currentDate: DateTime) (fixture: MatchFixture) =
        if fixture.Played then
            Played
        elif fixture.ScheduledDate.Date < currentDate.Date then
            Unresolved
        else
            Scheduled

    let allFixtures (gs: GameState) : FixtureProjection list =
        gs.Competitions
        |> Map.toList
        |> List.collect (fun (competitionId, competition) ->
            competition.Fixtures
            |> Map.toList
            |> List.map (fun (matchId, fixture) ->
                { MatchId = matchId
                  CompetitionId = competitionId
                  CompetitionName = competition.Name
                  Round = fixture.Round
                  Date = fixture.ScheduledDate
                  HomeClubId = fixture.HomeClubId
                  AwayClubId = fixture.AwayClubId
                  HomeName = clubName gs fixture.HomeClubId
                  AwayName = clubName gs fixture.AwayClubId
                  Status = fixtureStatus gs.CurrentDate fixture
                  HomeScore = fixture.HomeScore
                  AwayScore = fixture.AwayScore
                  Events = fixture.Events }))
        |> List.sortBy (fun fixture -> fixture.Date, fixture.MatchId)

    let tryFixture (gs: GameState) (matchId: MatchId) : FixtureProjection option =
        allFixtures gs |> List.tryFind (fun fixture -> fixture.MatchId = matchId)

    let isPast fixture =
        match fixture.Status with
        | Played
        | Unresolved -> true
        | Scheduled -> false

    let isFuture fixture = fixture.Status = Scheduled

    let upcomingUserFixtures (count: int) (gs: GameState) : FixtureProjection list =
        allFixtures gs
        |> List.filter (fun fixture ->
            fixture.Status = Scheduled
            && (fixture.HomeClubId = gs.UserClubId || fixture.AwayClubId = gs.UserClubId))
        |> List.truncate count

    let recentUserResults (count: int) (gs: GameState) : RecentResultProjection list =
        allFixtures gs
        |> List.filter (fun fixture ->
            fixture.Status = Played
            && (fixture.HomeClubId = gs.UserClubId || fixture.AwayClubId = gs.UserClubId))
        |> List.sortByDescending (fun fixture -> fixture.Date, fixture.MatchId)
        |> List.truncate count
        |> List.map (fun fixture ->
            let outcome =
                match fixture.HomeScore, fixture.AwayScore with
                | Some home, Some away when home = away -> Drew
                | Some home, Some away ->
                    let userScore = if fixture.HomeClubId = gs.UserClubId then home else away
                    let opponentScore = if fixture.HomeClubId = gs.UserClubId then away else home

                    if userScore > opponentScore then Won else Lost
                | _ -> ScoreUnavailable

            let injuryCount =
                fixture.Events
                |> List.sumBy (fun event ->
                    match event.Data with
                    | FootballEngine.Core.Match.EventData.Injury _ -> 1
                    | _ -> 0)

            { Fixture = fixture
              Outcome = outcome
              Change =
                if injuryCount > 0 then
                    InjuryEvents injuryCount
                elif not fixture.Events.IsEmpty then
                    MatchEvents fixture.Events.Length
                else
                    NoRecordedChange })

    let userStanding (competitionId: CompetitionId) (gs: GameState) : StandingProjection option =
        gs.Competitions
        |> Map.tryFind competitionId
        |> Option.bind (fun competition ->
            competition
            |> Competition.rankedStandings
            |> List.indexed
            |> List.tryFind (fun (_, (clubId, standing)) -> clubId = gs.UserClubId && standing.Played > 0)
            |> Option.map (fun (index, (_, standing)) ->
                { Position = index + 1
                  Total = competition.ClubIds.Length
                  Standing = standing }))

    let userHasConfirmedResults (competitionId: CompetitionId) (gs: GameState) : bool =
        gs.Competitions
        |> Map.tryFind competitionId
        |> Option.exists (fun competition ->
            competition.Standings
            |> Map.tryFind gs.UserClubId
            |> Option.exists (fun standing -> standing.Played > 0))

    let clubPulse (gs: GameState) : ClubPulse option =
        gs.Clubs
        |> Map.tryFind gs.UserClubId
        |> Option.map (fun club ->
            let players = GameState.getSquad gs.UserClubId gs

            let unavailable =
                players
                |> List.filter (fun player ->
                    match player.Status with
                    | PlayerStatus.Available -> false
                    | PlayerStatus.Suspended _
                    | PlayerStatus.Injured _ -> true)
                |> List.length

            { Morale = club.Morale
              AvailablePlayers = players.Length - unavailable
              UnavailablePlayers = unavailable
              TotalPlayers = players.Length
              TransferBudget = Finance.availableForTransfers club.Finance
              WageBudget = club.Finance.WageBudget
              WageCommitment = club.Finance.WageCommitment })

    let nextSevenDays (gs: GameState) : FixtureProjection list =
        let endDate = gs.CurrentDate.Date.AddDays 7.0

        upcomingUserFixtures Int32.MaxValue gs
        |> List.filter (fun fixture -> fixture.Date.Date <= endDate)

    let decisionQueue (gs: GameState) : DecisionProjection list =
        let messageDecisions =
            gs.Inbox
            |> List.filter (fun message ->
                message.RequiresAction
                && (message.ActionTaken.IsNone || message.ActionTaken = Some false))
            |> List.sortByDescending (fun message -> message.Date, message.Id)
            |> List.map (fun message ->
                { Key = "inbox:" + string message.Id
                  Priority = 1
                  Subject = InboxDecision message
                  Deadline = None
                  Cost = None
                  Route = Route.Inbox(InboxRoute(Some message.Id)) })

        let financialDecisions =
            gs.Clubs
            |> Map.tryFind gs.UserClubId
            |> Option.map (fun club ->
                club.Finance.Obligations
                |> List.filter (fun obligation ->
                    obligation.State <> ObligationPaid
                    && obligation.State <> ObligationCancelled
                    && obligation.DueDate.Date <= gs.CurrentDate.Date.AddDays 7.0)
                |> List.sortBy (fun obligation -> obligation.DueDate, obligation.Id)
                |> List.map (fun obligation ->
                    { Key = "obligation:" + obligation.Id
                      Priority = if obligation.State = ObligationOverdue then 0 else 1
                      Subject = ObligationDecision obligation
                      Deadline = Some obligation.DueDate
                      Cost = Some obligation.Amount
                      Route = Route.Finances(FinanceRoute.Operation obligation.Id) }))
            |> Option.defaultValue []

        let fixtureDecisions =
            nextSevenDays gs
            |> List.tryHead
            |> Option.map (fun fixture ->
                { Key = "fixture:" + string fixture.MatchId
                  Priority = 2
                  Subject = FixtureDecision fixture
                  Deadline = Some fixture.Date
                  Cost = None
                  Route = Route.Match(fixture.MatchId, MatchPhase.Prematch) })
            |> Option.toList

        messageDecisions @ financialDecisions @ fixtureDecisions
        |> List.sortBy (fun decision ->
            decision.Priority, decision.Deadline |> Option.defaultValue DateTime.MaxValue, decision.Key)
        |> List.truncate 3

    let ledgerDirection (entry: LedgerEntry) : LedgerDirection =
        if entry.Debits |> List.exists (fun posting -> posting.Account = CashAccount) then
            Income
        elif entry.Credits |> List.exists (fun posting -> posting.Account = CashAccount) then
            Expense
        else
            Commitment

    let allLedgerRows (finance: ClubFinance) : LedgerProjection list =
        finance.Ledger
        |> List.map (fun entry ->
            { Entry = entry
              Direction = ledgerDirection entry })

    let ledgerRows
        (finance: ClubFinance)
        (filter: FinanceLedgerFilter)
        (sort: FinanceLedgerSort)
        : LedgerProjection list =
        let matches row =
            match filter, row.Direction with
            | AllLedgerEntries, _ -> true
            | IncomeEntries, Income -> true
            | ExpenseEntries, Expense -> true
            | CommitmentEntries, Commitment -> true
            | _ -> false

        let rows = allLedgerRows finance |> List.filter matches

        match sort with
        | NewestLedgerFirst -> rows |> List.sortByDescending (fun row -> row.Entry.Date, row.Entry.Id)
        | OldestLedgerFirst -> rows |> List.sortBy (fun row -> row.Entry.Date, row.Entry.Id)
        | LargestLedgerFirst ->
            rows
            |> List.sortByDescending (fun row -> row.Entry.Amount, row.Entry.Date, row.Entry.Id)
        | SmallestLedgerFirst -> rows |> List.sortBy (fun row -> row.Entry.Amount, row.Entry.Date, row.Entry.Id)

    let private cashFlowForDate (date: DateTime) (rows: LedgerProjection list) =
        let amount direction =
            rows
            |> List.filter (fun row -> row.Direction = direction)
            |> List.sumBy (fun row -> row.Entry.Amount)

        { Date = date
          Income = amount Income
          Expenses = amount Expense
          Commitments = amount Commitment }

    let cashFlow (finance: ClubFinance) : CashFlowProjection list =
        allLedgerRows finance
        |> List.groupBy (fun row -> row.Entry.Date.Date)
        |> List.map (fun (date, rows) -> cashFlowForDate date rows)
        |> List.sortByDescending _.Date

    let financeProjection (gs: GameState) : FinanceProjection option =
        gs.Clubs
        |> Map.tryFind gs.UserClubId
        |> Option.map (fun club ->
            let finance = club.Finance

            let activeObligations =
                finance.Obligations
                |> List.filter (fun obligation ->
                    obligation.State <> ObligationPaid && obligation.State <> ObligationCancelled)

            let availableTransfers = Finance.availableForTransfers finance

            let health =
                if
                    activeObligations
                    |> List.exists (fun obligation -> obligation.State = ObligationOverdue)
                    || availableTransfers < 0m
                then
                    ActionRequired
                elif
                    finance.Cash < finance.BoardPolicy.MinimumCashReserve
                    || finance.Debt > finance.BoardPolicy.MaximumDebt
                then
                    Watch
                else
                    Healthy

            { Finance = finance
              AvailableForTransfers = availableTransfers
              ActiveObligations = activeObligations
              Forecast = Finance.forecast finance
              Health = health
              Ledger = allLedgerRows finance
              Flow = cashFlow finance })

    let financeOperation (finance: ClubFinance) (operationId: string) : FinanceOperationProjection =
        match
            allLedgerRows finance
            |> List.tryFind (fun row -> row.Entry.OperationId = operationId)
        with
        | Some row -> LedgerOperation row
        | None ->
            match
                finance.Obligations
                |> List.tryFind (fun obligation -> obligation.Id = operationId)
            with
            | Some obligation -> ObligationOperation obligation
            | None ->
                match
                    finance.Reservations
                    |> List.tryFind (fun reservation -> reservation.Id = operationId)
                with
                | Some reservation -> ReservationOperation reservation
                | None -> OperationNotFound operationId

    let private teamMatches filter fixture =
        match filter with
        | MyTeam -> true
        | AllTeams -> true
        | SelectedTeam clubId -> fixture.HomeClubId = clubId || fixture.AwayClubId = clubId

    let private dateMatches filter fixture =
        match filter with
        | AllCalendarDates -> true
        | PastCalendarDates -> isPast fixture
        | FutureCalendarDates -> isFuture fixture

    let visibleCalendarFixtures (state: CalendarState) (gs: GameState) : FixtureProjection list =
        allFixtures gs
        |> List.filter (teamMatches state.TeamFilter)
        |> List.filter (dateMatches state.DateFilter)
        |> List.filter (fun fixture ->
            state.CompetitionFilter
            |> Option.forall (fun competitionId -> fixture.CompetitionId = competitionId))

    let calendarTeamOptions (gs: GameState) : (ClubId * string option) list =
        let ids =
            allFixtures gs
            |> List.collect (fun fixture -> [ fixture.HomeClubId; fixture.AwayClubId ])
            |> Set.ofList
            |> Set.add gs.UserClubId
            |> Set.toList

        ids
        |> List.map (fun clubId -> clubId, clubName gs clubId)
        |> List.sortBy (fun (_, name) -> name)

    let calendarCompetitionOptions (gs: GameState) : (CompetitionId * string) list =
        allFixtures gs
        |> List.map (fun fixture -> fixture.CompetitionId, fixture.CompetitionName)
        |> List.distinctBy fst
        |> List.sortBy snd
