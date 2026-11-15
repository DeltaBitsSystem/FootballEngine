namespace FootballEngine.Domain

/// A mod-authored player; Rating and Potential use the 1..100 scale shown to humans.
type PlayerEntry =
    { Name: string
      Position: Position
      Age: int
      Rating: int
      Potential: int
      PreferredFoot: PreferredFoot option
      Nationality: string option }

type ClubEntry =
    { Name: string
      LeagueLevel: int
      Reputation: int option
      Kit: ClubKit option
      Players: PlayerEntry list }

type NamePool =
    { FirstNames: string list
      LastNames: string list }

type CountryData =
    { Country: Country
      Names: NamePool
      LeagueNames: string list
      LeagueRules: LeagueRules list
      Clubs: ClubEntry list
      Cups: CupFormat list
      CupNames: string list }
