namespace FootballEngine.Data

open System
open System.IO
open System.Text.Json
open FootballEngine.Domain
open ModTypes

/// Scans Builtins/ + Mods/, validates JSON, resolves conflicts, returns LoadedData.
module ModLoader =

    let MaxFileSize = 10L * 1024L * 1024L // 10MB

    // ─── Security ───

    let private isPathSafe (basePath: string) (filePath: string) : bool =
        try
            let fullBase = Path.GetFullPath basePath
            let fullPath = Path.GetFullPath filePath
            fullPath.StartsWith(fullBase, StringComparison.Ordinal)
        with _ ->
            false

    let private checkFileSize (path: string) : Result<int64, ModError> =
        let fi = FileInfo path

        if fi.Length > MaxFileSize then
            Error(FileTooLarge(path, fi.Length, MaxFileSize))
        else
            Ok fi.Length

    // ─── JSON reading ───

    let private checkSchemaVersion (path: string) (json: string) : Result<unit, ModError> =
        try
            use doc = JsonDocument.Parse(json)

            match doc.RootElement.TryGetProperty("SchemaVersion") with
            | true, elem ->
                let version = elem.GetInt32()

                if version > SchemaVersion.Current then
                    Error(SchemaMismatch(path, SchemaVersion.Current, version))
                else
                    Ok()
            | false, _ -> Ok()
        with _ ->
            Ok()

    let private readJsonFile<'T> (path: string) : Result<'T, ModError> =
        match checkFileSize path with
        | Error e -> Error e
        | Ok _ ->
            try
                let json = File.ReadAllText path

                match checkSchemaVersion path json with
                | Error e -> Error e
                | Ok() ->
                    let opts = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
                    Ok(JsonSerializer.Deserialize<'T>(json, opts))
            with ex ->
                Error(InvalidJson(path, ex.Message))

    // ─── Manifest loading ───

    let loadManifest (modDir: string) : Result<ModManifest, ModError> =
        let manifestPath = Path.Combine(modDir, "mod.json")

        if not (File.Exists manifestPath) then
            Error(InvalidManifest(modDir, "mod.json not found"))
        else
            match readJsonFile<ModManifestDto> manifestPath with
            | Error e -> Error e
            | Ok dto ->
                let modId = Path.GetFileName modDir

                let strategy =
                    match dto.MergeStrategy with
                    | Some "replace" -> Replace
                    | Some "append" -> Append
                    | Some "patch" -> Patch
                    | _ -> Replace

                Ok
                    { Id = modId
                      Name = dto.Name
                      Author = dto.Author
                      Version = dto.Version
                      Priority = dto.Priority
                      Dependencies = dto.Dependencies
                      Description = dto.Description
                      MergeStrategy = strategy }

    // ─── Country JSON loading ───

    let loadCountryFile (path: string) (modId: ModId) : Result<CountryData, ModError> =
        match readJsonFile<CountryDataDto> path with
        | Error e -> Error e
        | Ok dto ->
            match JsonConverters.countryDataFromDto dto with
            | Ok cd -> Ok cd
            | Error msg -> Error(InvalidCountryData(modId, dto.Country.Code, msg))

    // ─── International comp JSON loading ───

    let loadIntlFile (path: string) (modId: ModId) : Result<(string * CompetitionType), ModError> =
        match readJsonFile<InternationalCompDto> path with
        | Error e -> Error e
        | Ok dto ->
            match JsonConverters.intlCompFromDto dto with
            | Ok ic -> Ok ic
            | Error msg -> Error(InvalidInternationalComp(modId, msg))

    // ─── Directory scanning ───

    let private scanModDirs (baseDir: string) : string list =
        if not (Directory.Exists baseDir) then
            []
        else
            Directory.EnumerateDirectories baseDir
            |> Seq.filter (fun d -> isPathSafe baseDir d)
            |> Seq.toList

    let private scanJsonFiles (dir: string) : string list =
        if not (Directory.Exists dir) then
            []
        else
            Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            |> Seq.filter (fun f -> isPathSafe dir f)
            |> Seq.toList

    // ─── Load a single mod directory ───

    let private loadModDir
        (modDir: string)
        : Result<(ModManifest * CountryData list * (string * CompetitionType) list), ModError list> =
        let errors = ResizeArray<ModError>()

        match loadManifest modDir with
        | Error e ->
            errors.Add e
            Error(List.ofSeq errors)
        | Ok manifest ->
            let countries = ResizeArray<CountryData>()
            let intlComps = ResizeArray<string * CompetitionType>()
            let countriesDir = Path.Combine(modDir, "countries")
            let intlDir = Path.Combine(modDir, "international")

            if Directory.Exists countriesDir then
                for jsonFile in scanJsonFiles countriesDir do
                    let fn = Path.GetFileNameWithoutExtension jsonFile

                    if fn.StartsWith "country_" then
                        match loadCountryFile jsonFile manifest.Id with
                        | Ok cd -> countries.Add cd
                        | Error e -> errors.Add e
            else
                for jsonFile in scanJsonFiles modDir do
                    let fn = Path.GetFileNameWithoutExtension jsonFile

                    if fn.StartsWith "country_" then
                        match loadCountryFile jsonFile manifest.Id with
                        | Ok cd -> countries.Add cd
                        | Error e -> errors.Add e

            if Directory.Exists intlDir then
                for jsonFile in scanJsonFiles intlDir do
                    match loadIntlFile jsonFile manifest.Id with
                    | Ok ic -> intlComps.Add ic
                    | Error e -> errors.Add e

            for jsonFile in scanJsonFiles modDir do
                let fn = Path.GetFileNameWithoutExtension jsonFile

                if fn.StartsWith "intl_" then
                    match loadIntlFile jsonFile manifest.Id with
                    | Ok ic -> intlComps.Add ic
                    | Error e -> errors.Add e

            if errors.Count > 0 then
                Error(List.ofSeq errors)
            else
                Ok(manifest, List.ofSeq countries, List.ofSeq intlComps)

    // ─── Conflict resolution ───

    let private mergeCountryData (baseCd: CountryData) (otherCd: CountryData) (strategy: MergeStrategy) : CountryData =
        match strategy with
        | Replace -> otherCd
        | Append ->
            { baseCd with
                Names =
                    { FirstNames = baseCd.Names.FirstNames @ otherCd.Names.FirstNames |> List.distinct
                      LastNames = baseCd.Names.LastNames @ otherCd.Names.LastNames |> List.distinct }
                LeagueNames = baseCd.LeagueNames @ otherCd.LeagueNames |> List.distinct
                Clubs = baseCd.Clubs @ otherCd.Clubs
                Cups = baseCd.Cups @ otherCd.Cups }
        | Patch ->
            let mergeNamesPool orig upd =
                { FirstNames =
                    if upd.FirstNames.IsEmpty then
                        orig.FirstNames
                    else
                        upd.FirstNames
                  LastNames =
                    if upd.LastNames.IsEmpty then
                        orig.LastNames
                    else
                        upd.LastNames }

            { baseCd with
                Names = mergeNamesPool baseCd.Names otherCd.Names
                LeagueNames =
                    if otherCd.LeagueNames.IsEmpty then
                        baseCd.LeagueNames
                    else
                        otherCd.LeagueNames
                Clubs =
                    if otherCd.Clubs.IsEmpty then
                        baseCd.Clubs
                    else
                        otherCd.Clubs
                Cups = if otherCd.Cups.IsEmpty then baseCd.Cups else otherCd.Cups }

    let private resolveCountries
        (entries: (ModManifest * CountryData list) list)
        : Map<CountryCode, CountryData> * ModError list =
        let allEntries =
            entries
            |> List.collect (fun (manifest, cds) -> cds |> List.map (fun cd -> (cd.Country.Code, cd, manifest)))

        let grouped = allEntries |> List.groupBy (fun (code, _, _) -> code)

        let errors = ResizeArray<ModError>()
        let resolved = ResizeArray<CountryCode * CountryData>()

        for (code, items) in grouped do
            if items.Length > 1 then
                let sorted = items |> List.sortBy (fun (_, _, m) -> m.Priority)
                let (_, baseCd, _) = sorted[0]
                let mutable result = baseCd

                for (_, cd, manifest) in sorted |> List.tail do
                    result <- mergeCountryData result cd manifest.MergeStrategy

                resolved.Add(code, result)
            else
                let (_, cd, _) = items[0]
                resolved.Add(code, cd)

        Map.ofSeq (Seq.ofList (List.ofSeq resolved)), List.ofSeq errors

    let private resolveIntlComps
        (entries: (ModManifest * (string * CompetitionType) list) list)
        : (string * CompetitionType) list * ModError list =
        let allEntries =
            entries
            |> List.collect (fun (manifest, comps) -> comps |> List.map (fun (name, ct) -> (name, ct, manifest)))

        let grouped = allEntries |> List.groupBy (fun (name, _, _) -> name)

        let errors = ResizeArray<ModError>()
        let resolved = ResizeArray<string * CompetitionType>()

        for (name, items) in grouped do
            if items.Length > 1 then
                let winner = items |> List.maxBy (fun (_, _, m) -> m.Priority)
                let losers = items |> List.filter (fun (_, _, m) -> m.Id <> winner.Item3.Id)

                for (_, _, loser) in losers do
                    errors.Add(DuplicateCompName(name, loser.Id, winner.Item3.Id))

                resolved.Add(name, winner.Item2)
            else
                let (_, ct, _) = items[0]
                resolved.Add(name, ct)

        List.ofSeq resolved, List.ofSeq errors

    // ─── Dependency validation ───

    let private validateDependencies (manifests: ModManifest list) : ModError list =
        let ids = manifests |> List.map _.Id |> Set.ofList

        manifests
        |> List.collect (fun m ->
            m.Dependencies
            |> List.choose (fun dep ->
                if not (ids.Contains dep) then
                    Some(MissingDependency(m.Id, dep))
                else
                    None))

    // ─── Main load function ───

    let loadAll (builtinsDir: string) (modsDir: string) : Result<LoadedData, ModError list> =
        let allErrors = ResizeArray<ModError>()
        let allManifests = ResizeArray<ModManifest>()
        let allCountryEntries = ResizeArray<ModManifest * CountryData list>()
        let allIntlEntries = ResizeArray<ModManifest * (string * CompetitionType) list>()

        // 1. Load builtins
        for modDir in scanModDirs builtinsDir do
            match loadModDir modDir with
            | Error errs -> errs |> List.iter allErrors.Add
            | Ok(manifest, cds, ics) ->
                allManifests.Add manifest
                allCountryEntries.Add(manifest, cds)
                allIntlEntries.Add(manifest, ics)

        // 2. Load user mods
        for modDir in scanModDirs modsDir do
            match loadModDir modDir with
            | Error errs -> errs |> List.iter allErrors.Add
            | Ok(manifest, cds, ics) ->
                allManifests.Add manifest
                allCountryEntries.Add(manifest, cds)
                allIntlEntries.Add(manifest, ics)

        // 3. Resolve conflicts
        let countryMap, countryErrors = resolveCountries (List.ofSeq allCountryEntries)
        countryErrors |> List.iter allErrors.Add

        let intlComps, intlErrors = resolveIntlComps (List.ofSeq allIntlEntries)
        intlErrors |> List.iter allErrors.Add

        // 4. Validate dependencies
        let depErrors = validateDependencies (List.ofSeq allManifests)
        depErrors |> List.iter allErrors.Add

        // 5. Build result
        let manifests = List.ofSeq allManifests

        if allErrors.Count > 0 && countryMap.IsEmpty then
            Error(List.ofSeq allErrors)
        else
            Ok
                { Countries = countryMap
                  InternationalComps = intlComps
                  ActiveMods = manifests
                  Errors = List.ofSeq allErrors }
