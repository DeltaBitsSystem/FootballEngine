namespace FootballEngine.Core.Tests.GameData

open System
open System.Text
open Expecto
open FootballEngine.Data

module ModContractTests =

    let private currentFormatVersion = 1
    let private currentContentApi = 1
    let private disabledScriptsApi = 0
    let private zeroSha256 = String.replicate 64 "0"

    let private countryPath = "content/countries/community.argentina/country.json"

    let private expectOk context result =
        match result with
        | Ok value -> value
        | Error error -> failtestf "%s: %A" context error

    let private expectError context result =
        match result with
        | Error error -> error
        | Ok value -> failtestf "%s unexpectedly succeeded: %A" context value

    let private expectDiagnostic
        (code: string)
        (relativePath: string)
        (jsonPath: string option)
        (actionFragment: string)
        (error: ModError)
        =
        let diagnostic: ModDiagnostic option =
            error.Diagnostics
            |> List.tryFind (fun diagnostic -> diagnostic.Code = code && diagnostic.RelativePath = Some relativePath)

        match diagnostic with
        | None -> failtestf "expected diagnostic %s at %s, got %A" code relativePath error.Diagnostics
        | Some diagnostic ->
            Expect.equal diagnostic.Code code "diagnostic code is stable"
            Expect.equal diagnostic.RelativePath (Some relativePath) "diagnostic path is relative"
            Expect.equal diagnostic.JsonPath jsonPath "diagnostic includes the JSON path"
            Expect.isFalse (String.IsNullOrWhiteSpace diagnostic.Message) "diagnostic includes a message"

            Expect.isFalse
                (String.IsNullOrWhiteSpace diagnostic.SuggestedAction)
                "diagnostic includes a concrete action"

            Expect.stringContains
                diagnostic.SuggestedAction
                actionFragment
                "diagnostic action explains how to repair the input"

    let private countryJson (kind: string) (confederation: string) =
        sprintf
            """{
  "id": "community.argentina:country/argentina",
  "kind": "%s",
  "schema": 1,
  "code": "ARG",
  "confederation": "%s",
  "nameKey": "community.argentina:country/argentina.name"
}"""
            kind
            confederation

    let private countryBytes =
        countryJson "country" "CONMEBOL" |> Encoding.UTF8.GetBytes

    let private validDeclaredFiles = [ countryPath, ModSystem.hashSha256 countryBytes ]

    let private fileDeclarations (files: (string * string) list) =
        files
        |> List.map (fun (path, sha256) -> sprintf "{\"path\":\"%s\",\"sha256\":\"%s\"}" path sha256)
        |> String.concat ","

    let private manifestJson
        (formatVersionField: string)
        (contentApi: int)
        (scriptsApi: int)
        (modId: string)
        (namespaceName: string)
        (version: string)
        (engineMinInclusive: string)
        (engineMaxExclusive: string)
        (dependencyRange: string)
        (declaredFiles: (string * string) list)
        (packageSha256: string option)
        (extraFields: string)
        =
        let packageHashField =
            packageSha256
            |> Option.map (fun hash -> sprintf "\"packageSha256\": \"%s\"," hash)
            |> Option.defaultValue ""

        sprintf
            """{
  "format": "football-engine.mod",
  %s
  "modId": "%s",
  "version": "%s",
  "engine": {
    "minInclusive": "%s",
    "maxExclusive": "%s"
  },
  "api": {
    "content": %d,
    "scripts": %d
  },
  "namespace": "%s",
  "title": "Argentina Data",
  "authors": [
    {
      "id": "community.pablo",
      "displayName": "Pablo"
    }
  ],
  "description": "Declarative Argentina data",
  "dependencies": [
    {
      "modId": "football-engine.base",
      "range": "%s"
    }
  ],
  "precedence": {
    "layer": 100,
    "after": ["football-engine.base"],
    "before": []
  },
  "files": [%s],
  %s
  %s
  "signature": null
}"""
            formatVersionField
            modId
            version
            engineMinInclusive
            engineMaxExclusive
            contentApi
            scriptsApi
            namespaceName
            dependencyRange
            (fileDeclarations declaredFiles)
            extraFields
            packageHashField

    let private standardManifestWithoutPackageHash declaredFiles =
        manifestJson
            (sprintf "\"formatVersion\": %d," currentFormatVersion)
            currentContentApi
            disabledScriptsApi
            "community.argentina"
            "community.argentina"
            "1.4.0"
            "2026.1.0"
            "2027.0.0"
            ">=1.0.0 <2.0.0"
            declaredFiles
            None
            ""

    let private canonicalJson context json =
        ModSystem.canonicalizeJson json |> expectOk context

    // The package identity excludes packageSha256 and then indexes all files by canonical path and bytes hash.
    let private packageSha256 manifestWithoutPackageHash (actualFiles: (string * byte[]) list) =
        let canonicalManifest =
            canonicalJson "manifest without packageSha256 must canonicalize" manifestWithoutPackageHash

        let canonicalFiles =
            actualFiles
            |> Map.ofList
            |> Map.toList
            |> List.sortBy fst
            |> List.map (fun (path, bytes) -> path + "\n" + ModSystem.hashSha256 bytes)
            |> String.concat "\n"

        ModSystem.hashSha256 (Encoding.UTF8.GetBytes(canonicalManifest + "\n" + canonicalFiles))

    let private makePackage
        (declaredFiles: (string * string) list)
        (actualFiles: (string * byte[]) list)
        (symlinkPaths: string list)
        : PackageInput =
        let manifestWithoutPackageHash = standardManifestWithoutPackageHash declaredFiles
        let packageHash = packageSha256 manifestWithoutPackageHash actualFiles

        let manifestWithPackageHash =
            manifestWithoutPackageHash.Replace(
                "\"signature\": null",
                sprintf "\"packageSha256\": \"%s\",\n  \"signature\": null" packageHash
            )

        { ManifestJson = manifestWithPackageHash
          Files = actualFiles |> Map.ofList
          SymlinkPaths = symlinkPaths |> Set.ofList }

    let private validPackage () =
        makePackage validDeclaredFiles [ countryPath, countryBytes ] []

    let private parseFixture
        (formatVersionField: string)
        (contentApi: int)
        (scriptsApi: int)
        (modId: string)
        (namespaceName: string)
        (version: string)
        (engineMinInclusive: string)
        (engineMaxExclusive: string)
        (dependencyRange: string)
        (extraFields: string)
        =
        manifestJson
            formatVersionField
            contentApi
            scriptsApi
            modId
            namespaceName
            version
            engineMinInclusive
            engineMaxExclusive
            dependencyRange
            validDeclaredFiles
            (Some zeroSha256)
            extraFields

    let private standardParseFixture =
        parseFixture
            (sprintf "\"formatVersion\": %d," currentFormatVersion)
            currentContentApi
            disabledScriptsApi
            "community.argentina"
            "community.argentina"
            "1.4.0"
            "2026.1.0"
            "2027.0.0"
            ">=1.0.0 <2.0.0"
            ""

    let tests =
        testList
            "FootballEngine.Core.GameData.ModContract"
            [ test "a valid manifest is parsed without changing identity fields" {
                  let manifest: ModManifest =
                      ModSystem.parseManifest standardParseFixture |> expectOk "valid manifest"

                  Expect.equal manifest.FormatVersion currentFormatVersion "format version is preserved"
                  Expect.equal manifest.ModId "community.argentina" "mod ID is declared, not derived from a folder"
                  Expect.equal manifest.Namespace "community.argentina" "namespace is preserved"
                  Expect.equal manifest.Version "1.4.0" "strict SemVer is preserved"
              }

              test "missing formatVersion is rejected instead of receiving a legacy default" {
                  let json =
                      parseFixture
                          ""
                          currentContentApi
                          disabledScriptsApi
                          "community.argentina"
                          "community.argentina"
                          "1.4.0"
                          "2026.1.0"
                          "2027.0.0"
                          ">=1.0.0 <2.0.0"
                          ""

                  let error = ModSystem.parseManifest json |> expectError "missing formatVersion"

                  expectDiagnostic
                      "MOD-MANIFEST-FORMAT-VERSION"
                      "manifest.json"
                      (Some "/formatVersion")
                      "formatVersion"
                      error
              }

              test "an older formatVersion is rejected without migration" {
                  let json =
                      parseFixture
                          "\"formatVersion\": 0,"
                          currentContentApi
                          disabledScriptsApi
                          "community.argentina"
                          "community.argentina"
                          "1.4.0"
                          "2026.1.0"
                          "2027.0.0"
                          ">=1.0.0 <2.0.0"
                          ""

                  let error = ModSystem.parseManifest json |> expectError "older formatVersion"

                  expectDiagnostic
                      "MOD-MANIFEST-FORMAT-VERSION"
                      "manifest.json"
                      (Some "/formatVersion")
                      "version 1"
                      error
              }

              test "a future formatVersion is rejected instead of being partially read" {
                  let json =
                      parseFixture
                          "\"formatVersion\": 2,"
                          currentContentApi
                          disabledScriptsApi
                          "community.argentina"
                          "community.argentina"
                          "1.4.0"
                          "2026.1.0"
                          "2027.0.0"
                          ">=1.0.0 <2.0.0"
                          ""

                  let error = ModSystem.parseManifest json |> expectError "future formatVersion"

                  expectDiagnostic
                      "MOD-MANIFEST-FORMAT-VERSION"
                      "manifest.json"
                      (Some "/formatVersion")
                      "version 1"
                      error
              }

              test "an incompatible content API is fatal" {
                  let json =
                      parseFixture
                          "\"formatVersion\": 1,"
                          2
                          disabledScriptsApi
                          "community.argentina"
                          "community.argentina"
                          "1.4.0"
                          "2026.1.0"
                          "2027.0.0"
                          ">=1.0.0 <2.0.0"
                          ""

                  let error = ModSystem.parseManifest json |> expectError "incompatible content API"

                  expectDiagnostic "MOD-MANIFEST-CONTENT-API" "manifest.json" (Some "/api/content") "content API" error
              }

              test "a nonzero scripts API is rejected in v1" {
                  let json =
                      parseFixture
                          "\"formatVersion\": 1,"
                          currentContentApi
                          1
                          "community.argentina"
                          "community.argentina"
                          "1.4.0"
                          "2026.1.0"
                          "2027.0.0"
                          ">=1.0.0 <2.0.0"
                          ""

                  let error = ModSystem.parseManifest json |> expectError "nonzero scripts API"

                  expectDiagnostic
                      "MOD-MANIFEST-SCRIPTS-API"
                      "manifest.json"
                      (Some "/api/scripts")
                      "scripts API 0"
                      error
              }

              test "invalid mod ID and namespace are rejected at the manifest boundary" {
                  let invalidModId =
                      parseFixture
                          "\"formatVersion\": 1,"
                          currentContentApi
                          disabledScriptsApi
                          "Community.Argentina"
                          "community.argentina"
                          "1.4.0"
                          "2026.1.0"
                          "2027.0.0"
                          ">=1.0.0 <2.0.0"
                          ""

                  let modIdError =
                      ModSystem.parseManifest invalidModId |> expectError "uppercase mod ID"

                  expectDiagnostic "MOD-ID-INVALID" "manifest.json" (Some "/modId") "lowercase" modIdError

                  let invalidNamespace =
                      parseFixture
                          "\"formatVersion\": 1,"
                          currentContentApi
                          disabledScriptsApi
                          "community.argentina"
                          "community argentina"
                          "1.4.0"
                          "2026.1.0"
                          "2027.0.0"
                          ">=1.0.0 <2.0.0"
                          ""

                  let namespaceError =
                      ModSystem.parseManifest invalidNamespace |> expectError "spaced namespace"

                  expectDiagnostic
                      "MOD-NAMESPACE-INVALID"
                      "manifest.json"
                      (Some "/namespace")
                      "namespace"
                      namespaceError
              }

              test "ContentId keeps namespace, kind and local ID as stable identity components" {
                  let contentId: ContentId =
                      ModSystem.parseContentId "community.argentina:club/river-plate"
                      |> expectOk "valid content ID"

                  Expect.equal contentId.Namespace "community.argentina" "namespace is stable"
                  Expect.equal contentId.Kind "club" "kind is stable"
                  Expect.equal contentId.LocalId "river-plate" "local ID is stable"
              }

              test "ContentId rejects names, spaces, uppercase and malformed separators" {
                  let invalidIds =
                      [ "Community.argentina:club/river-plate"
                        "community argentina:club/river-plate"
                        "community.argentina:Club/river-plate"
                        "community.argentina:club"
                        "community.argentina/club/river-plate"
                        "community.argentina:club/river plate"
                        "community.argentina:club/river?plate" ]

                  for invalidId in invalidIds do
                      let error =
                          ModSystem.parseContentId invalidId
                          |> expectError ("invalid ContentId " + invalidId)

                      Expect.isTrue
                          (error.Diagnostics
                           |> List.exists (fun diagnostic -> diagnostic.Code = "MOD-CONTENT-ID-INVALID"))
                          ("invalid ContentId must have a stable code: " + invalidId)
              }

              test "strict SemVer rejects abbreviated mod versions" {
                  let json =
                      parseFixture
                          "\"formatVersion\": 1,"
                          currentContentApi
                          disabledScriptsApi
                          "community.argentina"
                          "community.argentina"
                          "1.4"
                          "2026.1.0"
                          "2027.0.0"
                          ">=1.0.0 <2.0.0"
                          ""

                  let error = ModSystem.parseManifest json |> expectError "abbreviated SemVer"

                  expectDiagnostic "MOD-SEMVER-INVALID" "manifest.json" (Some "/version") "SemVer" error
              }

              test "engine and dependency ranges require strict SemVer syntax" {
                  let invalidEngineVersion =
                      parseFixture
                          "\"formatVersion\": 1,"
                          currentContentApi
                          disabledScriptsApi
                          "community.argentina"
                          "community.argentina"
                          "1.4.0"
                          "2026.1"
                          "2027.0.0"
                          ">=1.0.0 <2.0.0"
                          ""

                  let engineError =
                      ModSystem.parseManifest invalidEngineVersion
                      |> expectError "abbreviated engine SemVer"

                  expectDiagnostic
                      "MOD-SEMVER-INVALID"
                      "manifest.json"
                      (Some "/engine/minInclusive")
                      "SemVer"
                      engineError

                  let invalidRange =
                      parseFixture
                          "\"formatVersion\": 1,"
                          currentContentApi
                          disabledScriptsApi
                          "community.argentina"
                          "community.argentina"
                          "1.4.0"
                          "2026.1.0"
                          "2027.0.0"
                          "latest"
                          ""

                  let rangeError =
                      ModSystem.parseManifest invalidRange |> expectError "invalid dependency range"

                  expectDiagnostic "MOD-RANGE-INVALID" "manifest.json" (Some "/dependencies/0/range") "range" rangeError
              }

              test "unknown and duplicate manifest keys are errors, not ignored fields" {
                  let unknownKey =
                      parseFixture
                          "\"formatVersion\": 1,"
                          currentContentApi
                          disabledScriptsApi
                          "community.argentina"
                          "community.argentina"
                          "1.4.0"
                          "2026.1.0"
                          "2027.0.0"
                          ">=1.0.0 <2.0.0"
                          "\"mystery\": true,"

                  let unknownError =
                      ModSystem.parseManifest unknownKey |> expectError "unknown manifest key"

                  expectDiagnostic "MOD-MANIFEST-UNKNOWN-KEY" "manifest.json" (Some "/mystery") "remove" unknownError

                  let duplicateKey =
                      parseFixture
                          "\"formatVersion\": 1,"
                          currentContentApi
                          disabledScriptsApi
                          "community.argentina"
                          "community.argentina"
                          "1.4.0"
                          "2026.1.0"
                          "2027.0.0"
                          ">=1.0.0 <2.0.0"
                          "\"title\": \"shadow title\","

                  let duplicateError =
                      ModSystem.parseManifest duplicateKey |> expectError "duplicate manifest key"

                  expectDiagnostic
                      "MOD-MANIFEST-DUPLICATE-KEY"
                      "manifest.json"
                      (Some "/title")
                      "remove duplicate"
                      duplicateError
              }

              test "canonicalization is stable when object property order changes" {
                  let first =
                      canonicalJson "first property order" """{ "b": 2, "nested": { "z": 4, "a": 3 }, "a": 1 }"""

                  let second =
                      canonicalJson "second property order" """{ "a": 1, "nested": { "a": 3, "z": 4 }, "b": 2 }"""

                  Expect.equal first second "property order cannot change canonical identity"
                  Expect.equal first "{\"a\":1,\"b\":2,\"nested\":{\"a\":3,\"z\":4}}" "canonical form is deterministic"
              }

              test "SHA-256 is sensitive to every byte rather than object identity or length alone" {
                  let oneZero = ModSystem.hashSha256 [| 0uy |]
                  let twoZeros = ModSystem.hashSha256 [| 0uy; 0uy |]
                  let oneByteChanged = ModSystem.hashSha256 [| 1uy |]

                  Expect.equal oneZero.Length 64 "SHA-256 is represented by 64 hexadecimal characters"
                  Expect.notEqual oneZero twoZeros "adding a byte changes the hash"
                  Expect.notEqual oneZero oneByteChanged "changing a byte changes the hash"
              }

              test "a valid package passes only after all declared file hashes are verified" {
                  match ModSystem.validatePackage (validPackage ()) with
                  | Ok() -> ()
                  | Error error -> failtestf "valid package was rejected: %A" error
              }

              test "an undeclared file is rejected with a relative path and repair action" {
                  let extraPath = "content/countries/community.argentina/extra.json"
                  let extraBytes = Encoding.UTF8.GetBytes "{}"

                  let package =
                      makePackage validDeclaredFiles [ countryPath, countryBytes; extraPath, extraBytes ] []

                  let error = ModSystem.validatePackage package |> expectError "undeclared file"

                  expectDiagnostic "MOD-PACKAGE-FILE-UNDECLARED" extraPath None "declare" error
              }

              test "a declared file with the wrong hash is rejected instead of loaded" {
                  let wrongHash = String.replicate 64 "f"

                  let package =
                      makePackage [ countryPath, wrongHash ] [ countryPath, countryBytes ] []

                  let error = ModSystem.validatePackage package |> expectError "wrong file hash"

                  expectDiagnostic "MOD-PACKAGE-FILE-HASH" countryPath None "recalculate" error
              }

              test "path traversal is rejected before package contents can escape the root" {
                  let traversalPath = "../outside.json"
                  let bytes = Encoding.UTF8.GetBytes "{}"

                  let package =
                      makePackage [ traversalPath, ModSystem.hashSha256 bytes ] [ traversalPath, bytes ] []

                  let error = ModSystem.validatePackage package |> expectError "path traversal"

                  expectDiagnostic "MOD-PATH-TRAVERSAL" traversalPath None "relative" error
              }

              test "symlinked package entries are rejected rather than followed" {
                  let package =
                      makePackage validDeclaredFiles [ countryPath, countryBytes ] [ countryPath ]

                  let error = ModSystem.validatePackage package |> expectError "symlink entry"

                  expectDiagnostic "MOD-PATH-SYMLINK" countryPath None "symlink" error
              }

              test "unknown content enum values fail instead of falling back to a known simulation value" {
                  let bytes = countryJson "country" "UNKNOWN_CONFEDERATION" |> Encoding.UTF8.GetBytes

                  let package =
                      makePackage [ countryPath, ModSystem.hashSha256 bytes ] [ countryPath, bytes ] []

                  let error = ModSystem.validatePackage package |> expectError "unknown enum value"

                  expectDiagnostic "MOD-ENUM-UNKNOWN" countryPath (Some "/confederation") "known" error
              }

              test "validation accumulates errors but never publishes a partial package" {
                  let extraPath = "content/countries/community.argentina/extra.json"

                  let brokenBytes =
                      countryJson "country" "UNKNOWN_CONFEDERATION" |> Encoding.UTF8.GetBytes

                  let package =
                      makePackage
                          [ countryPath, String.replicate 64 "f" ]
                          [ countryPath, brokenBytes; extraPath, Encoding.UTF8.GetBytes "{}" ]
                          []

                  let error =
                      ModSystem.validatePackage package |> expectError "multiple package errors"

                  Expect.isTrue (error.Diagnostics.Length >= 3) "all independent package errors are accumulated"

                  expectDiagnostic "MOD-PACKAGE-FILE-HASH" countryPath None "recalculate" error

                  expectDiagnostic "MOD-PACKAGE-FILE-UNDECLARED" extraPath None "declare" error

                  expectDiagnostic "MOD-ENUM-UNKNOWN" countryPath (Some "/confederation") "known" error
              } ]
