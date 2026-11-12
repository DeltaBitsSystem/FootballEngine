namespace FootballEngine.Data

open System
open System.Collections.Generic
open System.IO.Compression
open System.Globalization
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open FootballEngine.Domain
open FootballEngine.Data.ModPrimitives

/// Manifest parsing and canonical JSON: the bytes a package hash is computed over.
module ModParsing =
    let rec internal parseManifestDocument (json: string) : Result<ParsedManifest, ModError> =
        let diagnostics = ResizeArray<ModDiagnostic>()
        let relativePath = Some "manifest.json"
        let mutable success: Result<ParsedManifest, ModError> option = None

        let add code jsonPath message action =
            addDiagnostic diagnostics code None None relativePath jsonPath None message action

        try
            use document =
                JsonDocument.Parse(
                    json,
                    JsonDocumentOptions(
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 128
                    )
                )

            let root = document.RootElement

            if root.ValueKind <> JsonValueKind.Object then
                add
                    "MOD-MANIFEST-JSON"
                    None
                    "manifest.json must contain a JSON object"
                    "Replace the manifest with a valid JSON object."
            else
                let properties = root.EnumerateObject() |> Seq.toList

                let allowedTop =
                    set
                        [ "format"
                          "formatVersion"
                          "modId"
                          "version"
                          "engine"
                          "api"
                          "namespace"
                          "title"
                          "authors"
                          "description"
                          "dependencies"
                          "precedence"
                          "files"
                          "packageSha256"
                          "signature" ]

                for property in properties do
                    if not (Set.contains property.Name allowedTop) then
                        add
                            "MOD-MANIFEST-UNKNOWN-KEY"
                            (Some(appendJsonPath "" property.Name))
                            ("Unknown manifest key '" + property.Name + "'.")
                            "remove the unknown manifest key."

                properties
                |> List.groupBy (fun property -> property.Name)
                |> List.iter (fun (name, occurrences) ->
                    if occurrences.Length > 1 then
                        add
                            "MOD-MANIFEST-DUPLICATE-KEY"
                            (Some(appendJsonPath "" name))
                            ("Manifest key '" + name + "' occurs more than once.")
                            "remove duplicate manifest keys.")

                let findProperty name =
                    properties |> List.tryFind (fun property -> property.Name = name)

                let required name =
                    match findProperty name with
                    | Some property -> Some property.Value
                    | None ->
                        add
                            "MOD-MANIFEST-MISSING-FIELD"
                            (Some(appendJsonPath "" name))
                            ("Required manifest field '" + name + "' is missing.")
                            ("Add the required manifest field '" + name + "'.")

                        None

                let readString (path: string) (element: JsonElement) =
                    if element.ValueKind = JsonValueKind.String then
                        Some(element.GetString())
                    else
                        add
                            "MOD-MANIFEST-TYPE"
                            (Some path)
                            "Manifest field must be a string."
                            "Use a JSON string for this field."

                        None

                let readInt (path: string) (element: JsonElement) =
                    if element.ValueKind = JsonValueKind.Number then
                        let mutable value = 0

                        if element.TryGetInt32(&value) then
                            Some value
                        else
                            add
                                "MOD-MANIFEST-TYPE"
                                (Some path)
                                "Manifest integer is outside the supported range."
                                "Use a 32-bit JSON integer."

                            None
                    else
                        add
                            "MOD-MANIFEST-TYPE"
                            (Some path)
                            "Manifest field must be an integer."
                            "Use a JSON integer for this field."

                        None

                let readArray (path: string) (element: JsonElement) =
                    if element.ValueKind = JsonValueKind.Array then
                        Some(element.EnumerateArray() |> Seq.toList)
                    else
                        add
                            "MOD-MANIFEST-TYPE"
                            (Some path)
                            "Manifest field must be an array."
                            "Use a JSON array for this field."

                        None

                let readObject (path: string) (element: JsonElement) =
                    if element.ValueKind = JsonValueKind.Object then
                        Some(element.EnumerateObject() |> Seq.toList)
                    else
                        add
                            "MOD-MANIFEST-TYPE"
                            (Some path)
                            "Manifest field must be an object."
                            "Use a JSON object for this field."

                        None

                let inspectObjectKeys (path: string) (objectProperties: JsonProperty list) (allowedKeys: string list) =
                    let allowed = Set.ofList allowedKeys

                    for property in objectProperties do
                        if not (Set.contains property.Name allowed) then
                            add
                                "MOD-MANIFEST-UNKNOWN-KEY"
                                (Some(appendJsonPath path property.Name))
                                ("Unknown manifest key '" + property.Name + "'.")
                                "remove the unknown manifest key."

                    objectProperties
                    |> List.groupBy (fun property -> property.Name)
                    |> List.iter (fun (name, occurrences) ->
                        if occurrences.Length > 1 then
                            add
                                "MOD-MANIFEST-DUPLICATE-KEY"
                                (Some(appendJsonPath path name))
                                ("Manifest key '" + name + "' occurs more than once.")
                                "remove duplicate manifest keys.")

                let findNested (properties: JsonProperty list) name =
                    properties |> List.tryFind (fun property -> property.Name = name)

                let requiredNested (path: string) (properties: JsonProperty list) name =
                    match findNested properties name with
                    | Some property -> Some property.Value
                    | None ->
                        add
                            "MOD-MANIFEST-MISSING-FIELD"
                            (Some(appendJsonPath path name))
                            ("Required manifest field '" + name + "' is missing.")
                            ("Add the required manifest field '" + name + "'.")

                        None

                let formatValue =
                    match required "format" with
                    | Some element -> readString "/format" element
                    | None -> None

                match formatValue with
                | Some value when value <> "football-engine.mod" ->
                    add
                        "MOD-MANIFEST-FORMAT"
                        (Some "/format")
                        "The manifest format is not supported."
                        "Set format to football-engine.mod."
                | _ -> ()

                let formatVersion =
                    match findProperty "formatVersion" with
                    | None ->
                        add
                            "MOD-MANIFEST-FORMAT-VERSION"
                            (Some "/formatVersion")
                            "formatVersion is required and must be exactly 1."
                            "Add formatVersion 1; older and newer versions are not migrated."

                        None
                    | Some property ->
                        match readInt "/formatVersion" property.Value with
                        | Some 1 -> Some 1
                        | Some _ ->
                            add
                                "MOD-MANIFEST-FORMAT-VERSION"
                                (Some "/formatVersion")
                                "Only manifest format version 1 is supported."
                                "Set formatVersion to version 1; do not rely on migration."

                            None
                        | None ->
                            add
                                "MOD-MANIFEST-FORMAT-VERSION"
                                (Some "/formatVersion")
                                "formatVersion is required and must be exactly 1."
                                "Set formatVersion to version 1."

                            None

                let modIdText =
                    match required "modId" with
                    | Some element ->
                        match readString "/modId" element with
                        | Some value when isValidModIdText value -> Some value
                        | Some value ->
                            add
                                "MOD-ID-INVALID"
                                (Some "/modId")
                                ("Invalid mod ID '" + value + "'.")
                                "Use a stable lowercase ASCII mod ID."

                            None
                        | None -> None
                    | None -> None

                let version =
                    match required "version" with
                    | Some element ->
                        match readString "/version" element with
                        | Some value ->
                            match parseSemVersion value with
                            | Some _ -> Some value
                            | None ->
                                add
                                    "MOD-SEMVER-INVALID"
                                    (Some "/version")
                                    "The mod version must use strict SemVer 2.0 syntax."
                                    "Use strict SemVer: MAJOR.MINOR.PATCH with optional valid prerelease/build identifiers."

                                None
                        | None -> None
                    | None -> None

                let engineValues =
                    match required "engine" with
                    | Some element ->
                        match readObject "/engine" element with
                        | Some objectProperties ->
                            inspectObjectKeys "/engine" objectProperties [ "minInclusive"; "maxExclusive" ]

                            let minimum =
                                match requiredNested "/engine" objectProperties "minInclusive" with
                                | Some value -> readString "/engine/minInclusive" value
                                | None -> None

                            let maximum =
                                match requiredNested "/engine" objectProperties "maxExclusive" with
                                | Some value -> readString "/engine/maxExclusive" value
                                | None -> None

                            minimum, maximum
                        | None -> None, None
                    | None -> None, None

                let engineMinimum =
                    match fst engineValues with
                    | Some value ->
                        match parseSemVersion value with
                        | Some parsed -> Some(parsed, value)
                        | None ->
                            add
                                "MOD-SEMVER-INVALID"
                                (Some "/engine/minInclusive")
                                "The engine minimum must use strict SemVer 2.0 syntax."
                                "Use strict SemVer with a complete MAJOR.MINOR.PATCH engine version."

                            None
                    | None -> None

                let engineMaximum =
                    match snd engineValues with
                    | Some value ->
                        match parseSemVersion value with
                        | Some parsed -> Some(parsed, value)
                        | None ->
                            add
                                "MOD-SEMVER-INVALID"
                                (Some "/engine/maxExclusive")
                                "The engine maximum must use strict SemVer 2.0 syntax."
                                "Use strict SemVer with a complete MAJOR.MINOR.PATCH engine version."

                            None
                    | None -> None

                match engineMinimum, engineMaximum with
                | Some(minimum, _), Some(maximum, _) ->
                    if compareSemVersion minimum maximum >= 0 then
                        add
                            "MOD-ENGINE-RANGE-INVALID"
                            (Some "/engine")
                            "The engine minimum must be lower than maxExclusive."
                            "Choose a non-empty engine version interval."

                    if
                        compareSemVersion currentEngineVersion minimum < 0
                        || compareSemVersion currentEngineVersion maximum >= 0
                    then
                        add
                            "MOD-ENGINE-INCOMPATIBLE"
                            (Some "/engine")
                            "The package engine range does not include engine version 2026.1.0."
                            "Set minInclusive at or below 2026.1.0 and maxExclusive above it."
                | _ -> ()

                let apiValues =
                    match required "api" with
                    | Some element ->
                        match readObject "/api" element with
                        | Some objectProperties ->
                            inspectObjectKeys "/api" objectProperties [ "content"; "scripts" ]

                            let content =
                                match requiredNested "/api" objectProperties "content" with
                                | Some value -> readInt "/api/content" value
                                | None -> None

                            let scripts =
                                match requiredNested "/api" objectProperties "scripts" with
                                | Some value -> readInt "/api/scripts" value
                                | None -> None

                            content, scripts
                        | None -> None, None
                    | None -> None, None

                match fst apiValues with
                | Some value when value <> CurrentContentApi ->
                    add
                        "MOD-MANIFEST-CONTENT-API"
                        (Some "/api/content")
                        "The content API is incompatible with this reader."
                        "Use content API 1 or publish a package for this reader."
                | None -> ()
                | _ -> ()

                match snd apiValues with
                | Some value when value <> CurrentScriptsApi ->
                    add
                        "MOD-MANIFEST-SCRIPTS-API"
                        (Some "/api/scripts")
                        "Scripts are not enabled by content API 1."
                        "Set scripts API 0; executable mod code is not accepted."
                | None -> ()
                | _ -> ()

                let namespaceValue =
                    match required "namespace" with
                    | Some element ->
                        match readString "/namespace" element with
                        | Some value when isValidNamespaceText value -> Some value
                        | Some value ->
                            add
                                "MOD-NAMESPACE-INVALID"
                                (Some "/namespace")
                                ("Invalid namespace '" + value + "'.")
                                "Use a stable lowercase ASCII namespace."

                            None
                        | None -> None
                    | None -> None

                match modIdText, namespaceValue with
                | Some modId, Some namespaceId when modId <> namespaceId ->
                    add
                        "MOD-NAMESPACE-MISMATCH"
                        (Some "/namespace")
                        "namespace must equal modId in format version 1."
                        "Set namespace to the declared lowercase modId."
                | _ -> ()

                let title =
                    match required "title" with
                    | Some element -> readString "/title" element
                    | None -> None

                let description =
                    match required "description" with
                    | Some element -> readString "/description" element
                    | None -> None

                match required "authors" with
                | Some element ->
                    match readArray "/authors" element with
                    | Some authors ->
                        authors
                        |> List.iteri (fun index author ->
                            let path = "/authors/" + string index

                            match readObject path author with
                            | Some authorProperties ->
                                inspectObjectKeys path authorProperties [ "id"; "displayName" ]

                                match requiredNested path authorProperties "id" with
                                | Some idElement ->
                                    match readString (appendJsonPath path "id") idElement with
                                    | Some value when not (isValidModIdText value) ->
                                        add
                                            "MOD-ID-INVALID"
                                            (Some(appendJsonPath path "id"))
                                            ("Invalid author ID '" + value + "'.")
                                            "Use a stable lowercase ASCII author ID."
                                    | _ -> ()
                                | None -> ()

                                match requiredNested path authorProperties "displayName" with
                                | Some displayName ->
                                    readString (appendJsonPath path "displayName") displayName |> ignore
                                | None -> ()
                            | None -> ())
                    | None -> ()
                | None -> ()

                let dependencies = ResizeArray<ModId * string>()

                match required "dependencies" with
                | Some element ->
                    match readArray "/dependencies" element with
                    | Some dependencyItems ->
                        dependencyItems
                        |> List.iteri (fun index dependency ->
                            let path = "/dependencies/" + string index

                            match readObject path dependency with
                            | Some dependencyProperties ->
                                inspectObjectKeys path dependencyProperties [ "modId"; "range" ]

                                let dependencyId =
                                    match requiredNested path dependencyProperties "modId" with
                                    | Some value ->
                                        match readString (appendJsonPath path "modId") value with
                                        | Some text when isValidModIdText text -> Some(ModId text)
                                        | Some text ->
                                            add
                                                "MOD-ID-INVALID"
                                                (Some(appendJsonPath path "modId"))
                                                ("Invalid dependency mod ID '" + text + "'.")
                                                "Use a stable lowercase ASCII mod ID."

                                            None
                                        | None -> None
                                    | None -> None

                                let dependencyRange =
                                    match requiredNested path dependencyProperties "range" with
                                    | Some value ->
                                        match readString (appendJsonPath path "range") value with
                                        | Some text ->
                                            match parseRange text with
                                            | Some _ -> Some text
                                            | None ->
                                                add
                                                    "MOD-RANGE-INVALID"
                                                    (Some(appendJsonPath path "range"))
                                                    "Dependency range must contain strict >= and/or < SemVer comparisons."
                                                    "Use a range such as >=1.0.0 <2.0.0."

                                                None
                                        | None -> None
                                    | None -> None

                                match dependencyId, dependencyRange with
                                | Some id, Some range -> dependencies.Add(id, range)
                                | _ -> ()
                            | None -> ())
                    | None -> ()
                | None -> ()

                let precedenceValues =
                    match required "precedence" with
                    | Some element ->
                        match readObject "/precedence" element with
                        | Some objectProperties ->
                            inspectObjectKeys "/precedence" objectProperties [ "layer"; "after"; "before" ]

                            let layer =
                                match requiredNested "/precedence" objectProperties "layer" with
                                | Some value -> readInt "/precedence/layer" value
                                | None -> None

                            let after =
                                match requiredNested "/precedence" objectProperties "after" with
                                | Some value -> readArray "/precedence/after" value
                                | None -> None

                            let before =
                                match requiredNested "/precedence" objectProperties "before" with
                                | Some value -> readArray "/precedence/before" value
                                | None -> None

                            layer, after, before
                        | None -> None, None, None
                    | None -> None, None, None

                let parseModIdArray path items =
                    let values = ResizeArray<ModId>()

                    items
                    |> List.iteri (fun index item ->
                        let itemPath = path + "/" + string index

                        match readString itemPath item with
                        | Some value when isValidModIdText value -> values.Add(ModId value)
                        | Some value ->
                            add
                                "MOD-ID-INVALID"
                                (Some itemPath)
                                ("Invalid precedence mod ID '" + value + "'.")
                                "Use a stable lowercase ASCII mod ID."
                        | None -> ())

                    List.ofSeq values

                let priority, afterItems, beforeItems = precedenceValues

                let after =
                    match afterItems with
                    | Some items -> parseModIdArray "/precedence/after" items
                    | None -> []

                let before =
                    match beforeItems with
                    | Some items -> parseModIdArray "/precedence/before" items
                    | None -> []

                let declaredFiles = ResizeArray<string * string>()

                match required "files" with
                | Some element ->
                    match readArray "/files" element with
                    | Some fileItems ->
                        fileItems
                        |> List.iteri (fun index fileItem ->
                            let path = "/files/" + string index

                            match readObject path fileItem with
                            | Some fileProperties ->
                                inspectObjectKeys path fileProperties [ "path"; "sha256" ]

                                let filePath =
                                    match requiredNested path fileProperties "path" with
                                    | Some value ->
                                        match readString (appendJsonPath path "path") value with
                                        | Some text ->
                                            if not (isSafeRelativePath text) then
                                                let code =
                                                    if pathLooksLikeTraversal text then
                                                        "MOD-PATH-TRAVERSAL"
                                                    else
                                                        "MOD-PATH-INVALID"

                                                addDiagnostic
                                                    diagnostics
                                                    code
                                                    None
                                                    None
                                                    (Some text)
                                                    None
                                                    None
                                                    ("Unsafe relative package path '" + text + "'.")
                                                    "Use a normalized relative path without traversal segments."

                                            if hasExecutableExtension text then
                                                addDiagnostic
                                                    diagnostics
                                                    "MOD-PATH-EXTENSION"
                                                    None
                                                    None
                                                    (Some text)
                                                    None
                                                    None
                                                    "Executable and script files are not accepted in a declarative mod package."
                                                    "Remove the executable file; content API 1 accepts declarative data only."

                                            Some text
                                        | None -> None
                                    | None -> None

                                let fileHash =
                                    match requiredNested path fileProperties "sha256" with
                                    | Some value ->
                                        match readString (appendJsonPath path "sha256") value with
                                        | Some text when isSha256 text -> Some text
                                        | Some text ->
                                            add
                                                "MOD-SHA256-INVALID"
                                                (Some(appendJsonPath path "sha256"))
                                                "File hash must be a lowercase hexadecimal SHA-256 value."
                                                "recalculate the SHA-256 hash and write 64 lowercase hex digits."

                                            None
                                        | None -> None
                                    | None -> None

                                match filePath, fileHash with
                                | Some pathValue, Some hashValue -> declaredFiles.Add(pathValue, hashValue)
                                | _ -> ()
                            | None -> ())
                    | None -> ()
                | None -> ()

                let declaredFileList = List.ofSeq declaredFiles
                let declaredPaths = declaredFileList |> List.map fst

                declaredPaths
                |> List.groupBy id
                |> List.iter (fun (path, occurrences) ->
                    if occurrences.Length > 1 then
                        add
                            "MOD-MANIFEST-DUPLICATE-FILE"
                            None
                            ("File path '" + path + "' is declared more than once.")
                            "Declare each package path exactly once.")

                if
                    declaredPaths
                    <> (declaredPaths
                        |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right)))
                then
                    add
                        "MOD-MANIFEST-FILES-ORDER"
                        (Some "/files")
                        "Manifest files must be ordered by canonical relative path."
                        "Sort the complete files array by path using ordinal order."

                let packageSha256 =
                    match required "packageSha256" with
                    | Some element ->
                        match readString "/packageSha256" element with
                        | Some value when isSha256 value -> Some value
                        | Some value ->
                            add
                                "MOD-PACKAGE-HASH-INVALID"
                                (Some "/packageSha256")
                                "packageSha256 must be a lowercase hexadecimal SHA-256 value."
                                "Recalculate the package hash and write 64 lowercase hex digits."

                            None
                        | None -> None
                    | None -> None

                match required "signature" with
                | Some element when element.ValueKind <> JsonValueKind.Null ->
                    add
                        "MOD-SIGNATURE-UNSUPPORTED"
                        (Some "/signature")
                        "Signatures are reserved but not supported by this reader."
                        "Set signature to null until a supported verifier is available."
                | _ -> ()

                let validEngine = engineMinimum.IsSome && engineMaximum.IsSome

                let validCore =
                    modIdText.IsSome && version.IsSome && namespaceValue.IsSome && priority.IsSome

                if
                    diagnostics.Count = 0
                    && formatVersion.IsSome
                    && validCore
                    && validEngine
                    && packageSha256.IsSome
                then
                    let manifest =
                        { Id = ModId modIdText.Value
                          Version = version.Value
                          Dependencies = List.ofSeq dependencies
                          Priority = priority.Value
                          Before = before
                          After = after }

                    let canonicalResult = canonicalizeElement root "" true

                    match canonicalResult with
                    | Ok canonical ->
                        success <-
                            Some(
                                Ok
                                    { Manifest = manifest
                                      PackageSha256 = packageSha256.Value
                                      DeclaredFiles = declaredFileList
                                      CanonicalWithoutPackageSha256 = canonical }
                            )
                    | Error error -> error.Diagnostics |> List.iter diagnostics.Add
        with ex ->
            add
                "MOD-MANIFEST-JSON"
                None
                ("Manifest JSON could not be parsed: " + ex.Message)
                "Fix the manifest JSON syntax and encode it as UTF-8."

        match success with
        | Some result when diagnostics.Count = 0 -> result
        | None -> Error(errorFromDiagnostics diagnostics)
        | Some _ -> Error(errorFromDiagnostics diagnostics)

    and internal canonicalNumber (raw: string) =
        let sign, unsigned =
            if raw.StartsWith("-", StringComparison.Ordinal) then
                "-", raw.Substring(1)
            else
                "", raw

        let exponentIndex = unsigned.IndexOfAny([| 'e'; 'E' |])

        let mantissa, exponent =
            if exponentIndex >= 0 then
                let exponentText = unsigned.Substring(exponentIndex + 1)
                let mutable parsed = 0

                if
                    Int32.TryParse(exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, &parsed)
                then
                    unsigned.Substring(0, exponentIndex), parsed
                else
                    unsigned, 0
            else
                unsigned, 0

        let decimalIndex = mantissa.IndexOf('.')

        let integerPart, fractionalPart =
            if decimalIndex >= 0 then
                mantissa.Substring(0, decimalIndex), mantissa.Substring(decimalIndex + 1)
            else
                mantissa, ""

        let mutable digits = integerPart + fractionalPart
        let mutable decimalPosition = integerPart.Length + exponent

        while digits.Length > 0 && digits[0] = '0' do
            digits <- digits.Substring(1)
            decimalPosition <- decimalPosition - 1

        if digits.Length = 0 || digits |> Seq.forall (fun character -> character = '0') then
            "0"
        else
            while digits.Length > 1 && digits[digits.Length - 1] = '0' do
                digits <- digits.Substring(0, digits.Length - 1)

            let result =
                if decimalPosition <= 0 then
                    "0." + String.replicate (-decimalPosition) "0" + digits
                elif decimalPosition >= digits.Length then
                    digits + String.replicate (decimalPosition - digits.Length) "0"
                else
                    digits.Substring(0, decimalPosition) + "." + digits.Substring(decimalPosition)

            if sign = "-" then "-" + result else result

    and internal canonicalizeElement
        (element: JsonElement)
        (path: string)
        (skipPackageSha256: bool)
        : Result<string, ModError> =
        let makeJsonError code jsonPath message action =
            Error(errorFromDiagnostic (diagnostic code None None None (Some jsonPath) None message action))

        match element.ValueKind with
        | JsonValueKind.Null -> Ok "null"
        | JsonValueKind.True -> Ok "true"
        | JsonValueKind.False -> Ok "false"
        | JsonValueKind.String -> Ok(JsonSerializer.Serialize(element.GetString()))
        | JsonValueKind.Number -> Ok(canonicalNumber (element.GetRawText()))
        | JsonValueKind.Array ->
            let values = ResizeArray<string>()
            let mutable error = None

            element.EnumerateArray()
            |> Seq.iteri (fun index child ->
                if error.IsNone then
                    match canonicalizeElement child (path + "/" + string index) false with
                    | Ok value -> values.Add value
                    | Error item -> error <- Some item)

            match error with
            | Some item -> Error item
            | None -> Ok("[" + String.concat "," (List.ofSeq values) + "]")
        | JsonValueKind.Object ->
            let properties = element.EnumerateObject() |> Seq.toList

            let duplicate =
                properties
                |> List.groupBy (fun property -> property.Name)
                |> List.tryFind (fun (_, occurrences) -> occurrences.Length > 1)

            match duplicate with
            | Some(name, _) ->
                makeJsonError
                    "MOD-JSON-DUPLICATE-KEY"
                    (appendJsonPath path name)
                    ("JSON property '" + name + "' occurs more than once.")
                    "Remove duplicate JSON properties before canonicalization."
            | None ->
                let canonicalProperties = ResizeArray<string>()
                let mutable error = None

                properties
                |> List.filter (fun property -> not (skipPackageSha256 && path = "" && property.Name = "packageSha256"))
                |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left.Name, right.Name))
                |> List.iter (fun property ->
                    if error.IsNone then
                        match canonicalizeElement property.Value (appendJsonPath path property.Name) false with
                        | Ok value -> canonicalProperties.Add(JsonSerializer.Serialize(property.Name) + ":" + value)
                        | Error item -> error <- Some item)

                match error with
                | Some item -> Error item
                | None -> Ok("{" + String.concat "," (List.ofSeq canonicalProperties) + "}")
        | _ -> makeJsonError "MOD-JSON-INVALID" path "Unsupported JSON token." "Use only valid JSON values."

    let canonicalizeJson (json: string) : Result<string, ModError> =
        try
            use document =
                JsonDocument.Parse(
                    json,
                    JsonDocumentOptions(
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 128
                    )
                )

            canonicalizeElement document.RootElement "" false
        with ex ->
            Error
                { Diagnostics =
                    [ diagnostic
                          "MOD-JSON-INVALID"
                          None
                          None
                          None
                          None
                          None
                          ("JSON could not be parsed: " + ex.Message)
                          "Fix the JSON syntax and encode the document as UTF-8." ] }

    let hashSha256 (bytes: byte[]) =
        use sha256 = SHA256.Create()

        sha256.ComputeHash(bytes)
        |> Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    let parseManifest (json: string) : Result<ModManifest, ModError> =
        match parseManifestDocument json with
        | Ok parsed -> Ok parsed.Manifest
        | Error error -> Error error

    let parseContentId (value: string) : Result<ContentId, ModError> =
        match parseContentIdValue value with
        | Some contentId -> Ok contentId
        | None ->
            Error
                { Diagnostics =
                    [ diagnostic
                          "MOD-CONTENT-ID-INVALID"
                          None
                          None
                          None
                          None
                          (if isNull value then None else Some value)
                          "Content ID must use namespace:kind/local-id with lowercase stable identifiers."
                          "Use a lowercase qualified content ID such as community.example:club/river." ] }
