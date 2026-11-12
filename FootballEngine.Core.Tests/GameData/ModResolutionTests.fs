namespace FootballEngine.Core.Tests.GameData

open Expecto
open FootballEngine.Data

module ModResolutionTests =
    // Provisional contract: IDs are wrapped strings, package content is canonical text,
    // and resolution is atomic over an input list of packages.
    let private makeModId value : ModId = ModId value

    let private makeContentId value : ContentId = ContentId value

    let private contentMap entries : Map<ContentId, string> =
        entries |> List.map (fun (id, value) -> makeContentId id, value) |> Map.ofList

    let private makeManifest modName version dependencies priority before after : ModManifest =
        { Id = makeModId modName
          Version = version
          Dependencies =
            dependencies
            |> List.map (fun (dependencyId, range) -> makeModId dependencyId, range)
          Priority = priority
          Before = before |> List.map makeModId
          After = after |> List.map makeModId }

    let private emptyPackage modName version packageSha256 : ModPackage =
        { Manifest = makeManifest modName version [] 0 [] []
          PackageSha256 = packageSha256
          SportsContent = Map.empty
          Translations = Map.empty
          Assets = Map.empty
          Patches = [] }

    let private withDependencies dependencies (package: ModPackage) =
        { package with
            Manifest =
                { package.Manifest with
                    Dependencies =
                        dependencies
                        |> List.map (fun (dependencyId, range) -> makeModId dependencyId, range) } }

    let private withPriority priority (package: ModPackage) =
        { package with
            Manifest =
                { package.Manifest with
                    Priority = priority } }

    let private withBefore before (package: ModPackage) =
        { package with
            Manifest =
                { package.Manifest with
                    Before = before |> List.map makeModId } }

    let private withAfter after (package: ModPackage) =
        { package with
            Manifest =
                { package.Manifest with
                    After = after |> List.map makeModId } }

    let private withPackageSha packageSha256 (package: ModPackage) =
        { package with
            PackageSha256 = packageSha256 }

    let private withSports entries (package: ModPackage) =
        { package with
            SportsContent = contentMap entries }

    let private withTranslations entries (package: ModPackage) =
        { package with
            Translations = contentMap entries }

    let private withAssets entries (package: ModPackage) =
        { package with
            Assets = contentMap entries }

    let private withPatches (operations: PatchOperation list) (package: ModPackage) =
        { package with Patches = operations }

    let private input relativePath package : PackageSelection =
        { RelativePath = relativePath
          Package = package }

    let private expectOk (message: string) (result: Result<'a, ModError>) : 'a =
        match result with
        | Ok value -> value
        | Error error -> failtestf "%s: unexpected error %A" message error

    let private expectError (message: string) (result: Result<'a, ModError>) =
        match result with
        | Error _ -> ()
        | Ok value -> failtestf "%s: expected an error, got %A" message value

    let private resolveOne package : ResolvedDataSet =
        ModResolution.resolve [ input "package.femod" package ]
        |> expectOk "single package should resolve"

    let modResolutionTests =
        testList
            "Mod resolution"
            [ testCase "missing dependency is rejected"
              <| fun _ ->
                  let package =
                      emptyPackage "community.child" "1.0.0" "sha-child"
                      |> withDependencies [ "community.base", ">=1.0.0 <2.0.0" ]

                  ModResolution.resolve [ input "child.femod" package ]
                  |> expectError "a missing dependency must fail resolution"

              testCase "incompatible dependency range is rejected"
              <| fun _ ->
                  let basePackage = emptyPackage "community.base" "1.0.0" "sha-base"

                  let package =
                      emptyPackage "community.child" "1.0.0" "sha-child"
                      |> withDependencies [ "community.base", ">=2.0.0 <3.0.0" ]

                  ModResolution.resolve [ input "child.femod" package; input "base.femod" basePackage ]
                  |> expectError "an unsatisfied version range must fail resolution"

              testCase "dependency cycle is rejected"
              <| fun _ ->
                  let first =
                      emptyPackage "community.first" "1.0.0" "sha-first"
                      |> withDependencies [ "community.second", ">=1.0.0 <2.0.0" ]

                  let second =
                      emptyPackage "community.second" "1.0.0" "sha-second"
                      |> withDependencies [ "community.first", ">=1.0.0 <2.0.0" ]

                  ModResolution.resolve [ input "first.femod" first; input "second.femod" second ]
                  |> expectError "a dependency cycle must fail resolution"

              testCase "dependency lock is exact and stable"
              <| fun _ ->
                  let basePackage = emptyPackage "community.base" "1.0.0" "sha-base"

                  let featurePackage =
                      emptyPackage "community.feature" "1.2.0" "sha-feature"
                      |> withDependencies [ "community.base", ">=1.0.0 <2.0.0" ]
                      |> withPriority 20
                      |> withAfter [ "community.base" ]

                  let expected = [ basePackage; featurePackage ]

                  let lockFromReversedInput =
                      ModResolution.buildDependencyLock
                          [ input "z/feature.femod" featurePackage; input "a/base.femod" basePackage ]
                      |> expectOk "the selected packages should produce a lock"

                  let lockFromSortedInput =
                      ModResolution.buildDependencyLock
                          [ input "a/base.femod" basePackage; input "z/feature.femod" featurePackage ]
                      |> expectOk "the same selected packages should produce a lock"

                  Expect.equal
                      lockFromReversedInput
                      expected
                      "the lock contains exact package values in dependency order"

                  Expect.equal
                      lockFromReversedInput
                      lockFromSortedInput
                      "the lock is independent of input order and path"

              testCase "dependencies are applied before dependents"
              <| fun _ ->
                  let club = makeContentId "community.base:club/river"

                  let basePackage =
                      emptyPackage "community.base" "1.0.0" "sha-base"
                      |> withSports [ "community.base:club/river", "base" ]

                  let dependentPackage =
                      emptyPackage "community.dependent" "1.0.0" "sha-dependent"
                      |> withDependencies [ "community.base", ">=1.0.0 <2.0.0" ]
                      |> withPatches [ PatchOperation.Replace(club, "dependent") ]

                  let resolved =
                      ModResolution.resolve [ input "dependent.femod" dependentPackage; input "base.femod" basePackage ]
                      |> expectOk "a dependent patch should resolve after its dependency"

                  Expect.equal
                      (Map.tryFind club resolved.SportsContent)
                      (Some "dependent")
                      "the dependent patch observes the dependency content"

              testCase "after precedence resolves a same-priority write"
              <| fun _ ->
                  let club = makeContentId "community.base:club/river"

                  let basePackage =
                      emptyPackage "community.base" "1.0.0" "sha-base"
                      |> withSports [ "community.base:club/river", "base" ]

                  let earlierPackage =
                      emptyPackage "community.earlier" "1.0.0" "sha-earlier"
                      |> withDependencies [ "community.base", ">=1.0.0 <2.0.0" ]
                      |> withPriority 50
                      |> withPatches [ PatchOperation.Replace(club, "earlier") ]

                  let laterPackage =
                      emptyPackage "community.later" "1.0.0" "sha-later"
                      |> withDependencies [ "community.base", ">=1.0.0 <2.0.0" ]
                      |> withPriority 50
                      |> withAfter [ "community.earlier" ]
                      |> withPatches [ PatchOperation.Replace(club, "later") ]

                  let resolved =
                      ModResolution.resolve
                          [ input "later.femod" laterPackage
                            input "earlier.femod" earlierPackage
                            input "base.femod" basePackage ]
                      |> expectOk "after precedence should order the writes"

                  Expect.equal
                      (Map.tryFind club resolved.SportsContent)
                      (Some "later")
                      "after must be applied after the referenced mod"

              testCase "before precedence resolves a same-priority write"
              <| fun _ ->
                  let club = makeContentId "community.base:club/river"

                  let basePackage =
                      emptyPackage "community.base" "1.0.0" "sha-base"
                      |> withSports [ "community.base:club/river", "base" ]

                  let beforePackage =
                      emptyPackage "community.before" "1.0.0" "sha-before"
                      |> withDependencies [ "community.base", ">=1.0.0 <2.0.0" ]
                      |> withPriority 50
                      |> withBefore [ "community.after" ]
                      |> withPatches [ PatchOperation.Replace(club, "before") ]

                  let afterPackage =
                      emptyPackage "community.after" "1.0.0" "sha-after"
                      |> withDependencies [ "community.base", ">=1.0.0 <2.0.0" ]
                      |> withPriority 50
                      |> withPatches [ PatchOperation.Replace(club, "after") ]

                  let resolved =
                      ModResolution.resolve
                          [ input "after.femod" afterPackage
                            input "before.femod" beforePackage
                            input "base.femod" basePackage ]
                      |> expectOk "before precedence should order the writes"

                  Expect.equal
                      (Map.tryFind club resolved.SportsContent)
                      (Some "after")
                      "before must be applied before the referenced mod"

              testCase "unresolved priority tie is rejected"
              <| fun _ ->
                  let first = emptyPackage "community.tie-a" "1.0.0" "sha-a" |> withPriority 10

                  let second = emptyPackage "community.tie-b" "1.0.0" "sha-b" |> withPriority 10

                  ModResolution.resolve [ input "a.femod" first; input "b.femod" second ]
                  |> expectError "equal priority without precedence must fail"

              testCase "duplicate mod IDs are rejected"
              <| fun _ ->
                  let first = emptyPackage "community.duplicate" "1.0.0" "sha-one"
                  let second = emptyPackage "community.duplicate" "2.0.0" "sha-two"

                  ModResolution.resolve [ input "one.femod" first; input "two.femod" second ]
                  |> expectError "a mod ID can have only one selected package"

              testCase "duplicate content IDs are rejected"
              <| fun _ ->
                  let first =
                      emptyPackage "community.first" "1.0.0" "sha-one"
                      |> withPriority 10
                      |> withSports [ "community.shared:club/river", "first" ]

                  let second =
                      emptyPackage "community.second" "1.0.0" "sha-two"
                      |> withPriority 20
                      |> withSports [ "community.shared:club/river", "second" ]

                  ModResolution.resolve [ input "first.femod" first; input "second.femod" second ]
                  |> expectError "a ContentId can have only one owner"

              testCase "patch operations add replace remove and assert by ContentId"
              <| fun _ ->
                  let existing = makeContentId "community.base:club/river"
                  let added = makeContentId "community.base:club/boca"
                  let removed = makeContentId "community.base:club/old"

                  let initial = Map.ofList [ existing, "82"; removed, "retire" ]

                  ModResolution.applyPatch
                      initial
                      [ PatchOperation.Assert(existing, "82")
                        PatchOperation.Add(added, "new")
                        PatchOperation.Replace(existing, "85")
                        PatchOperation.Remove removed ]
                  |> expectOk "valid ContentId patch operations should apply"
                  |> fun patched ->
                      Expect.equal (Map.tryFind existing patched) (Some "85") "replace targets the requested ContentId"
                      Expect.equal (Map.tryFind added patched) (Some "new") "add targets the requested ContentId"
                      Expect.isNone (Map.tryFind removed patched) "remove targets the requested ContentId"

              testCase "failed assert rejects a patch"
              <| fun _ ->
                  let existing = makeContentId "community.base:club/river"
                  let initial = Map.ofList [ existing, "82" ]

                  ModResolution.applyPatch initial [ PatchOperation.Assert(existing, "81") ]
                  |> expectError "an assert against a different value must fail"

              testCase "conflicting writes without precedence are rejected"
              <| fun _ ->
                  let club = makeContentId "community.base:club/river"

                  let basePackage =
                      emptyPackage "community.base" "1.0.0" "sha-base"
                      |> withSports [ "community.base:club/river", "base" ]

                  let first =
                      emptyPackage "community.first" "1.0.0" "sha-first"
                      |> withDependencies [ "community.base", ">=1.0.0 <2.0.0" ]
                      |> withPriority 40
                      |> withPatches [ PatchOperation.Replace(club, "first") ]

                  let second =
                      emptyPackage "community.second" "1.0.0" "sha-second"
                      |> withDependencies [ "community.base", ">=1.0.0 <2.0.0" ]
                      |> withPriority 40
                      |> withPatches [ PatchOperation.Replace(club, "second") ]

                  ModResolution.resolve
                      [ input "first.femod" first
                        input "second.femod" second
                        input "base.femod" basePackage ]
                  |> expectError "incompatible writes need explicit precedence"

              testCase "resolved result is identical for a different input order"
              <| fun _ ->
                  let basePackage =
                      emptyPackage "community.base" "1.0.0" "sha-base"
                      |> withSports [ "community.base:club/river", "base" ]

                  let dependentPackage =
                      emptyPackage "community.dependent" "1.0.0" "sha-dependent"
                      |> withDependencies [ "community.base", ">=1.0.0 <2.0.0" ]
                      |> withPatches [ PatchOperation.Replace(makeContentId "community.base:club/river", "dependent") ]

                  let first =
                      ModResolution.resolve [ input "dependent.femod" dependentPackage; input "base.femod" basePackage ]
                      |> expectOk "the first package order should resolve"

                  let second =
                      ModResolution.resolve [ input "base.femod" basePackage; input "dependent.femod" dependentPackage ]
                      |> expectOk "the second package order should resolve"

                  Expect.equal first second "canonical resolution must not depend on input order"

              testCase "translations and assets do not change resolvedSportsHash"
              <| fun _ ->
                  let basePackage =
                      emptyPackage "community.presentation" "1.0.0" "sha-presentation-base"
                      |> withSports [ "community.base:club/river", "82" ]
                      |> withTranslations [ "community.base:translation/es-ar", "River Plate" ]
                      |> withAssets [ "community.base:asset/river-logo", "svg-a" ]

                  let translatedPackage =
                      basePackage
                      |> withPackageSha "sha-presentation-translation"
                      |> withTranslations [ "community.base:translation/es-ar", "Club Atletico River Plate" ]

                  let assetPackage =
                      basePackage
                      |> withPackageSha "sha-presentation-asset"
                      |> withAssets [ "community.base:asset/river-logo", "svg-b" ]

                  let baseResolved = resolveOne basePackage
                  let translatedResolved = resolveOne translatedPackage
                  let assetResolved = resolveOne assetPackage

                  Expect.equal
                      translatedResolved.ResolvedSportsHash
                      baseResolved.ResolvedSportsHash
                      "translation changes are outside the sports hash"

                  Expect.equal
                      assetResolved.ResolvedSportsHash
                      baseResolved.ResolvedSportsHash
                      "asset changes are outside the sports hash"

                  Expect.isTrue
                      (translatedResolved.ResolvedPresentationHash
                       <> baseResolved.ResolvedPresentationHash)
                      "translation changes remain visible in the presentation hash"

                  Expect.isTrue
                      (assetResolved.ResolvedPresentationHash <> baseResolved.ResolvedPresentationHash)
                      "asset changes remain visible in the presentation hash"

              testCase "sports content changes resolvedSportsHash"
              <| fun _ ->
                  let basePackage =
                      emptyPackage "community.sports" "1.0.0" "sha-sports-base"
                      |> withSports [ "community.base:club/river", "82" ]

                  let changedPackage =
                      basePackage
                      |> withPackageSha "sha-sports-changed"
                      |> withSports [ "community.base:club/river", "85" ]

                  let baseResolved = resolveOne basePackage
                  let changedResolved = resolveOne changedPackage

                  Expect.isTrue
                      (changedResolved.ResolvedSportsHash <> baseResolved.ResolvedSportsHash)
                      "sports content changes must change the sports hash"

              testCase "resolved snapshot restores without installed packages"
              <| fun _ ->
                  let resolved =
                      emptyPackage "community.snapshot" "1.0.0" "sha-restore"
                      |> withSports [ "community.snapshot:club/river", "sports-value" ]
                      |> withTranslations [ "community.snapshot:translation/es", "translation-value" ]
                      |> withAssets [ "community.snapshot:asset/crest", "asset-value" ]
                      |> resolveOne

                  let restored =
                      ModResolution.restoreDataSet resolved.DataLock
                      |> expectOk "the saved snapshot should restore"

                  Expect.equal restored.SportsContent resolved.SportsContent "sports snapshot"
                  Expect.equal restored.Translations resolved.Translations "translation snapshot"
                  Expect.equal restored.Assets resolved.Assets "asset snapshot"
                  Expect.equal restored.DataLock resolved.DataLock "restored lock"

              testCase "restored snapshot validates the dependency lock"
              <| fun _ ->
                  let resolved =
                      emptyPackage "community.snapshot" "1.0.0" "sha-restore"
                      |> withSports [ "community.snapshot:club/river", "sports-value" ]
                      |> resolveOne

                  let invalidLock =
                      { resolved.DataLock with
                          DependencyLockHash = String.replicate 64 "0" }

                  Expect.isError (ModResolution.restoreDataSet invalidLock) "a mismatched dependency lock must fail"

              testCase "invalid resolution does not publish a dataset"
              <| fun _ ->
                  let club = makeContentId "community.base:club/river"

                  let basePackage =
                      emptyPackage "community.base" "1.0.0" "sha-base"
                      |> withSports [ "community.base:club/river", "82" ]

                  let invalidPackage =
                      emptyPackage "community.invalid" "1.0.0" "sha-invalid"
                      |> withDependencies [ "community.base", ">=1.0.0 <2.0.0" ]
                      |> withPatches [ PatchOperation.Assert(club, "81") ]

                  ModResolution.resolve [ input "base.femod" basePackage; input "invalid.femod" invalidPackage ]
                  |> expectError "a failed patch must not publish a partial dataset" ]
