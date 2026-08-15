namespace FootballEngine.Types


type AttackPattern =
    | LeftFlank
    | RightFlank
    | Central
    | LongBall
    | ShortPass

type PatternResult =
    | SuccessfulXG of float
    | LostPossession
    | StillInProgress

type PatternRecord =
    { Pattern: AttackPattern
      Attempts: int
      Successes: int
      TotalXG: float }

type AdaptiveState = { Records: PatternRecord[] }


module AdaptiveTactics =

    let recordAttempt (pattern: AttackPattern) (result: PatternResult) (state: AdaptiveState) : AdaptiveState =
        { Records =
            state.Records
            |> Array.map (fun r ->
                if r.Pattern <> pattern then
                    r
                else
                    match result with
                    | SuccessfulXG xg ->
                        { r with
                            Attempts = r.Attempts + 1
                            Successes = r.Successes + 1
                            TotalXG = r.TotalXG + xg }
                    | LostPossession -> { r with Attempts = r.Attempts + 1 }
                    | StillInProgress -> r) }


    let initial =
        { Records =
            [| { Pattern = LeftFlank
                 Attempts = 0
                 Successes = 0
                 TotalXG = 0.0 }
               { Pattern = RightFlank
                 Attempts = 0
                 Successes = 0
                 TotalXG = 0.0 }
               { Pattern = Central
                 Attempts = 0
                 Successes = 0
                 TotalXG = 0.0 }
               { Pattern = LongBall
                 Attempts = 0
                 Successes = 0
                 TotalXG = 0.0 }
               { Pattern = ShortPass
                 Attempts = 0
                 Successes = 0
                 TotalXG = 0.0 } |] }
