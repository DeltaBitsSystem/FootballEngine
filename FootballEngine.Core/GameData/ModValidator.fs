namespace FootballEngine.Data

open System
open System.IO
open ModTypes


/// Validates a mod directory and returns a report.
module ModValidator =

    type ValidationResult =
        { Errors: ModError list
          Warnings: string list
          Summary: string }

    let validateCountryData (modId: ModId) (cd: CountryDataDto) : ModError list =
        let errors = ResizeArray<ModError>()

        if String.IsNullOrWhiteSpace cd.Country.Code then
            errors.Add(InvalidCountryData(modId, "unknown", "Country code is required"))
        elif cd.Country.Code.Length <> 3 then
            errors.Add(InvalidCountryData(modId, cd.Country.Code, "Country code must be 3 characters"))

        if String.IsNullOrWhiteSpace cd.Country.Name then
            errors.Add(InvalidCountryData(modId, cd.Country.Code, "Country name is required"))

        if cd.LeagueNames.Length <> cd.LeagueRules.Length then
            errors.Add(
                InvalidCountryData(
                    modId,
                    cd.Country.Code,
                    "Number of league names does not match number of league rules"
                )
            )

        for club in cd.Clubs do
            if String.IsNullOrWhiteSpace club.Name then
                errors.Add(InvalidCountryData(modId, cd.Country.Code, "Club name is required"))

            if club.LeagueLevel < 0 || club.LeagueLevel >= cd.LeagueNames.Length then
                errors.Add(
                    InvalidCountryData(
                        modId,
                        cd.Country.Code,
                        $"Club '{club.Name}' has invalid league level {club.LeagueLevel}"
                    )
                )

        List.ofSeq errors

    let validateModPath (modDir: string) : ValidationResult =
        let errors = ResizeArray<ModError>()
        let warnings = ResizeArray<string>()

        if not (Directory.Exists modDir) then
            errors.Add(InvalidManifest(modDir, "Directory does not exist"))

            { Errors = List.ofSeq errors
              Warnings = []
              Summary = "FAILED" }
        else
            let modId = Path.GetFileName modDir

            // Check mod.json
            match ModLoader.loadManifest modDir with
            | Error e -> errors.Add e
            | Ok manifest ->
                warnings.Add("Mod: " + manifest.Name + " v" + manifest.Version + " by " + manifest.Author)
                warnings.Add("Priority: " + string manifest.Priority)
                warnings.Add("Strategy: " + string manifest.MergeStrategy)

                let deps =
                    if manifest.Dependencies.IsEmpty then
                        "none"
                    else
                        String.concat ", " manifest.Dependencies

                warnings.Add("Dependencies: " + deps)

            // Count JSON files
            let jsonFiles =
                Directory.EnumerateFiles(modDir, "*.json", SearchOption.TopDirectoryOnly)
                |> Seq.filter (fun f -> Path.GetFileName f <> "mod.json")
                |> Seq.toList

            let countriesDir = Path.Combine(modDir, "countries")
            let intlDir = Path.Combine(modDir, "international")

            let extraCountryFiles =
                if Directory.Exists countriesDir then
                    Directory.EnumerateFiles(countriesDir, "*.json", SearchOption.TopDirectoryOnly)
                    |> Seq.toList
                else
                    []

            let extraIntlFiles =
                if Directory.Exists intlDir then
                    Directory.EnumerateFiles(intlDir, "*.json", SearchOption.TopDirectoryOnly)
                    |> Seq.toList
                else
                    []

            let allJsonFiles = jsonFiles @ extraCountryFiles @ extraIntlFiles
            warnings.Add("JSON files: " + string allJsonFiles.Length)

            // Load country files
            let mutable countryCount = 0
            let mutable clubCount = 0
            let mutable leagueCount = 0

            for jf in jsonFiles do
                let fn = Path.GetFileNameWithoutExtension jf

                if fn.StartsWith "country_" then
                    match ModLoader.loadCountryFile jf modId with
                    | Error e -> errors.Add e
                    | Ok cd ->
                        countryCount <- countryCount + 1
                        clubCount <- clubCount + cd.Clubs.Length
                        leagueCount <- leagueCount + cd.LeagueNames.Length
                        warnings.Add("Country: " + cd.Country.Name + " (" + cd.Country.Code + ")")
                        warnings.Add("  Confederation: " + string cd.Country.Confederation)
                        warnings.Add("  Clubs: " + string cd.Clubs.Length)
                        warnings.Add("  Leagues: " + string cd.LeagueNames.Length)
                        warnings.Add("  Cups: " + string cd.Cups.Length)

            for jf in extraCountryFiles do
                let fn = Path.GetFileNameWithoutExtension jf

                if fn.StartsWith "country_" then
                    match ModLoader.loadCountryFile jf modId with
                    | Error e -> errors.Add e
                    | Ok cd ->
                        countryCount <- countryCount + 1
                        clubCount <- clubCount + cd.Clubs.Length
                        leagueCount <- leagueCount + cd.LeagueNames.Length
                        warnings.Add("Country: " + cd.Country.Name + " (" + cd.Country.Code + ")")
                        warnings.Add("  Confederation: " + string cd.Country.Confederation)
                        warnings.Add("  Clubs: " + string cd.Clubs.Length)
                        warnings.Add("  Leagues: " + string cd.LeagueNames.Length)
                        warnings.Add("  Cups: " + string cd.Cups.Length)

            // Load international comp files
            for jf in jsonFiles do
                let fn = Path.GetFileNameWithoutExtension jf

                if fn.StartsWith "intl_" then
                    match ModLoader.loadIntlFile jf modId with
                    | Error e -> errors.Add e
                    | Ok(name, _) -> warnings.Add("International: " + name)

            for jf in extraIntlFiles do
                match ModLoader.loadIntlFile jf modId with
                | Error e -> errors.Add e
                | Ok(name, _) -> warnings.Add("International: " + name)

            // Summary
            warnings.Add ""

            warnings.Add(
                "Total: "
                + string countryCount
                + " countries, "
                + string clubCount
                + " clubs, "
                + string leagueCount
                + " leagues"
            )

            let summary =
                if errors.Count = 0 then
                    "VALID (" + string countryCount + " countries, " + string clubCount + " clubs)"
                else
                    "INVALID (" + string errors.Count + " errors)"

            { Errors = List.ofSeq errors
              Warnings = List.ofSeq warnings
              Summary = summary }
