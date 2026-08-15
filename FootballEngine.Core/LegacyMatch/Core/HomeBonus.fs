namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types



module HomeBonus =
    let build (clubSide: ClubSide) (config: HomeAdvantageConfig) : HomeBonus =
        let isHome = clubSide = HomeClub
        let mult = if isHome then config.Strength else 0.0

        { AttackDuel = config.DuelAttackBonus * mult
          DefendDuel = config.DuelDefenseBonus * mult
          Tackle = config.TackleBonus * mult
          PassAcc = config.PassAccuracyBonus * mult
          ShotCompos = config.ShotComposureBonus * mult
          SetPlay = config.SetPlayAccuracyBonus * mult
          FreeKick = config.FreeKickComposure * mult
          Penalty = config.PenaltyBonus * mult
          CardReduc = config.CardReduction * mult
          FatigueReduc = config.FatigueReduction * mult }
