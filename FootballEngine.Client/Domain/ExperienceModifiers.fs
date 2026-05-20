namespace FootballEngine.Domain

type ExperienceModifiers = {
    ShootingConfidence: float
    PassingRhythm: float
    DuelMentality: float
    HighPressureBonus: float
}

module ExperienceModifiers =
    let defaultModifiers = {
        ShootingConfidence = 1.0
        PassingRhythm = 1.0
        DuelMentality = 1.0
        HighPressureBonus = 1.0
    }
