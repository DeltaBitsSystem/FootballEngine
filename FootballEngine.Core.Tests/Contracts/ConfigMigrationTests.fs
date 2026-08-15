namespace FootballEngine.Core.Tests.Contracts

open System
open System.IO
open Expecto
open FootballEngine.Core.Match

module ConfigMigrationTests =

    let private oldSections =
        [ "PerformanceConfig"
          "DuelConfig"
          "ShotConfig"
          "PassConfig"
          "CrossConfig"
          "DribbleConfig"
          "TackleConfig"
          "SetPieceConfig"
          "HomeAdvantageConfig"
          "PhysicsConfig"
          "GKConfig"
          "OutfieldReceptionConfig"
          "TimingConfig"
          "MatchVolumeConfig"
          "ManagerConfig"
          "BuildUpConfig"
          "DecisionConfig"
          "PerceptionConfig"
          "DiagnosticsConfig"
          "MatchSituationConfig"
          "InfluenceConfig" ]

    let private newSections =
        [ "Ball"
          "Movement"
          "Actions"
          "SetPiece"
          "Team"
          "Referee"
          "Manager"
          "Environment"
          "Match"
          "Weights" ]

    let configMigrationTests =
        testList
            "FootballEngine.Core.Contracts.ConfigMigration"
            [ test "docs/CONFIG_MIGRATION.md exists" {
                  Expect.isTrue (File.Exists "docs/CONFIG_MIGRATION.md") "migration doc must exist"
              }

              test "every old section is listed once" {
                  let full = File.ReadAllText "docs/CONFIG_MIGRATION.md"
                  let content = full.Substring(0, full.IndexOf "## Spot-check")

                  for section in oldSections do
                      let count = content.Split([| section |], StringSplitOptions.None).Length - 1

                      Expect.equal count 1 (sprintf "old section %s must appear exactly once in the table" section)
              }

              test "every new section has at least one source" {
                  let content = File.ReadAllText "docs/CONFIG_MIGRATION.md"

                  for section in newSections do
                      let hasSource =
                          content.Contains(sprintf "| `%s`" section)
                          || content.Contains(sprintf "| `%s" section)

                      Expect.isTrue hasSource (sprintf "new section %s must have a mapping row" section)
              }

              test "spot-check: old field -> new field mappings resolve in BalanceConfig" {
                  let content = File.ReadAllText "docs/CONFIG_MIGRATION.md"

                  let mappings =
                      [ "PhysicsConfig.Gravity", "Ball.Gravity"
                        "PhysicsConfig.MaxShotSpeed", "Ball.MaxShotSpeed"
                        "PassConfig.BaseMean", "Weights.Pass.BaseMean"
                        "ShotConfig.QualityGate", "Weights.Shot.QualityGate"
                        "DuelConfig.DuelSteepness", "Weights.Duel.DuelSteepness"
                        "SetPieceConfig.WallDistance", "SetPiece.WallDistance"
                        "ManagerConfig.MaxSubs", "Manager.MaxSubs"
                        "HomeAdvantageConfig.CrowdSupport", "Environment.CrowdInfluence"
                        "FoulAnalysis.YellowThreshold", "Referee.FoulYellowWorthyChance" ]

                  for oldField, newField in mappings do
                      Expect.stringContains
                          content
                          (sprintf "| `%s`" oldField)
                          (sprintf "old field %s must appear in the table" oldField)

                      let parts = newField.Split('.')
                      let section = parts.[0]
                      let field = parts.[1]

                      let sectionField =
                          typeof<BalanceConfig>.GetProperty section
                          |> Option.ofObj
                          |> Option.map _.PropertyType
                          |> Option.bind (fun t -> t.GetProperty field |> Option.ofObj)
                          |> Option.isSome

                      Expect.isTrue sectionField (sprintf "new field %s must exist in BalanceConfig" newField)
              } ]
