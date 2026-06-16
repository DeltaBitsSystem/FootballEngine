module FootballEngine.Tests.Layer2.EnvironmentSystemsTests

open Expecto
open FootballEngine.Domain
open FootballEngine.Simulation
open FootballEngine.Tests.Infrastructure.Builders
open FootballEngine.Types.PhysicsContract

let private makePlayerWithAgility (agility: int) =
    let basePlayer = makePlayer 1 MC 10

    { basePlayer with
        Physical =
            { basePlayer.Physical with
                Agility = agility } }

let environmentSystemsTests =
    testList
        "EnvironmentSystems"
        [ testList
              "WeatherSystem.ballSpeedModifier"
              [ test "Clear weather returns 1.0" {
                    let result = WeatherSystem.ballSpeedModifier Clear
                    Expect.equal result 1.0 "Clear should be 1.0"
                }

                test "LightRain returns modifier < 1.0" {
                    let result = WeatherSystem.ballSpeedModifier LightRain
                    Expect.isLessThan result 1.0 "LightRain should reduce ball speed"
                    Expect.isGreaterThan result 0.0 "LightRain modifier should be positive"
                }

                test "HeavyRain returns modifier < LightRain" {
                    let light = WeatherSystem.ballSpeedModifier LightRain
                    let heavy = WeatherSystem.ballSpeedModifier HeavyRain
                    Expect.isLessThan heavy light "HeavyRain should reduce speed more than LightRain"
                }

                test "Snow returns modifier < HeavyRain" {
                    let heavy = WeatherSystem.ballSpeedModifier HeavyRain
                    let snow = WeatherSystem.ballSpeedModifier Snow
                    Expect.isLessThan snow heavy "Snow should reduce speed more than HeavyRain"
                }

                test "Windy returns modifier between Snow and LightRain" {
                    let light = WeatherSystem.ballSpeedModifier LightRain
                    let snow = WeatherSystem.ballSpeedModifier Snow
                    let windy = WeatherSystem.ballSpeedModifier Windy
                    Expect.isLessThan windy light "Windy should reduce speed more than LightRain"
                    Expect.isGreaterThan windy snow "Windy should reduce speed less than Snow"
                }

                test "all weather modifiers are positive" {
                    for weather in [ Clear; LightRain; HeavyRain; Snow; Windy ] do
                        let result = WeatherSystem.ballSpeedModifier weather
                        Expect.isGreaterThan result 0.0 $"%A{weather} modifier should be positive"
                } ]

          testList
              "WeatherSystem.slipProbability"
              [ test "Dry pitch has lowest slip probability" {
                    let player = makePlayerWithAgility 10

                    let dry = WeatherSystem.slipProbability Dry player
                    let damp = WeatherSystem.slipProbability Damp player
                    let wet = WeatherSystem.slipProbability Wet player
                    let waterlogged = WeatherSystem.slipProbability Waterlogged player
                    Expect.isLessThan dry damp "Dry should be < Damp"
                    Expect.isLessThan damp wet "Damp should be < Wet"
                    Expect.isLessThan wet waterlogged "Wet should be < Waterlogged"
                }

                test "higher agility reduces slip probability" {
                    let lowAgility = WeatherSystem.slipProbability Wet (makePlayerWithAgility 3)
                    let highAgility = WeatherSystem.slipProbability Wet (makePlayerWithAgility 18)
                    Expect.isLessThan highAgility lowAgility "High agility player should slip less"
                }

                test "slip probability is non-negative" {
                    for pitch in [ Dry; Damp; Wet; Waterlogged ] do
                        let player = makePlayerWithAgility 20
                        let result = WeatherSystem.slipProbability pitch player
                        Expect.isGreaterThanOrEqual result 0.0 $"%A{pitch} slip should be >= 0"
                } ]

          testList
              "CrowdSystem.homeAdvantage"
              [ test "empty stadium returns near zero advantage" {
                    let crowd =
                        { StadiumCapacity = 0
                          HomeSupport = 0.0
                          CurrentMomentum = 0.0
                          MatchImportance = 0.0 }

                    let result = CrowdSystem.homeAdvantage crowd
                    Expect.equal result 0.0 "Empty stadium should give 0 advantage"
                }

                test "full stadium with high support returns positive advantage" {
                    let crowd =
                        { StadiumCapacity = 80000
                          HomeSupport = 1.0
                          CurrentMomentum = 5.0
                          MatchImportance = 1.0 }

                    let result = CrowdSystem.homeAdvantage crowd
                    Expect.isGreaterThan result 0.0 "Full stadium should give positive advantage"
                }

                test "higher stadium capacity increases advantage" {
                    let small =
                        { StadiumCapacity = 20000
                          HomeSupport = 0.5
                          CurrentMomentum = 0.0
                          MatchImportance = 0.5 }

                    let large =
                        { StadiumCapacity = 80000
                          HomeSupport = 0.5
                          CurrentMomentum = 0.0
                          MatchImportance = 0.5 }

                    Expect.isGreaterThan
                        (CrowdSystem.homeAdvantage large)
                        (CrowdSystem.homeAdvantage small)
                        "Larger stadium should give more advantage"
                }

                test "home advantage is bounded" {
                    let crowd =
                        { StadiumCapacity = 80000
                          HomeSupport = 1.0
                          CurrentMomentum = 10.0
                          MatchImportance = 1.0 }

                    let result = CrowdSystem.homeAdvantage crowd
                    Expect.isLessThanOrEqual result 0.15 "Advantage should not exceed CrowdMaxAdvantage"
                } ]

          testList
              "CrowdSystem.pressureOnAwayTeam"
              [ test "pressure scales with home advantage" {
                    let lowCrowd =
                        { StadiumCapacity = 10000
                          HomeSupport = 0.2
                          CurrentMomentum = 0.0
                          MatchImportance = 0.2 }

                    let highCrowd =
                        { StadiumCapacity = 80000
                          HomeSupport = 1.0
                          CurrentMomentum = 5.0
                          MatchImportance = 1.0 }

                    let lowPressure = CrowdSystem.pressureOnAwayTeam lowCrowd
                    let highPressure = CrowdSystem.pressureOnAwayTeam highCrowd
                    Expect.isGreaterThan highPressure lowPressure "Bigger crowd should create more pressure"
                }

                test "pressure is always non-negative" {
                    let crowd =
                        { StadiumCapacity = 0
                          HomeSupport = 0.0
                          CurrentMomentum = 0.0
                          MatchImportance = 0.0 }

                    let result = CrowdSystem.pressureOnAwayTeam crowd
                    Expect.isGreaterThanOrEqual result 0.0 "Pressure should be non-negative"
                } ] ]
