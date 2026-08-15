namespace FootballEngine.World

open FootballEngine.Domain

type Conflict<'Intent> =
    | SameTarget of Intent<'Intent> * Intent<'Intent>
    | BudgetExceeded of ClubId * decimal * Intent<'Intent> list
    | LogicViolation of string * Intent<'Intent>

type Resolution<'Intent> =
    { Accepted: Intent<'Intent> list
      Rejected: (Intent<'Intent> * string) list }
