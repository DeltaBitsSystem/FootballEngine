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
open FootballEngine.Data.ModContent
open FootballEngine.Data.ModResolution

/// Loading packages from unpacked workspaces on disk, with the file-system safety checks.
module ModWorkspace =
    type internal ParsedContent =
        | SportsContentEntry of ContentId * string
        | TranslationEntry of ContentId * string
        | AssetEntry of ContentId * string
        | PatchEntries of PatchOperation list

    type internal WorkspaceScan =
        { ManifestJson: string option
          Files: Map<string, byte[]>
          SymlinkPaths: Set<string>
          Diagnostics: ModDiagnostic list }

    type internal InstalledWorkspace =
        { RelativePath: string
          FullPath: string
          RootOrder: int }

    [<Literal>]
    let internal MaxWorkspaceFileBytes = 10L * 1024L * 1024L

    let internal hasFileAttribute (attributes: FileAttributes) flag = (attributes &&& flag) = flag

    let internal ioDiagnostic (code: string) (relativePath: string) (message: string) (suggestedAction: string) =
        diagnostic code None None (Some relativePath) None None message suggestedAction

    let internal contentDiagnostic
        (manifest: ModManifest)
        (relativePath: string)
        (code: string)
        (jsonPath: string option)
        (entityId: string option)
        (message: string)
        (suggestedAction: string)
        =
        diagnostic
            code
            (Some manifest.ModId)
            (Some manifest.Version)
            (Some relativePath)
            jsonPath
            entityId
            message
            suggestedAction

    let internal contextualizeContentError (manifest: ModManifest) (relativePath: string) (error: ModError) : ModError =
        { Diagnostics =
            error.Diagnostics
            |> List.map (fun item ->
                { item with
                    ModId = Some manifest.ModId
                    Version = Some manifest.Version
                    RelativePath = Some relativePath }) }

    let internal findReparseComponent (path: string) : Result<string option, exn> =
        try
            let rec visit (current: string) =
                let attributes = File.GetAttributes current

                if hasFileAttribute attributes FileAttributes.ReparsePoint then
                    Some current
                else
                    let parent = Directory.GetParent current

                    if
                        isNull parent
                        || String.Equals(parent.FullName, current, StringComparison.Ordinal)
                    then
                        None
                    else
                        visit parent.FullName

            Ok(visit (Path.GetFullPath path))
        with ex ->
            Error ex

    let internal validateDirectoryRoot relativePath root : Result<string, ModError> =
        if String.IsNullOrWhiteSpace root then
            Error(
                { Diagnostics =
                    [ ioDiagnostic
                          "MOD-IO-DIRECTORY-INVALID"
                          relativePath
                          "The requested directory path is empty."
                          "Provide an existing directory path." ] }
            )
        else
            try
                let fullPath = Path.GetFullPath root

                if not (Directory.Exists fullPath) then
                    if File.Exists fullPath then
                        Error(
                            { Diagnostics =
                                [ ioDiagnostic
                                      "MOD-IO-DIRECTORY-INVALID"
                                      relativePath
                                      "The requested workspace root is a file, not a directory."
                                      "Provide a directory containing manifest.json." ] }
                        )
                    else
                        Error(
                            { Diagnostics =
                                [ ioDiagnostic
                                      "MOD-IO-DIRECTORY-MISSING"
                                      relativePath
                                      "The requested directory does not exist."
                                      "Create the directory before loading the package." ] }
                        )
                else
                    match findReparseComponent fullPath with
                    | Error ex ->
                        Error(
                            { Diagnostics =
                                [ ioDiagnostic
                                      "MOD-IO-DIRECTORY-READ"
                                      relativePath
                                      ("The workspace directory could not be inspected: " + ex.Message)
                                      "Make every workspace path component readable." ] }
                        )
                    | Ok(Some reparsePath) ->
                        Error(
                            { Diagnostics =
                                [ ioDiagnostic
                                      "MOD-PATH-SYMLINK"
                                      relativePath
                                      ("The workspace contains a symbolic link or reparse point at '"
                                       + reparsePath
                                       + "'.")
                                      "Use a real directory for every workspace path component." ] }
                        )
                    | Ok None -> Ok fullPath
            with ex ->
                Error(
                    { Diagnostics =
                        [ ioDiagnostic
                              "MOD-IO-DIRECTORY-READ"
                              relativePath
                              ("The workspace directory could not be opened: " + ex.Message)
                              "Provide a readable directory path." ] }
                )

    let internal readWorkspaceFile (fullPath: string) (relativePath: string) : Result<byte[], ModError> =
        try
            let attributes = File.GetAttributes fullPath

            if hasFileAttribute attributes FileAttributes.ReparsePoint then
                Error(
                    { Diagnostics =
                        [ ioDiagnostic
                              "MOD-PATH-SYMLINK"
                              relativePath
                              "Package files backed by symbolic links or reparse points are not accepted."
                              "Copy the file into the workspace instead of linking it." ] }
                )
            elif hasFileAttribute attributes FileAttributes.Directory then
                Error(
                    { Diagnostics =
                        [ ioDiagnostic
                              "MOD-IO-FILE-READ"
                              relativePath
                              "The package entry is a directory where a file was expected."
                              "Replace the entry with a regular file." ] }
                )
            else
                let length = FileInfo(fullPath).Length

                if length > MaxWorkspaceFileBytes then
                    Error(
                        { Diagnostics =
                            [ ioDiagnostic
                                  "MOD-IO-FILE-SIZE"
                                  relativePath
                                  ("The package file is larger than the supported limit of "
                                   + string MaxWorkspaceFileBytes
                                   + " bytes.")
                                  "Split the package data into supported files." ] }
                    )
                else
                    Ok(File.ReadAllBytes fullPath)
        with ex ->
            Error(
                { Diagnostics =
                    [ ioDiagnostic
                          "MOD-IO-FILE-READ"
                          relativePath
                          ("The package file could not be read: " + ex.Message)
                          "Make the file readable and retry the package load." ] }
            )

    let internal decodeManifest (bytes: byte[]) : Result<string, ModError> =
        try
            let encoding = UTF8Encoding(false, true)
            Ok(encoding.GetString bytes)
        with ex ->
            Error(
                { Diagnostics =
                    [ ioDiagnostic
                          "MOD-IO-UTF8"
                          "manifest.json"
                          ("manifest.json is not valid UTF-8: " + ex.Message)
                          "Encode manifest.json as strict UTF-8 without a byte-order mark requirement." ] }
            )

    let internal scanWorkspace (relativePath: string) (workspace: string) : WorkspaceScan =
        let diagnostics = ResizeArray<ModDiagnostic>()
        let files = ResizeArray<string * byte[]>()
        let symlinkPaths = HashSet<string>(StringComparer.Ordinal)
        let mutable manifestJson = None
        let mutable manifestBlocked = false

        let addError (error: ModError) =
            error.Diagnostics |> List.iter diagnostics.Add

        match validateDirectoryRoot relativePath workspace with
        | Error error -> addError error
        | Ok fullWorkspace ->
            let rec visit currentDirectory currentRelativePath =
                let entries =
                    try
                        Directory.GetFileSystemEntries currentDirectory
                        |> Array.sortWith (fun left right ->
                            StringComparer.Ordinal.Compare(Path.GetFileName left, Path.GetFileName right))
                    with ex ->
                        diagnostics.Add(
                            ioDiagnostic
                                "MOD-IO-DIRECTORY-READ"
                                (if String.IsNullOrEmpty currentRelativePath then
                                     "workspace"
                                 else
                                     currentRelativePath)
                                ("The package directory could not be enumerated: " + ex.Message)
                                "Make every package directory readable."
                        )

                        [||]

                for entry in entries do
                    let entryName = Path.GetFileName entry

                    if String.IsNullOrEmpty entryName then
                        diagnostics.Add(
                            ioDiagnostic
                                "MOD-PATH-INVALID"
                                (if String.IsNullOrEmpty currentRelativePath then
                                     "workspace"
                                 else
                                     currentRelativePath)
                                "A package entry has no valid relative name."
                                "Use ordinary file and directory names inside the workspace."
                        )
                    else
                        let entryRelativePath =
                            if String.IsNullOrEmpty currentRelativePath then
                                normalizedPath entryName
                            else
                                normalizedPath (currentRelativePath + "/" + entryName)

                        if not (isSafeRelativePath entryRelativePath) then
                            diagnostics.Add(
                                ioDiagnostic
                                    (if pathLooksLikeTraversal entryRelativePath then
                                         "MOD-PATH-TRAVERSAL"
                                     else
                                         "MOD-PATH-INVALID")
                                    entryRelativePath
                                    ("Unsafe package entry path '" + entryRelativePath + "'.")
                                    "Use a normalized relative path without traversal segments."
                            )
                        else
                            let attributes =
                                try
                                    Some(File.GetAttributes entry)
                                with ex ->
                                    diagnostics.Add(
                                        ioDiagnostic
                                            "MOD-IO-FILE-READ"
                                            entryRelativePath
                                            ("The package entry could not be inspected: " + ex.Message)
                                            "Make every package entry readable."
                                    )

                                    None

                            match attributes with
                            | None -> ()
                            | Some value when hasFileAttribute value FileAttributes.ReparsePoint ->
                                symlinkPaths.Add entryRelativePath |> ignore

                                if entryRelativePath = "manifest.json" then
                                    manifestBlocked <- true
                            | Some value when hasFileAttribute value FileAttributes.Directory ->
                                visit entry entryRelativePath
                            | Some _ ->
                                match readWorkspaceFile entry entryRelativePath with
                                | Error error ->
                                    addError error

                                    if entryRelativePath = "manifest.json" then
                                        manifestBlocked <- true
                                | Ok bytes when entryRelativePath = "manifest.json" ->
                                    match decodeManifest bytes with
                                    | Ok text -> manifestJson <- Some text
                                    | Error error ->
                                        addError error
                                        manifestBlocked <- true
                                | Ok bytes -> files.Add(entryRelativePath, bytes)

            visit fullWorkspace ""

            if manifestJson.IsNone then
                symlinkPaths
                |> Seq.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right))
                |> Seq.iter (fun path ->
                    diagnostics.Add(
                        ioDiagnostic
                            "MOD-PATH-SYMLINK"
                            path
                            "Package entries backed by symbolic links or reparse points are not accepted."
                            "Copy the entry into the workspace instead of using a symlink or reparse point."
                    ))

                if not manifestBlocked then
                    diagnostics.Add(
                        ioDiagnostic
                            "MOD-IO-MANIFEST-MISSING"
                            "manifest.json"
                            "The workspace does not contain a readable manifest.json at its root."
                            "Add manifest.json to the workspace root."
                    )

        { ManifestJson = manifestJson
          Files = files |> List.ofSeq |> Map.ofList
          SymlinkPaths = symlinkPaths |> Set.ofSeq
          Diagnostics = List.ofSeq diagnostics }

    let internal isValidJsonPointer (value: string) =
        if isNull value then
            false
        elif value = "" then
            true
        elif value[0] <> '/' then
            false
        else
            let mutable valid = true

            for segment in value.Substring(1).Split('/') do
                let mutable index = 0

                while valid && index < segment.Length do
                    if segment[index] = '~' then
                        if
                            index + 1 >= segment.Length
                            || (segment[index + 1] <> '0' && segment[index + 1] <> '1')
                        then
                            valid <- false
                        else
                            index <- index + 2
                    else
                        index <- index + 1

            valid

    let internal parsePatchOperations
        (manifest: ModManifest)
        (relativePath: string)
        (target: ContentId)
        (operationsElement: JsonElement)
        : Result<PatchOperation list, ModError> =
        let diagnostics = ResizeArray<ModDiagnostic>()
        let operations = ResizeArray<PatchOperation>()

        let add code jsonPath entityId message suggestedAction =
            diagnostics.Add(
                contentDiagnostic manifest relativePath code (Some jsonPath) entityId message suggestedAction
            )

        if operationsElement.ValueKind <> JsonValueKind.Array then
            add
                "MOD-PATCH-TYPE"
                "/operations"
                (Some(contentIdValue target))
                "Patch operations must be a JSON array."
                "Use an array containing strict patch operation objects."
        else
            operationsElement.EnumerateArray()
            |> Seq.iteri (fun index operationElement ->
                let operationPath = "/operations/" + string index

                if operationElement.ValueKind <> JsonValueKind.Object then
                    add
                        "MOD-PATCH-OPERATION"
                        operationPath
                        (Some(contentIdValue target))
                        "Each patch operation must be a JSON object."
                        "Use an object with op, path, and the exact operation value field."
                else
                    let properties = operationElement.EnumerateObject() |> Seq.toList

                    let find name =
                        properties |> List.tryFind (fun property -> property.Name = name)

                    let mutable validOperation = true

                    properties
                    |> List.groupBy (fun property -> property.Name)
                    |> List.iter (fun (name, occurrences) ->
                        if occurrences.Length > 1 then
                            validOperation <- false

                            add
                                "MOD-PATCH-DUPLICATE-KEY"
                                (appendJsonPath operationPath name)
                                (Some(contentIdValue target))
                                ("Patch operation key '" + name + "' occurs more than once.")
                                "Remove duplicate operation keys.")

                    let opText =
                        match find "op" with
                        | None ->
                            validOperation <- false

                            add
                                "MOD-PATCH-MISSING-FIELD"
                                (appendJsonPath operationPath "op")
                                (Some(contentIdValue target))
                                "Patch operation field 'op' is required."
                                "Use assert, add, replace, or remove."

                            None
                        | Some property when property.Value.ValueKind = JsonValueKind.String ->
                            Some(property.Value.GetString())
                        | Some _ ->
                            validOperation <- false

                            add
                                "MOD-PATCH-TYPE"
                                (appendJsonPath operationPath "op")
                                (Some(contentIdValue target))
                                "Patch operation field 'op' must be a string."
                                "Use one of the supported operation names as a JSON string."

                            None

                    let allowedKeys =
                        match opText with
                        | Some "assert" -> set [ "op"; "path"; "equals" ]
                        | Some "add"
                        | Some "replace" -> set [ "op"; "path"; "value" ]
                        | Some "remove" -> set [ "op"; "path" ]
                        | _ -> set [ "op"; "path"; "value"; "equals" ]

                    properties
                    |> List.iter (fun property ->
                        if not (Set.contains property.Name allowedKeys) then
                            validOperation <- false

                            add
                                "MOD-PATCH-UNKNOWN-KEY"
                                (appendJsonPath operationPath property.Name)
                                (Some(contentIdValue target))
                                ("Unknown patch operation field '" + property.Name + "'.")
                                "Remove the unknown field and use the exact operation schema.")

                    let pathText =
                        match find "path" with
                        | None ->
                            validOperation <- false

                            add
                                "MOD-PATCH-MISSING-FIELD"
                                (appendJsonPath operationPath "path")
                                (Some(contentIdValue target))
                                "Patch operation field 'path' is required."
                                "Use an RFC 6901 JSON pointer path."

                            None
                        | Some property when property.Value.ValueKind = JsonValueKind.String ->
                            Some(property.Value.GetString())
                        | Some _ ->
                            validOperation <- false

                            add
                                "MOD-PATCH-TYPE"
                                (appendJsonPath operationPath "path")
                                (Some(contentIdValue target))
                                "Patch operation field 'path' must be a string."
                                "Use an RFC 6901 JSON pointer path as a JSON string."

                            None

                    match pathText with
                    | Some path when isValidJsonPointer path -> ()
                    | Some _ ->
                        validOperation <- false

                        add
                            "MOD-PATCH-PATH"
                            (appendJsonPath operationPath "path")
                            (Some(contentIdValue target))
                            "Patch operation path is not a valid JSON pointer."
                            "Use an empty path or a slash-prefixed path with only ~0 and ~1 escapes."
                    | None -> ()

                    let canonicalValue fieldName =
                        match find fieldName with
                        | None ->
                            validOperation <- false

                            add
                                "MOD-PATCH-MISSING-FIELD"
                                (appendJsonPath operationPath fieldName)
                                (Some(contentIdValue target))
                                ("Patch operation field '" + fieldName + "' is required for this operation.")
                                ("Add the canonical JSON field '" + fieldName + "'.")

                            None
                        | Some property ->
                            match
                                canonicalizeElement property.Value (appendJsonPath operationPath fieldName) false
                            with
                            | Ok value -> Some value
                            | Error error ->
                                validOperation <- false

                                error
                                |> contextualizeContentError manifest relativePath
                                |> fun item -> item.Diagnostics |> List.iter diagnostics.Add

                                None

                    match opText with
                    | Some "assert" ->
                        match canonicalValue "equals" with
                        | Some value when validOperation -> operations.Add(Assert(target, value))
                        | _ -> ()
                    | Some "add" ->
                        match canonicalValue "value" with
                        | Some value when validOperation -> operations.Add(Add(target, value))
                        | _ -> ()
                    | Some "replace" ->
                        match canonicalValue "value" with
                        | Some value when validOperation -> operations.Add(Replace(target, value))
                        | _ -> ()
                    | Some "remove" when validOperation -> operations.Add(Remove target)
                    | Some _ ->
                        validOperation <- false

                        add
                            "MOD-PATCH-OPERATION"
                            (appendJsonPath operationPath "op")
                            (Some(contentIdValue target))
                            "Patch operation name is not supported."
                            "Use assert, add, replace, or remove."
                    | None -> ())

        if diagnostics.Count = 0 then
            Ok(List.ofSeq operations)
        else
            Error { Diagnostics = List.ofSeq diagnostics }

    let rec internal parsePackageContents
        (manifest: ModManifest)
        (files: Map<string, byte[]>)
        : Result<
              Map<ContentId, string> * Map<ContentId, string> * Map<ContentId, string> * PatchOperation list,
              ModError
           >
        =
        let diagnostics = ResizeArray<ModDiagnostic>()
        let sports = ResizeArray<ContentId * string>()
        let translations = ResizeArray<ContentId * string>()
        let assets = ResizeArray<ContentId * string>()
        let patches = ResizeArray<PatchOperation>()

        files
        |> Map.toList
        |> List.filter (fun (path, _) ->
            path.StartsWith("content/", StringComparison.Ordinal)
            && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        |> List.sortWith (fun (left, _) (right, _) -> StringComparer.Ordinal.Compare(left, right))
        |> List.iter (fun (path, bytes) ->
            match parseContentFile manifest path bytes with
            | Error error -> error.Diagnostics |> List.iter diagnostics.Add
            | Ok(SportsContentEntry(contentId, value)) -> sports.Add(contentId, value)
            | Ok(TranslationEntry(contentId, value)) -> translations.Add(contentId, value)
            | Ok(AssetEntry(contentId, value)) -> assets.Add(contentId, value)
            | Ok(PatchEntries operations) -> operations |> List.iter patches.Add)

        if diagnostics.Count > 0 then
            Error { Diagnostics = List.ofSeq diagnostics }
        else
            Ok(Map.ofSeq sports, Map.ofSeq translations, Map.ofSeq assets, List.ofSeq patches)

    and internal workspaceRelativeName (workspace: string) : Result<string, ModError> =
        if String.IsNullOrWhiteSpace workspace then
            Error
                { Diagnostics =
                    [ ioDiagnostic
                          "MOD-PATH-INVALID"
                          "workspace"
                          "The workspace path is empty."
                          "Provide a package workspace directory." ] }
        else
            try
                let fullPath = Path.GetFullPath workspace
                let name = DirectoryInfo(fullPath).Name |> normalizedPath

                if isSafeRelativePath name then
                    Ok name
                else
                    Error
                        { Diagnostics =
                            [ ioDiagnostic
                                  "MOD-PATH-INVALID"
                                  "workspace"
                                  "The workspace does not have a safe relative directory name."
                                  "Use a named workspace directory without traversal or absolute segments." ] }
            with ex ->
                Error
                    { Diagnostics =
                        [ ioDiagnostic
                              "MOD-PATH-INVALID"
                              "workspace"
                              ("The workspace path is invalid: " + ex.Message)
                              "Provide a valid package workspace directory." ] }

    and internal loadPackageAt (relativePath: string) (workspace: string) : Result<PackageSelection, ModError> =
        let scan = scanWorkspace relativePath workspace

        match scan.ManifestJson with
        | None -> Error { Diagnostics = scan.Diagnostics }
        | Some manifestJson ->
            let packageInput =
                { ManifestJson = manifestJson
                  Files = scan.Files
                  SymlinkPaths = scan.SymlinkPaths }

            let validationDiagnostics =
                match validatePackage packageInput with
                | Ok() -> []
                | Error error -> error.Diagnostics

            let scanAndValidationDiagnostics = scan.Diagnostics @ validationDiagnostics

            if not scanAndValidationDiagnostics.IsEmpty then
                Error { Diagnostics = scanAndValidationDiagnostics }
            else
                match parseManifestDocument manifestJson with
                | Error error -> Error error
                | Ok parsedManifest ->
                    match parsePackageContents parsedManifest.Manifest scan.Files with
                    | Error error -> Error error
                    | Ok(sportsContent, translations, assets, patches) ->
                        Ok
                            { RelativePath = relativePath
                              Package =
                                { Manifest = parsedManifest.Manifest
                                  PackageSha256 = parsedManifest.PackageSha256
                                  SportsContent = sportsContent
                                  Translations = translations
                                  Assets = assets
                                  Patches = patches } }

    and loadPackage (workspace: string) : Result<PackageSelection, ModError> =
        match workspaceRelativeName workspace with
        | Error error -> Error error
        | Ok relativePath -> loadPackageAt relativePath workspace

    and internal discoverInstalledRoot
        (logicalName: string)
        (rootOrder: int)
        (root: string)
        : InstalledWorkspace list * ModDiagnostic list =
        let diagnostics = ResizeArray<ModDiagnostic>()
        let workspaces = ResizeArray<InstalledWorkspace>()

        match validateDirectoryRoot logicalName root with
        | Error error -> error.Diagnostics |> List.iter diagnostics.Add
        | Ok fullRoot ->
            let entries =
                try
                    Directory.GetFileSystemEntries fullRoot
                    |> Array.sortWith (fun left right ->
                        StringComparer.Ordinal.Compare(Path.GetFileName left, Path.GetFileName right))
                with ex ->
                    diagnostics.Add(
                        ioDiagnostic
                            "MOD-IO-DIRECTORY-READ"
                            logicalName
                            ("The installed root could not be enumerated: " + ex.Message)
                            "Make the installed root readable."
                    )

                    [||]

            for entry in entries do
                let name = Path.GetFileName entry
                let relativePath = if isNull name then "" else normalizedPath name

                if not (isSafeRelativePath relativePath) then
                    diagnostics.Add(
                        ioDiagnostic
                            (if pathLooksLikeTraversal relativePath then
                                 "MOD-PATH-TRAVERSAL"
                             else
                                 "MOD-PATH-INVALID")
                            relativePath
                            ("Unsafe installed workspace name '" + relativePath + "'.")
                            "Use a direct child directory with a safe relative name."
                    )
                else
                    let attributes =
                        try
                            Some(File.GetAttributes entry)
                        with ex ->
                            diagnostics.Add(
                                ioDiagnostic
                                    "MOD-IO-FILE-READ"
                                    relativePath
                                    ("The installed workspace could not be inspected: " + ex.Message)
                                    "Make the installed entry readable."
                            )

                            None

                    match attributes with
                    | None -> ()
                    | Some value when hasFileAttribute value FileAttributes.ReparsePoint ->
                        diagnostics.Add(
                            ioDiagnostic
                                "MOD-PATH-SYMLINK"
                                relativePath
                                "Installed workspaces backed by symbolic links or reparse points are not accepted."
                                "Copy the package into the installed root instead of linking it."
                        )
                    | Some value when hasFileAttribute value FileAttributes.Directory ->
                        workspaces.Add(
                            { RelativePath = relativePath
                              FullPath = entry
                              RootOrder = rootOrder }
                        )
                    | Some _ -> ()

        List.ofSeq workspaces, List.ofSeq diagnostics

    and internal installedSelectionOrder (left: PackageSelection) (right: PackageSelection) =
        let pathComparison =
            StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath)

        if pathComparison <> 0 then
            pathComparison
        else
            let idComparison =
                StringComparer.Ordinal.Compare(left.Package.Manifest.ModId, right.Package.Manifest.ModId)

            if idComparison <> 0 then idComparison else 0

    and internal duplicateInstalledDiagnostics (selections: PackageSelection list) =
        let diagnostics = ResizeArray<ModDiagnostic>()

        selections
        |> List.sortWith installedSelectionOrder
        |> List.groupBy (fun selection -> selection.Package.Manifest.Id)
        |> List.iter (fun (_, group) ->
            if group.Length > 1 then
                for duplicate in group.Tail do
                    let modId, version, relativePath = selectionContext duplicate

                    addDiagnostic
                        diagnostics
                        "MOD-ID-DUPLICATE"
                        modId
                        version
                        relativePath
                        (Some "/manifest/modId")
                        None
                        "More than one installed workspace declares the same mod ID."
                        "Keep exactly one installed package for each mod ID.")

        List.ofSeq diagnostics

    and loadInstalled
        (builtinsDir: string)
        (modsDir: string)
        (activeModIds: string list)
        (modOrder: string list)
        : Result<ResolvedDataSet, ModError> =
        let builtins, builtinDiagnostics = discoverInstalledRoot "builtins" 0 builtinsDir
        let mods, modDiagnostics = discoverInstalledRoot "mods" 1 modsDir
        let discoveryDiagnostics = builtinDiagnostics @ modDiagnostics

        let workspaces =
            (builtins @ mods)
            |> List.sortWith (fun left right ->
                let pathComparison =
                    StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath)

                if pathComparison <> 0 then
                    pathComparison
                else
                    compare left.RootOrder right.RootOrder)

        let loadedSelections = ResizeArray<PackageSelection>()
        let loadDiagnostics = ResizeArray<ModDiagnostic>()

        for workspace in workspaces do
            match loadPackageAt workspace.RelativePath workspace.FullPath with
            | Ok selection -> loadedSelections.Add selection
            | Error error -> error.Diagnostics |> List.iter loadDiagnostics.Add

        let allLoadDiagnostics = discoveryDiagnostics @ List.ofSeq loadDiagnostics

        if not allLoadDiagnostics.IsEmpty then
            Error { Diagnostics = allLoadDiagnostics }
        else
            let allSelections = List.ofSeq loadedSelections
            let duplicateDiagnostics = duplicateInstalledDiagnostics allSelections

            if not duplicateDiagnostics.IsEmpty then
                Error { Diagnostics = duplicateDiagnostics }
            else
                let byId =
                    allSelections
                    |> List.map (fun selection -> selection.Package.Manifest.ModId, selection)
                    |> Map.ofList

                let requestedActiveIds =
                    if isNull (box activeModIds) then
                        []
                    else
                        activeModIds |> Set.ofList |> Set.toList

                let activeDiagnostics = ResizeArray<ModDiagnostic>()

                let activeIdText value =
                    if isNull value then "<null>" else value

                requestedActiveIds
                |> List.iter (fun activeId ->
                    if isNull activeId || not (Map.containsKey activeId byId) then
                        addDiagnostic
                            activeDiagnostics
                            "MOD-ACTIVE-MISSING"
                            None
                            None
                            None
                            (Some "/activeModIds")
                            (if isNull activeId then None else Some activeId)
                            ("Active mod '" + activeIdText activeId + "' has no installed package.")
                            "Install the requested mod or remove it from activeModIds.")

                let selectedSelections =
                    if requestedActiveIds.IsEmpty then
                        allSelections
                    else
                        let selectedIds = HashSet<string>(StringComparer.Ordinal)

                        let rec includeDependencies modId =
                            if selectedIds.Add modId then
                                match Map.tryFind modId byId with
                                | Some selection ->
                                    for dependencyId, _ in selection.Package.Manifest.Dependencies do
                                        includeDependencies (modIdValue dependencyId)
                                | None -> ()

                        requestedActiveIds
                        |> List.iter (fun activeId ->
                            if not (isNull activeId) then
                                includeDependencies activeId)

                        allSelections
                        |> List.filter (fun selection -> selectedIds.Contains selection.Package.Manifest.ModId)

                let _ = modOrder

                match resolve selectedSelections with
                | Ok resolved when activeDiagnostics.Count = 0 -> validatePhysicalDataLock resolved
                | Ok _ -> Error { Diagnostics = List.ofSeq activeDiagnostics }
                | Error error -> Error { Diagnostics = List.ofSeq activeDiagnostics @ error.Diagnostics }

    and internal parseContentFile
        (manifest: ModManifest)
        (relativePath: string)
        (bytes: byte[])
        : Result<ParsedContent, ModError> =
        let diagnostics = ResizeArray<ModDiagnostic>()
        let mutable parsed = None

        let add code jsonPath entityId message suggestedAction =
            diagnostics.Add(contentDiagnostic manifest relativePath code jsonPath entityId message suggestedAction)

        let appendError error =
            error
            |> contextualizeContentError manifest relativePath
            |> fun item -> item.Diagnostics |> List.iter diagnostics.Add

        try
            let encoding = UTF8Encoding(false, true)
            let json = encoding.GetString bytes

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

            match canonicalizeElement root "" false with
            | Error error -> appendError error
            | Ok canonical ->
                if root.ValueKind <> JsonValueKind.Object then
                    add
                        "MOD-CONTENT-JSON"
                        None
                        None
                        "A content file must contain a JSON object."
                        "Replace the file with a JSON object matching a content schema."
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

                    match kindText with
                    | Some "patch" ->
                        let targetText = requiredString "target"

                        let target =
                            match targetText with
                            | Some value ->
                                match parseContentIdValue value with
                                | Some contentId -> Some contentId
                                | None ->
                                    add
                                        "MOD-CONTENT-ID-INVALID"
                                        (Some "/target")
                                        (Some value)
                                        "Patch target must be a valid qualified ContentId."
                                        "Use namespace:kind/local-id for the patch target."

                                    None
                            | None -> None

                        let schemaValid =
                            match find "schema" with
                            | Some property when property.Value.ValueKind = JsonValueKind.Number ->
                                let mutable value = 0

                                property.Value.TryGetInt32(&value) && value = 1
                            | _ -> false

                        if not schemaValid then
                            add
                                "MOD-CONTENT-SCHEMA"
                                (Some "/schema")
                                (targetText |> Option.filter (fun value -> not (isNull value)))
                                "Patch schema must be the integer 1."
                                "Publish the patch with schema 1."

                        match find "operations" with
                        | None ->
                            add
                                "MOD-PATCH-MISSING-FIELD"
                                (Some "/operations")
                                (targetText |> Option.filter (fun value -> not (isNull value)))
                                "Patch field 'operations' is required."
                                "Add an array of assert, add, replace, or remove operations."
                        | Some operationsElement ->
                            match target with
                            | None -> ()
                            | Some contentId ->
                                match parsePatchOperations manifest relativePath contentId operationsElement.Value with
                                | Ok operations when schemaValid -> parsed <- Some(PatchEntries operations)
                                | Ok _ -> ()
                                | Error error -> appendError error
                    | Some kind ->
                        let idText = requiredString "id"

                        match idText with
                        | Some value ->
                            match parseContentIdValue value with
                            | Some contentId ->
                                parsed <-
                                    Some(
                                        match kind with
                                        | "translation" -> TranslationEntry(contentId, canonical)
                                        | "asset" -> AssetEntry(contentId, canonical)
                                        | _ -> SportsContentEntry(contentId, canonical)
                                    )
                            | None ->
                                add
                                    "MOD-CONTENT-ID-INVALID"
                                    (Some "/id")
                                    (Some value)
                                    "Content ID is not a valid qualified lowercase ID."
                                    "Use namespace:kind/local-id with lowercase stable identifiers."
                        | None -> ()
                    | None -> ()
        with ex ->
            add
                "MOD-CONTENT-JSON"
                None
                None
                ("Content JSON could not be parsed: " + ex.Message)
                "Fix the JSON syntax, depth, and UTF-8 encoding."

        if diagnostics.Count = 0 then
            match parsed with
            | Some content -> Ok content
            | None ->
                Error
                    { Diagnostics =
                        [ contentDiagnostic
                              manifest
                              relativePath
                              "MOD-CONTENT-JSON"
                              None
                              None
                              "The content file did not produce a supported entity."
                              "Use a supported content kind and schema 1." ] }
        else
            Error { Diagnostics = List.ofSeq diagnostics }
