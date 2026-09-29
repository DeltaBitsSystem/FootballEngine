namespace FootballEngine.Database

open System.Text.Json
open System.Text.Json.Serialization

module JsonCodec =
    let private options =
        let value = JsonSerializerOptions(JsonSerializerDefaults.General)
        value.Converters.Add(JsonFSharpConverter())
        value

    let serialize<'T> (value: 'T) =
        JsonSerializer.Serialize(value, options)

    let deserialize<'T> (json: string) =
        JsonSerializer.Deserialize<'T>(json, options)
