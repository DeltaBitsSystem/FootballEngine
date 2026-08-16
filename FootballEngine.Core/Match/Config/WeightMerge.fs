namespace FootballEngine.Core.Match

open System
open System.Reflection
open System.Text.Json
open Microsoft.FSharp.Reflection

/// One generic reflection-based merge replaces the hand-written WeightsLoader
/// (2,060 lines): new balance fields get JSON overrides for free, with no
/// per-field parsing code to maintain.
module WeightMerge =

    let private getFields (t: Type) : PropertyInfo[] =
        match FSharpType.GetRecordFields(t) with
        | fields when fields.Length > 0 -> fields
        | _ -> t.GetProperties(BindingFlags.Public ||| BindingFlags.Instance)

    let private findField (fields: PropertyInfo[]) (name: string) : PropertyInfo option =
        fields
        |> Array.tryFind (fun f -> String.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))

    let rec private mergeInto (current: obj) (target: Type) (obj: JsonElement) (path: string) : Result<obj, string> =
        let fields = getFields target

        let findField (name: string) : PropertyInfo option =
            fields
            |> Array.tryFind (fun f -> String.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))

        let applyScalar
            (prop: PropertyInfo)
            (elem: JsonElement)
            (list: (PropertyInfo * obj) list)
            : Result<(PropertyInfo * obj) list, string> =
            let newName = prop.Name
            let t = prop.PropertyType

            if t.IsArray then
                if elem.ValueKind <> JsonValueKind.Array then
                    Error(sprintf "WeightMerge: field '%s%s' must be an array of numbers" path newName)
                else
                    let values =
                        elem.EnumerateArray()
                        |> Seq.map (fun e ->
                            if e.ValueKind = JsonValueKind.Number then
                                Some(e.GetDouble())
                            else
                                None)
                        |> Seq.toArray

                    if values |> Array.exists Option.isNone then
                        Error(sprintf "WeightMerge: field '%s%s' must be an array of numbers" path newName)
                    else
                        let arr = Array.CreateInstance(t.GetElementType(), values.Length)

                        for i = 0 to values.Length - 1 do
                            arr.SetValue(values.[i].Value, i)

                        Ok((prop, box arr) :: list)
            elif t.IsEnum then
                if elem.ValueKind = JsonValueKind.Number then
                    Ok((prop, box (elem.GetInt32())) :: list)
                else
                    Error(sprintf "WeightMerge: enum field '%s%s' must be an integer" path newName)
            elif t = typeof<bool> then
                if elem.ValueKind = JsonValueKind.True || elem.ValueKind = JsonValueKind.False then
                    Ok((prop, box (elem.GetBoolean())) :: list)
                else
                    Error(sprintf "WeightMerge: field '%s%s' must be a boolean" path newName)
            elif t = typeof<int> then
                if elem.ValueKind = JsonValueKind.Number then
                    Ok((prop, box (int (elem.GetDouble()))) :: list)
                else
                    Error(sprintf "WeightMerge: field '%s%s' must be a number" path newName)
            elif t = typeof<float> then
                if elem.ValueKind = JsonValueKind.Number then
                    Ok((prop, box (elem.GetDouble())) :: list)
                else
                    Error(sprintf "WeightMerge: field '%s%s' must be a number" path newName)
            elif t = typeof<string> then
                if elem.ValueKind = JsonValueKind.String then
                    Ok((prop, box (elem.GetString())) :: list)
                else
                    Error(sprintf "WeightMerge: field '%s%s' must be a string" path newName)
            elif t.IsValueType && not t.IsPrimitive then
                // measure-typed scalar (float<meter>, int<subtick>, ...) — measures are erased at runtime
                if elem.ValueKind = JsonValueKind.Number then
                    let value = elem.GetDouble()

                    if t = typeof<int> || t.IsEnum then
                        Ok((prop, box (int value)) :: list)
                    else
                        Ok((prop, box value) :: list)
                else
                    Error(sprintf "WeightMerge: field '%s%s' must be a number" path newName)
            else
                Error(sprintf "WeightMerge: field '%s%s' has unsupported type %s" path newName t.Name)

        let rec applyProperties (acc: Result<(PropertyInfo * obj) list, string>) (e: JsonElement.ObjectEnumerator) =
            let mutable e = e

            if not (e.MoveNext()) then
                acc
            else
                let prop = e.Current
                let field = findField prop.Name

                match field, acc with
                | None, _ -> Error(sprintf "WeightMerge: unknown field '%s%s'" path prop.Name)
                | Some f, Error _ -> acc
                | Some f, Ok list ->
                    let elem = prop.Value

                    if elem.ValueKind = JsonValueKind.Object then
                        match mergeInto (f.GetValue current) f.PropertyType elem (path + f.Name + ".") with
                        | Ok merged -> applyProperties (Ok((f, merged) :: list)) e
                        | Error err -> Error err
                    else
                        match applyScalar f elem list with
                        | Ok updated -> applyProperties (Ok updated) e
                        | Error err -> Error err

        match applyProperties (Ok []) (obj.EnumerateObject()) with
        | Error err -> Error err
        | Ok updated ->
            let values =
                fields
                |> Array.map (fun prop ->
                    updated
                    |> List.tryFind (fun (p, _) -> p = prop)
                    |> Option.map snd
                    |> Option.defaultWith (fun () -> prop.GetValue(current)))

            Ok(FSharpValue.MakeRecord(target, values, true))

    let mergeWithDefaults (json: string) (defaults: BalanceConfig) : Result<BalanceConfig, string> =
        try
            use doc = JsonDocument.Parse(json)

            if doc.RootElement.ValueKind <> JsonValueKind.Object then
                Error "WeightMerge: root must be a JSON object"
            else
                match mergeInto (box defaults) typeof<BalanceConfig> doc.RootElement "" with
                | Ok merged -> Ok(unbox<BalanceConfig> merged)
                | Error err -> Error err
        with ex ->
            Error(sprintf "WeightMerge: invalid JSON: %s" ex.Message)

    /// Missing file -> Ok defaults.
    let load (path: string) (defaults: BalanceConfig) : Result<BalanceConfig, string> =
        if not (System.IO.File.Exists path) then
            Ok defaults
        else
            mergeWithDefaults (System.IO.File.ReadAllText path) defaults
