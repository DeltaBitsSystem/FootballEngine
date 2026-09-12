namespace FootballEngine.Core.Tests.GameData

open System
open System.IO
open System.Text
open System.Text.Json
open Expecto
open FootballEngine.Data

module ModPackageIoTests =
    let private loadPackageContract: string -> Result<PackageSelection, ModError> =
        ModSystem.loadPackage

    let private loadInstalledContract
        : string -> string -> string list -> string list -> Result<ResolvedDataSet, ModError> =
        ModSystem.loadInstalled

    let private expectOk context (result: Result<'a, ModError>) : 'a =
        match result with
        | Ok value -> value
        | Error error -> failtestf "%s: unexpected error %A" context error

    let private expectError context (result: Result<'a, ModError>) =
        match result with
        | Error error -> error
        | Ok value -> failtestf "%s unexpectedly succeeded: %A" context value

    let private jsonString (value: string) = JsonSerializer.Serialize(value)

    let private utf8Bytes (text: string) = Encoding.UTF8.GetBytes text

    let private sha256Text text =
        text |> utf8Bytes |> ModSystem.hashSha256

    let private withTempDirectory (body: string -> unit) =
        let root =
            Path.Combine(Path.GetTempPath(), "football-engine-mod-io-" + Guid.NewGuid().ToString("N"))

        Directory.CreateDirectory root |> ignore

        try
            body root
        finally
            if Directory.Exists root then
                Directory.Delete(root, true)

    let private manifestWithoutPackageHash
        (formatVersion: int option)
        (modId: string)
        (version: string)
        (dependencies: (string * string) list)
        (priority: int)
        (declaredFiles: (string * string) list)
        =
        let formatVersionField =
            match formatVersion with
            | Some value -> sprintf "\"formatVersion\": %d," value
            | None -> ""

        let dependencyJson =
            dependencies
            |> List.map (fun (dependencyId, range) ->
                sprintf "{\"modId\":%s,\"range\":%s}" (jsonString dependencyId) (jsonString range))
            |> String.concat ","

        let fileJson =
            declaredFiles
            |> List.map (fun (path, hash) -> sprintf "{\"path\":%s,\"sha256\":%s}" (jsonString path) (jsonString hash))
            |> String.concat ","

        sprintf
            """{
  "format": "football-engine.mod",
  %s
  "modId": %s,
  "version": %s,
  "engine": {
    "minInclusive": "2026.1.0",
    "maxExclusive": "2027.0.0"
  },
  "api": {
    "content": 1,
    "scripts": 0
  },
  "namespace": %s,
  "title": "Test package",
  "authors": [
    {
      "id": "community.test",
      "displayName": "Test"
    }
  ],
  "description": "Package IO fixture",
  "dependencies": [%s],
  "precedence": {
    "layer": %d,
    "after": [],
    "before": []
  },
  "files": [%s],
  "signature": null
}"""
            formatVersionField
            (jsonString modId)
            (jsonString version)
            (jsonString modId)
            dependencyJson
            priority
            fileJson

    let private packageSha256 manifestWithoutHash (actualFiles: (string * byte[]) list) =
        let canonicalManifest =
            ModSystem.canonicalizeJson manifestWithoutHash
            |> expectOk "manifest without packageSha256 must be canonical JSON"

        let canonicalFiles =
            actualFiles
            |> List.sortWith (fun (left, _) (right, _) -> StringComparer.Ordinal.Compare(left, right))
            |> List.map (fun (path, bytes) -> path + "\n" + ModSystem.hashSha256 bytes)
            |> String.concat "\n"

        ModSystem.hashSha256 (utf8Bytes (canonicalManifest + "\n" + canonicalFiles))

    let private writeWorkspace
        (root: string)
        (directoryName: string)
        (modId: string)
        (version: string)
        (dependencies: (string * string) list)
        (priority: int)
        (formatVersion: int option)
        (files: (string * string) list)
        (declaredFiles: (string * string) list option)
        =
        let workspace = Path.Combine(root, directoryName)
        Directory.CreateDirectory workspace |> ignore

        let actualFiles = files |> List.map (fun (path, text) -> path, utf8Bytes text)

        for relativePath, bytes in actualFiles do
            let filePath =
                Path.Combine(workspace, relativePath.Replace('/', Path.DirectorySeparatorChar))

            let parent = Path.GetDirectoryName filePath

            if not (String.IsNullOrEmpty parent) then
                Directory.CreateDirectory parent |> ignore

            File.WriteAllBytes(filePath, bytes)

        let defaultDeclarations =
            actualFiles |> List.map (fun (path, bytes) -> path, ModSystem.hashSha256 bytes)

        let declarations =
            declaredFiles
            |> Option.defaultValue defaultDeclarations
            |> List.sortWith (fun (left, _) (right, _) -> StringComparer.Ordinal.Compare(left, right))

        let manifestWithoutHash =
            manifestWithoutPackageHash formatVersion modId version dependencies priority declarations

        let packageHash = packageSha256 manifestWithoutHash actualFiles

        let manifest =
            manifestWithoutHash.Replace(
                "  \"signature\": null",
                sprintf "  \"packageSha256\": \"%s\",\n  \"signature\": null" packageHash
            )

        File.WriteAllText(Path.Combine(workspace, "manifest.json"), manifest, UTF8Encoding(false))
        workspace

    let private writePackage root directoryName modId priority dependencies files =
        writeWorkspace root directoryName modId "1.0.0" dependencies priority (Some 1) files None

    let private makeInstalledRoots root =
        let builtinsDir = Path.Combine(root, "builtins")
        let modsDir = Path.Combine(root, "mods")
        Directory.CreateDirectory builtinsDir |> ignore
        Directory.CreateDirectory modsDir |> ignore
        builtinsDir, modsDir

    let private clubJson modId localId =
        sprintf
            """{
  "id": "%s:club/%s",
  "kind": "club",
  "schema": 1
}"""
            modId
            localId

    let private countryJson modId =
        sprintf
            """{
  "id": "%s:country/argentina",
  "kind": "country",
  "schema": 1,
  "code": "ARG",
  "confederation": "CONMEBOL",
  "nameKey": "%s:country/argentina.name"
}"""
            modId
            modId

    let private translationJson modId =
        sprintf
            """{
  "id": "%s:translation/es-ar",
  "kind": "translation",
  "schema": 1,
  "locale": "es-AR",
  "entries": {
    "%s:country/argentina.name": "Argentina"
  }
}"""
            modId
            modId

    let private assetJson modId =
        sprintf
            """{
  "id": "%s:asset/crest",
  "kind": "asset",
  "schema": 1,
  "sha256": "%s",
  "mime": "image/svg+xml",
  "bytes": 6,
  "width": 1,
  "height": 1,
  "usage": "logo",
  "required": false
}"""
            modId
            (String.replicate 64 "a")

    let private patchJson modId =
        sprintf
            """{
  "target": "%s:country/argentina",
  "kind": "patch",
  "schema": 1,
  "operations": [
    {
      "op": "replace",
      "path": "/nameKey",
      "value": "%s:country/argentina.updatedName"
    }
   ]
}"""
            modId
            modId

    let private packageIds (resolved: ResolvedDataSet) =
        resolved.Packages |> List.map (fun package -> package.Manifest.ModId)

    let private expectDiagnostic code relativePath (error: ModError) =
        Expect.isTrue
            (error.Diagnostics
             |> List.exists (fun diagnostic -> diagnostic.Code = code && diagnostic.RelativePath = Some relativePath))
            (sprintf "expected diagnostic %s for %s, got %A" code relativePath error.Diagnostics)

    let tests =
        testList
            "FootballEngine.Core.GameData.ModPackageIo"
            [ testCase "loads a valid workspace package"
              <| fun _ ->
                  withTempDirectory (fun root ->
                      let countryPath = "content/countries/community.valid/country.json"

                      let workspace =
                          writePackage
                              root
                              "valid-workspace"
                              "community.valid"
                              0
                              []
                              [ countryPath, countryJson "community.valid" ]

                      let selection =
                          loadPackageContract workspace |> expectOk "a valid workspace must load"

                      let countryId = ContentId "community.valid:country/argentina"

                      Expect.equal selection.Package.Manifest.ModId "community.valid" "manifest ID is declared"
                      Expect.equal selection.Package.Manifest.Version "1.0.0" "manifest version is preserved"
                      Expect.isTrue (Map.containsKey countryId selection.Package.SportsContent) "country is loaded"
                      Expect.isTrue selection.Package.Translations.IsEmpty "no translation was declared")

              testCase "rejects missing manifest, missing declared file, and invalid manifest JSON"
              <| fun _ ->
                  withTempDirectory (fun root ->
                      let missingManifest = Path.Combine(root, "missing-manifest")
                      Directory.CreateDirectory missingManifest |> ignore

                      loadPackageContract missingManifest
                      |> expectError "a workspace without manifest.json must fail"
                      |> ignore

                      let missingPath = "content/missing/entity.json"

                      let missingDeclaredFile =
                          writeWorkspace
                              root
                              "missing-file"
                              "community.missing"
                              "1.0.0"
                              []
                              0
                              (Some 1)
                              []
                              (Some [ missingPath, String.replicate 64 "0" ])

                      let missingFileError =
                          loadPackageContract missingDeclaredFile
                          |> expectError "a declared file that is absent must fail"

                      expectDiagnostic "MOD-PACKAGE-FILE-MISSING" missingPath missingFileError

                      let invalidManifest = Path.Combine(root, "invalid-manifest")
                      Directory.CreateDirectory invalidManifest |> ignore

                      File.WriteAllText(
                          Path.Combine(invalidManifest, "manifest.json"),
                          "{ not valid json",
                          UTF8Encoding(false)
                      )

                      let invalidManifestError =
                          loadPackageContract invalidManifest
                          |> expectError "invalid manifest JSON must fail"

                      expectDiagnostic "MOD-MANIFEST-JSON" "manifest.json" invalidManifestError)

              testCase "rejects undeclared files and incorrect declared hashes"
              <| fun _ ->
                  withTempDirectory (fun root ->
                      let countryPath = "content/countries/community.integrity/country.json"
                      let extraPath = "content/countries/community.integrity/extra.json"
                      let country = countryJson "community.integrity"

                      let undeclared =
                          writeWorkspace
                              root
                              "undeclared"
                              "community.integrity"
                              "1.0.0"
                              []
                              0
                              (Some 1)
                              [ countryPath, country; extraPath, "{}" ]
                              (Some [ countryPath, sha256Text country ])

                      let undeclaredError =
                          loadPackageContract undeclared |> expectError "an undeclared file must fail"

                      expectDiagnostic "MOD-PACKAGE-FILE-UNDECLARED" extraPath undeclaredError

                      let wrongHash =
                          writeWorkspace
                              root
                              "wrong-hash"
                              "community.integrity"
                              "1.0.0"
                              []
                              0
                              (Some 1)
                              [ countryPath, country ]
                              (Some [ countryPath, String.replicate 64 "f" ])

                      let wrongHashError =
                          loadPackageContract wrongHash |> expectError "a wrong declared hash must fail"

                      expectDiagnostic "MOD-PACKAGE-FILE-HASH" countryPath wrongHashError)

              testCase "reversing discovered directory creation order preserves the resolved hash"
              <| fun _ ->
                  withTempDirectory (fun root ->
                      let firstRoot = Path.Combine(root, "first")
                      let secondRoot = Path.Combine(root, "second")
                      let firstBuiltins, firstMods = makeInstalledRoots firstRoot
                      let secondBuiltins, secondMods = makeInstalledRoots secondRoot

                      let writeBase modsDir directoryName =
                          writePackage
                              modsDir
                              directoryName
                              "community.base"
                              0
                              []
                              [ "content/base.json", clubJson "community.base" "base" ]
                          |> ignore

                      let writeAddon modsDir directoryName =
                          writePackage
                              modsDir
                              directoryName
                              "community.addon"
                              10
                              [ "community.base", ">=1.0.0 <2.0.0" ]
                              [ "content/addon.json", clubJson "community.addon" "addon" ]
                          |> ignore

                      writeBase firstMods "01-base"
                      writeAddon firstMods "02-addon"
                      writeAddon secondMods "02-addon"
                      writeBase secondMods "01-base"

                      let firstResolved =
                          loadInstalledContract firstBuiltins firstMods [] []
                          |> expectOk "first discovery order must resolve"

                      let secondResolved =
                          loadInstalledContract secondBuiltins secondMods [] []
                          |> expectOk "reversed discovery order must resolve"

                      Expect.equal
                          firstResolved.ResolvedSportsHash
                          secondResolved.ResolvedSportsHash
                          "filesystem discovery order cannot change the sports hash"

                      Expect.equal firstResolved secondResolved "the complete resolved dataset is stable"

                      Expect.equal
                          (packageIds firstResolved)
                          [ "community.base"; "community.addon" ]
                          "empty activeModIds selects every discovered package in lock order")

              testCase "activeModIds excludes a discovered package that is not selected"
              <| fun _ ->
                  withTempDirectory (fun root ->
                      let builtinsDir, modsDir = makeInstalledRoots root

                      writePackage
                          modsDir
                          "selected"
                          "community.selected"
                          0
                          []
                          [ "content/selected.json", clubJson "community.selected" "selected" ]
                      |> ignore

                      writePackage
                          modsDir
                          "ignored"
                          "community.ignored"
                          10
                          []
                          [ "content/ignored.json", clubJson "community.ignored" "ignored" ]
                      |> ignore

                      let resolved =
                          loadInstalledContract builtinsDir modsDir [ "community.selected" ] [ "community.selected" ]
                          |> expectOk "the explicit active selection must resolve"

                      Expect.equal
                          (packageIds resolved)
                          [ "community.selected" ]
                          "unselected packages stay out of the lock"

                      Expect.isTrue
                          (Map.containsKey (ContentId "community.selected:club/selected") resolved.SportsContent)
                          "selected content is loaded"

                      Expect.isFalse
                          (Map.containsKey (ContentId "community.ignored:club/ignored") resolved.SportsContent)
                          "unselected content is not loaded")

              testCase "active selection includes direct and transitive dependencies"
              <| fun _ ->
                  withTempDirectory (fun root ->
                      let builtinsDir, modsDir = makeInstalledRoots root

                      writePackage
                          builtinsDir
                          "base"
                          "community.base"
                          0
                          []
                          [ "content/base.json", clubJson "community.base" "base" ]
                      |> ignore

                      writePackage
                          modsDir
                          "middle"
                          "community.middle"
                          10
                          [ "community.base", ">=1.0.0 <2.0.0" ]
                          [ "content/middle.json", clubJson "community.middle" "middle" ]
                      |> ignore

                      writePackage
                          modsDir
                          "leaf"
                          "community.leaf"
                          20
                          [ "community.middle", ">=1.0.0 <2.0.0" ]
                          [ "content/leaf.json", clubJson "community.leaf" "leaf" ]
                      |> ignore

                      let resolved =
                          loadInstalledContract builtinsDir modsDir [ "community.leaf" ] [ "community.leaf" ]
                          |> expectOk "a selected mod must bring all dependencies"

                      Expect.equal
                          (packageIds resolved)
                          [ "community.base"; "community.middle"; "community.leaf" ]
                          "dependency lock contains the transitive closure in stable order")

              testCase "a missing active mod is an error"
              <| fun _ ->
                  withTempDirectory (fun root ->
                      let builtinsDir, modsDir = makeInstalledRoots root

                      loadInstalledContract builtinsDir modsDir [ "community.missing" ] []
                      |> expectError "an active mod ID with no package must fail"
                      |> ignore)

              testCase "modOrder cannot change the deterministic dependency lock"
              <| fun _ ->
                  withTempDirectory (fun root ->
                      let builtinsDir, modsDir = makeInstalledRoots root

                      writePackage
                          modsDir
                          "high"
                          "community.high"
                          20
                          []
                          [ "content/high.json", clubJson "community.high" "high" ]
                      |> ignore

                      writePackage
                          modsDir
                          "low"
                          "community.low"
                          10
                          []
                          [ "content/low.json", clubJson "community.low" "low" ]
                      |> ignore

                      let first =
                          loadInstalledContract
                              builtinsDir
                              modsDir
                              [ "community.high"; "community.low" ]
                              [ "community.high"; "community.low" ]
                          |> expectOk "first settings order must resolve"

                      let second =
                          loadInstalledContract
                              builtinsDir
                              modsDir
                              [ "community.high"; "community.low" ]
                              [ "community.low"; "community.high" ]
                          |> expectOk "reversed settings order must resolve"

                      Expect.equal first second "settings order cannot replace the deterministic lock"

                      Expect.equal
                          (packageIds first)
                          [ "community.low"; "community.high" ]
                          "the lock uses precedence layer before settings order")

              testCase "content kind determines the destination map or patch list"
              <| fun _ ->
                  withTempDirectory (fun root ->
                      let modId = "community.kinds"

                      let workspace =
                          writePackage
                              root
                              "kind-workspace"
                              modId
                              0
                              []
                              [ "content/country.json", countryJson modId
                                "content/translation.json", translationJson modId
                                "content/asset.json", assetJson modId
                                "content/patch.json", patchJson modId ]

                      let package =
                          loadPackageContract workspace
                          |> expectOk "all supported content kinds must load"
                          |> fun selection -> selection.Package

                      Expect.isTrue
                          (Map.containsKey (ContentId "community.kinds:country/argentina") package.SportsContent)
                          "country belongs to SportsContent"

                      Expect.isTrue
                          (Map.containsKey (ContentId "community.kinds:translation/es-ar") package.Translations)
                          "translation belongs to Translations"

                      Expect.isTrue
                          (Map.containsKey (ContentId "community.kinds:asset/crest") package.Assets)
                          "asset belongs to Assets"

                      Expect.equal package.Patches.Length 1 "patch belongs to Patches"

                      let patchTarget operation =
                          match operation with
                          | PatchOperation.Add(contentId, _)
                          | PatchOperation.Replace(contentId, _)
                          | PatchOperation.Remove contentId
                          | PatchOperation.Assert(contentId, _) -> contentId

                      match package.Patches with
                      | [ operation ] ->
                          Expect.equal
                              (patchTarget operation)
                              (ContentId "community.kinds:country/argentina")
                              "patch target is preserved"
                      | operations -> failtestf "expected one patch operation, got %A" operations)

              testCase "rejects packages without exactly formatVersion 1"
              <| fun _ ->
                  withTempDirectory (fun root ->
                      let legacyWithoutVersion =
                          writeWorkspace
                              root
                              "legacy-without-version"
                              "community.legacy"
                              "1.0.0"
                              []
                              0
                              None
                              [ "content/legacy.json", clubJson "community.legacy" "legacy" ]
                              None

                      let missingVersionError =
                          loadPackageContract legacyWithoutVersion
                          |> expectError "a manifest without formatVersion must fail"

                      Expect.isTrue
                          (missingVersionError.Diagnostics
                           |> List.exists (fun diagnostic -> diagnostic.Code = "MOD-MANIFEST-FORMAT-VERSION"))
                          "missing formatVersion must not receive a legacy default"

                      let legacyVersionZero =
                          writeWorkspace
                              root
                              "legacy-version-zero"
                              "community.legacy.zero"
                              "1.0.0"
                              []
                              0
                              (Some 0)
                              [ "content/legacy.json", clubJson "community.legacy.zero" "legacy" ]
                              None

                      let versionZeroError =
                          loadPackageContract legacyVersionZero |> expectError "formatVersion 0 must fail"

                      Expect.isTrue
                          (versionZeroError.Diagnostics
                           |> List.exists (fun diagnostic -> diagnostic.Code = "MOD-MANIFEST-FORMAT-VERSION"))
                          "formatVersion 0 must not be migrated")

              testCase "loads femod packages and applies explicit modOrder"
              <| fun _ ->
                  withTempDirectory (fun root ->
                      let firstWorkspace =
                          writePackage
                              root
                              "first-workspace"
                              "community.first"
                              10
                              []
                              [ "content/first.json", clubJson "community.first" "first" ]

                      let secondWorkspace =
                          writePackage
                              root
                              "second-workspace"
                              "community.second"
                              10
                              []
                              [ "content/second.json", clubJson "community.second" "second" ]

                      let builtinsDir, modsDir = makeInstalledRoots root

                      ModSystem.packWorkspace firstWorkspace (Path.Combine(builtinsDir, "first.femod"))
                      |> expectOk "first workspace must pack"
                      |> ignore

                      ModSystem.packWorkspace secondWorkspace (Path.Combine(builtinsDir, "second.femod"))
                      |> expectOk "second workspace must pack"
                      |> ignore

                      let tied =
                          ModSystem.loadInstalledPackages builtinsDir modsDir [] []
                          |> expectError "equal priorities without order must fail"

                      Expect.isTrue
                          (tied.Diagnostics
                           |> List.exists (fun diagnostic -> diagnostic.Code = "MOD-PRECEDENCE-TIE"))
                          "same-priority packages require explicit precedence"

                      let resolved =
                          ModSystem.loadInstalledPackages
                              builtinsDir
                              modsDir
                              []
                              [ "community.first"; "community.second" ]
                          |> expectOk "explicit modOrder must resolve the packages"

                      Expect.equal
                          (packageIds resolved)
                          [ "community.first"; "community.second" ]
                          "explicit order becomes the deterministic package lock")

              ]
