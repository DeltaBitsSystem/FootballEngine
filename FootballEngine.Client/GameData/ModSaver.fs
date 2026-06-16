namespace FootballEngine.Data

open System.IO
open System.Text.Json

module ModSaver =

    let private options = JsonSerializerOptions(WriteIndented = true)

    let saveMod
        (basePath: string)
        (manifest: ModManifestDto)
        (countries: Map<string, CountryDataDto>)
        (intlComps: InternationalCompDto list)
        =
        try
            let modDir = Path.Combine(basePath, manifest.Name)

            if not (Directory.Exists modDir) then
                Directory.CreateDirectory modDir |> ignore

            // Save manifest
            let manifestPath = Path.Combine(modDir, "mod.json")
            let manifestJson = JsonSerializer.Serialize(manifest, options)
            File.WriteAllText(manifestPath, manifestJson)

            // Save countries
            let countriesDir = Path.Combine(modDir, "countries")

            if not (Directory.Exists countriesDir) then
                Directory.CreateDirectory countriesDir |> ignore

            for KeyValue(code, data) in countries do
                let countryPath = Path.Combine(countriesDir, $"country_{code}.json")
                let json = JsonSerializer.Serialize(data, options)
                File.WriteAllText(countryPath, json)

            // Save international competitions
            let intlDir = Path.Combine(modDir, "international")

            if not (Directory.Exists intlDir) then
                Directory.CreateDirectory intlDir |> ignore

            for dto in intlComps do
                let safeName = dto.Name.Replace(" ", "_").Replace("/", "_")
                let intlPath = Path.Combine(intlDir, $"intl_{safeName}.json")
                let json = JsonSerializer.Serialize(dto, options)
                File.WriteAllText(intlPath, json)

            Ok()
        with ex ->
            Error ex.Message
