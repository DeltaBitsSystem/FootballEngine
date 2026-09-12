namespace FootballEngine.Domain

open System

module Career =

    let private objectiveTarget objective total =
        match objective with
        | WinLeague -> 1
        | TopFour -> min 4 total
        | TopHalf -> max 1 (total / 2)
        | MidTable -> max 1 (total * 3 / 4)
        | Survival -> total

    let reviewObjective
        (date: DateTime)
        (clubId: ClubId)
        (competition: Competition)
        (club: Club)
        : BoardObjectiveReview =
        let rank =
            competition
            |> Competition.rankedStandings
            |> List.tryFindIndex (fun (id, _) -> id = clubId)
            |> Option.map ((+) 1)

        let met =
            match club.BoardObjective, rank with
            | LeagueObjective objective, Some position ->
                position <= objectiveTarget objective competition.ClubIds.Length
            | _ -> false

        { ClubId = clubId
          Objective =
            match club.BoardObjective with
            | LeagueObjective objective -> objective
            | _ -> Survival
          Rank = rank
          Met = met
          Explanation =
            match rank with
            | Some position when met -> $"Finished {position}; board objective met"
            | Some position -> $"Finished {position}; board objective missed"
            | None -> "Competition standing was not confirmed" }

    let dismiss (date: DateTime) (reason: string) (staffId: StaffId) (gs: GameState) : GameState =
        match gs.Staff |> Map.tryFind staffId, GameState.userManager gs with
        | Some staff, Some current when current.Id = staffId ->
            let clubId = staff.Contract |> Option.map _.ClubId

            { gs with
                Staff =
                    gs.Staff
                    |> Map.add
                        staffId
                        { staff with
                            Contract = None
                            Status = Unemployed }
                CareerHistory =
                    match clubId with
                    | Some id -> ManagerDismissed(date, id, reason) :: gs.CareerHistory
                    | None -> gs.CareerHistory }
        | _ -> gs

    let resign (date: DateTime) (staffId: StaffId) (gs: GameState) : GameState =
        match gs.Staff |> Map.tryFind staffId with
        | Some staff ->
            match staff.Contract with
            | Some contract ->
                { gs with
                    Staff =
                        gs.Staff
                        |> Map.add
                            staffId
                            { staff with
                                Contract = None
                                Status = Unemployed }
                    CareerHistory = ManagerResigned(date, contract.ClubId) :: gs.CareerHistory }
            | None -> gs
        | None -> gs

    let hire
        (date: DateTime)
        (staffId: StaffId)
        (clubId: ClubId)
        (salary: decimal)
        (expiryYear: int)
        (gs: GameState)
        : Result<GameState, string> =
        match gs.Staff |> Map.tryFind staffId, gs.Clubs |> Map.tryFind clubId with
        | Some staff, Some _ when staff.Contract.IsNone ->
            Ok
                { gs with
                    Staff =
                        gs.Staff
                        |> Map.add
                            staffId
                            { staff with
                                Contract =
                                    Some
                                        { ClubId = clubId
                                          Salary = salary
                                          ExpiryYear = expiryYear }
                                Status = Active }
                    CareerHistory = ManagerHired(date, clubId) :: gs.CareerHistory }
        | Some _, Some _ -> Error "Manager is already employed"
        | None, _ -> Error "Manager does not exist"
        | _, None -> Error "Club does not exist"
