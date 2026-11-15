namespace FootballEngine

open System
open System.IO
open System.IO.Compression
open System.Text
open System.Text.Json
open FootballEngine.Data
open FootballEngine.ModEditorTypes

module ModEditorPackage =
    let private zeroHash = String.replicate 64 "0"

    let private json value = JsonSerializer.Serialize value

    let slug (value: string) =
        let builder = StringBuilder()

        for ch in value.ToLowerInvariant().Trim() do
            if Char.IsLetterOrDigit ch && ch < '\u0080' then
                builder.Append ch |> ignore
            elif builder.Length > 0 && builder.[builder.Length - 1] <> '-' then
                builder.Append '-' |> ignore

        let text = builder.ToString().Trim('-')
        if text = "" then "item" else text

    let private editorError code message action : ModDiagnostic =
        { Code = code
          ModId = None
          Version = None
          RelativePath = None
          JsonPath = None
          EntityId = None
          Message = message
          SuggestedAction = action }

    // ─── Normalisation ──────────────────────────────────────────────────────
    // Package JSON written by hand or by older tools may omit lists; System.Text.Json then yields
    // null F# lists, which crash the first List.length in the view.

    let private orEmpty (values: 'a list) =
        if isNull (box values) then [] else values

    let normalizeClub (club: ClubEntryDto) =
        { club with
            Players = orEmpty club.Players }

    let normalizeCountry (country: CountryDataDto) =
        { country with
            Names =
                { FirstNames =
                    orEmpty (
                        if isNull (box country.Names) then
                            []
                        else
                            country.Names.FirstNames
                    )
                  LastNames =
                    orEmpty (
                        if isNull (box country.Names) then
                            []
                        else
                            country.Names.LastNames
                    ) }
            LeagueNames = orEmpty country.LeagueNames
            LeagueRules =
                orEmpty country.LeagueRules
                |> List.map (fun rule ->
                    { rule with
                        Tiebreakers = orEmpty rule.Tiebreakers
                        Promotion = orEmpty rule.Promotion
                        Relegation = orEmpty rule.Relegation })
            Clubs = orEmpty country.Clubs |> List.map normalizeClub
            Cups = orEmpty country.Cups }

    let normalizeCompetition (competition: InternationalCompDto) =
        { competition with
            Qualification = orEmpty competition.Qualification }

    // ─── Writing ────────────────────────────────────────────────────────────

    let private packageHash (manifestWithZeroHash: string) (files: (string * byte[]) list) =
        let manifestWithoutHash =
            manifestWithZeroHash.Replace($"\"packageSha256\": \"{zeroHash}\",", "")

        ModParsing.canonicalizeJson manifestWithoutHash
        |> Result.map (fun canonicalManifest ->
            let canonicalFiles =
                files
                |> List.sortBy fst
                |> List.map (fun (path, bytes) -> path + "\n" + ModParsing.hashSha256 bytes)
                |> String.concat "\n"

            ModParsing.hashSha256 (Encoding.UTF8.GetBytes(canonicalManifest + "\n" + canonicalFiles)))

    let private fileDeclarations files =
        files
        |> List.sortBy fst
        |> List.map (fun (path, bytes) -> $"{{\"path\":{json path},\"sha256\":\"{ModParsing.hashSha256 bytes}\"}}")
        |> String.concat ","

    /// Patched packages must load first, so every source mod is both a dependency and an ordering edge.
    let dependenciesOf (document: EditorDocument) =
        let sourceMods =
            Seq.append document.Sources.Values document.CompetitionSources.Values
            |> Seq.map _.ModId
            |> List.ofSeq

        (document.Manifest.Dependencies @ sourceMods)
        |> List.filter (fun dependency -> dependency <> document.Manifest.Id)
        |> List.distinct
        |> List.sort

    let private makeManifest (document: EditorDocument) files =
        let manifest = document.Manifest
        let dependencies = dependenciesOf document

        let dependencyDeclarations =
            dependencies
            |> List.map (fun dependency -> $"{{\"modId\":{json dependency},\"range\":\">=0.0.0 <999999.0.0\"}}")
            |> String.concat ","

        let after = dependencies |> List.map json |> String.concat ","

        let author =
            if String.IsNullOrWhiteSpace manifest.Author then
                "Anonymous"
            else
                manifest.Author

        $"""{{
  "format": "football-engine.mod",
  "formatVersion": 1,
  "modId": {json manifest.Id},
  "version": {json manifest.Version},
  "engine": {{ "minInclusive": "2026.1.0", "maxExclusive": "2027.0.0" }},
  "api": {{ "content": 1, "scripts": 0 }},
  "namespace": {json manifest.Id},
  "title": {json manifest.Name},
  "authors": [{{ "id": "community.editor", "displayName": {json author} }}],
  "description": {json manifest.Description},
  "dependencies": [{dependencyDeclarations}],
  "precedence": {{ "layer": {manifest.Priority}, "after": [{after}], "before": [] }},
  "files": [{fileDeclarations files}],
  "packageSha256": "{zeroHash}",
  "signature": null
}}"""

    let private countryEntity (countryId: string) (data: CountryDataDto) =
        $"{{\"id\":{json countryId},\"kind\":\"country\",\"schema\":1,\"code\":{json data.Country.Code},\"confederation\":{json data.Country.Confederation},\"nameKey\":\"{countryId}.name\",\"domain\":{json data}}}"

    let private competitionEntity (competitionId: string) (data: InternationalCompDto) =
        $"{{\"id\":{json competitionId},\"kind\":\"competition\",\"schema\":1,\"nameKey\":\"{competitionId}.name\",\"domain\":{json data}}}"

    /// A resolved replace swaps the whole target entity, so the value repeats the target's identity
    /// and translation key with the edited domain; the empty pointer says exactly that.
    let private patchContent (source: CountrySource) (entity: string) =
        $"{{\"target\":{json source.ContentId},\"kind\":\"patch\",\"schema\":1,\"operations\":[{{\"op\":\"replace\",\"path\":\"\",\"value\":{entity}}}]}}"

    let buildPackageInput (document: EditorDocument) : Result<PackageInput, ModError> =
        let manifestId = document.Manifest.Id
        let files = ResizeArray<string * byte[]>()

        for KeyValue(code, data) in document.Countries do
            let fileName = code.ToLowerInvariant()

            match document.Sources |> Map.tryFind code with
            | Some source ->
                files.Add(
                    $"content/patches/country-{fileName}.json",
                    Encoding.UTF8.GetBytes(patchContent source (countryEntity source.ContentId data))
                )
            | None ->
                let countryId = $"{manifestId}:country/{fileName}"
                files.Add($"content/countries/{fileName}.json", Encoding.UTF8.GetBytes(countryEntity countryId data))

        for competition in document.InternationalComps do
            let fileName = slug competition.Name

            match document.CompetitionSources |> Map.tryFind competition.Name with
            | Some source ->
                files.Add(
                    $"content/patches/competition-{fileName}.json",
                    Encoding.UTF8.GetBytes(patchContent source (competitionEntity source.ContentId competition))
                )
            | None ->
                files.Add(
                    $"content/competitions/{fileName}.json",
                    Encoding.UTF8.GetBytes(competitionEntity $"{manifestId}:competition/{fileName}" competition)
                )

        let fileList = List.ofSeq files
        let manifestWithZeroHash = makeManifest document fileList

        match packageHash manifestWithZeroHash fileList with
        | Error error -> Error error
        | Ok hash ->
            let manifestJson =
                manifestWithZeroHash.Replace($"\"packageSha256\": \"{zeroHash}\"", $"\"packageSha256\": \"{hash}\"")

            Ok
                { ManifestJson = manifestJson
                  Files = fileList |> Map.ofList
                  SymlinkPaths = Set.empty }

    let private safeName (value: string) =
        value.Replace("/", "_").Replace("\\", "_").Replace("..", "_")

    let fileNameFor (manifest: ModManifestDto) =
        $"{safeName manifest.Id}-{safeName manifest.Version}.femod"

    let private writeFemod (packageInput: PackageInput) (outputPath: string) : Result<unit, ModError> =
        let workspace =
            Path.Combine(Path.GetTempPath(), "football-engine-editor-" + Guid.NewGuid().ToString("N"))

        try
            try
                Directory.CreateDirectory workspace |> ignore

                File.WriteAllText(
                    Path.Combine(workspace, "manifest.json"),
                    packageInput.ManifestJson,
                    UTF8Encoding(false)
                )

                for relativePath, bytes in packageInput.Files |> Map.toList do
                    let filePath =
                        Path.Combine(workspace, relativePath.Replace('/', Path.DirectorySeparatorChar))

                    Directory.CreateDirectory(Path.GetDirectoryName filePath) |> ignore
                    File.WriteAllBytes(filePath, bytes)

                Directory.CreateDirectory(Path.GetDirectoryName outputPath) |> ignore
                ModPackageFile.packWorkspace workspace outputPath
            with ex ->
                Error
                    { Diagnostics =
                        [ editorError "EDITOR-IO" ex.Message "Check that the Mods folder is writable and try again." ] }
        finally
            if Directory.Exists workspace then
                Directory.Delete(workspace, true)

    // ─── Reading ────────────────────────────────────────────────────────────

    let private tryProperty (element: JsonElement) (name: string) =
        match element.ValueKind with
        | JsonValueKind.Object ->
            match element.TryGetProperty name with
            | true, value -> Some value
            | _ -> None
        | _ -> None

    let private stringProperty element name =
        tryProperty element name
        |> Option.bind (fun value ->
            if value.ValueKind = JsonValueKind.String then
                Some(value.GetString())
            else
                None)
        |> Option.defaultValue ""

    type private PackageView =
        { Manifest: ModManifestDto
          Document: EditorDocument
          CountryIds: Map<string, string>
          CompetitionIds: Map<string, string> }

    let private readPackageView (path: string) : Result<PackageView, string> =
        try
            use archive = ZipFile.OpenRead path

            let readEntry (entry: ZipArchiveEntry) =
                use stream = entry.Open()
                use reader = new StreamReader(stream, Encoding.UTF8)
                reader.ReadToEnd()

            match archive.Entries |> Seq.tryFind (fun entry -> entry.FullName = "manifest.json") with
            | None -> Error "The package has no manifest.json."
            | Some manifestEntry ->
                use manifestDoc = JsonDocument.Parse(readEntry manifestEntry)
                let root = manifestDoc.RootElement

                let author =
                    tryProperty root "authors"
                    |> Option.bind (fun authors ->
                        if authors.ValueKind = JsonValueKind.Array && authors.GetArrayLength() > 0 then
                            Some(stringProperty (authors.[0]) "displayName")
                        else
                            None)
                    |> Option.defaultValue ""

                let dependencies =
                    tryProperty root "dependencies"
                    |> Option.filter (fun value -> value.ValueKind = JsonValueKind.Array)
                    |> Option.map (fun value ->
                        value.EnumerateArray()
                        |> Seq.map (fun d -> stringProperty d "modId")
                        |> List.ofSeq)
                    |> Option.defaultValue []

                let priority =
                    tryProperty root "precedence"
                    |> Option.bind (fun value -> tryProperty value "layer")
                    |> Option.filter (fun value -> value.ValueKind = JsonValueKind.Number)
                    |> Option.map (fun value -> value.GetInt32())
                    |> Option.defaultValue 100

                let manifest: ModManifestDto =
                    { Id = stringProperty root "modId"
                      Name = stringProperty root "title"
                      Author = author
                      Version = stringProperty root "version"
                      Priority = priority
                      Dependencies = dependencies
                      Description = stringProperty root "description"
                      MergeStrategy = None }

                let mutable countries = Map.empty
                let mutable competitions = []
                let mutable sources = Map.empty
                let mutable competitionSources = Map.empty
                let mutable countryIds = Map.empty
                let mutable competitionIds = Map.empty

                let addDomain (kind: string) (domain: JsonElement) (contentId: string) (patchTarget: string option) =
                    let source =
                        patchTarget
                        |> Option.map (fun target ->
                            { ModId =
                                (if target.Contains ':' then
                                     target.Substring(0, target.IndexOf ':')
                                 else
                                     target)
                              ContentId = target })

                    match kind with
                    | "country" ->
                        let country =
                            JsonSerializer.Deserialize<CountryDataDto>(domain.GetRawText())
                            |> normalizeCountry

                        let code = country.Country.Code
                        countries <- countries |> Map.add code country
                        countryIds <- countryIds |> Map.add code contentId
                        source |> Option.iter (fun value -> sources <- sources |> Map.add code value)
                    | "competition" ->
                        let competition =
                            JsonSerializer.Deserialize<InternationalCompDto>(domain.GetRawText())
                            |> normalizeCompetition

                        competitions <- competitions @ [ competition ]
                        competitionIds <- competitionIds |> Map.add competition.Name contentId

                        source
                        |> Option.iter (fun value ->
                            competitionSources <- competitionSources |> Map.add competition.Name value)
                    | _ -> ()

                let contentEntries =
                    archive.Entries
                    |> Seq.filter (fun entry -> entry.FullName.StartsWith "content/" && entry.FullName.EndsWith ".json")
                    |> Seq.sortBy _.FullName
                    |> List.ofSeq

                for entry in contentEntries do
                    use content = JsonDocument.Parse(readEntry entry)
                    let element = content.RootElement

                    match stringProperty element "kind" with
                    | "patch" ->
                        let target = stringProperty element "target"
                        // Only whole-entity replacements map back to editable data; finer patches are kept out
                        // of the editor rather than shown as if they were the full entity.
                        let kind =
                            if target.Contains ":country/" then "country"
                            elif target.Contains ":competition/" then "competition"
                            else ""

                        tryProperty element "operations"
                        |> Option.filter (fun ops -> ops.ValueKind = JsonValueKind.Array)
                        |> Option.iter (fun ops ->
                            for op in ops.EnumerateArray() do
                                if stringProperty op "op" = "replace" then
                                    tryProperty op "value"
                                    |> Option.bind (fun entity -> tryProperty entity "domain")
                                    |> Option.iter (fun domain -> addDomain kind domain target (Some target)))
                    | kind ->
                        tryProperty element "domain"
                        |> Option.iter (fun domain -> addDomain kind domain (stringProperty element "id") None)

                Ok
                    { Manifest = manifest
                      Document =
                        { Manifest = manifest
                          Countries = countries
                          InternationalComps = competitions
                          Sources = sources
                          CompetitionSources = competitionSources }
                      CountryIds = countryIds
                      CompetitionIds = competitionIds }
        with ex ->
            Error ex.Message

    let private isBuiltinPath (path: string) =
        Path.GetFullPath(path).StartsWith(Path.GetFullPath ModPaths.builtinsDir, StringComparison.Ordinal)

    let private listFemods dir =
        if Directory.Exists dir then
            Directory.GetFiles(dir, "*.femod", SearchOption.TopDirectoryOnly)
            |> Array.sort
            |> List.ofArray
        else
            []

    let scanLibrary () : LibraryEntry list * string list =
        let errors = ResizeArray<string>()

        let entries =
            (listFemods ModPaths.builtinsDir @ listFemods ModPaths.modsDir)
            |> List.choose (fun path ->
                match readPackageView path with
                | Error error ->
                    errors.Add($"{Path.GetFileName path}: {error}")
                    None
                | Ok view ->
                    let clubs =
                        view.Document.Countries.Values
                        |> Seq.sumBy (fun country -> country.Clubs.Length)

                    let players =
                        view.Document.Countries.Values
                        |> Seq.sumBy (fun country -> country.Clubs |> List.sumBy (fun club -> club.Players.Length))

                    Some
                        { Path = path
                          ModId = view.Manifest.Id
                          Title =
                            (if view.Manifest.Name = "" then
                                 view.Manifest.Id
                             else
                                 view.Manifest.Name)
                          Version = view.Manifest.Version
                          Author = view.Manifest.Author
                          IsBuiltin = isBuiltinPath path
                          Countries = view.Document.Countries.Count
                          Clubs = clubs
                          Players = players
                          Competitions = view.Document.InternationalComps.Length
                          CountryNames =
                            view.Document.Countries
                            |> Map.toList
                            |> List.map (fun (code, country) -> code, country.Country.Name) })

        entries, List.ofSeq errors

    /// Opens a package for editing. A built-in package is never edited in place: it becomes a new mod
    /// whose entities override the originals, so the shipped data stays intact and the change is removable.
    let openPackage (path: string) (asOverride: bool) : Result<EditorDocument, string> =
        readPackageView path
        |> Result.map (fun view ->
            if not asOverride then
                view.Document
            else
                let baseName = slug view.Manifest.Name

                { Manifest =
                    { view.Manifest with
                        Id = $"community.{baseName}-edit"
                        Name = $"{view.Manifest.Name} (edit)"
                        Author = ""
                        Version = "1.0.0"
                        Dependencies = [] }
                  Countries = view.Document.Countries
                  InternationalComps = view.Document.InternationalComps
                  Sources =
                    view.CountryIds
                    |> Map.map (fun code contentId ->
                        view.Document.Sources
                        |> Map.tryFind code
                        |> Option.defaultValue
                            { ModId = view.Manifest.Id
                              ContentId = contentId })
                  CompetitionSources =
                    view.CompetitionIds
                    |> Map.map (fun name contentId ->
                        view.Document.CompetitionSources
                        |> Map.tryFind name
                        |> Option.defaultValue
                            { ModId = view.Manifest.Id
                              ContentId = contentId }) })

    /// One country taken from another package, as an override of that package's entity.
    let importCountry (path: string) (code: string) : Result<CountryDataDto * CountrySource, string> =
        readPackageView path
        |> Result.bind (fun view ->
            match view.Document.Countries |> Map.tryFind code, view.CountryIds |> Map.tryFind code with
            | Some country, Some contentId ->
                let source =
                    view.Document.Sources
                    |> Map.tryFind code
                    |> Option.defaultValue
                        { ModId = view.Manifest.Id
                          ContentId = contentId }

                Ok(country, source)
            | _ -> Error $"Country {code} is not in {Path.GetFileName path}.")

    // ─── Validation and publishing ───────────────────────────────────────────

    let private modsDependingOn (document: EditorDocument) (library: LibraryEntry list) =
        let wanted = dependenciesOf document |> Set.ofList

        library
        |> List.filter (fun entry ->
            not entry.IsBuiltin
            && wanted.Contains entry.ModId
            && entry.ModId <> document.Manifest.Id)

    /// Validates the way the game loads: the package is built, resolved with the built-ins and its
    /// dependencies, and materialised into world data. Errors that only appear at career start are caught here.
    let validate (document: EditorDocument) (library: LibraryEntry list) : ModDiagnostic list =
        match buildPackageInput document with
        | Error error -> error.Diagnostics
        | Ok packageInput ->
            match ModContent.validatePackage packageInput with
            | Error error -> error.Diagnostics
            | Ok() ->
                let stage =
                    Path.Combine(Path.GetTempPath(), "football-engine-validate-" + Guid.NewGuid().ToString("N"))

                try
                    try
                        Directory.CreateDirectory stage |> ignore

                        for dependency in modsDependingOn document library do
                            File.Copy(dependency.Path, Path.Combine(stage, Path.GetFileName dependency.Path))

                        match writeFemod packageInput (Path.Combine(stage, fileNameFor document.Manifest)) with
                        | Error error -> error.Diagnostics
                        | Ok() ->
                            match
                                ModPackageFile.loadInstalledPackages
                                    ModPaths.builtinsDir
                                    stage
                                    [ document.Manifest.Id ]
                                    []
                            with
                            | Error error -> error.Diagnostics
                            | Ok resolved ->
                                match ResolvedWorldData.materialize resolved with
                                | Error error -> error.Diagnostics
                                | Ok _ -> []
                    with ex ->
                        [ editorError "EDITOR-IO" ex.Message "Check disk space and permissions, then validate again." ]
                finally
                    if Directory.Exists stage then
                        Directory.Delete(stage, true)

    let publish (document: EditorDocument) : Result<string, ModDiagnostic list> =
        match buildPackageInput document with
        | Error error -> Error error.Diagnostics
        | Ok packageInput ->
            let outputPath = Path.Combine(ModPaths.modsDir, fileNameFor document.Manifest)

            match writeFemod packageInput outputPath with
            | Ok() -> Ok outputPath
            | Error error -> Error error.Diagnostics

    // ─── Draft ──────────────────────────────────────────────────────────────

    let private draftPath =
        Path.Combine(
            Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData,
            "FootballEngine",
            "mod-editor",
            "draft.json"
        )

    /// Screenshot runs drive the editor with demo data; they must never replace the author's real draft.
    let mutable draftsEnabled = true

    let hasDraft () = draftsEnabled && File.Exists draftPath

    let saveDraft (document: EditorDocument) : Result<unit, string> =
        if not draftsEnabled then
            Ok()
        else
            try
                Directory.CreateDirectory(Path.GetDirectoryName draftPath) |> ignore
                let temporary = draftPath + ".tmp"
                File.WriteAllText(temporary, FootballEngine.Database.JsonCodec.serialize document, UTF8Encoding(false))
                File.Move(temporary, draftPath, true)
                Ok()
            with ex ->
                Error ex.Message

    let loadDraft () : Result<EditorDocument, string> =
        try
            let document =
                FootballEngine.Database.JsonCodec.deserialize<EditorDocument> (File.ReadAllText draftPath)

            Ok
                { document with
                    Countries = document.Countries |> Map.map (fun _ country -> normalizeCountry country)
                    InternationalComps = document.InternationalComps |> List.map normalizeCompetition }
        with ex ->
            Error ex.Message
