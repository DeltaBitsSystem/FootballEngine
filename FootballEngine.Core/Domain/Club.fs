namespace FootballEngine.Domain

open System

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

/// How a club's shirt looks. Data mods supply it; clubs without one get a generated kit in the UI.
type KitPattern =
    | Plain
    | Sash
    | Stripes
    | Hoops
    | Band
    | Halves

type ClubKit =
    { Primary: string
      Secondary: string
      Pattern: KitPattern }

type Club =
    { Id: ClubId
      Name: string
      Nationality: CountryCode
      Reputation: int
      PlayerIds: PlayerId list
      StaffIds: StaffId list
      Finance: ClubFinance
      Morale: int
      BoardObjective: BoardObjective
      CoordinationMemory: CoordinationMemory
      ScoutingMemory: Map<ClubId, OpponentMemory>
      Kit: ClubKit option
      BoardConfidence: int
      FansMood: int
      RecentResults: MatchOutcomeTag list
      ManagerSince: DateTime option }

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

    let setObjective (objective: BoardObjective) (club: Club) =
        { club with BoardObjective = objective }
