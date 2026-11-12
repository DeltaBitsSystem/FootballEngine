namespace FootballEngine.Core.Tests.Contracts

open System
open Expecto
open FootballEngine.Data
open FootballEngine.Domain

module DataLockTests =
    let private manifest modId version dependencies precedence before after : ModManifest =
        { Id = ModId modId
          Version = version
          Dependencies =
            dependencies
            |> List.map (fun (dependencyId, range) -> ModId dependencyId, range)
          Priority = precedence
          Before = before |> List.map ModId
          After = after |> List.map ModId }

    let private contentMap entries : Map<ContentId, string> =
        entries
        |> List.map (fun (contentId, value) -> ContentId contentId, value)
        |> Map.ofList

    let private package
        modId
        version
        packageSha256
        dependencies
        precedence
        before
        after
        sports
        translations
        assets
        : ModPackage =
        { Manifest = manifest modId version dependencies precedence before after
          PackageSha256 = packageSha256
          SportsContent = contentMap sports
          Translations = contentMap translations
          Assets = contentMap assets
          Patches = [] }

    let private selection relativePath package : PackageSelection =
        { RelativePath = relativePath
          Package = package }

    let private expectOk message result =
        match result with
        | Ok value -> value
        | Error error -> failtestf "%s: %A" message error

    let private resolve selections =
        ModResolution.resolve selections |> expectOk "resolution should succeed"

    let private assetJson sha256 required =
        sprintf
            "{\"id\":\"community.assets:asset/logo\",\"kind\":\"asset\",\"required\":%b,\"schema\":1,\"sha256\":\"%s\"}"
            required
            sha256

    let dataLockTests =
        testList
            "Data lock"
            [ testCase "lock preserves exact package identity and topological order"
              <| fun _ ->
                  let basePackage =
                      package
                          "community.base"
                          "1.0.0"
                          "sha-base"
                          []
                          0
                          []
                          []
                          [ ("community.base:club/base", "base") ]
                          []
                          []

                  let featurePackage =
                      package
                          "community.feature"
                          "2.0.0"
                          "sha-feature"
                          [ ("community.base", ">=1.0.0 <2.0.0") ]
                          20
                          []
                          [ "community.base" ]
                          [ ("community.feature:club/feature", "feature") ]
                          []
                          []

                  let resolved =
                      resolve
                          [ selection "z/feature.femod" featurePackage
                            selection "a/base.femod" basePackage ]

                  let packageSummary lockPackage =
                      lockPackage.ModId,
                      lockPackage.Version,
                      lockPackage.SchemaVersion,
                      lockPackage.Dependencies,
                      lockPackage.Precedence,
                      lockPackage.PackageSha256

                  Expect.equal
                      (resolved.DataLock.Packages |> List.map packageSummary)
                      [ ("community.base", "1.0.0", 1, [], 0, "sha-base")
                        ("community.feature", "2.0.0", 1, [ ("community.base", ">=1.0.0 <2.0.0") ], 20, "sha-feature") ]
                      "the lock contains exact package fields in final order"

                  Expect.equal
                      (ModResolution.toDataLock resolved)
                      resolved.DataLock
                      "toDataLock returns the canonical dataset lock"

              testCase "dependency and precedence changes alter dependency lock hash"
              <| fun _ ->
                  let basePackage = package "community.base" "1.0.0" "sha-base" [] 0 [] [] [] [] []

                  let supportPackage =
                      package "community.support" "1.0.0" "sha-support" [] 1 [] [] [] [] []

                  let feature dependencies after =
                      package "community.feature" "1.0.0" "sha-feature" dependencies 2 [] after [] [] []

                  let resolveSet featurePackage =
                      resolve
                          [ selection "feature.femod" featurePackage
                            selection "support.femod" supportPackage
                            selection "base.femod" basePackage ]

                  let originalResult =
                      resolveSet (feature [ ("community.base", ">=1.0.0 <2.0.0") ] [])

                  let changedDependency =
                      resolveSet (
                          feature
                              [ ("community.base", ">=1.0.0 <2.0.0")
                                ("community.support", ">=1.0.0 <2.0.0") ]
                              []
                      )

                  let changedPrecedence =
                      resolveSet (feature [ ("community.base", ">=1.0.0 <2.0.0") ] [ "community.support" ])

                  Expect.notEqual
                      originalResult.DataLock.DependencyLockHash
                      changedDependency.DataLock.DependencyLockHash
                      "dependency ranges and IDs are part of the dependency lock hash"

                  Expect.notEqual
                      originalResult.DataLock.DependencyLockHash
                      changedPrecedence.DataLock.DependencyLockHash
                      "precedence before/after is part of the dependency lock hash"

              testCase "runtime IDs are canonical and independent of selection input order"
              <| fun _ ->
                  let first =
                      package
                          "community.first"
                          "1.0.0"
                          "sha-first"
                          []
                          0
                          []
                          []
                          [ ("community.first:club/zulu", "z") ]
                          []
                          []

                  let second =
                      package
                          "community.second"
                          "1.0.0"
                          "sha-second"
                          []
                          1
                          []
                          []
                          [ ("community.second:club/alpha", "a") ]
                          []
                          []

                  let firstResolved =
                      resolve [ selection "first.femod" first; selection "second.femod" second ]

                  let secondResolved =
                      resolve [ selection "second.femod" second; selection "first.femod" first ]

                  Expect.equal
                      firstResolved.DataLock.RuntimeIdTable
                      [ { ContentId = "community.first:club/zulu"
                          RuntimeId = 0 }
                        { ContentId = "community.second:club/alpha"
                          RuntimeId = 1 } ]
                      "runtime IDs are assigned from canonical ContentId order"

                  Expect.equal
                      firstResolved.DataLock.RuntimeIdTable
                      secondResolved.DataLock.RuntimeIdTable
                      "runtime IDs do not depend on selection input order"

              testCase "sports and presentation snapshots have separate identities"
              <| fun _ ->
                  let sportsId = "community.snapshot:club/river"
                  let translationId = "community.snapshot:translation/river"
                  let assetId = "community.snapshot:asset/river"
                  let asset = assetJson (String.replicate 64 "a") false

                  let basePackage =
                      package
                          "community.snapshot"
                          "1.0.0"
                          "sha-snapshot"
                          []
                          0
                          []
                          []
                          [ (sportsId, "82") ]
                          [ (translationId, "River Plate") ]
                          [ (assetId, asset) ]

                  let translatedPackage =
                      { basePackage with
                          Translations = contentMap [ (translationId, "Club Atletico River Plate") ] }

                  let changedAssetPackage =
                      { basePackage with
                          Assets = contentMap [ (assetId, assetJson (String.replicate 64 "b") false) ] }

                  let baseLock =
                      resolve [ selection "snapshot.femod" basePackage ]
                      |> fun value -> value.DataLock

                  let translatedLock =
                      resolve [ selection "snapshot.femod" translatedPackage ]
                      |> fun value -> value.DataLock

                  let changedAssetLock =
                      resolve [ selection "snapshot.femod" changedAssetPackage ]
                      |> fun value -> value.DataLock

                  Expect.equal
                      translatedLock.ResolvedSportsHash
                      baseLock.ResolvedSportsHash
                      "translations do not change the sports hash"

                  Expect.equal
                      changedAssetLock.ResolvedSportsHash
                      baseLock.ResolvedSportsHash
                      "assets do not change the sports hash"

                  Expect.notEqual
                      translatedLock.ResolvedPresentationHash
                      baseLock.ResolvedPresentationHash
                      "translations change the presentation hash"

                  Expect.notEqual
                      changedAssetLock.ResolvedPresentationHash
                      baseLock.ResolvedPresentationHash
                      "assets change the presentation hash"

                  Expect.notEqual
                      baseLock.Snapshot.SportsContent
                      baseLock.Snapshot.Translations
                      "snapshot channels remain separate"

              testCase "required assets are copied from canonical asset JSON"
              <| fun _ ->
                  let sha256 = String.replicate 64 "c"
                  let assetId = "community.assets:asset/logo"

                  let resolved =
                      resolve
                          [ selection
                                "assets.femod"
                                (package
                                    "community.assets"
                                    "1.0.0"
                                    "sha-assets"
                                    []
                                    0
                                    []
                                    []
                                    []
                                    []
                                    [ (assetId, assetJson sha256 true) ]) ]

                  Expect.equal
                      resolved.DataLock.RequiredAssets
                      [ { AssetId = assetId
                          Sha256 = sha256
                          Required = true } ]
                      "required asset metadata is present in the lock"

              testCase "invalid lock is rejected by the strict validator"
              <| fun _ ->
                  let resolved =
                      resolve
                          [ selection
                                "invalid.femod"
                                (package "community.invalid" "1.0.0" "sha-invalid" [] 0 [] [] [] [] []) ]

                  let invalidFormat =
                      { resolved.DataLock with
                          FormatVersion = 2 }

                  let invalidHash =
                      { resolved.DataLock with
                          DependencyLockHash = "not-a-sha256" }

                  Expect.isError (DataLock.validate invalidFormat) "format versions other than 1 are rejected"
                  Expect.isError (DataLock.validate invalidHash) "non-SHA-256 hashes are rejected" ]
