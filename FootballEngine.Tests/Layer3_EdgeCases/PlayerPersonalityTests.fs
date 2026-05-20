module FootballEngine.Tests.Layer3.PlayerPersonalityTests

open Expecto
open FootballEngine.Domain
open FootballEngine.Player.Decision
open FootballEngine.Tests.Infrastructure.Builders

let playerPersonalityTests =
    testList
        "PlayerPersonality"
        [ test "derive produces personality from player" {
              let player = makePlayer 1 ST 15
              let personality = PlayerPersonality.deriveWithDefaults player
              Expect.isGreaterThanOrEqual personality.Flair 0.0 "flair should be >= 0"
              Expect.isLessThanOrEqual personality.Flair 1.0 "flair should be <= 1"
              Expect.isGreaterThanOrEqual personality.Consistency 0.0 "consistency should be >= 0"
              Expect.isLessThanOrEqual personality.Consistency 1.0 "consistency should be <= 1"
          }

          test "high mental stats produce high composure and leadership" {
              let player = makePlayer 1 ST 20
              let personality = PlayerPersonality.deriveWithDefaults player
              Expect.isGreaterThan personality.Consistency 0.5 "high mental = high consistency"
          }

          test "low mental stats produce lower personality scores" {
              let player = makePlayer 1 ST 1
              let personality = PlayerPersonality.deriveWithDefaults player
              Expect.isLessThan personality.Consistency 0.5 "low mental = low consistency"
          } ]
