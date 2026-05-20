namespace FootballEngine.Player.Decision

open FootballEngine
open FootballEngine.Domain
open FootballEngine.ML
open FootballEngine.Types


type PlayerPersonality =
    { Flair: float
      Consistency: float
      ImportantMatches: float
      Controversy: float
      Leadership: float
      Teamwork: float
      Ambition: float
      Pressure: float
      Sportsmanship: float
      Temperament: float }

module PlayerPersonality =

    let derive (player: Player) (pw: PersonalityWeights) : PlayerPersonality =
        let mental = player.Mental
        let norm = MathPipelines.normStat

        { Flair = norm mental.Vision * pw.FlairVisionWeight + norm player.Technical.Dribbling * pw.FlairDribblingWeight
          Consistency = norm mental.Concentration * pw.ConsistencyConcentrationWeight + norm mental.Composure * pw.ConsistencyComposureWeight
          ImportantMatches =
            norm mental.Composure * 0.4
            + norm mental.Leadership * 0.3
            + norm player.Morale * 0.3
          Controversy = norm mental.Aggression * pw.ControversyAggressionWeight + (1.0 - norm mental.Composure) * pw.ControversyComposureWeight
          Leadership = norm mental.Leadership * pw.LeadershipWeight
          Teamwork = norm mental.WorkRate * pw.TeamworkWorkRateWeight + norm mental.Positioning * pw.TeamworkPositioningWeight
          Ambition = norm player.Morale * pw.AmbitionMoraleWeight + norm mental.WorkRate * pw.AmbitionWorkRateWeight
          Pressure = norm mental.Composure * pw.PressureComposureWeight + norm mental.Concentration * pw.PressureConcentrationWeight
          Sportsmanship = 1.0 - norm mental.Aggression * pw.SportsmanshipAggressionWeight
          Temperament = norm mental.Composure * pw.TemperamentComposureWeight + norm mental.Concentration * pw.TemperamentConcentrationWeight }

    let deriveWithDefaults (player: Player) : PlayerPersonality =
        derive player EngineWeightDefaults.defaults.Personality

    let defaultPersonality =
        { Flair = 0.5
          Consistency = 0.5
          ImportantMatches = 0.5
          Controversy = 0.5
          Leadership = 0.5
          Teamwork = 0.5
          Ambition = 0.5
          Pressure = 0.5
          Sportsmanship = 0.5
          Temperament = 0.5 }

module OpponentModel =

    type OpponentInsight =
        { IsSlow: bool
          IsAggressive: bool
          GKWeakSide: ClubSide
          HighPress: bool }

    let analyze (opponentFrame: TeamFrame) (opponentRoster: PlayerRoster) : OpponentInsight =
        let mutable avgPace = 0.0
        let mutable avgAggression = 0.0
        let mutable count = 0

        for i = 0 to opponentFrame.SlotCount - 1 do
            match opponentFrame.Physics.Occupancy[i] with
            | OccupancyKind.Active rosterIdx ->
                let p = opponentRoster.Players[rosterIdx]
                avgPace <- avgPace + float p.Physical.Pace
                avgAggression <- avgAggression + float p.Mental.Aggression
                count <- count + 1
            | _ -> ()

        let avgPaceNorm = if count > 0 then avgPace / float count / 20.0 else 0.5

        let avgAggNorm =
            if count > 0 then
                avgAggression / float count / 20.0
            else
                0.5

        { IsSlow = avgPaceNorm < 0.4
          IsAggressive = avgAggNorm > 0.6
          GKWeakSide = HomeClub
          HighPress = avgAggNorm > 0.5 }
