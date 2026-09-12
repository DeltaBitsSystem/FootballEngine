namespace FootballEngine.Domain

open System

[<Serializable; CLIMutable; StructuralEquality; StructuralComparison>]
type DataLockPackage =
    { ModId: string
      Version: string
      SchemaVersion: int
      Dependencies: (string * string) list
      Precedence: int
      Before: string list
      After: string list
      PackageSha256: string }

[<Serializable; CLIMutable; StructuralEquality; StructuralComparison>]
type DataSnapshot =
    { SportsContent: string
      Translations: string
      Assets: string }

[<Serializable; CLIMutable; StructuralEquality; StructuralComparison>]
type RuntimeIdEntry = { ContentId: string; RuntimeId: int }

[<Serializable; CLIMutable; StructuralEquality; StructuralComparison>]
type DataLockAsset =
    { AssetId: string
      Sha256: string
      Required: bool }

[<Serializable; CLIMutable; StructuralEquality; StructuralComparison>]
type DataLock =
    { FormatVersion: int
      EngineVersion: string
      ContentApi: int
      Packages: DataLockPackage list
      DependencyLockHash: string
      ResolvedSportsHash: string
      ResolvedPresentationHash: string
      RuntimeIdTable: RuntimeIdEntry list
      Snapshot: DataSnapshot
      RequiredAssets: DataLockAsset list }

module DataLock =
    let private isAsciiLowerHex character =
        (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')

    let private isSha256 (value: string) =
        not (String.IsNullOrWhiteSpace value)
        && value.Length = 64
        && (value |> Seq.forall isAsciiLowerHex)

    let private isProvisionalFixture (value: string) =
        not (String.IsNullOrWhiteSpace value)
        && value.StartsWith("sha-", StringComparison.Ordinal)
        && value.Length > 4
        && (value.Substring(4)
            |> Seq.forall (fun character ->
                (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character = '-'
                || character = '_'))

    let private firstError (checks: Result<unit, string> list) : Result<unit, string> =
        let rec loop remaining =
            match remaining with
            | [] -> Ok()
            | Ok() :: tail -> loop tail
            | Error message :: _ -> Error message

        loop checks

    let private nonEmpty name (value: string) =
        if String.IsNullOrWhiteSpace value then
            Error(name + " must not be empty")
        else
            Ok()

    let private validHash name value =
        if String.IsNullOrWhiteSpace value then
            Error(name + " must not be empty")
        elif isSha256 value || isProvisionalFixture value then
            Ok()
        else
            Error(name + " must be a lowercase hexadecimal SHA-256 value")

    let private duplicate name key values =
        match
            values
            |> List.groupBy key
            |> List.tryFind (fun (_, occurrences) -> occurrences.Length > 1)
        with
        | Some(value, _) -> Error(sprintf "%s contains duplicate '%O'" name value)
        | None -> Ok()

    let private ordinalStringCompare left right =
        StringComparer.Ordinal.Compare(left, right)

    let private dependencyCompare (leftId, leftRange) (rightId, rightRange) =
        let idComparison = ordinalStringCompare leftId rightId

        if idComparison <> 0 then
            idComparison
        else
            ordinalStringCompare leftRange rightRange

    let private runtimeCompare (left: RuntimeIdEntry) (right: RuntimeIdEntry) =
        let leftId = if isNull (box left) then "" else left.ContentId
        let rightId = if isNull (box right) then "" else right.ContentId
        ordinalStringCompare leftId rightId

    let private assetCompare (left: DataLockAsset) (right: DataLockAsset) =
        let leftId = if isNull (box left) then "" else left.AssetId
        let rightId = if isNull (box right) then "" else right.AssetId
        ordinalStringCompare leftId rightId

    let validate (dataLock: DataLock) : Result<unit, string> =
        if isNull (box dataLock) then
            Error "data lock must not be null"
        elif dataLock.FormatVersion <> 1 then
            Error "format version must be exactly 1"
        elif dataLock.ContentApi <> 1 then
            Error "content API must be exactly 1"
        else
            let snapshotCheck =
                if isNull (box dataLock.Snapshot) then
                    Error "snapshot must not be null"
                else
                    Ok()

            let packageCheck =
                if isNull (box dataLock.Packages) then
                    Error "packages must not be null"
                else
                    let packages = dataLock.Packages

                    let packageNullCheck =
                        packages
                        |> List.mapi (fun index package ->
                            if isNull (box package) then
                                Error(sprintf "package %d must not be null" index)
                            else
                                Ok())

                    match firstError packageNullCheck with
                    | Error message -> Error message
                    | Ok() ->
                        let packageIndexes =
                            packages |> List.mapi (fun index package -> package.ModId, index) |> Map.ofList

                        let validateDependencies index (package: DataLockPackage) =
                            if isNull (box package.Dependencies) then
                                Error(sprintf "package %s dependencies must not be null" package.ModId)
                            else
                                let dependencies = package.Dependencies

                                let dependencyChecks =
                                    dependencies
                                    |> List.mapi (fun dependencyIndex (dependencyId, range) ->
                                        let dependencyReferenceCheck =
                                            match Map.tryFind dependencyId packageIndexes with
                                            | None ->
                                                Error(
                                                    sprintf
                                                        "package %s dependency %s is not present in the lock"
                                                        package.ModId
                                                        dependencyId
                                                )
                                            | Some dependencyPackageIndex when dependencyPackageIndex >= index ->
                                                Error(
                                                    sprintf
                                                        "package %s dependency %s must precede the package"
                                                        package.ModId
                                                        dependencyId
                                                )
                                            | Some _ -> Ok()

                                        firstError
                                            [ nonEmpty
                                                  (sprintf "package %s dependency %d ID" package.ModId dependencyIndex)
                                                  dependencyId
                                              nonEmpty
                                                  (sprintf
                                                      "package %s dependency %d range"
                                                      package.ModId
                                                      dependencyIndex)
                                                  range
                                              dependencyReferenceCheck ])

                                let validatePrecedence name ids =
                                    if isNull (box ids) then
                                        Error(sprintf "package %s %s constraints must not be null" package.ModId name)
                                    else
                                        firstError
                                            [ if ids <> List.sortWith ordinalStringCompare ids then
                                                  Error(
                                                      sprintf
                                                          "package %s %s constraints are not in canonical order"
                                                          package.ModId
                                                          name
                                                  )
                                              else
                                                  Ok()
                                              duplicate (sprintf "package %s %s constraints" package.ModId name) id ids
                                              yield!
                                                  ids
                                                  |> List.mapi (fun precedenceIndex precedenceId ->
                                                      nonEmpty
                                                          (sprintf
                                                              "package %s %s constraint %d ID"
                                                              package.ModId
                                                              name
                                                              precedenceIndex)
                                                          precedenceId) ]

                                firstError
                                    [ if dependencies <> List.sortWith dependencyCompare dependencies then
                                          Error(
                                              sprintf "package %s dependencies are not in canonical order" package.ModId
                                          )
                                      else
                                          Ok()
                                      duplicate (sprintf "package %s dependencies" package.ModId) fst dependencies
                                      validatePrecedence "before" package.Before
                                      validatePrecedence "after" package.After
                                      yield! dependencyChecks ]

                        let packageChecks =
                            packages
                            |> List.mapi (fun index package ->
                                firstError
                                    [ nonEmpty (sprintf "package %d mod ID" index) package.ModId
                                      nonEmpty (sprintf "package %d version" index) package.Version
                                      if package.SchemaVersion <> 1 then
                                          Error(sprintf "package %s schema version must be exactly 1" package.ModId)
                                      else
                                          Ok()
                                      validHash
                                          (sprintf "package %s package SHA-256" package.ModId)
                                          package.PackageSha256
                                      validateDependencies index package ])

                        firstError
                            [ duplicate "packages" (fun package -> package.ModId) packages
                              yield! packageChecks ]

            let runtimeCheck =
                if isNull (box dataLock.RuntimeIdTable) then
                    Error "runtime ID table must not be null"
                else
                    let entries = dataLock.RuntimeIdTable

                    let duplicateContentIds =
                        duplicate
                            "runtime ID table"
                            (fun entry -> if isNull (box entry) then "<null>" else entry.ContentId)
                            entries

                    let duplicateRuntimeIds =
                        duplicate
                            "runtime ID table"
                            (fun entry ->
                                if isNull (box entry) then
                                    Int32.MinValue
                                else
                                    entry.RuntimeId)
                            entries

                    let contentChecks =
                        entries
                        |> List.mapi (fun index entry ->
                            if isNull (box entry) then
                                Error(sprintf "runtime ID entry %d must not be null" index)
                            else
                                nonEmpty (sprintf "runtime ID entry %d content ID" index) entry.ContentId)

                    let consecutiveCheck =
                        entries
                        |> List.mapi (fun index entry ->
                            if isNull (box entry) then
                                Error(sprintf "runtime ID entry %d must not be null" index)
                            elif entry.RuntimeId = index then
                                Ok()
                            else
                                Error(sprintf "runtime ID for %s must be %d" entry.ContentId index))

                    firstError
                        [ duplicateContentIds
                          duplicateRuntimeIds
                          if entries <> List.sortWith runtimeCompare entries then
                              Error "runtime ID table is not ordered by canonical content ID"
                          else
                              Ok()
                          yield! contentChecks
                          yield! consecutiveCheck ]

            let assetCheck =
                if isNull (box dataLock.RequiredAssets) then
                    Error "required assets must not be null"
                else
                    let assets = dataLock.RequiredAssets

                    let duplicateAssetIds =
                        duplicate
                            "required assets"
                            (fun asset -> if isNull (box asset) then "<null>" else asset.AssetId)
                            assets

                    let assetChecks =
                        assets
                        |> List.mapi (fun index asset ->
                            if isNull (box asset) then
                                Error(sprintf "asset %d must not be null" index)
                            else
                                firstError
                                    [ nonEmpty (sprintf "asset %d ID" index) asset.AssetId
                                      validHash (sprintf "asset %s SHA-256" asset.AssetId) asset.Sha256 ])

                    firstError
                        [ duplicateAssetIds
                          if assets <> List.sortWith assetCompare assets then
                              Error "required assets are not ordered by canonical asset ID"
                          else
                              Ok()
                          yield! assetChecks ]

            firstError
                [ nonEmpty "engine version" dataLock.EngineVersion
                  validHash "dependency lock hash" dataLock.DependencyLockHash
                  validHash "resolved sports hash" dataLock.ResolvedSportsHash
                  validHash "resolved presentation hash" dataLock.ResolvedPresentationHash
                  snapshotCheck
                  packageCheck
                  runtimeCheck
                  assetCheck ]
