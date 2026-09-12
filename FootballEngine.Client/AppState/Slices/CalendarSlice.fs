namespace FootballEngine

open System
open FootballEngine.AppTypes
open FootballEngine.AppMsgs

module CalendarSlice =
    let init: CalendarState =
        { View = AgendaView
          DateFilter = FutureCalendarDates
          TeamFilter = MyTeam
          CompetitionFilter = None
          AnchorMonth = None
          SelectedFixtureId = None }

    let private shiftMonth (amount: int) (anchor: DateTime option) =
        let current = anchor |> Option.defaultValue (DateTime(2000, 1, 1))
        Some(DateTime(current.Year, current.Month, 1).AddMonths amount)

    let update (msg: CalendarMsg) (state: CalendarState) : CalendarState =
        match msg with
        | CalendarMsg.SetView view -> { state with View = view }
        | CalendarMsg.SetDateFilter filter -> { state with DateFilter = filter }
        | CalendarMsg.SetTeamFilter filter -> { state with TeamFilter = filter }
        | CalendarMsg.SetCompetitionFilter competitionId ->
            { state with
                CompetitionFilter = competitionId }
        | CalendarMsg.MoveMonth amount ->
            { state with
                AnchorMonth = shiftMonth amount state.AnchorMonth }
        | CalendarMsg.SelectFixture fixtureId ->
            { state with
                SelectedFixtureId = fixtureId }
