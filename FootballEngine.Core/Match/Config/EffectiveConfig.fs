namespace FootballEngine.Core.Match

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json

type ConfigError =
    | InvalidWorldConfig of string list
    | InvalidModConfig of id: string * message: string
    | InvalidCareerOverride of message: string

type EffectiveConfig =
    { Balance: BalanceConfig
      Content: string
      Hash: uint64
      Revision: uint64 }

module EffectiveConfig =

    let private jsonOptions =
        JsonSerializerOptions(WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let private serialize (config: BalanceConfig) =
        JsonSerializer.Serialize(config, jsonOptions)

    let private hashContent (content: string) : uint64 =
        let bytes = SHA256.HashData(Encoding.UTF8.GetBytes content)
        let mutable result = 0UL

        for i in 0..7 do
            result <- result ||| (uint64 bytes[i] <<< (i * 8))

        result

    let private make (config: BalanceConfig) : EffectiveConfig =
        let content = serialize config
        let hash = hashContent content

        { Balance = config
          Content = content
          Hash = hash
          Revision = hash }

    let fromBalance (config: BalanceConfig) : Result<EffectiveConfig, ConfigError> =
        match ConfigValidation.validateAll config with
        | Ok() -> Ok(make config)
        | Error errors -> Error(InvalidWorldConfig errors)

    /// Defaults, then mods ordered by stable id, then the career override.
    /// A failed import returns an error; callers can retain their previous value.
    let resolve (mods: (string * string) list) (careerOverride: string option) : Result<EffectiveConfig, ConfigError> =
        let ordered =
            mods
            |> List.sortWith (fun (leftId, _) (rightId, _) -> StringComparer.Ordinal.Compare(leftId, rightId))

        let merged =
            ordered
            |> List.fold
                (fun state (id, json) ->
                    match state with
                    | Error _ -> state
                    | Ok config ->
                        match WeightMerge.mergeWithDefaults json config with
                        | Ok next -> Ok next
                        | Error message -> Error(InvalidModConfig(id, message)))
                (Ok BalanceConfig.defaultConfig)

        match merged, careerOverride with
        | Error error, _ -> Error error
        | Ok config, None -> fromBalance config
        | Ok config, Some json ->
            match WeightMerge.mergeWithDefaults json config with
            | Ok overridden -> fromBalance overridden
            | Error message -> Error(InvalidCareerOverride message)

    let resolveOrPrevious
        (previous: EffectiveConfig option)
        (mods: (string * string) list)
        (careerOverride: string option)
        : EffectiveConfig =
        match resolve mods careerOverride with
        | Ok current -> current
        | Error _ -> previous |> Option.defaultValue (make BalanceConfig.defaultConfig)

    let defaultConfig = make BalanceConfig.defaultConfig
