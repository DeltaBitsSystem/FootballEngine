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
open FootballEngine.Data.ModWorkspace

/// Reading and writing .femod archives and the installed package set.
module ModPackageFile =
    [<Literal>]
    let internal MaxPackageFileBytes = 10L * 1024L * 1024L

    [<Literal>]
    let internal MaxPackageBytes = 64L * 1024L * 1024L

    [<Literal>]
    let internal MaxPackageUncompressedBytes = 128L * 1024L * 1024L

    let internal packageIoError code path message action =
        { Diagnostics = [ ioDiagnostic code path message action ] }

    let internal readZipPackage (relativePath: string) (packagePath: string) : Result<PackageSelection, ModError> =
        if not (String.Equals(Path.GetExtension packagePath, ".femod", StringComparison.OrdinalIgnoreCase)) then
            Error(
                packageIoError
                    "MOD-PACKAGE-EXTENSION"
                    relativePath
                    "Only .femod packages are accepted by the runtime."
                    "Rename or repack the content as a .femod package."
            )
        else
            try
                let packageLength = FileInfo(packagePath).Length

                if packageLength > MaxPackageBytes then
                    Error(
                        packageIoError
                            "MOD-PACKAGE-SIZE"
                            relativePath
                            ($"The .femod package is larger than the supported compressed limit of {MaxPackageBytes} bytes.")
                            "Reduce the package size or split its content into separate packages."
                    )
                else
                    use stream = File.OpenRead packagePath
                    use archive = new ZipArchive(stream, ZipArchiveMode.Read, false, Encoding.UTF8)
                    let files = ResizeArray<string * byte[]>()
                    let seen = HashSet<string>(StringComparer.Ordinal)
                    let diagnostics = ResizeArray<ModDiagnostic>()
                    let mutable manifestBytes = None
                    let mutable totalBytes = 0L

                    for entry in archive.Entries do
                        let path = normalizedPath entry.FullName

                        if String.IsNullOrEmpty path || path.EndsWith("/", StringComparison.Ordinal) then
                            ()
                        elif not (isSafeRelativePath path) then
                            diagnostics.Add(
                                ioDiagnostic
                                    (if pathLooksLikeTraversal path then
                                         "MOD-PATH-TRAVERSAL"
                                     else
                                         "MOD-PATH-INVALID")
                                    path
                                    ($"Unsafe path '{path}' in .femod package.")
                                    "Use normalized relative paths without traversal segments."
                            )
                        elif not (seen.Add path) then
                            diagnostics.Add(
                                ioDiagnostic
                                    "MOD-PACKAGE-DUPLICATE-FILE"
                                    path
                                    "The .femod archive contains the same logical path more than once."
                                    "Keep exactly one ZIP entry for each path."
                            )
                        elif entry.Length > MaxPackageFileBytes then
                            diagnostics.Add(
                                ioDiagnostic
                                    "MOD-PACKAGE-FILE-SIZE"
                                    path
                                    ($"The package file is larger than the supported limit of {MaxPackageFileBytes} bytes.")
                                    "Split the content into smaller files."
                            )
                        elif totalBytes + entry.Length > MaxPackageUncompressedBytes then
                            diagnostics.Add(
                                ioDiagnostic
                                    "MOD-PACKAGE-TOTAL-SIZE"
                                    relativePath
                                    ($"The uncompressed package exceeds the supported limit of {MaxPackageUncompressedBytes} bytes.")
                                    "Reduce the package's total uncompressed content."
                            )
                        else
                            use input = entry.Open()
                            use output = new MemoryStream()
                            input.CopyTo output
                            let bytes = output.ToArray()
                            totalBytes <- totalBytes + int64 bytes.Length

                            if int64 bytes.Length <> entry.Length then
                                diagnostics.Add(
                                    ioDiagnostic
                                        "MOD-PACKAGE-READ"
                                        path
                                        "The ZIP entry length changed while it was being read."
                                        "Recreate the package and retry."
                                )
                            elif path = "manifest.json" then
                                manifestBytes <- Some bytes
                            else
                                files.Add(path, bytes)

                    if diagnostics.Count > 0 then
                        Error { Diagnostics = List.ofSeq diagnostics }
                    else
                        match manifestBytes with
                        | None ->
                            Error(
                                packageIoError
                                    "MOD-IO-MANIFEST-MISSING"
                                    relativePath
                                    "A .femod package must contain manifest.json at its root."
                                    "Add manifest.json to the package root."
                            )
                        | Some bytes ->
                            match decodeManifest bytes with
                            | Error error -> Error error
                            | Ok manifestJson ->
                                let input =
                                    { ManifestJson = manifestJson
                                      Files = files |> List.ofSeq |> Map.ofList
                                      SymlinkPaths = Set.empty }

                                match validatePackage input with
                                | Error error -> Error error
                                | Ok() ->
                                    match parseManifestDocument manifestJson with
                                    | Error error -> Error error
                                    | Ok parsed ->
                                        match parsePackageContents parsed.Manifest input.Files with
                                        | Error error -> Error error
                                        | Ok(sports, translations, assets, patches) ->
                                            Ok
                                                { RelativePath = relativePath
                                                  Package =
                                                    { Manifest = parsed.Manifest
                                                      PackageSha256 = parsed.PackageSha256
                                                      SportsContent = sports
                                                      Translations = translations
                                                      Assets = assets
                                                      Patches = patches } }
            with ex ->
                Error(
                    packageIoError
                        "MOD-PACKAGE-READ"
                        relativePath
                        ($"The .femod package could not be read: {ex.Message}")
                        "Recreate the package as a valid UTF-8 ZIP archive."
                )

    let loadFemod (packagePath: string) : Result<PackageSelection, ModError> =
        if String.IsNullOrWhiteSpace packagePath then
            Error(
                packageIoError "MOD-PACKAGE-PATH" "package" "The package path is empty." "Provide a .femod file path."
            )
        else
            try
                let fullPath = Path.GetFullPath packagePath

                if not (File.Exists fullPath) then
                    Error(
                        packageIoError
                            "MOD-PACKAGE-MISSING"
                            packagePath
                            "The .femod package does not exist."
                            "Install the package or remove it from the active content profile."
                    )
                elif hasFileAttribute (File.GetAttributes fullPath) FileAttributes.ReparsePoint then
                    Error(
                        packageIoError
                            "MOD-PATH-SYMLINK"
                            packagePath
                            "A .femod package backed by a symlink or reparse point is not accepted."
                            "Copy the package as a regular file."
                    )
                else
                    readZipPackage (Path.GetFileName fullPath) fullPath
            with ex ->
                Error(
                    packageIoError
                        "MOD-PACKAGE-PATH"
                        packagePath
                        ($"The .femod package path is invalid: {ex.Message}")
                        "Provide a readable regular .femod file."
                )

    let internal writeZipPackage
        (outputPath: string)
        (manifestJson: string)
        (files: (string * byte[]) list)
        : Result<unit, ModError> =
        try
            let fullOutput = Path.GetFullPath outputPath

            if not (String.Equals(Path.GetExtension fullOutput, ".femod", StringComparison.OrdinalIgnoreCase)) then
                Error(
                    packageIoError
                        "MOD-PACKAGE-EXTENSION"
                        outputPath
                        "Packages must be written with the .femod extension."
                        "Choose an output path ending in .femod."
                )
            else
                let parent = Path.GetDirectoryName fullOutput

                if not (String.IsNullOrEmpty parent) then
                    Directory.CreateDirectory parent |> ignore

                if
                    File.Exists fullOutput
                    && hasFileAttribute (File.GetAttributes fullOutput) FileAttributes.ReparsePoint
                then
                    Error(
                        packageIoError
                            "MOD-PATH-SYMLINK"
                            outputPath
                            "The output package path is backed by a symlink or reparse point."
                            "Choose a regular output file path."
                    )
                else
                    let temporaryOutput = fullOutput + "." + Guid.NewGuid().ToString("N") + ".tmp"

                    try
                        let writeArchive () =
                            use stream =
                                new FileStream(temporaryOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None)

                            use archive = new ZipArchive(stream, ZipArchiveMode.Create, false, Encoding.UTF8)

                            let writeEntry path bytes =
                                let entry = archive.CreateEntry(path, CompressionLevel.Optimal)
                                use output = entry.Open()
                                output.Write(bytes, 0, bytes.Length)

                            writeEntry "manifest.json" (Encoding.UTF8.GetBytes manifestJson)

                            files
                            |> List.sortWith (fun (left, _) (right, _) -> StringComparer.Ordinal.Compare(left, right))
                            |> List.iter (fun (path, bytes) -> writeEntry path bytes)

                        writeArchive ()

                        let flushOutput () =
                            use flushedStream =
                                new FileStream(temporaryOutput, FileMode.Open, FileAccess.ReadWrite, FileShare.None)

                            flushedStream.Flush(true)

                        flushOutput ()

                        if File.Exists fullOutput then
                            File.Replace(temporaryOutput, fullOutput, null)
                        else
                            File.Move(temporaryOutput, fullOutput)

                        Ok()
                    finally
                        if File.Exists temporaryOutput then
                            try
                                File.Delete temporaryOutput
                            with _ ->
                                ()
        with ex ->
            Error(
                packageIoError
                    "MOD-PACKAGE-WRITE"
                    outputPath
                    ($"The .femod package could not be written: {ex.Message}")
                    "Choose a writable output path and retry."
            )

    let packWorkspace (workspace: string) (outputPath: string) : Result<unit, ModError> =
        let scan = scanWorkspace "workspace" workspace

        match scan.ManifestJson with
        | None -> Error { Diagnostics = scan.Diagnostics }
        | Some manifestJson ->
            let input =
                { ManifestJson = manifestJson
                  Files = scan.Files
                  SymlinkPaths = scan.SymlinkPaths }

            match scan.Diagnostics, validatePackage input with
            | [], Ok() -> writeZipPackage outputPath manifestJson (Map.toList scan.Files)
            | diagnostics, Ok() -> Error { Diagnostics = diagnostics }
            | diagnostics, Error error -> Error { Diagnostics = diagnostics @ error.Diagnostics }

    type internal InstalledPackageFile =
        { RelativePath: string
          FullPath: string
          RootOrder: int }

    let internal discoverFemodRoot
        (logicalName: string)
        (rootOrder: int)
        (required: bool)
        (root: string)
        : InstalledPackageFile list * ModDiagnostic list =
        let diagnostics = ResizeArray<ModDiagnostic>()
        let packages = ResizeArray<InstalledPackageFile>()

        if String.IsNullOrWhiteSpace root then
            diagnostics.Add(
                ioDiagnostic
                    "MOD-IO-DIRECTORY-INVALID"
                    logicalName
                    "The package root is empty."
                    "Provide a package root directory."
            )
        elif not (Directory.Exists root) then
            if required then
                diagnostics.Add(
                    ioDiagnostic
                        "MOD-IO-DIRECTORY-MISSING"
                        logicalName
                        "The required builtin package directory does not exist."
                        "Install the bundled .femod packages."
                )
        else
            try
                Directory.EnumerateFiles(root, "*.femod", SearchOption.TopDirectoryOnly)
                |> Seq.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right))
                |> Seq.iter (fun path ->
                    let name = Path.GetFileName path

                    if hasFileAttribute (File.GetAttributes path) FileAttributes.ReparsePoint then
                        diagnostics.Add(
                            ioDiagnostic
                                "MOD-PATH-SYMLINK"
                                name
                                "A discovered .femod package is a symlink or reparse point."
                                "Copy the package as a regular file."
                        )
                    else
                        packages.Add(
                            { RelativePath = name
                              FullPath = path
                              RootOrder = rootOrder }
                        ))
            with ex ->
                diagnostics.Add(
                    ioDiagnostic
                        "MOD-IO-DIRECTORY-READ"
                        logicalName
                        ($"The package root could not be enumerated: {ex.Message}")
                        "Make the package root readable."
                )

        List.ofSeq packages, List.ofSeq diagnostics

    let internal applyModOrder (modOrder: string list) (selections: PackageSelection list) =
        let diagnostics = ResizeArray<ModDiagnostic>()

        let byId =
            selections
            |> List.map (fun selection -> selection.Package.Manifest.ModId, selection)
            |> Map.ofList

        let requested = if isNull (box modOrder) then [] else modOrder

        requested
        |> List.groupBy id
        |> List.iter (fun (modId, entries) ->
            if entries.Length > 1 then
                addDiagnostic
                    diagnostics
                    "MOD-ORDER-DUPLICATE"
                    (Some modId)
                    None
                    None
                    (Some "/modOrder")
                    (Some modId)
                    "The active mod order contains the same mod ID more than once."
                    "Keep each active mod ID at most once in modOrder.")

        let orderedIds =
            requested
            |> List.filter (fun modId -> Map.containsKey modId byId)
            |> List.distinct

        let addOrderEdge (previousId: string) (nextId: string) selection =
            if selection.Package.Manifest.ModId <> nextId then
                selection
            else
                let manifest = selection.Package.Manifest

                if manifest.After |> List.exists (fun modId -> modIdValue modId = previousId) then
                    selection
                else
                    { selection with
                        Package =
                            { selection.Package with
                                Manifest =
                                    { manifest with
                                        After = ModId previousId :: manifest.After } } }

        let rec addEdges ids current =
            match ids with
            | previousId :: nextId :: tail ->
                let updated = current |> List.map (addOrderEdge previousId nextId)
                addEdges (nextId :: tail) updated
            | _ -> current

        addEdges orderedIds selections, List.ofSeq diagnostics

    let loadInstalledPackages
        (builtinsDir: string)
        (modsDir: string)
        (activeModIds: string list)
        (modOrder: string list)
        : Result<ResolvedDataSet, ModError> =
        let builtins, builtinDiagnostics = discoverFemodRoot "builtins" 0 true builtinsDir
        let mods, modDiagnostics = discoverFemodRoot "mods" 1 false modsDir
        let discovered = builtins @ mods
        let diagnostics = ResizeArray<ModDiagnostic>(builtinDiagnostics @ modDiagnostics)
        let selections = ResizeArray<PackageSelection>()

        for item in discovered |> List.sortBy (fun item -> item.RelativePath, item.RootOrder) do
            match loadFemod item.FullPath with
            | Ok selection ->
                selections.Add(
                    { selection with
                        RelativePath = item.RelativePath }
                )
            | Error error -> error.Diagnostics |> List.iter diagnostics.Add

        if diagnostics.Count > 0 then
            Error { Diagnostics = List.ofSeq diagnostics }
        else
            let allSelections = List.ofSeq selections

            let byId =
                allSelections
                |> List.map (fun selection -> selection.Package.Manifest.ModId, selection)
                |> Map.ofList

            let requested =
                if isNull (box activeModIds) then
                    []
                else
                    activeModIds |> Set.ofList |> Set.toList

            let activeDiagnostics = ResizeArray<ModDiagnostic>()

            for activeId in requested do
                if not (Map.containsKey activeId byId) then
                    addDiagnostic
                        activeDiagnostics
                        "MOD-ACTIVE-MISSING"
                        None
                        None
                        None
                        (Some "/activeModIds")
                        (Some activeId)
                        ($"Active mod '{activeId}' has no installed .femod package.")
                        "Install the package or remove the ID from the active content profile."

            let selectedIds = HashSet<string>(StringComparer.Ordinal)

            let rec includeDependencies modId =
                if selectedIds.Add modId then
                    match Map.tryFind modId byId with
                    | Some selection ->
                        for dependencyId, _ in selection.Package.Manifest.Dependencies do
                            includeDependencies (modIdValue dependencyId)
                    | None -> ()

            if requested.IsEmpty then
                allSelections
                |> List.iter (fun selection -> selectedIds.Add selection.Package.Manifest.ModId |> ignore)
            else
                requested |> List.iter includeDependencies

            let selected =
                allSelections
                |> List.filter (fun selection -> selectedIds.Contains selection.Package.Manifest.ModId)

            let selectedWithOrder, orderDiagnostics = applyModOrder modOrder selected
            let allDiagnostics = List.ofSeq activeDiagnostics @ orderDiagnostics

            match resolve selectedWithOrder with
            | Ok resolved when allDiagnostics.IsEmpty -> validatePhysicalDataLock resolved
            | Ok _ -> Error { Diagnostics = allDiagnostics }
            | Error error -> Error { Diagnostics = allDiagnostics @ error.Diagnostics }
