namespace FootballEngine.Domain

type CupObjective =
    | WinDomesticCup
    | WinContinentalCup
    | WinChampionsLeague

type LeagueObjective =
    | Survival
    | MidTable
    | TopHalf
    | TopFour
    | WinLeague

type BoardObjective =
    | LeagueObjective of LeagueObjective
    | CupObjective of CupObjective
    | Promotion
    | Relegation

type Club =
    { Id: ClubId
      Name: string
      Nationality: CountryCode
      Reputation: int
      PlayerIds: PlayerId list
      StaffIds: StaffId list
      Budget: decimal
      Morale: int
      BoardObjective: BoardObjective
      CoordinationMemory: CoordinationMemory }

module Club =

    let averageSkill (players: Map<PlayerId, Player>) (club: Club) =
        let skills =
            club.PlayerIds
            |> List.choose (fun pid -> players |> Map.tryFind pid |> Option.map _.CurrentSkill)

        match skills with
        | [] -> 0
        | s -> List.sum s / s.Length

    let addPlayer (playerId: PlayerId) (club: Club) =
        { club with
            PlayerIds = playerId :: (club.PlayerIds |> List.filter ((<>) playerId)) }

    let removePlayer (playerId: PlayerId) (club: Club) =
        { club with
            PlayerIds = club.PlayerIds |> List.filter ((<>) playerId) }

    let adjustBudget (delta: decimal) (club: Club) =
        { club with
            Budget = max 0m (club.Budget + delta) }

    let setObjective (objective: BoardObjective) (club: Club) =
        { club with BoardObjective = objective }
