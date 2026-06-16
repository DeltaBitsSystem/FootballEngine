module FootballEngine.Tests.Diagnostics.FootballRanges

type Range =
    { Name: string
      Low: float
      High: float
      FailLowHint: string
      FailHighHint: string }

let goals =
    { Name = "Goals/90min"
      Low = 2.5
      High = 3.5
      FailLowHint = "check ShotAction finishing, GKAction save difficulty"
      FailHighHint = "check shot power, GK weakness" }

let shots =
    { Name = "Shots/90min"
      Low = 20.0
      High = 30.0
      FailLowHint = "players don't shoot enough, check ActionEligibility, PlayerDecision"
      FailHighHint = "excessive shots, check shot decision thresholds" }

let shotConversion =
    { Name = "ShotConversion%"
      Low = 8.0
      High = 15.0
      FailLowHint = "shots are weak or GK too good, check ShotAction, GKAction"
      FailHighHint = "shots too powerful or GK too weak" }

let passAccuracy =
    { Name = "PassAccuracy%"
      Low = 75.0
      High = 90.0
      FailLowHint = "pass success rate too low, check PassAction, ActionMath"
      FailHighHint = "pass completion too high, check interception difficulty" }

let passVolume =
    { Name = "Passes/90min"
      Low = 300.0
      High = 600.0
      FailLowHint = "players don't pass enough, check PlayerDecision pass frequency"
      FailHighHint = "excessive passing, check decision thresholds" }

let corners =
    { Name = "Corners/90min"
      Low = 6.0
      High = 14.0
      FailLowHint = "ball doesn't go out of bounds enough, check OutcomeResolver"
      FailHighHint = "excessive out-of-bounds, check ball physics" }

let fouls =
    { Name = "Fouls/90min"
      Low = 15.0
      High = 35.0
      FailLowHint = "no physical play, check FoulAnalysis thresholds"
      FailHighHint = "excessive fouling, check tackle aggression" }

let dribbles =
    { Name = "Dribbles/90min"
      Low = 8.0
      High = 25.0
      FailLowHint = "players don't dribble, check Dribble success rate"
      FailHighHint = "excessive dribbling, check decision logic" }

let possessionHome =
    { Name = "PossessionHome%"
      Low = 42.0
      High = 58.0
      FailLowHint = "home team too passive, check TeamDirector, tactical influence"
      FailHighHint = "home team too dominant, check away team logic" }

let drawRate =
    { Name = "DrawRate%"
      Low = 15.0
      High = 40.0
      FailLowHint = "too few draws, check shot conversion and momentum"
      FailHighHint = "too many draws, check finishing, urgency, late-game dynamics" }

let homeWinRate =
    { Name = "HomeWinRate%"
      Low = 30.0
      High = 55.0
      FailLowHint = "home team too weak, check home advantage, homeAttackDir"
      FailHighHint = "home team too dominant, check away team quality" }
