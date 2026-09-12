namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.AppTypes
open FootballEngine.Core.Match

module ScoutingSlice =

    type AssignmentRow =
        { Assignment: ScoutingAssignment
          Player: FootballEngine.Domain.Player option
          Knowledge: PlayerKnowledge option }

    let private targetPlayer target =
        match target with
        | PlayerTarget playerId -> Some playerId
        | PositionTarget _ -> None

    let assignmentRows (gs: GameState) : AssignmentRow list =
        gs.Market.Assignments
        |> Map.toList
        |> List.map snd
        |> List.sortByDescending _.Started
        |> List.map (fun assignment ->
            let playerId = targetPlayer assignment.Target

            { Assignment = assignment
              Player = playerId |> Option.bind (fun id -> gs.Players |> Map.tryFind id)
              Knowledge = playerId |> Option.bind (fun id -> gs.Market.Knowledge |> Map.tryFind id) })

    let shortlist (gs: GameState) =
        gs.Market.Shortlists |> Map.tryFind gs.UserClubId |> Option.defaultValue []

    let toggleShortlist (clubId: ClubId) (playerId: FootballEngine.Domain.PlayerId) (market: MarketState) =
        let current = market.Shortlists |> Map.tryFind clubId |> Option.defaultValue []

        let updated =
            if List.contains playerId current then
                current |> List.filter ((<>) playerId)
            else
                current @ [ playerId ]

        { market with
            Shortlists = market.Shortlists |> Map.add clubId updated }

    let status (row: AssignmentRow) =
        match row.Knowledge, row.Assignment.Status with
        | Some _, _ -> "Informe listo"
        | None, AssignmentAssigned -> "Asignado"
        | None, AssignmentInProgress -> "En curso"
        | None, AssignmentReady -> "Listo para revisar"
        | None, AssignmentCancelled -> "Cancelado"

    type ScoutingView =
        { NextOpponent: ClubId option
          Report: ScoutReport option }

    /// The next opponent is the soonest pending fixture involving the user
    /// club; a fixture already played today drops out through Played, so it
    /// never blocks the report for the one that follows.
    let nextOpponent (gs: GameState) : ClubId option =
        gs.Competitions
        |> Map.toSeq
        |> Seq.collect (fun (_, comp) -> comp.Fixtures |> Map.toSeq |> Seq.map snd)
        |> Seq.filter (fun f ->
            // Dates, not instants: a match scheduled earlier today but not yet
            // simulated is still the next one, not a missed one.
            not f.Played
            && f.ScheduledDate.Date >= gs.CurrentDate.Date
            && MatchFixture.involves gs.UserClubId f)
        |> Seq.sortBy _.ScheduledDate
        |> Seq.tryHead
        |> Option.map (fun f ->
            if f.HomeClubId = gs.UserClubId then
                f.AwayClubId
            else
                f.HomeClubId)

    /// The staff members whose judgement shapes match reports: a scout, or an
    /// analyst where no scout is on the books. The best pair of eyes decides
    /// how much noise the report carries.
    let private scoutingStaff (gs: GameState) : Staff option =
        GameState.getStaff gs.UserClubId gs
        |> List.filter (fun s ->
            match s.Role with
            | Scout
            | RecruitmentAnalyst
            | PerformanceAnalyst -> true
            | _ -> false)
        |> List.sortByDescending _.Knowledge.JudgingPlayerAbility
        |> List.tryHead

    /// Human report: noised by the staff's judging ability, seeded per day so
    /// the report is stable within a day and reproducible across replays.
    let buildReportWithBalance (balance: BalanceConfig) (gs: GameState) : ScoutReport option =
        match nextOpponent gs with
        | None -> None
        | Some rivalId ->
            match gs.Clubs[gs.UserClubId].ScoutingMemory |> Map.tryFind rivalId with
            | None -> None
            | Some mem ->
                let cfg = balance.Scout

                let noise =
                    match scoutingStaff gs with
                    | Some s -> cfg.NoiseByAbility * (1.0 - float s.Knowledge.JudgingPlayerAbility / 100.0)
                    | None -> cfg.NoiseByAbility

                // Same day, same report: mixing both club ids with the day keeps
                // the distortion stable within a day yet shifts it with the
                // calendar, so two days read as two different scouting sessions.
                let seed =
                    uint64 gs.UserClubId * 31UL
                    + uint64 rivalId * 17UL
                    + uint64 (gs.CurrentDate.Ticks &&& 0xFFFFFFFFL)

                Some(ScoutReport.generate cfg noise seed gs.UserClubId rivalId mem)

    let buildReport (gs: GameState) : ScoutReport option =
        let balance = WeightMergeSettings.mergeWithOverridesSave None
        buildReportWithBalance balance gs

    let buildReportWithSave
        (saveOpt: FootballEngine.Settings.SaveSettings option)
        (gs: GameState)
        : ScoutReport option =
        let balance = WeightMergeSettings.mergeWithOverridesSave saveOpt
        buildReportWithBalance balance gs

    let viewWithBalance (balance: BalanceConfig) (gs: GameState) : ScoutingView =
        { NextOpponent = nextOpponent gs
          Report = buildReportWithBalance balance gs }

    let view (gs: GameState) : ScoutingView =
        { NextOpponent = nextOpponent gs
          Report = buildReport gs }

    let viewWithSave (saveOpt: FootballEngine.Settings.SaveSettings option) (gs: GameState) : ScoutingView =
        let balance = WeightMergeSettings.mergeWithOverridesSave saveOpt
        viewWithBalance balance gs
