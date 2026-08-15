namespace FootballEngine.Domain

type Confederation =
    | UEFA
    | CONMEBOL
    | CONCACAF
    | CAF
    | AFC
    | OFC

type Country =
    { Code: CountryCode
      Name: string
      Confederation: Confederation }
