namespace FootballEngine.Tools

open System
open Microsoft.FSharp.Reflection
open FootballEngine.Types

/// Reads BalanceConfig.defaultConfig via reflection and emits weights.json
/// with ALL current parameter values. Run ONCE to bootstrap the ML pipeline.
module ConfigExtractor =

    let private isFSharpRecord (t: Type) =
        FSharpType.IsRecord(t)

    let private escapeString (s: string) : string =
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\n", "\\n")
         .Replace("\r", "\\r")
         .Replace("\t", "\\t")

    let private formatValue (v: obj) : string =
        match v with
        | null -> "null"
        | :? string as s -> "\"" + escapeString s + "\""
        | :? int as i -> string i
        | :? int64 as i -> string i
        | :? int32 as i -> string i
        | :? int16 as i -> string i
        | :? byte as b -> string b
        | :? uint32 as u -> string u
        | :? float as f -> string f
        | :? float32 as f -> string f
        | :? bool as b -> if b then "true" else "false"
        | _ -> "\"" + escapeString (v.ToString()) + "\""

    let rec private toJson indent (v: obj) : string =
        let tab n = String.replicate n "  "

        match v with
        | null -> "null"

        | :? string as s -> formatValue v

        | :? int
        | :? int64
        | :? int32
        | :? int16
        | :? byte
        | :? uint32
        | :? float
        | :? float32
        | :? bool -> formatValue v

        | :? Array as arr ->
            if arr.Length = 0 then "[]"
            else
                let inner =
                    arr
                    |> Seq.cast<obj>
                    |> Seq.mapi (fun i elem ->
                        let comma = if i < arr.Length - 1 then "," else ""
                        sprintf "\n%s%s%s" (tab (indent + 1)) (toJson (indent + 1) elem) comma)
                    |> String.concat ""
                sprintf "[%s\n%s]" inner (tab indent)

        | _ when isFSharpRecord (v.GetType()) ->
            let fields = FSharpType.GetRecordFields(v.GetType())
            let values = FSharpValue.GetRecordFields(v)

            let fieldsJson =
                fields
                |> Array.zip values
                |> Array.mapi (fun i (val_, field) ->
                    let comma = if i < fields.Length - 1 then "," else ""
                    sprintf "\n%s\"%s\": %s%s" (tab (indent + 1)) field.Name (toJson (indent + 1) val_) comma)
                |> String.concat ""
            sprintf "{%s\n%s}" fieldsJson (tab indent)

        | _ -> formatValue v

    let extractToJson (config: obj) : string =
        if not (isFSharpRecord (config.GetType())) then
            failwith "Config must be an F# record type"

        let fields = FSharpType.GetRecordFields(config.GetType())
        let values = FSharpValue.GetRecordFields(config)

        let sectionsJson =
            fields
            |> Array.zip values
            |> Array.mapi (fun i (val_, field) ->
                let comma = if i < fields.Length - 1 then "," else ""
                sprintf "\n  \"%s\": %s%s" field.Name (toJson 1 val_) comma)
            |> String.concat ""

        sprintf "{%s\n}" sectionsJson

    let saveWeightsJson (config: BalanceConfig) (outputPath: string) : unit =
        let json = extractToJson (box config)
        System.IO.File.WriteAllText(outputPath, json)
        printfn "weights.json written to %s" outputPath

    /// Run extraction from CLI: dotnet fsi Tools/ConfigExtractorRun.fsx
    let run (outputPath: string) : unit =
        printfn "Extracting BalanceConfig.defaultConfig via reflection..."
        saveWeightsJson BalanceConfig.defaultConfig outputPath

        // Count fields for verification
        let json = System.IO.File.ReadAllText outputPath
        let fieldCount =
            System.Text.RegularExpressions.Regex.Matches(json, "\"[A-Za-z_]+\":")
            |> Seq.cast<System.Text.RegularExpressions.Match>
            |> Seq.length

        printfn "Total fields extracted: %d" fieldCount
        printfn "Done."
