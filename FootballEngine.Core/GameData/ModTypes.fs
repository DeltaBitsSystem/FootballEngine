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

[<StructuralEquality; StructuralComparison>]
type ModId = ModId of string

[<StructuralEquality; StructuralComparison>]
type ContentId =
    | ContentId of string

    member this.Namespace =
        let (ContentId value) = this

        if isNull value then
            ""
        else
            let separator = value.IndexOf(':')

            if separator > 0 then value.Substring(0, separator) else ""

    member this.Kind =
        let (ContentId value) = this

        if isNull value then
            ""
        else
            let start = value.IndexOf(':')
            let finish = if start >= 0 then value.IndexOf('/', start + 1) else -1

            if start >= 0 && finish > start + 1 then
                value.Substring(start + 1, finish - start - 1)
            else
                ""

    member this.LocalId =
        let (ContentId value) = this

        if isNull value then
            ""
        else
            let separator = value.IndexOf('/')

            if separator >= 0 && separator + 1 < value.Length then
                value.Substring(separator + 1)
            else
                ""

type ModManifest =
    { Id: ModId
      Version: string
      Dependencies: (ModId * string) list
      Priority: int
      Before: ModId list
      After: ModId list }

    member this.FormatVersion = 1

    member this.ModId =
        let (ModId value) = this.Id
        value

    member this.Namespace = this.ModId

type ModPackage =
    { Manifest: ModManifest
      PackageSha256: string
      SportsContent: Map<ContentId, string>
      Translations: Map<ContentId, string>
      Assets: Map<ContentId, string>
      Patches: PatchOperation list }

and PatchOperation =
    | Add of ContentId * string
    | Replace of ContentId * string
    | Remove of ContentId
    | Assert of ContentId * string

type PackageSelection =
    { RelativePath: string
      Package: ModPackage }

type PackageInput =
    { ManifestJson: string
      Files: Map<string, byte[]>
      SymlinkPaths: Set<string> }

type ModDiagnostic =
    { Code: string
      ModId: string option
      Version: string option
      RelativePath: string option
      JsonPath: string option
      EntityId: string option
      Message: string
      SuggestedAction: string }

type ModError = { Diagnostics: ModDiagnostic list }

[<StructuralEquality; StructuralComparison>]
type ResolvedDataSet =
    { Packages: ModPackage list
      SportsContent: Map<ContentId, string>
      Translations: Map<ContentId, string>
      Assets: Map<ContentId, string>
      ResolvedSportsHash: string
      ResolvedPresentationHash: string
      DataLock: FootballEngine.Domain.DataLock }

    member this.Lock = this.DataLock

    member this.DependencyLock = this.DataLock
