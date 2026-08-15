namespace FootballEngine.Data

/// Centralized paths for builtin data and user mods.
module ModPaths =

    let builtinsDir: string =
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "Data", "Builtins")

    let modsDir: string =
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "Mods")
