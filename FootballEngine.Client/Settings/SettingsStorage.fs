namespace FootballEngine

open System
open System.IO
open FootballEngine.Settings
open FootballEngine.Core.Match

module SettingsStorage =

    let settingsFilePath () : string =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "football_engine_settings.json"
        )

    let loadGlobalAsync () : Async<Result<GlobalSettings, string>> =
        async {
            try
                let path = settingsFilePath ()

                if not (File.Exists path) then
                    return Ok GlobalSettings.defaults
                else
                    try
                        let json = File.ReadAllText path

                        if String.IsNullOrWhiteSpace json then
                            return Ok GlobalSettings.defaults
                        else
                            match GlobalSettings.fromJson json with
                            | Error _ ->
                                // corrupt JSON -> Ok defaults, no crash (0 users, file can be deleted)
                                return Ok GlobalSettings.defaults
                            | Ok parsed ->
                                match GlobalSettings.validate parsed with
                                | Error _ ->
                                    // invalid values (e.g. FontScale out of range) -> defaults, no crash
                                    return Ok GlobalSettings.defaults
                                | Ok valid ->
                                    // GlobalSettings currently has no BalanceOverrides, so ConfigValidation.validateAll
                                    // is not needed here; kept for forward compat if Simulation moves to Global.
                                    // If it had overrides, we would delegate to ConfigValidation.validateAll.
                                    return Ok valid
                    with _ ->
                        // file read or deserialization threw -> defaults, no crash
                        return Ok GlobalSettings.defaults
            with _ ->
                // outer guard: never throw, return defaults
                return Ok GlobalSettings.defaults
        }

    let saveGlobalAsync (settings: GlobalSettings) : Async<Result<unit, string>> =
        async {
            // validate before persisting — why: avoid writing corrupt config that breaks next launch
            match GlobalSettings.validate settings with
            | Error errs -> return Error(String.concat "; " errs)
            | Ok _ ->
                // If overrides existed, ConfigValidation.validateAll would be checked here as well.
                // GlobalSettings has no BalanceOverrides currently, so no extra check needed.
                try
                    let path = settingsFilePath ()
                    let json = GlobalSettings.toJson settings

                    let dir = Path.GetDirectoryName path

                    if not (String.IsNullOrEmpty dir) && not (Directory.Exists dir) then
                        Directory.CreateDirectory dir |> ignore

                    // atomic write — why: crash mid-write would corrupt main file; temp+Move is atomic
                    let tempPath = path + ".tmp"
                    File.WriteAllText(tempPath, json)

                    try
                        // .NET 8+ overwrite overload — atomic replace
                        File.Move(tempPath, path, true)
                    with _ ->
                        // fallback for platforms without overwrite overload
                        if File.Exists path then
                            File.Delete path

                        File.Move(tempPath, path)

                    return Ok()
                with ex ->
                    return Error ex.Message
        }
