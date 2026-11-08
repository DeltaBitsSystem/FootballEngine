namespace FootballEngine.Client.Match3D

open FootballEngine.Settings

/// The player's 3D quality tier, published by the app whenever global settings load or change. The renderer reads
/// it when a match viewer opens, so a new tier applies from the next match view without restarting the game.
module ViewerQualitySelection =
    let mutable private tier = ViewerQualityTier.Ultra

    let select (selected: ViewerQualityTier) = tier <- selected

    let current () = tier
