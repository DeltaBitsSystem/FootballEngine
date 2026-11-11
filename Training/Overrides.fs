namespace Training

open Microsoft.FSharp.Reflection
open FootballEngine.Core.Match

/// `--set Section.Field=value` overrides on BalanceConfig, so a balance experiment needs no source edit or rebuild.
/// Paths follow the record field names (`Keeper.MaxDive`, `Decision.Shot.ShootMinThreshold`); units of measure are
/// erased at runtime, so values are plain numbers.
module Overrides =

    let rec private setPath (value: obj) (path: string list) (text: string) : Result<obj, string> =
        match path with
        | [] -> Error "empty path"
        | field :: rest ->
            let kind = value.GetType()

            if not (FSharpType.IsRecord(kind, true)) then
                Error $"{kind.Name} is not a record, cannot select '{field}'"
            else
                let fields = FSharpType.GetRecordFields(kind, true)

                match fields |> Array.tryFindIndex (fun p -> p.Name = field) with
                | None ->
                    let names = fields |> Array.map _.Name |> String.concat ", "
                    Error $"{kind.Name} has no field '{field}' (fields: {names})"
                | Some index ->
                    let values = FSharpValue.GetRecordFields(value, true)
                    let current = values[index]

                    let updated =
                        match rest with
                        | [] ->
                            let invariant = System.Globalization.CultureInfo.InvariantCulture

                            match current with
                            | :? float -> Ok(box (System.Double.Parse(text, invariant)))
                            | :? float32 -> Ok(box (System.Single.Parse(text, invariant)))
                            | :? int -> Ok(box (System.Int32.Parse(text, invariant)))
                            | :? bool -> Ok(box (System.Boolean.Parse text))
                            | other -> Error $"{field} is a {other.GetType().Name}; only numbers and booleans can be set"
                        | _ -> setPath current rest text

                    updated
                    |> Result.map (fun next ->
                        values[index] <- next
                        FSharpValue.MakeRecord(kind, values, true))

    let apply (cfg: BalanceConfig) (assignment: string) : Result<BalanceConfig, string> =
        match assignment.Split('=', 2) with
        | [| path; text |] ->
            setPath (box cfg) (path.Split('.') |> List.ofArray) text
            |> Result.map unbox<BalanceConfig>
            |> Result.mapError (fun e -> $"--set {assignment}: {e}")
        | _ -> Error $"--set {assignment}: expected Path.To.Field=value"

    /// Splits `--set` assignments out of the arguments and applies them, rejecting a config the engine would reject.
    let parse (args: string list) : Result<BalanceConfig * string list * string list, string> =
        let rec loop cfg applied rest args =
            match args with
            | "--set" :: assignment :: tail ->
                match apply cfg assignment with
                | Ok next -> loop next (assignment :: applied) rest tail
                | Error e -> Error e
            | head :: tail -> loop cfg applied (head :: rest) tail
            | [] ->
                match ConfigValidation.validateAll cfg with
                | Ok() -> Ok(cfg, List.rev applied, List.rev rest)
                | Error errors -> Error(String.concat "; " errors)

        loop BalanceConfig.defaultConfig [] [] args
