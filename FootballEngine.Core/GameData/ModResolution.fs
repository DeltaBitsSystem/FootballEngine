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

/// Dependency order, patches, precedence and the data lock a save records.
module ModResolution =
    let internal selectionContext (selection: PackageSelection) =
        let manifest = selection.Package.Manifest
        Some manifest.ModId, Some manifest.Version, Some selection.RelativePath

    let internal validateModIdForResolution diagnostics selection path modId =
        let modIdText = modIdValue modId

        if not (isValidModIdText modIdText) then
            let contextModId, version, relativePath = selectionContext selection

            addDiagnostic
                diagnostics
                "MOD-ID-INVALID"
                contextModId
                version
                relativePath
                (Some path)
                None
                ("Invalid mod ID '" + modIdText + "'.")
                "Use a stable lowercase ASCII mod ID."

    let internal validateContentIdForResolution diagnostics selection path contentId =
        let value = contentIdValue contentId

        if not (isValidContentIdText value) then
            let contextModId, version, relativePath = selectionContext selection

            addDiagnostic
                diagnostics
                "MOD-CONTENT-ID-INVALID"
                contextModId
                version
                relativePath
                (Some path)
                (Some value)
                "Invalid content ID in a resolved package."
                "Use namespace:kind/local-id with lowercase stable identifiers."

    let internal compareSelections left right =
        let leftManifest = left.Package.Manifest
        let rightManifest = right.Package.Manifest
        let priorityComparison = compare leftManifest.Priority rightManifest.Priority

        if priorityComparison <> 0 then
            priorityComparison
        else
            let idComparison =
                StringComparer.Ordinal.Compare(leftManifest.ModId, rightManifest.ModId)

            if idComparison <> 0 then
                idComparison
            else
                StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath)

    let internal buildOrder (selections: PackageSelection list) : Result<PackageSelection list, ModError> =
        let diagnostics = ResizeArray<ModDiagnostic>()

        let sortedSelections =
            selections
            |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath))

        for selection in sortedSelections do
            if not (isSafeRelativePath selection.RelativePath) then
                let code =
                    if pathLooksLikeTraversal selection.RelativePath then
                        "MOD-PATH-TRAVERSAL"
                    else
                        "MOD-PATH-INVALID"

                let contextModId, version, _ = selectionContext selection

                addDiagnostic
                    diagnostics
                    code
                    contextModId
                    version
                    (Some selection.RelativePath)
                    None
                    None
                    ("Unsafe package selection path '" + selection.RelativePath + "'.")
                    "Use a normalized relative path inside the package root."

            let manifest = selection.Package.Manifest
            validateModIdForResolution diagnostics selection "/manifest/modId" manifest.Id

            match parseSemVersion manifest.Version with
            | None ->
                let contextModId, _, relativePath = selectionContext selection

                addDiagnostic
                    diagnostics
                    "MOD-SEMVER-INVALID"
                    contextModId
                    (Some manifest.Version)
                    relativePath
                    (Some "/manifest/version")
                    None
                    "Resolved package version must use strict SemVer 2.0 syntax."
                    "Use strict SemVer: MAJOR.MINOR.PATCH with valid prerelease/build identifiers."
            | Some _ -> ()

            for index, (dependencyId, range) in manifest.Dependencies |> List.indexed do
                validateModIdForResolution
                    diagnostics
                    selection
                    ("/manifest/dependencies/" + string index + "/modId")
                    dependencyId

                if parseRange range |> Option.isNone then
                    let contextModId, version, relativePath = selectionContext selection

                    addDiagnostic
                        diagnostics
                        "MOD-RANGE-INVALID"
                        contextModId
                        version
                        relativePath
                        (Some("/manifest/dependencies/" + string index + "/range"))
                        None
                        "Dependency range is not a supported >=/< SemVer range."
                        "Use a range such as >=1.0.0 <2.0.0."

            for index, precedenceId in manifest.Before |> List.indexed do
                validateModIdForResolution
                    diagnostics
                    selection
                    ("/manifest/precedence/before/" + string index)
                    precedenceId

            for index, precedenceId in manifest.After |> List.indexed do
                validateModIdForResolution
                    diagnostics
                    selection
                    ("/manifest/precedence/after/" + string index)
                    precedenceId

            for contentId, value in Map.toList selection.Package.SportsContent do
                validateContentIdForResolution diagnostics selection "/sportsContent" contentId

                if isNull value then
                    let contextModId, version, relativePath = selectionContext selection

                    addDiagnostic
                        diagnostics
                        "MOD-CONTENT-VALUE"
                        contextModId
                        version
                        relativePath
                        (Some "/sportsContent")
                        (Some(contentIdValue contentId))
                        "Resolved sports content cannot have a null canonical value."
                        "Provide the canonical content text for this ContentId."

            for contentId, value in Map.toList selection.Package.Translations do
                validateContentIdForResolution diagnostics selection "/translations" contentId

                if isNull value then
                    let contextModId, version, relativePath = selectionContext selection

                    addDiagnostic
                        diagnostics
                        "MOD-CONTENT-VALUE"
                        contextModId
                        version
                        relativePath
                        (Some "/translations")
                        (Some(contentIdValue contentId))
                        "Resolved translation content cannot have a null canonical value."
                        "Provide the canonical presentation text for this ContentId."

            for contentId, value in Map.toList selection.Package.Assets do
                validateContentIdForResolution diagnostics selection "/assets" contentId

                if isNull value then
                    let contextModId, version, relativePath = selectionContext selection

                    addDiagnostic
                        diagnostics
                        "MOD-CONTENT-VALUE"
                        contextModId
                        version
                        relativePath
                        (Some "/assets")
                        (Some(contentIdValue contentId))
                        "Resolved asset content cannot have a null canonical value."
                        "Provide the canonical asset hash or reference text for this ContentId."

            for index, operation in selection.Package.Patches |> List.indexed do
                let contentId =
                    match operation with
                    | Add(id, _) -> id
                    | Replace(id, _) -> id
                    | Remove id -> id
                    | Assert(id, _) -> id

                validateContentIdForResolution diagnostics selection ("/patches/" + string index) contentId

        let groupedById =
            sortedSelections
            |> List.groupBy (fun selection -> selection.Package.Manifest.Id)

        for _, group in groupedById do
            if group.Length > 1 then
                let first = group.Head

                for duplicate in group.Tail do
                    let contextModId, version, relativePath = selectionContext duplicate

                    addDiagnostic
                        diagnostics
                        "MOD-ID-DUPLICATE"
                        contextModId
                        version
                        relativePath
                        (Some "/manifest/modId")
                        None
                        "More than one selected package declares the same mod ID."
                        "Select exactly one package for each mod ID."

        let uniqueSelections =
            sortedSelections
            |> List.distinctBy (fun selection -> selection.Package.Manifest.Id)

        let byId =
            uniqueSelections
            |> List.map (fun selection -> selection.Package.Manifest.Id, selection)
            |> Map.ofList

        let ids =
            uniqueSelections |> List.map (fun selection -> selection.Package.Manifest.Id)

        let versions =
            uniqueSelections
            |> List.choose (fun selection ->
                match parseSemVersion selection.Package.Manifest.Version with
                | Some version -> Some(selection.Package.Manifest.Id, version)
                | None -> None)
            |> Map.ofList

        for selection in uniqueSelections do
            let manifest = selection.Package.Manifest

            for dependencyId, rangeText in manifest.Dependencies do
                match Map.tryFind dependencyId byId with
                | None ->
                    let contextModId, version, relativePath = selectionContext selection

                    addDiagnostic
                        diagnostics
                        "MOD-DEPENDENCY-MISSING"
                        contextModId
                        version
                        relativePath
                        (Some "/manifest/dependencies")
                        None
                        ("Dependency '" + modIdValue dependencyId + "' is not selected.")
                        "Select the dependency package or remove the dependency declaration."
                | Some dependency ->
                    match parseRange rangeText, Map.tryFind dependencyId versions with
                    | Some range, Some dependencyVersion when not (satisfiesRange dependencyVersion range) ->
                        let contextModId, version, relativePath = selectionContext selection

                        addDiagnostic
                            diagnostics
                            "MOD-DEPENDENCY-RANGE"
                            contextModId
                            version
                            relativePath
                            (Some "/manifest/dependencies")
                            None
                            ("Selected dependency '"
                             + dependency.Package.Manifest.ModId
                             + "@"
                             + dependency.Package.Manifest.Version
                             + "' does not satisfy range '"
                             + rangeText
                             + "'.")
                            "Install a compatible dependency version or update the declared range."
                    | _ -> ()

        let initialEdges = ids |> List.map (fun id -> id, Set.empty) |> Map.ofList

        let addEdge graph fromId toId =
            match Map.tryFind fromId graph with
            | Some targets -> Map.add fromId (Set.add toId targets) graph
            | None -> graph

        let mutable edges = initialEdges

        for selection in uniqueSelections do
            let currentId = selection.Package.Manifest.Id
            let manifest = selection.Package.Manifest

            for dependencyId, _ in manifest.Dependencies do
                if Map.containsKey dependencyId byId then
                    edges <- addEdge edges dependencyId currentId

            for beforeId in manifest.Before do
                if Map.containsKey beforeId byId then
                    edges <- addEdge edges currentId beforeId
                else
                    let contextModId, version, relativePath = selectionContext selection

                    addDiagnostic
                        diagnostics
                        "MOD-PRECEDENCE-REFERENCE"
                        contextModId
                        version
                        relativePath
                        (Some "/manifest/precedence/before")
                        None
                        ("before references unselected mod '" + modIdValue beforeId + "'.")
                        "Select the referenced mod or remove the precedence constraint."

            for afterId in manifest.After do
                if Map.containsKey afterId byId then
                    edges <- addEdge edges afterId currentId
                else
                    let contextModId, version, relativePath = selectionContext selection

                    addDiagnostic
                        diagnostics
                        "MOD-PRECEDENCE-REFERENCE"
                        contextModId
                        version
                        relativePath
                        (Some "/manifest/precedence/after")
                        None
                        ("after references unselected mod '" + modIdValue afterId + "'.")
                        "Select the referenced mod or remove the precedence constraint."

        let rec reaches visited current target =
            if current = target then
                true
            elif Set.contains current visited then
                false
            else
                let nextVisited = Set.add current visited

                match Map.tryFind current edges with
                | None -> false
                | Some targets -> targets |> Set.exists (fun next -> reaches nextVisited next target)

        let selectionById = byId

        for firstIndex in 0 .. ids.Length - 1 do
            for secondIndex in firstIndex + 1 .. ids.Length - 1 do
                let firstId = ids[firstIndex]
                let secondId = ids[secondIndex]
                let firstSelection = Map.find firstId selectionById
                let secondSelection = Map.find secondId selectionById

                if
                    firstSelection.Package.Manifest.Priority = secondSelection.Package.Manifest.Priority
                    && not (reaches Set.empty firstId secondId)
                    && not (reaches Set.empty secondId firstId)
                then
                    let contextModId, version, relativePath = selectionContext secondSelection

                    addDiagnostic
                        diagnostics
                        "MOD-PRECEDENCE-TIE"
                        contextModId
                        version
                        relativePath
                        (Some "/manifest/precedence")
                        None
                        ("Mods '"
                         + firstSelection.Package.Manifest.ModId
                         + "' and '"
                         + secondSelection.Package.Manifest.ModId
                         + "' have the same priority without an explicit ordering constraint.")
                        "Declare before/after (or a dependency) between the mods, or change one priority."

        let rec topologicalOrder remaining ordered =
            if Set.isEmpty remaining then
                Some(List.rev ordered)
            else
                let ready =
                    remaining
                    |> Set.toList
                    |> List.filter (fun candidate ->
                        remaining
                        |> Set.forall (fun other ->
                            other = candidate || not (Map.find other edges |> Set.contains candidate)))
                    |> List.map (fun id -> Map.find id selectionById)
                    |> List.sortWith compareSelections

                match ready with
                | [] -> None
                | first :: _ -> topologicalOrder (Set.remove first.Package.Manifest.Id remaining) (first :: ordered)

        if diagnostics.Count > 0 then
            if ids.Length > 0 then
                match topologicalOrder (Set.ofList ids) [] with
                | None ->
                    addDiagnostic
                        diagnostics
                        "MOD-DEPENDENCY-CYCLE"
                        None
                        None
                        None
                        None
                        None
                        "Dependency or precedence constraints contain a cycle."
                        "Remove the cycle from dependencies and before/after constraints."
                | Some _ -> ()

            Error(errorFromDiagnostics diagnostics)
        else
            match topologicalOrder (Set.ofList ids) [] with
            | Some order -> Ok order
            | None ->
                addDiagnostic
                    diagnostics
                    "MOD-DEPENDENCY-CYCLE"
                    None
                    None
                    None
                    None
                    None
                    "Dependency or precedence constraints contain a cycle."
                    "Remove the cycle from dependencies and before/after constraints."

                Error(errorFromDiagnostics diagnostics)

    let buildDependencyLock (selections: PackageSelection list) : Result<ModPackage list, ModError> =
        match buildOrder selections with
        | Ok ordered -> Ok(ordered |> List.map (fun selection -> selection.Package))
        | Error error -> Error error

    let applyPatch
        (initial: Map<ContentId, string>)
        (operations: PatchOperation list)
        : Result<Map<ContentId, string>, ModError> =
        let diagnostics = ResizeArray<ModDiagnostic>()
        let mutable current = initial

        let addPatchDiagnostic code contentId message action =
            addDiagnostic diagnostics code None None None None (Some(contentIdValue contentId)) message action

        for contentId in Map.toSeq initial |> Seq.map fst do
            if not (isValidContentIdText (contentIdValue contentId)) then
                addPatchDiagnostic
                    "MOD-CONTENT-ID-INVALID"
                    contentId
                    "The initial content map contains an invalid content ID."
                    "Use namespace:kind/local-id with lowercase stable identifiers."

        for operation in operations do
            match operation with
            | Add(contentId, value) ->
                if not (isValidContentIdText (contentIdValue contentId)) then
                    addPatchDiagnostic
                        "MOD-CONTENT-ID-INVALID"
                        contentId
                        "Patch operation targets an invalid content ID."
                        "Use namespace:kind/local-id with lowercase stable identifiers."
                elif Map.containsKey contentId current then
                    addPatchDiagnostic
                        "MOD-PATCH-ADD-EXISTS"
                        contentId
                        "Patch add targets a content ID that already exists."
                        "Use replace for an existing content ID or choose a new ID."
                else
                    current <- Map.add contentId value current
            | Replace(contentId, value) ->
                if not (isValidContentIdText (contentIdValue contentId)) then
                    addPatchDiagnostic
                        "MOD-CONTENT-ID-INVALID"
                        contentId
                        "Patch operation targets an invalid content ID."
                        "Use namespace:kind/local-id with lowercase stable identifiers."
                elif Map.containsKey contentId current then
                    current <- Map.add contentId value current
                else
                    addPatchDiagnostic
                        "MOD-PATCH-REPLACE-MISSING"
                        contentId
                        "Patch replace targets a content ID that does not exist."
                        "Use add for a new content ID or correct the target ID."
            | Remove contentId ->
                if not (isValidContentIdText (contentIdValue contentId)) then
                    addPatchDiagnostic
                        "MOD-CONTENT-ID-INVALID"
                        contentId
                        "Patch operation targets an invalid content ID."
                        "Use namespace:kind/local-id with lowercase stable identifiers."
                elif Map.containsKey contentId current then
                    current <- Map.remove contentId current
                else
                    addPatchDiagnostic
                        "MOD-PATCH-REMOVE-MISSING"
                        contentId
                        "Patch remove targets a content ID that does not exist."
                        "Correct the target ID or remove this operation."
            | Assert(contentId, expected) ->
                if not (isValidContentIdText (contentIdValue contentId)) then
                    addPatchDiagnostic
                        "MOD-CONTENT-ID-INVALID"
                        contentId
                        "Patch operation targets an invalid content ID."
                        "Use namespace:kind/local-id with lowercase stable identifiers."
                else
                    match Map.tryFind contentId current with
                    | Some actual when actual = expected -> ()
                    | Some actual ->
                        addPatchDiagnostic
                            "MOD-PATCH-ASSERT-FAILED"
                            contentId
                            ("Patch assert expected '" + expected + "' but found '" + actual + "'.")
                            "Update the assert to the exact base content or change the patch target."
                    | None ->
                        addPatchDiagnostic
                            "MOD-PATCH-ASSERT-MISSING"
                            contentId
                            "Patch assert targets a content ID that does not exist."
                            "Load the package defining the target before this patch."

        if diagnostics.Count = 0 then
            Ok current
        else
            Error(errorFromDiagnostics diagnostics)

    let internal mergeContentMap
        (diagnostics: ResizeArray<ModDiagnostic>)
        (mapName: string)
        (target: Map<ContentId, string>)
        (source: Map<ContentId, string>)
        =
        source
        |> Map.fold
            (fun state contentId value ->
                if Map.containsKey contentId state then
                    diagnostics.Add(
                        diagnostic
                            "MOD-CONTENT-DUPLICATE"
                            None
                            None
                            None
                            (Some("/" + mapName))
                            (Some(contentIdValue contentId))
                            "A content ID has more than one owner in the selected packages."
                            "Keep one definition for each content ID and use an ordered patch for changes."
                    )

                    state
                else
                    Map.add contentId value state)
            target

    let internal canonicalContentMap label (content: Map<ContentId, string>) =
        let entries =
            content
            |> Map.toList
            |> List.sortWith (fun (left, _) (right, _) ->
                StringComparer.Ordinal.Compare(contentIdValue left, contentIdValue right))
            |> List.map (fun (contentId, value) ->
                let id = contentIdValue contentId

                label
                + "\n"
                + string id.Length
                + ":"
                + id
                + "\n"
                + string value.Length
                + ":"
                + value)

        label + "\n" + string entries.Length + "\n" + String.concat "\n" entries

    let internal lockText (value: string) =
        if isNull value then
            "-1:"
        else
            string value.Length + ":" + value

    let internal lockInt (value: int) =
        value.ToString(CultureInfo.InvariantCulture)

    let internal canonicalDependencies (manifest: ModManifest) =
        manifest.Dependencies
        |> List.map (fun (dependencyId, range) -> modIdValue dependencyId, range)
        |> List.sortWith (fun (leftId, leftRange) (rightId, rightRange) ->
            let idComparison = StringComparer.Ordinal.Compare(leftId, rightId)

            if idComparison <> 0 then
                idComparison
            else
                StringComparer.Ordinal.Compare(leftRange, rightRange))

    let internal canonicalPrecedenceIds ids =
        ids
        |> List.map modIdValue
        |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right))

    let internal canonicalDependencyLock (packages: FootballEngine.Domain.DataLockPackage list) =
        packages
        |> List.mapi (fun index package ->
            let fields =
                [ "index=" + lockInt index
                  "modId=" + lockText package.ModId
                  "version=" + lockText package.Version
                  "schemaVersion=" + lockInt package.SchemaVersion
                  "precedence=" + lockInt package.Precedence
                  "packageSha256=" + lockText package.PackageSha256
                  "dependenciesCount=" + lockInt package.Dependencies.Length ]

            let dependencyFields =
                package.Dependencies
                |> List.map (fun (dependencyId, range) -> "dependency=" + lockText dependencyId + ":" + lockText range)

            let precedenceFields =
                [ "afterCount=" + lockInt package.After.Length
                  yield! package.After |> List.map (fun modId -> "after=" + lockText modId)
                  "beforeCount=" + lockInt package.Before.Length
                  yield! package.Before |> List.map (fun modId -> "before=" + lockText modId) ]

            String.concat "\n" (fields @ dependencyFields @ precedenceFields))
        |> String.concat "\n"

    let internal runtimeIdTable (sports: Map<ContentId, string>) =
        sports
        |> Map.toList
        |> List.map (fun (contentId, _) -> contentIdValue contentId)
        |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right))
        |> List.mapi (fun runtimeId contentId ->
            { ContentId = contentId
              RuntimeId = runtimeId })

    let internal requiredAssets (assets: Map<ContentId, string>) =
        assets
        |> Map.toList
        |> List.choose (fun (contentId, canonical) ->
            try
                use document = JsonDocument.Parse canonical
                let root = document.RootElement

                if root.ValueKind <> JsonValueKind.Object then
                    None
                else
                    match root.TryGetProperty("sha256"), root.TryGetProperty("required") with
                    | (true, sha256), (true, required) when
                        sha256.ValueKind = JsonValueKind.String
                        && (required.ValueKind = JsonValueKind.True
                            || required.ValueKind = JsonValueKind.False)
                        ->
                        let hash = sha256.GetString()

                        if isNull hash then
                            None
                        else
                            Some
                                { AssetId = contentIdValue contentId
                                  Sha256 = hash
                                  Required = required.GetBoolean() }
                    | _ -> None
            with _ ->
                None)
        |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left.AssetId, right.AssetId))

    let internal makeDataLock
        (packages: ModPackage list)
        (sports: Map<ContentId, string>)
        (translations: Map<ContentId, string>)
        (assets: Map<ContentId, string>)
        : FootballEngine.Domain.DataLock =
        let sportsCanonical = canonicalContentMap "sports" sports
        let translationsCanonical = canonicalContentMap "translations" translations
        let assetsCanonical = canonicalContentMap "assets" assets

        let presentationCanonical =
            String.concat "\n" [ sportsCanonical; translationsCanonical; assetsCanonical ]

        let packageLocks =
            packages
            |> List.map (fun package ->
                let manifest = package.Manifest

                { ModId = manifest.ModId
                  Version = manifest.Version
                  SchemaVersion = manifest.FormatVersion
                  Dependencies = canonicalDependencies manifest
                  Precedence = manifest.Priority
                  Before = canonicalPrecedenceIds manifest.Before
                  After = canonicalPrecedenceIds manifest.After
                  PackageSha256 = package.PackageSha256 })

        { FormatVersion = CurrentFormatVersion
          EngineVersion =
            currentEngineVersion.Major.ToString(CultureInfo.InvariantCulture)
            + "."
            + currentEngineVersion.Minor.ToString(CultureInfo.InvariantCulture)
            + "."
            + currentEngineVersion.Patch.ToString(CultureInfo.InvariantCulture)
          ContentApi = CurrentContentApi
          Packages = packageLocks
          DependencyLockHash = hashSha256 (Encoding.UTF8.GetBytes(canonicalDependencyLock packageLocks))
          ResolvedSportsHash = hashSha256 (Encoding.UTF8.GetBytes sportsCanonical)
          ResolvedPresentationHash = hashSha256 (Encoding.UTF8.GetBytes presentationCanonical)
          RuntimeIdTable = runtimeIdTable sports
          Snapshot =
            { SportsContent = sportsCanonical
              Translations = translationsCanonical
              Assets = assetsCanonical }
          RequiredAssets = requiredAssets assets }

    let internal validatePhysicalDataLock (resolved: ResolvedDataSet) : Result<ResolvedDataSet, ModError> =
        match DataLock.validate resolved.DataLock with
        | Ok() -> Ok resolved
        | Error message ->
            Error
                { Diagnostics =
                    [ diagnostic
                          "MOD-DATA-LOCK-INVALID"
                          None
                          None
                          None
                          None
                          None
                          ("The resolved data lock is invalid: " + message)
                          "Repair the package metadata and content hashes before loading the dataset." ] }

    let internal duplicateContentDiagnostics (selections: PackageSelection list) =
        let diagnostics = ResizeArray<ModDiagnostic>()

        let allContentEntries =
            selections
            |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath))
            |> List.collect (fun selection ->
                [ "sports", selection.Package.SportsContent
                  "translations", selection.Package.Translations
                  "assets", selection.Package.Assets ]
                |> List.collect (fun (mapName, content) ->
                    content
                    |> Map.toList
                    |> List.map (fun (contentId, _) -> contentId, mapName, selection)))

        allContentEntries
        |> List.groupBy (fun (contentId, _, _) -> contentId)
        |> List.iter (fun (contentId, entries) ->
            if entries.Length > 1 then
                let firstSelection = entries.Head |> fun (_, _, selection) -> selection

                entries.Tail
                |> List.iter (fun (_, _, selection) ->
                    let contextModId, version, relativePath = selectionContext selection

                    addDiagnostic
                        diagnostics
                        "MOD-CONTENT-DUPLICATE"
                        contextModId
                        version
                        relativePath
                        (Some "/content")
                        (Some(contentIdValue contentId))
                        ("Content ID is also owned by mod '"
                         + firstSelection.Package.Manifest.ModId
                         + "'.")
                        "Keep one owner for each content ID and use an ordered patch for modifications."))

        List.ofSeq diagnostics

    let resolve (selections: PackageSelection list) : Result<ResolvedDataSet, ModError> =
        let duplicateDiagnostics = duplicateContentDiagnostics selections

        match buildOrder selections with
        | Error error ->
            if duplicateDiagnostics.IsEmpty then
                Error error
            else
                Error { Diagnostics = error.Diagnostics @ duplicateDiagnostics }
        | Ok ordered ->
            let diagnostics = ResizeArray<ModDiagnostic>()

            duplicateDiagnostics |> List.iter diagnostics.Add

            if diagnostics.Count > 0 then
                Error(errorFromDiagnostics diagnostics)
            else
                let mutable sports = Map.empty
                let mutable translations = Map.empty
                let mutable assets = Map.empty
                let mutable patchError = None

                for selection in ordered do
                    sports <- mergeContentMap diagnostics "sportsContent" sports selection.Package.SportsContent

                    translations <-
                        mergeContentMap diagnostics "translations" translations selection.Package.Translations

                    assets <- mergeContentMap diagnostics "assets" assets selection.Package.Assets

                    match applyPatch sports selection.Package.Patches with
                    | Ok patched -> sports <- patched
                    | Error error ->
                        error.Diagnostics |> List.iter diagnostics.Add
                        patchError <- Some error

                if diagnostics.Count > 0 || patchError.IsSome then
                    Error(errorFromDiagnostics diagnostics)
                else
                    let packages = ordered |> List.map (fun selection -> selection.Package)
                    let dataLock = makeDataLock packages sports translations assets

                    Ok
                        { Packages = packages
                          SportsContent = sports
                          Translations = translations
                          Assets = assets
                          ResolvedSportsHash = dataLock.ResolvedSportsHash
                          ResolvedPresentationHash = dataLock.ResolvedPresentationHash
                          DataLock = dataLock }

    let toDataLock (resolved: ResolvedDataSet) : FootballEngine.Domain.DataLock =
        makeDataLock resolved.Packages resolved.SportsContent resolved.Translations resolved.Assets

    let internal manifestFromDataLock (package: FootballEngine.Domain.DataLockPackage) : ModManifest =
        { Id = ModId package.ModId
          Version = package.Version
          Dependencies = package.Dependencies |> List.map (fun (id, range) -> ModId id, range)
          Priority = package.Precedence
          Before = package.Before |> List.map ModId
          After = package.After |> List.map ModId }

    /// Rebuilds the immutable dataset from the snapshot embedded in a career save.
    /// Package content is intentionally not consulted; the resolved snapshot is the
    /// authority for an existing career.
    let restoreDataSet (dataLock: FootballEngine.Domain.DataLock) : Result<ResolvedDataSet, ModError> =
        let snapshotError message =
            { Diagnostics =
                [ diagnostic
                      "MOD-SNAPSHOT-INVALID"
                      None
                      None
                      None
                      (Some "/snapshot")
                      None
                      message
                      "Repair the saved resolved snapshot or create a new career." ] }

        let parseCanonicalMap label (text: string) : Result<Map<ContentId, string>, string> =
            let invalid message = Error message
            let prefix = label + "\n"

            let readLengthPrefixed path cursor =
                if cursor < 0 || cursor > text.Length then
                    Error(path + " starts outside the snapshot")
                else
                    let colon = text.IndexOf(':', cursor)

                    if colon < cursor then
                        Error(path + " is missing its length delimiter")
                    else
                        let lengthText = text.Substring(cursor, colon - cursor)
                        let mutable length = 0

                        if
                            not (Int32.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, &length))
                            || length < 0
                        then
                            Error(path + " has an invalid length")
                        elif colon + 1 + length > text.Length then
                            Error(path + " exceeds the snapshot bounds")
                        else
                            Ok(text.Substring(colon + 1, length), colon + 1 + length)

            let rec readEntries index count cursor entries =
                if index = count then
                    if cursor = text.Length then
                        Ok(entries |> List.rev |> Map.ofList)
                    else
                        Error(label + " contains trailing bytes")
                else
                    let entryPrefix = label + "\n"

                    if
                        cursor + entryPrefix.Length > text.Length
                        || text.Substring(cursor, entryPrefix.Length) <> entryPrefix
                    then
                        Error(sprintf "%s entry %d is missing its label" label index)
                    else
                        match readLengthPrefixed (sprintf "%s[%d].id" label index) (cursor + entryPrefix.Length) with
                        | Error message -> Error message
                        | Ok(id, afterId) ->
                            if afterId >= text.Length || text[afterId] <> '\n' then
                                Error(sprintf "%s entry %d is missing the ID/value separator" label index)
                            else
                                match readLengthPrefixed (sprintf "%s[%d].value" label index) (afterId + 1) with
                                | Error message -> Error message
                                | Ok(value, afterValue) ->
                                    match parseContentIdValue id with
                                    | None -> Error(sprintf "%s entry %d has an invalid ContentId" label index)
                                    | Some contentId when
                                        List.exists (fun (existing, _) -> existing = contentId) entries
                                        ->
                                        Error(sprintf "%s contains duplicate ContentId '%s'" label id)
                                    | Some contentId ->
                                        if index < count - 1 then
                                            if afterValue >= text.Length || text[afterValue] <> '\n' then
                                                Error(sprintf "%s entry %d is missing its record separator" label index)
                                            else
                                                readEntries
                                                    (index + 1)
                                                    count
                                                    (afterValue + 1)
                                                    ((contentId, value) :: entries)
                                        else
                                            readEntries (index + 1) count afterValue ((contentId, value) :: entries)

            if isNull text then
                invalid (label + " snapshot is null")
            elif not (text.StartsWith(prefix, StringComparison.Ordinal)) then
                invalid (label + " snapshot has an invalid label")
            else
                let countStart = prefix.Length
                let countEnd = text.IndexOf('\n', countStart)

                if countEnd < 0 then
                    invalid (label + " snapshot is missing its entry count")
                else
                    let countText = text.Substring(countStart, countEnd - countStart)
                    let mutable count = 0

                    if
                        not (Int32.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, &count))
                        || count < 0
                    then
                        invalid (label + " snapshot has an invalid entry count")
                    elif count > 1_000_000 then
                        invalid (label + " snapshot contains too many entries")
                    else
                        readEntries 0 count (countEnd + 1) []

        let parseAndValidateMap label text =
            parseCanonicalMap label text
            |> Result.bind (fun content ->
                if canonicalContentMap label content = text then
                    Ok content
                else
                    Error(label + " snapshot is not canonical"))

        match DataLock.validate dataLock with
        | Error message -> Error(snapshotError ("DataLock is invalid: " + message))
        | Ok() ->
            let snapshot = dataLock.Snapshot

            match
                parseAndValidateMap "sports" snapshot.SportsContent,
                parseAndValidateMap "translations" snapshot.Translations,
                parseAndValidateMap "assets" snapshot.Assets
            with
            | Error message, _, _
            | _, Error message, _
            | _, _, Error message -> Error(snapshotError message)
            | Ok sports, Ok translations, Ok assets ->
                let presentationCanonical =
                    String.concat "\n" [ snapshot.SportsContent; snapshot.Translations; snapshot.Assets ]

                if
                    hashSha256 (Encoding.UTF8.GetBytes snapshot.SportsContent)
                    <> dataLock.ResolvedSportsHash
                then
                    Error(snapshotError "ResolvedSportsHash does not match the sports snapshot")
                elif
                    hashSha256 (Encoding.UTF8.GetBytes presentationCanonical)
                    <> dataLock.ResolvedPresentationHash
                then
                    Error(snapshotError "ResolvedPresentationHash does not match the presentation snapshot")
                elif
                    hashSha256 (Encoding.UTF8.GetBytes(canonicalDependencyLock dataLock.Packages))
                    <> dataLock.DependencyLockHash
                then
                    Error(snapshotError "DependencyLockHash does not match the package lock")
                elif runtimeIdTable sports <> dataLock.RuntimeIdTable then
                    Error(snapshotError "RuntimeIdTable does not match the sports snapshot")
                elif requiredAssets assets <> dataLock.RequiredAssets then
                    Error(snapshotError "RequiredAssets does not match the asset snapshot")
                else
                    let packages =
                        dataLock.Packages
                        |> List.map (fun package ->
                            { Manifest = manifestFromDataLock package
                              PackageSha256 = package.PackageSha256
                              SportsContent = Map.empty
                              Translations = Map.empty
                              Assets = Map.empty
                              Patches = [] })

                    Ok
                        { Packages = packages
                          SportsContent = sports
                          Translations = translations
                          Assets = assets
                          ResolvedSportsHash = dataLock.ResolvedSportsHash
                          ResolvedPresentationHash = dataLock.ResolvedPresentationHash
                          DataLock = dataLock }
