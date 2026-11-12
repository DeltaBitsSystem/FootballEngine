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
open FootballEngine.Data.ModParsing

/// Validation of a package's content files against the content kinds the engine accepts.
module ModContent =
    let internal packageHash (parsed: ParsedManifest) (actualFiles: (string * byte[]) list) =
        let canonicalFiles =
            actualFiles
            |> List.sortWith (fun (left, _) (right, _) -> StringComparer.Ordinal.Compare(left, right))
            |> List.map (fun (path, bytes) -> path + "\n" + hashSha256 bytes)
            |> String.concat "\n"

        hashSha256 (Encoding.UTF8.GetBytes(parsed.CanonicalWithoutPackageSha256 + "\n" + canonicalFiles))

    let internal knownContentKinds =
        set
            [ "country"
              "club"
              "person"
              "namePool"
              "competition"
              "rule"
              "balance"
              "translation"
              "asset"
              "generator"
              "patch" ]

    let internal allowedContentKeys kind =
        match kind with
        | "country" -> set [ "id"; "kind"; "schema"; "code"; "confederation"; "nameKey"; "domain" ]
        | "club" ->
            set
                [ "id"
                  "kind"
                  "schema"
                  "countryId"
                  "nameKey"
                  "shortNameKey"
                  "reputation"
                  "competitionMembership"
                  "assets" ]
        | "person" ->
            set
                [ "id"
                  "kind"
                  "schema"
                  "nameKey"
                  "nationalityId"
                  "attributes"
                  "initialClubId" ]
        | "namePool" ->
            set
                [ "id"
                  "kind"
                  "schema"
                  "countryId"
                  "locale"
                  "firstNames"
                  "lastNames"
                  "rules" ]
        | "competition" ->
            set
                [ "id"
                  "kind"
                  "schema"
                  "domain"
                  "nameKey"
                  "edition"
                  "participants"
                  "format"
                  "calendar"
                  "standings"
                  "prizes"
                  "ruleId"
                  "qualification" ]
        | "rule" ->
            set
                [ "id"
                  "kind"
                  "schema"
                  "points"
                  "tiebreakers"
                  "promotion"
                  "relegation"
                  "registration"
                  "eligibility" ]
        | "balance" -> set [ "id"; "kind"; "schema"; "values"; "world"; "sections" ]
        | "translation" -> set [ "id"; "kind"; "schema"; "locale"; "entries" ]
        | "asset" ->
            set
                [ "id"
                  "kind"
                  "schema"
                  "sha256"
                  "mime"
                  "bytes"
                  "width"
                  "height"
                  "usage"
                  "required" ]
        | "generator" -> set [ "id"; "kind"; "schema"; "parameters" ]
        | "patch" -> set [ "target"; "kind"; "schema"; "operations" ]
        | _ -> Set.empty

    let internal validateContentFile
        (diagnostics: ResizeArray<ModDiagnostic>)
        (manifest: ModManifest)
        (relativePath: string)
        (bytes: byte[])
        (seenIds: HashSet<string>)
        =
        let modId = Some manifest.ModId
        let version = Some manifest.Version
        let path = Some relativePath

        let add code jsonPath entityId message action =
            addDiagnostic diagnostics code modId version path jsonPath entityId message action

        try
            let utf8 = UTF8Encoding(false, true)
            let json = utf8.GetString(bytes)

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

            let rec visitDuplicates (currentPath: string) (element: JsonElement) =
                match element.ValueKind with
                | JsonValueKind.Object ->
                    let properties = element.EnumerateObject() |> Seq.toList

                    properties
                    |> List.groupBy (fun property -> property.Name)
                    |> List.iter (fun (name, occurrences) ->
                        if occurrences.Length > 1 then
                            add
                                "MOD-CONTENT-DUPLICATE-KEY"
                                (Some(appendJsonPath currentPath name))
                                None
                                "A content JSON object contains a duplicate property."
                                "Remove the duplicate JSON property.")

                    properties
                    |> List.iter (fun property ->
                        visitDuplicates (appendJsonPath currentPath property.Name) property.Value)
                | JsonValueKind.Array ->
                    element.EnumerateArray()
                    |> Seq.iteri (fun index item -> visitDuplicates (currentPath + "/" + string index) item)
                | _ -> ()

            visitDuplicates "" root

            if root.ValueKind <> JsonValueKind.Object then
                add
                    "MOD-CONTENT-JSON"
                    None
                    None
                    "A content file must contain a JSON object."
                    "Replace the file with an entity JSON object."
            else
                let properties = root.EnumerateObject() |> Seq.toList

                let find name =
                    properties |> List.tryFind (fun property -> property.Name = name)

                let requiredString name =
                    match find name with
                    | None ->
                        add
                            "MOD-CONTENT-MISSING-FIELD"
                            (Some(appendJsonPath "" name))
                            None
                            ("Required content field '" + name + "' is missing.")
                            ("Add the required content field '" + name + "'.")

                        None
                    | Some property when property.Value.ValueKind = JsonValueKind.String ->
                        Some(property.Value.GetString())
                    | Some _ ->
                        add
                            "MOD-CONTENT-TYPE"
                            (Some(appendJsonPath "" name))
                            None
                            ("Content field '" + name + "' must be a string.")
                            "Use the type declared by the content schema."

                        None

                let kindText = requiredString "kind"

                let idText =
                    match kindText with
                    | Some "patch" -> None
                    | _ -> requiredString "id"

                match find "schema" with
                | None ->
                    add
                        "MOD-CONTENT-MISSING-FIELD"
                        (Some "/schema")
                        idText
                        "Required content field 'schema' is missing."
                        "Add schema 1 to the content entity."
                | Some property ->
                    let mutable schema = 0

                    if
                        property.Value.ValueKind <> JsonValueKind.Number
                        || not (property.Value.TryGetInt32(&schema))
                    then
                        add
                            "MOD-CONTENT-TYPE"
                            (Some "/schema")
                            idText
                            "Content schema must be an integer."
                            "Use schema 1 as a JSON integer."
                    elif schema <> 1 then
                        add
                            "MOD-CONTENT-SCHEMA"
                            (Some "/schema")
                            idText
                            "Only content schema 1 is supported."
                            "Publish the entity with schema 1; no migration is performed."

                match kindText with
                | Some kind ->
                    if not (Set.contains kind knownContentKinds) then
                        add
                            "MOD-ENUM-UNKNOWN"
                            (Some "/kind")
                            idText
                            ("Unknown content kind '" + kind + "'.")
                            "Use a supported declarative content kind."
                | None -> ()

                let knownKind =
                    kindText |> Option.filter (fun kind -> Set.contains kind knownContentKinds)

                if knownKind.IsSome then
                    let kind = knownKind.Value
                    let allowed = allowedContentKeys kind

                    properties
                    |> List.iter (fun property ->
                        if not (Set.contains property.Name allowed) then
                            add
                                "MOD-CONTENT-UNKNOWN-KEY"
                                (Some(appendJsonPath "" property.Name))
                                idText
                                ("Unknown field '" + property.Name + "' for content kind '" + kind + "'.")
                                "remove the unknown content field or use the supported schema.")

                    if kind = "country" then
                        let requiredCountryString name =
                            match find name with
                            | Some property when property.Value.ValueKind = JsonValueKind.String ->
                                Some(property.Value.GetString())
                            | Some _ ->
                                add
                                    "MOD-CONTENT-TYPE"
                                    (Some(appendJsonPath "" name))
                                    idText
                                    ("Country field '" + name + "' must be a string.")
                                    "Use the type declared by the country schema."

                                None
                            | None ->
                                add
                                    "MOD-CONTENT-MISSING-FIELD"
                                    (Some(appendJsonPath "" name))
                                    idText
                                    ("Required country field '" + name + "' is missing.")
                                    ("Add the required country field '" + name + "'.")

                                None

                        let code = requiredCountryString "code"
                        let confederation = requiredCountryString "confederation"
                        requiredCountryString "nameKey" |> ignore

                        match code with
                        | Some value when
                            value.Length <> 3
                            || not (value |> Seq.forall (fun character -> character >= 'A' && character <= 'Z'))
                            ->
                            add
                                "MOD-CONTENT-FIELD"
                                (Some "/code")
                                idText
                                "Country code must contain three uppercase ASCII letters."
                                "Use the canonical three-letter country code."
                        | _ -> ()

                        match confederation with
                        | Some value when
                            not (Set.contains value (set [ "AFC"; "CAF"; "CONCACAF"; "CONMEBOL"; "OFC"; "UEFA" ]))
                            ->
                            add
                                "MOD-ENUM-UNKNOWN"
                                (Some "/confederation")
                                idText
                                ("Unknown country confederation '" + value + "'.")
                                "Use a known supported confederation enum value."
                        | _ -> ()

                match idText, kindText with
                | Some id, Some kind ->
                    match parseContentIdValue id with
                    | None ->
                        add
                            "MOD-CONTENT-ID-INVALID"
                            (Some "/id")
                            (Some id)
                            "Content id is not a valid qualified lowercase ID."
                            "Use namespace:kind/local-id with lowercase stable identifiers."
                    | Some contentId ->
                        if not (seenIds.Add id) then
                            add
                                "MOD-CONTENT-DUPLICATE"
                                (Some "/id")
                                (Some id)
                                "The same content ID is declared more than once in the package."
                                "Keep one owner for each content ID."

                        if contentId.Kind <> kind then
                            add
                                "MOD-CONTENT-KIND-MISMATCH"
                                (Some "/kind")
                                (Some id)
                                "The content kind does not match the kind encoded in the ID."
                                "Make the kind field and qualified content ID agree."

                        if contentId.Namespace <> manifest.Namespace then
                            add
                                "MOD-CONTENT-OWNER"
                                (Some "/id")
                                (Some id)
                                "A content definition must belong to the manifest namespace."
                                "Use this mod namespace for definitions; use patches for foreign content."
                | _ -> ()
        with ex ->
            add
                "MOD-CONTENT-JSON"
                None
                None
                ("Content JSON could not be parsed: " + ex.Message)
                "Fix the JSON syntax, depth and UTF-8 encoding."

    let validatePackage (package: PackageInput) : Result<unit, ModError> =
        let diagnostics = ResizeArray<ModDiagnostic>()

        let addPathDiagnostic code path message action =
            addDiagnostic diagnostics code None None (Some path) None None message action

        let parsedManifest =
            match parseManifestDocument package.ManifestJson with
            | Ok parsed -> Some parsed
            | Error error ->
                error.Diagnostics |> List.iter diagnostics.Add
                None

        let actualFiles = package.Files |> Map.toList

        for path, _ in actualFiles do
            if not (isSafeRelativePath path) then
                let code =
                    if pathLooksLikeTraversal path then
                        "MOD-PATH-TRAVERSAL"
                    else
                        "MOD-PATH-INVALID"

                addPathDiagnostic
                    code
                    path
                    ("Unsafe package path '" + path + "'.")
                    "Use a normalized relative path without traversal segments."

            if hasExecutableExtension path then
                addPathDiagnostic
                    "MOD-PATH-EXTENSION"
                    path
                    "Executable and script files are not accepted in a declarative mod package."
                    "Remove the executable file; content API 1 accepts declarative data only."

        for path in
            package.SymlinkPaths
            |> Set.toList
            |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right)) do
            addPathDiagnostic
                "MOD-PATH-SYMLINK"
                path
                "Package entries backed by symbolic links are not accepted."
                "Copy the file into the package instead of using a symlink or reparse point."

        let isSymlinkComponent path =
            let normalized = normalizedPath path

            package.SymlinkPaths
            |> Set.exists (fun symlinkPath ->
                let normalizedSymlink = normalizedPath symlinkPath

                normalized = normalizedSymlink
                || normalized.StartsWith(normalizedSymlink + "/", StringComparison.Ordinal))

        for path, _ in actualFiles do
            if isSymlinkComponent path && not (Set.contains path package.SymlinkPaths) then
                addPathDiagnostic
                    "MOD-PATH-SYMLINK"
                    path
                    "A component of this package path is a symbolic link."
                    "Copy every package path component into the package instead of using a symlink."

        match parsedManifest with
        | Some parsed ->

            let declared = Map.ofList parsed.DeclaredFiles

            for path, _ in parsed.DeclaredFiles do
                if hasExecutableExtension path && not (Map.containsKey path package.Files) then
                    addPathDiagnostic
                        "MOD-PATH-EXTENSION"
                        path
                        "Executable and script files are not accepted in a declarative mod package."
                        "Remove the executable file; content API 1 accepts declarative data only."

            for path, _ in actualFiles do
                if not (Map.containsKey path declared) then
                    addPathDiagnostic
                        "MOD-PACKAGE-FILE-UNDECLARED"
                        path
                        "The package contains a file absent from manifest.files."
                        "declare the file with its exact canonical relative path and SHA-256 hash."

            for path, expectedHash in parsed.DeclaredFiles do
                match Map.tryFind path package.Files with
                | None ->
                    addPathDiagnostic
                        "MOD-PACKAGE-FILE-MISSING"
                        path
                        "A file declared by the manifest is missing from the package."
                        "Include the declared file or remove its manifest entry."
                | Some bytes ->
                    let actualHash = hashSha256 bytes

                    if actualHash <> expectedHash then
                        addPathDiagnostic
                            "MOD-PACKAGE-FILE-HASH"
                            path
                            ("File SHA-256 mismatch; expected "
                             + expectedHash
                             + " but found "
                             + actualHash
                             + ".")
                            "recalculate the file hash in manifest.json."

            let actualPackageHash = packageHash parsed actualFiles

            if actualPackageHash <> parsed.PackageSha256 then
                addPathDiagnostic
                    "MOD-PACKAGE-HASH"
                    "manifest.json"
                    ("packageSha256 mismatch; expected "
                     + parsed.PackageSha256
                     + " but found "
                     + actualPackageHash
                     + ".")
                    "recalculate packageSha256 from the canonical manifest and sorted file hashes."

            let seenIds = HashSet<string>(StringComparer.Ordinal)

            parsed.DeclaredFiles
            |> List.iter (fun (path, _) ->
                match Map.tryFind path package.Files with
                | Some bytes when
                    path.StartsWith("content/", StringComparison.Ordinal)
                    && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ->
                    validateContentFile diagnostics parsed.Manifest path bytes seenIds
                | _ -> ())
        | None -> ()

        if diagnostics.Count = 0 then
            Ok()
        else
            Error(errorFromDiagnostics diagnostics)
