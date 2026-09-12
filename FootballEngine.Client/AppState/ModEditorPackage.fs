namespace FootballEngine

open System
open System.IO
open System.Text
open System.Text.Json
open FootballEngine.Data

module ModEditorPackage =
    let private zeroHash = String.replicate 64 "0"

    let private json value = JsonSerializer.Serialize value

    let private slug (value: string) =
        value.ToLowerInvariant().Trim().Replace(" ", "-").Replace("/", "-").Replace(".", "")

    let private packageHash (manifestWithZeroHash: string) (files: (string * byte[]) list) =
        let manifestWithoutHash =
            manifestWithZeroHash.Replace($"\"packageSha256\": \"{zeroHash}\",", "")

        let canonicalManifest =
            match ModSystem.canonicalizeJson manifestWithoutHash with
            | Ok value -> value
            | Error error -> failwithf "generated manifest could not be canonicalized: %A" error

        let canonicalFiles =
            files
            |> List.sortBy fst
            |> List.map (fun (path, bytes) -> path + "\n" + ModSystem.hashSha256 bytes)
            |> String.concat "\n"

        ModSystem.hashSha256 (Encoding.UTF8.GetBytes(canonicalManifest + "\n" + canonicalFiles))

    let private fileDeclarations files =
        files
        |> List.sortBy fst
        |> List.map (fun (path, bytes) -> $"{{\"path\":{json path},\"sha256\":\"{ModSystem.hashSha256 bytes}\"}}")
        |> String.concat ","

    let private dependencyDeclarations (dependencies: string list) =
        dependencies
        |> List.map (fun dependency -> $"{{\"modId\":{json dependency},\"range\":\">=0.0.0 <999999.0.0\"}}")
        |> String.concat ","

    let private makeManifest (manifest: ModManifestDto) files =
        $"""{{
  "format": "football-engine.mod",
  "formatVersion": 1,
  "modId": {json manifest.Id},
  "version": {json manifest.Version},
  "engine": {{ "minInclusive": "2026.1.0", "maxExclusive": "2027.0.0" }},
  "api": {{ "content": 1, "scripts": 0 }},
  "namespace": {json manifest.Id},
  "title": {json manifest.Name},
  "authors": [{{ "id": "community.editor", "displayName": {json manifest.Author} }}],
  "description": {json manifest.Description},
  "dependencies": [{dependencyDeclarations manifest.Dependencies}],
  "precedence": {{ "layer": {manifest.Priority}, "after": [], "before": [] }},
  "files": [{fileDeclarations files}],
  "packageSha256": "{zeroHash}",
  "signature": null
}}"""

    let private countryContent (manifestId: string) (data: CountryDataDto) =
        let countryId = $"{manifestId}:country/{data.Country.Code.ToLowerInvariant()}"
        let domain = JsonSerializer.Serialize data

        $"{{\"id\":{json countryId},\"kind\":\"country\",\"schema\":1,\"code\":\"{data.Country.Code}\",\"confederation\":\"{data.Country.Confederation}\",\"nameKey\":\"{countryId}.name\",\"domain\":{domain}}}"

    let private competitionContent (manifestId: string) (data: InternationalCompDto) =
        let competitionId = $"{manifestId}:competition/{slug data.Name}"
        let domain = JsonSerializer.Serialize data

        $"{{\"id\":{json competitionId},\"kind\":\"competition\",\"schema\":1,\"nameKey\":\"{competitionId}.name\",\"domain\":{domain}}}"

    let buildPackageInput
        (manifest: ModManifestDto)
        (countries: Map<string, CountryDataDto>)
        (intlComps: InternationalCompDto list)
        : PackageInput =
        let files = ResizeArray<string * byte[]>()

        for KeyValue(code, data) in countries do
            files.Add(
                $"content/countries/{code.ToLowerInvariant()}.json",
                Encoding.UTF8.GetBytes(countryContent manifest.Id data)
            )

        for competition in intlComps do
            files.Add(
                $"content/competitions/{slug competition.Name}.json",
                Encoding.UTF8.GetBytes(competitionContent manifest.Id competition)
            )

        let fileList = List.ofSeq files
        let manifestWithZeroHash = makeManifest manifest fileList
        let hash = packageHash manifestWithZeroHash fileList

        let manifestJson =
            manifestWithZeroHash.Replace($"\"packageSha256\": \"{zeroHash}\"", $"\"packageSha256\": \"{hash}\"")

        { ManifestJson = manifestJson
          Files = fileList |> Map.ofList
          SymlinkPaths = Set.empty }

    let validateMod
        (manifest: ModManifestDto)
        (countries: Map<string, CountryDataDto>)
        (intlComps: InternationalCompDto list)
        : Result<unit, ModError> =
        buildPackageInput manifest countries intlComps |> ModSystem.validatePackage

    let saveMod
        (basePath: string)
        (manifest: ModManifestDto)
        (countries: Map<string, CountryDataDto>)
        (intlComps: InternationalCompDto list)
        : Result<unit, string> =
        try
            let packageInput = buildPackageInput manifest countries intlComps

            match ModSystem.validatePackage packageInput with
            | Error error -> Error(error.Diagnostics |> List.map _.Message |> String.concat "; ")
            | Ok() ->
                let workspace =
                    Path.Combine(Path.GetTempPath(), "football-engine-editor-" + Guid.NewGuid().ToString("N"))

                Directory.CreateDirectory workspace |> ignore

                try
                    File.WriteAllText(
                        Path.Combine(workspace, "manifest.json"),
                        packageInput.ManifestJson,
                        UTF8Encoding(false)
                    )

                    for relativePath, bytes in packageInput.Files |> Map.toList do
                        let filePath =
                            Path.Combine(workspace, relativePath.Replace('/', Path.DirectorySeparatorChar))

                        let parent = Path.GetDirectoryName filePath

                        if not (String.IsNullOrEmpty parent) then
                            Directory.CreateDirectory parent |> ignore

                        File.WriteAllBytes(filePath, bytes)

                    let safeId = manifest.Id.Replace("/", "_").Replace("\\", "_").Replace("..", "_")

                    let safeVersion =
                        manifest.Version.Replace("/", "_").Replace("\\", "_").Replace("..", "_")

                    let outputPath = Path.Combine(basePath, $"{safeId}-{safeVersion}.femod")
                    Directory.CreateDirectory basePath |> ignore

                    match ModSystem.packWorkspace workspace outputPath with
                    | Ok() -> Ok()
                    | Error error -> Error(error.Diagnostics |> List.map _.Message |> String.concat "; ")
                finally
                    if Directory.Exists workspace then
                        Directory.Delete(workspace, true)
        with ex ->
            Error ex.Message
