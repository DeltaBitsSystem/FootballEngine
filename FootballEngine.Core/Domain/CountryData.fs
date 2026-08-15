namespace FootballEngine.Domain

type ClubEntry =
    { Name: string
      LeagueLevel: int
      Reputation: int option }

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
