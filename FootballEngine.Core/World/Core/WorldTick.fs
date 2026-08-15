namespace FootballEngine.World

type WorldTick =
    | Daily
    | Weekly
    | Monthly
    | Seasonal
    | OnDemand

type WorldTime =
    { Day: int
      Week: int
      Month: int
      Season: int }

module WorldTime =
    let shouldRun (freq: WorldTick) (t: WorldTime) =
        match freq with
        | Daily -> true
        | Weekly -> t.Day % 7 = 0
        | Monthly -> t.Day % 30 = 0
        | Seasonal -> t.Day = 1
        | OnDemand -> false
