namespace FootballEngine.Core.Match

open FootballEngine.Settings

/// Settings-aware wrappers for WeightMerge.mergeWithOverrides. This file is
/// compiled after UserSettings so it can reference GlobalSettings/SaveSettings
/// without creating a cycle (WeightMerge.fs itself stays free of Settings).
module WeightMergeSettings =

    /// Save overrides > Global overrides > defaultConfig. If the chosen
    /// override is invalid (validateAll fails) we fall back to default without
    /// throwing — a corrupt save never crashes the engine.
    let mergeWithOverridesSave (saveOpt: SaveSettings option) : BalanceConfig =
        let saveCfg = saveOpt |> Option.bind (fun s -> s.Simulation.BalanceOverrides)
        WeightMerge.mergeWithOverridesSingle saveCfg

    /// Full priority chain. Global currently has no BalanceOverrides (it stores
    /// Appearance/MatchView/etc.), so globalCfg is always None — kept for
    /// forward compat if Simulation ever moves to Global.
    let mergeWithOverridesGlobalSave (globalOpt: GlobalSettings option) (saveOpt: SaveSettings option) : BalanceConfig =
        let _globalCfg: BalanceConfig option = None
        let saveCfg = saveOpt |> Option.bind (fun s -> s.Simulation.BalanceOverrides)
        // use global param to avoid unused warning if future adds overrides
        let _ = globalOpt
        WeightMerge.mergeWithOverrides _globalCfg saveCfg

    /// Spec-friendly alias: Save option -> BalanceConfig.
    let mergeWithOverridesFromSave = mergeWithOverridesSave

    /// Spec-friendly alias: Global -> Save -> BalanceConfig.
    let mergeWithOverridesFromSettings = mergeWithOverridesGlobalSave
