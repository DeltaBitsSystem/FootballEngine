namespace FootballEngine.Core.Tests.Contracts

open System
open System.IO
open System.Reflection
open System.Text.Json
open Expecto
open Microsoft.FSharp.Reflection
open FootballEngine.Core.Match

module ConfigTests =

    let rec private walkFields (value: obj) (path: string) (visit: string * obj -> unit) =
        let t = value.GetType()

        if t.IsArray then
            let arr = value :?> System.Array
            visit (path, box arr)
        elif FSharpType.IsRecord(t, true) then
            for f in FSharpType.GetRecordFields(t) do
                walkFields (f.GetValue value) (path + f.Name + ".") visit
        elif t = typeof<string> then
            visit (path.TrimEnd '.', value)
        else
            visit (path.TrimEnd '.', value)

    let private scalarLeaf (value: obj) : obj =
        match value with
        | :? float as v -> box v
        | :? int as v -> box v
        | :? bool as v -> box v
        | :? string as v -> box v
        | :? System.Array as arr ->
            box (
                seq {
                    for i in 0 .. arr.Length - 1 do
                        yield Convert.ToDouble(arr.GetValue i)
                }
                |> Seq.toList
            )
        | v -> box (Convert.ToDouble v)

    let private collectLeaves (cfg: obj) : Map<string, obj> =
        let m = System.Collections.Generic.Dictionary<string, obj>()
        walkFields cfg "" (fun (p, v) -> m.[p] <- scalarLeaf v)
        m |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

    let private leafPaths (cfg: obj) : string list =
        let m = System.Collections.Generic.Dictionary<string, obj>()
        walkFields cfg "" (fun (p, v) -> m.[p] <- v)
        m.Keys |> Seq.toList |> List.sort

    let private roundTripJson (cfg: BalanceConfig) : string =
        JsonSerializer.Serialize(
            cfg,
            JsonSerializerOptions(WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        )

    let configTests =
        testList
            "FootballEngine.Core.Contracts.Config"
            [ test "defaultConfig is structurally sane: no NaN, no negative floats" {
                  let leaves = collectLeaves (box BalanceConfig.defaultConfig)

                  // Legitimately negative by design (mirrors weights.json).
                  let allowNegative =
                      Set.ofList
                          [ "Weights.SetPiece.KickOffPartnerOffsetX"
                            "Weights.Physics.Gravity"
                            "Weights.Physics.ArrivalConvergenceThreshold"
                            "Weights.Manager.MomentumThreshold"
                            "Weights.Decision.PassAttackPhasePenalty" ]

                  for KeyValue(path, value) in leaves do
                      match value with
                      | :? float as f ->
                          Expect.isFalse (Double.IsNaN f) (sprintf "%s must not be NaN" path)

                          if not (allowNegative.Contains path) then
                              Expect.isFalse (f < 0.0) (sprintf "%s must not be negative" path)
                      | :? System.Collections.Generic.List<double> as arr ->
                          for v in arr do
                              Expect.isFalse (Double.IsNaN v) (sprintf "%s must not contain NaN" path)
                      | _ -> ()

                  // probabilities must be in [0,1]
                  for path in
                      [ "Referee.FoulMinorChance"
                        "Referee.FoulTacticalChance"
                        "Referee.FoulYellowWorthyChance"
                        "Referee.FoulRedWorthyChance"
                        "Referee.HomeCardBonus"
                        "Ball.SaveReflexWeight"
                        "Ball.SavePositioningWeight"
                        "Actions.DuelFoulChance"
                        "Actions.GkPunchChance"
                        "Actions.GkParryChance"
                        "Actions.PassHeavyTouchChance"
                        "Actions.DribbleBaseSuccess"
                        "Actions.TackleBaseSuccess"
                        "SetPiece.FreeKickShotChance" ] do
                      match Map.tryFind path leaves with
                      | Some(:? float as v) -> Expect.isTrue (v >= 0.0 && v <= 1.0) (sprintf "%s must be in [0,1]" path)
                      | _ -> failwithf "config field %s missing" path
              }

              test "defaultConfig has all 10 sections" {
                  let t = typeof<BalanceConfig>

                  for section in
                      [ "Ball"
                        "Movement"
                        "Actions"
                        "SetPiece"
                        "Team"
                        "Referee"
                        "Manager"
                        "Environment"
                        "Match"
                        "Weights" ] do
                      Expect.isTrue
                          (FSharpType.GetRecordFields(t) |> Array.exists (fun f -> f.Name = section))
                          (sprintf "section %s must exist" section)
              }

              test "WeightMerge: JSON of defaults round-trips to the same config" {
                  let json = roundTripJson BalanceConfig.defaultConfig

                  match WeightMerge.mergeWithDefaults json BalanceConfig.defaultConfig with
                  | Error err -> failtestf "merge failed: %s" err
                  | Ok merged ->
                      let a = leafPaths (box BalanceConfig.defaultConfig)
                      let b = leafPaths (box merged)
                      Expect.equal b a "same leaf structure"

                      for path in a do
                          let original = (collectLeaves (box BalanceConfig.defaultConfig)).[path]
                          let mergedValue = (collectLeaves (box merged)).[path]
                          Expect.equal mergedValue original (sprintf "leaf %s must survive round-trip" path)
              }

              test "WeightMerge: partial JSON overrides exactly the named leaves" {
                  let json = """{ "actions": { "passBaseQuality": 0.9, "ShotVzBase": 0.7 } }"""

                  match WeightMerge.mergeWithDefaults json BalanceConfig.defaultConfig with
                  | Error err -> failtestf "merge failed: %s" err
                  | Ok merged ->
                      Expect.equal merged.Actions.PassBaseQuality 0.9 "camelCase override must apply"
                      Expect.equal merged.Actions.ShotVzBase 0.7 "mixed-case override must apply"

                      let original = collectLeaves (box BalanceConfig.defaultConfig)
                      let changed = collectLeaves (box merged)

                      let differing =
                          Map.toList original |> List.filter (fun (p, v) -> Map.find p changed <> v)

                      Expect.hasLength differing 2 "exactly two leaves must differ"
              }

              test "WeightMerge: unknown field -> Error naming it" {
                  match WeightMerge.mergeWithDefaults """{ "Bogus": 1.0 }""" BalanceConfig.defaultConfig with
                  | Error msg -> Expect.stringContains msg "Bogus" "error must name the unknown field"
                  | Ok _ -> failtest "unknown top-level field must error"

                  match
                      WeightMerge.mergeWithDefaults """{ "Actions": { "Nope": 1.0 } }""" BalanceConfig.defaultConfig
                  with
                  | Error msg -> Expect.stringContains msg "Nope" "error must name the nested unknown field"
                  | Ok _ -> failtest "unknown nested field must error"
              }

              test "WeightMerge: case-insensitive field names" {
                  match
                      WeightMerge.mergeWithDefaults """{ "bAlL": { "GRAVITY": 5.0 } }""" BalanceConfig.defaultConfig
                  with
                  | Error err -> failtestf "merge failed: %s" err
                  | Ok merged ->
                      Expect.equal merged.Ball.Gravity 5.0<meterPerSecondSquared> "case-insensitive override must apply"
              }

              test "WeightMerge: invalid JSON -> Error" {
                  match WeightMerge.mergeWithDefaults "{ not json" BalanceConfig.defaultConfig with
                  | Error _ -> ()
                  | Ok _ -> failtest "invalid JSON must error"
              }

              test "WeightMerge: load with missing file -> Ok defaults" {
                  match WeightMerge.load "/nonexistent/weights.json" BalanceConfig.defaultConfig with
                  | Ok cfg ->
                      Expect.equal
                          (leafPaths (box cfg))
                          (leafPaths (box BalanceConfig.defaultConfig))
                          "defaults returned"
                  | Error err -> failtestf "missing file must not error: %s" err
              }

              test "weights.json merges over defaults" {
                  // weights.json is the legacy flat section file — the new engine's
                  // config nests it under "Weights". Defaults mirror it 1:1 (P1),
                  // so the assertion is structure: every field is known and typed.
                  let json = sprintf """{ "Weights": %s }""" (File.ReadAllText "weights.json")

                  match WeightMerge.mergeWithDefaults json BalanceConfig.defaultConfig with
                  | Error err -> failtestf "weights.json must merge: %s" err
                  | Ok cfg ->
                      let a = leafPaths (box BalanceConfig.defaultConfig)
                      let b = leafPaths (box cfg)
                      Expect.equal b a "weights.json merge must keep the full leaf structure"
                      Expect.isNonEmpty b "merged config must have leaves"
              }

              test "weights_trained.json is a valid training artifact" {
                  // Trained model output — NOT a BalanceConfig overrides file.
                  let doc = JsonDocument.Parse(File.ReadAllText "weights_trained.json")
                  let root = doc.RootElement
                  Expect.isTrue (root.TryGetProperty "Version" |> fst) "weights_trained.json must have Version"

                  Expect.isTrue
                      (root.TryGetProperty "ProfileWeights" |> fst)
                      "weights_trained.json must have ProfileWeights"

                  doc.Dispose()
              } ]
