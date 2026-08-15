namespace FootballEngine.Simulation

open FootballEngine.Domain
open FootballEngine.ML
open FootballEngine.Types

type WeatherCondition =
    | Clear
    | LightRain
    | HeavyRain
    | Snow
    | Windy

type PitchCondition =
    | Dry
    | Damp
    | Wet
    | Waterlogged

module WeatherSystem =

    let ballSpeedModifier (ew: EnvironmentWeights) (weather: WeatherCondition) : float =
        match weather with
        | Clear -> ew.WeatherClearModifier
        | LightRain -> ew.WeatherLightRainModifier
        | HeavyRain -> ew.WeatherHeavyRainModifier
        | Snow -> ew.WeatherSnowModifier
        | Windy -> ew.WeatherWindyModifier

    let slipProbability (ew: EnvironmentWeights) (pitch: PitchCondition) (player: Player) : float =
        let agility = float player.Physical.Agility / 20.0

        let baseSlip =
            match pitch with
            | Dry -> ew.PitchDrySlipBase
            | Damp -> ew.PitchDampSlipBase
            | Wet -> ew.PitchWetSlipBase
            | Waterlogged -> ew.PitchWaterloggedSlipBase

        baseSlip * (1.0 - agility * ew.SlipAgilityReduction)

type CrowdInfluence =
    { StadiumCapacity: int
      HomeSupport: float
      CurrentMomentum: float
      MatchImportance: float }

module CrowdSystem =

    let homeAdvantage (ew: EnvironmentWeights) (crowd: CrowdInfluence) : float =
        let capacityFactor = float crowd.StadiumCapacity / ew.CrowdMaxCapacity
        let supportFactor = crowd.HomeSupport
        let momentumFactor = max 0.0 crowd.CurrentMomentum / 10.0
        let importanceFactor = crowd.MatchImportance

        (capacityFactor * ew.CrowdCapacityWeight
         + supportFactor * ew.CrowdSupportWeight
         + momentumFactor * ew.CrowdMomentumWeight
         + importanceFactor * ew.CrowdImportanceWeight)
        * ew.CrowdMaxAdvantage

    let pressureOnAwayTeam (ew: EnvironmentWeights) (crowd: CrowdInfluence) : float =
        let homeAdv = homeAdvantage ew crowd
        homeAdv * ew.AwayPressureCrowdMult
