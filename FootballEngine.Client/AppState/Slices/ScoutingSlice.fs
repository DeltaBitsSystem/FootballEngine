namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.AppTypes
open FootballEngine.Core.Match

module ScoutingSlice =

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
    let buildReport (gs: GameState) : ScoutReport option =
        match nextOpponent gs with
        | None -> None
        | Some rivalId ->
            match gs.Clubs[gs.UserClubId].ScoutingMemory |> Map.tryFind rivalId with
            | None -> None
            | Some mem ->
                let cfg = BalanceConfig.defaultConfig.Scout

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

    let view (gs: GameState) : ScoutingView =
        { NextOpponent = nextOpponent gs
          Report = buildReport gs }
