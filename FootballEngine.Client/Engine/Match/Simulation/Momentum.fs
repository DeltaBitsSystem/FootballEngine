namespace FootballEngine.Simulation

open FootballEngine.Domain
open FootballEngine.ML

type MatchMomentum =
    { HomeMomentum: float
      AwayMomentum: float
      LastEventSubTick: int
      ConsecutiveHomeEvents: int
      ConsecutiveAwayEvents: int }

module DynamicMomentum =

    let initial =
        { HomeMomentum = 0.0
          AwayMomentum = 0.0
          LastEventSubTick = 0
          ConsecutiveHomeEvents = 0
          ConsecutiveAwayEvents = 0 }

    let update (momentum: MatchMomentum) (subTick: int) (eventClub: ClubSide) : MatchMomentum =
        let mw = EngineWeightDefaults.defaults.Momentum
        let delta = mw.EventDelta
        let decay = mw.Decay

        let (homeM, awayM, homeC, awayC) =
            match eventClub with
            | HomeClub ->
                (momentum.HomeMomentum + delta, momentum.AwayMomentum - decay, momentum.ConsecutiveHomeEvents + 1, 0)
            | AwayClub ->
                (momentum.HomeMomentum - decay, momentum.AwayMomentum + delta, 0, momentum.ConsecutiveAwayEvents + 1)

        { HomeMomentum = max mw.Min (min mw.Max homeM)
          AwayMomentum = max mw.Min (min mw.Max awayM)
          LastEventSubTick = subTick
          ConsecutiveHomeEvents = homeC
          ConsecutiveAwayEvents = awayC }
