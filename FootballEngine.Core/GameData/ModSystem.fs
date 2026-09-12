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

module ModSystem =
    [<Literal>]
    let private CurrentFormatVersion = 1

    [<Literal>]
    let private CurrentContentApi = 1

    [<Literal>]
    let private CurrentScriptsApi = 0

    type private SemVersion =
        { Major: int
          Minor: int
          Patch: int
          PreRelease: string list }

    let private currentEngineVersion =
        { Major = 2026
          Minor = 1
          Patch = 0
          PreRelease = [] }

    type private RangeComparison =
        | AtLeast of SemVersion
        | Below of SemVersion

    type private ParsedManifest =
        { Manifest: ModManifest
          PackageSha256: string
          DeclaredFiles: (string * string) list
          CanonicalWithoutPackageSha256: string }

    let private modIdValue (ModId value) = value

    let private contentIdValue (ContentId value) = value

    let private diagnostic code modId version relativePath jsonPath entityId message suggestedAction =
        { Code = code
          ModId = modId
          Version = version
          RelativePath = relativePath
          JsonPath = jsonPath
          EntityId = entityId
          Message = message
          SuggestedAction = suggestedAction }

    let private addDiagnostic
        (diagnostics: ResizeArray<ModDiagnostic>)
        code
        modId
        version
        relativePath
        jsonPath
        entityId
        message
        suggestedAction
        =
        diagnostics.Add(diagnostic code modId version relativePath jsonPath entityId message suggestedAction)

    let private errorFromDiagnostics (diagnostics: ResizeArray<ModDiagnostic>) =
        { Diagnostics = List.ofSeq diagnostics }

    let private errorFromDiagnostic item = { Diagnostics = [ item ] }

    let private isAsciiLowerLetter character = character >= 'a' && character <= 'z'

    let private isAsciiDigit character = character >= '0' && character <= '9'

    let private isAsciiAlphaNumeric character =
        isAsciiLowerLetter character || isAsciiDigit character

    let private isAsciiSemVerCharacter character =
        isAsciiAlphaNumeric character || (character >= 'A' && character <= 'Z')

    let private isValidNamespaceText (value: string) =
        if String.IsNullOrEmpty value then
            false
        elif
            not (isAsciiAlphaNumeric value[0])
            || not (isAsciiAlphaNumeric value[value.Length - 1])
        then
            false
        else
            value
            |> Seq.forall (fun character ->
                isAsciiAlphaNumeric character
                || character = '.'
                || character = '_'
                || character = '-'
                || character = '/')
            && not (value.Contains("//", StringComparison.Ordinal))

    let private isValidKindText (value: string) =
        if String.IsNullOrEmpty value then
            false
        elif
            not (isAsciiAlphaNumeric value[0])
            || not (isAsciiAlphaNumeric value[value.Length - 1])
        then
            false
        else
            value
            |> Seq.forall (fun character ->
                isAsciiAlphaNumeric character
                || character = '.'
                || character = '_'
                || character = '-')

    let private isValidLocalIdText (value: string) =
        if String.IsNullOrEmpty value then
            false
        elif
            not (isAsciiAlphaNumeric value[0])
            || not (isAsciiAlphaNumeric value[value.Length - 1])
        then
            false
        else
            let segments = value.Split('/')

            segments
            |> Array.forall (fun segment ->
                not (String.IsNullOrEmpty segment)
                && segment <> "."
                && segment <> ".."
                && segment
                   |> Seq.forall (fun character ->
                       isAsciiAlphaNumeric character
                       || character = '.'
                       || character = '_'
                       || character = '-'))

    let private isValidModIdText (value: string) =
        isValidNamespaceText value
        && not (value.Contains(':', StringComparison.Ordinal))

    let private isValidContentIdText (value: string) =
        if String.IsNullOrEmpty value then
            false
        else
            let separator = value.IndexOf(':')

            let slash =
                if separator >= 0 then
                    value.IndexOf('/', separator + 1)
                else
                    -1

            separator > 0
            && slash > separator + 1
            && slash + 1 < value.Length
            && value.IndexOf(':', separator + 1) < 0
            && isValidNamespaceText (value.Substring(0, separator))
            && isValidKindText (value.Substring(separator + 1, slash - separator - 1))
            && isValidLocalIdText (value.Substring(slash + 1))

    let private isSha256 (value: string) =
        not (String.IsNullOrEmpty value)
        && value.Length = 64
        && (value
            |> Seq.forall (fun character -> isAsciiDigit character || (character >= 'a' && character <= 'f')))

    let private normalizedPath (path: string) =
        if isNull path then "" else path.Replace('\\', '/')

    let private isSafeRelativePath (path: string) =
        if String.IsNullOrEmpty path || path.Contains('\u0000') then
            false
        else
            let normalized = normalizedPath path
            let hasDrivePrefix = normalized.Length >= 2 && normalized[1] = ':'
            let segments = normalized.Split('/')

            not (normalized.StartsWith("/", StringComparison.Ordinal))
            && not hasDrivePrefix
            && segments.Length > 0
            && (segments
                |> Array.forall (fun segment -> not (String.IsNullOrEmpty segment) && segment <> "." && segment <> ".."))

    let private pathLooksLikeTraversal (path: string) =
        if String.IsNullOrEmpty path then
            false
        else
            let normalized = normalizedPath path
            let segments = normalized.Split('/')

            normalized.StartsWith("/", StringComparison.Ordinal)
            || (normalized.Length >= 2 && normalized[1] = ':')
            || normalized.Contains('\u0000')
            || (segments |> Array.exists (fun segment -> segment = ".."))

    let private hasExecutableExtension (path: string) =
        let normalized = normalizedPath path
        let lower = normalized.ToLowerInvariant()

        [ ".dll"
          ".exe"
          ".so"
          ".dylib"
          ".com"
          ".bat"
          ".cmd"
          ".ps1"
          ".sh"
          ".js"
          ".fsx"
          ".csx" ]
        |> List.exists (fun extension -> lower.EndsWith(extension, StringComparison.Ordinal))

    let private jsonPathSegment (segment: string) =
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)

    let private appendJsonPath path segment = path + "/" + jsonPathSegment segment

    let private parseSemVersion (value: string) : SemVersion option =
        if String.IsNullOrEmpty value || value |> Seq.exists Char.IsWhiteSpace then
            None
        else
            let plusIndex = value.IndexOf('+')

            let withoutBuild, buildPart =
                if plusIndex < 0 then
                    value, None
                elif value.IndexOf('+', plusIndex + 1) >= 0 then
                    value, Some ""
                else
                    value.Substring(0, plusIndex), Some(value.Substring(plusIndex + 1))

            let dashIndex = withoutBuild.IndexOf('-')

            let core, preRelease =
                if dashIndex < 0 then
                    withoutBuild, []
                else
                    withoutBuild.Substring(0, dashIndex),
                    withoutBuild.Substring(dashIndex + 1).Split('.') |> Array.toList

            let validIdentifier allowLeadingZero identifier =
                not (String.IsNullOrEmpty identifier)
                && identifier
                   |> Seq.forall (fun character -> isAsciiSemVerCharacter character || character = '-')
                && (allowLeadingZero
                    || identifier.Length = 1
                    || identifier[0] <> '0'
                    || not (identifier |> Seq.forall isAsciiDigit))

            let validBuild =
                match buildPart with
                | None -> true
                | Some build -> build.Split('.') |> Array.forall (validIdentifier true)

            let validPreRelease = preRelease |> List.forall (validIdentifier false)
            let coreParts = core.Split('.')

            let parseCorePart part =
                if
                    String.IsNullOrEmpty part
                    || part.Length > 1 && part[0] = '0'
                    || not (part |> Seq.forall isAsciiDigit)
                then
                    None
                else
                    let mutable parsed = 0

                    if Int32.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, &parsed) then
                        Some parsed
                    else
                        None

            if coreParts.Length <> 3 || not validBuild || not validPreRelease then
                None
            else
                match coreParts |> Array.map parseCorePart |> Array.toList with
                | [ Some major; Some minor; Some patch ] ->
                    Some
                        { Major = major
                          Minor = minor
                          Patch = patch
                          PreRelease = preRelease }
                | _ -> None

    let private compareIdentifier (left: string) (right: string) =
        let leftNumeric = left |> Seq.forall isAsciiDigit
        let rightNumeric = right |> Seq.forall isAsciiDigit

        match leftNumeric, rightNumeric with
        | true, true ->
            let leftTrimmed = left.TrimStart('0')
            let rightTrimmed = right.TrimStart('0')
            let leftNormalized = if leftTrimmed = "" then "0" else leftTrimmed
            let rightNormalized = if rightTrimmed = "" then "0" else rightTrimmed

            if leftNormalized.Length <> rightNormalized.Length then
                compare leftNormalized.Length rightNormalized.Length
            else
                StringComparer.Ordinal.Compare(leftNormalized, rightNormalized)
        | true, false -> -1
        | false, true -> 1
        | false, false -> StringComparer.Ordinal.Compare(left, right)

    let private compareSemVersion left right =
        let compareCore =
            compare left.Major right.Major
            |> fun result ->
                if result <> 0 then
                    result
                else
                    compare left.Minor right.Minor
            |> fun result ->
                if result <> 0 then
                    result
                else
                    compare left.Patch right.Patch

        if compareCore <> 0 then
            compareCore
        else
            match left.PreRelease, right.PreRelease with
            | [], [] -> 0
            | [], _ -> 1
            | _, [] -> -1
            | leftParts, rightParts ->
                let rec comparePre leftItems rightItems =
                    match leftItems, rightItems with
                    | [], [] -> 0
                    | [], _ -> -1
                    | _, [] -> 1
                    | leftHead :: leftTail, rightHead :: rightTail ->
                        let comparison = compareIdentifier leftHead rightHead

                        if comparison <> 0 then
                            comparison
                        else
                            comparePre leftTail rightTail

                comparePre leftParts rightParts

    let private parseRange (value: string) : RangeComparison list option =
        if String.IsNullOrWhiteSpace value then
            None
        else
            let tokens =
                value.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                |> Array.toList

            let parseToken (token: string) =
                if token.StartsWith(">=", StringComparison.Ordinal) then
                    token.Substring(2) |> parseSemVersion |> Option.map AtLeast
                elif token.StartsWith("<", StringComparison.Ordinal) then
                    token.Substring(1) |> parseSemVersion |> Option.map Below
                else
                    None

            if tokens.IsEmpty then
                None
            else
                let parsed = tokens |> List.map parseToken

                if parsed |> List.exists Option.isNone then
                    None
                else
                    let comparisons = parsed |> List.choose id

                    let atLeastCount =
                        comparisons
                        |> List.filter (function
                            | AtLeast _ -> true
                            | _ -> false)
                        |> List.length

                    let belowCount =
                        comparisons
                        |> List.filter (function
                            | Below _ -> true
                            | _ -> false)
                        |> List.length

                    if atLeastCount > 1 || belowCount > 1 then
                        None
                    else
                        let minimum =
                            comparisons
                            |> List.tryPick (function
                                | AtLeast version -> Some version
                                | _ -> None)

                        let maximum =
                            comparisons
                            |> List.tryPick (function
                                | Below version -> Some version
                                | _ -> None)

                        match minimum, maximum with
                        | Some minimumVersion, Some maximumVersion when
                            compareSemVersion minimumVersion maximumVersion >= 0
                            ->
                            None
                        | _ -> Some comparisons

    let private satisfiesRange version range =
        range
        |> List.forall (function
            | AtLeast minimum -> compareSemVersion version minimum >= 0
            | Below maximum -> compareSemVersion version maximum < 0)

    let private parseContentIdValue value =
        if isNull value || not (isValidContentIdText value) then
            None
        else
            Some(ContentId value)

    let rec private parseManifestDocument (json: string) : Result<ParsedManifest, ModError> =
        let diagnostics = ResizeArray<ModDiagnostic>()
        let relativePath = Some "manifest.json"
        let mutable success: Result<ParsedManifest, ModError> option = None

        let add code jsonPath message action =
            addDiagnostic diagnostics code None None relativePath jsonPath None message action

        try
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

            if root.ValueKind <> JsonValueKind.Object then
                add
                    "MOD-MANIFEST-JSON"
                    None
                    "manifest.json must contain a JSON object"
                    "Replace the manifest with a valid JSON object."
            else
                let properties = root.EnumerateObject() |> Seq.toList

                let allowedTop =
                    set
                        [ "format"
                          "formatVersion"
                          "modId"
                          "version"
                          "engine"
                          "api"
                          "namespace"
                          "title"
                          "authors"
                          "description"
                          "dependencies"
                          "precedence"
                          "files"
                          "packageSha256"
                          "signature" ]

                for property in properties do
                    if not (Set.contains property.Name allowedTop) then
                        add
                            "MOD-MANIFEST-UNKNOWN-KEY"
                            (Some(appendJsonPath "" property.Name))
                            ("Unknown manifest key '" + property.Name + "'.")
                            "remove the unknown manifest key."

                properties
                |> List.groupBy (fun property -> property.Name)
                |> List.iter (fun (name, occurrences) ->
                    if occurrences.Length > 1 then
                        add
                            "MOD-MANIFEST-DUPLICATE-KEY"
                            (Some(appendJsonPath "" name))
                            ("Manifest key '" + name + "' occurs more than once.")
                            "remove duplicate manifest keys.")

                let findProperty name =
                    properties |> List.tryFind (fun property -> property.Name = name)

                let required name =
                    match findProperty name with
                    | Some property -> Some property.Value
                    | None ->
                        add
                            "MOD-MANIFEST-MISSING-FIELD"
                            (Some(appendJsonPath "" name))
                            ("Required manifest field '" + name + "' is missing.")
                            ("Add the required manifest field '" + name + "'.")

                        None

                let readString (path: string) (element: JsonElement) =
                    if element.ValueKind = JsonValueKind.String then
                        Some(element.GetString())
                    else
                        add
                            "MOD-MANIFEST-TYPE"
                            (Some path)
                            "Manifest field must be a string."
                            "Use a JSON string for this field."

                        None

                let readInt (path: string) (element: JsonElement) =
                    if element.ValueKind = JsonValueKind.Number then
                        let mutable value = 0

                        if element.TryGetInt32(&value) then
                            Some value
                        else
                            add
                                "MOD-MANIFEST-TYPE"
                                (Some path)
                                "Manifest integer is outside the supported range."
                                "Use a 32-bit JSON integer."

                            None
                    else
                        add
                            "MOD-MANIFEST-TYPE"
                            (Some path)
                            "Manifest field must be an integer."
                            "Use a JSON integer for this field."

                        None

                let readArray (path: string) (element: JsonElement) =
                    if element.ValueKind = JsonValueKind.Array then
                        Some(element.EnumerateArray() |> Seq.toList)
                    else
                        add
                            "MOD-MANIFEST-TYPE"
                            (Some path)
                            "Manifest field must be an array."
                            "Use a JSON array for this field."

                        None

                let readObject (path: string) (element: JsonElement) =
                    if element.ValueKind = JsonValueKind.Object then
                        Some(element.EnumerateObject() |> Seq.toList)
                    else
                        add
                            "MOD-MANIFEST-TYPE"
                            (Some path)
                            "Manifest field must be an object."
                            "Use a JSON object for this field."

                        None

                let inspectObjectKeys (path: string) (objectProperties: JsonProperty list) (allowedKeys: string list) =
                    let allowed = Set.ofList allowedKeys

                    for property in objectProperties do
                        if not (Set.contains property.Name allowed) then
                            add
                                "MOD-MANIFEST-UNKNOWN-KEY"
                                (Some(appendJsonPath path property.Name))
                                ("Unknown manifest key '" + property.Name + "'.")
                                "remove the unknown manifest key."

                    objectProperties
                    |> List.groupBy (fun property -> property.Name)
                    |> List.iter (fun (name, occurrences) ->
                        if occurrences.Length > 1 then
                            add
                                "MOD-MANIFEST-DUPLICATE-KEY"
                                (Some(appendJsonPath path name))
                                ("Manifest key '" + name + "' occurs more than once.")
                                "remove duplicate manifest keys.")

                let findNested (properties: JsonProperty list) name =
                    properties |> List.tryFind (fun property -> property.Name = name)

                let requiredNested (path: string) (properties: JsonProperty list) name =
                    match findNested properties name with
                    | Some property -> Some property.Value
                    | None ->
                        add
                            "MOD-MANIFEST-MISSING-FIELD"
                            (Some(appendJsonPath path name))
                            ("Required manifest field '" + name + "' is missing.")
                            ("Add the required manifest field '" + name + "'.")

                        None

                let formatValue =
                    match required "format" with
                    | Some element -> readString "/format" element
                    | None -> None

                match formatValue with
                | Some value when value <> "football-engine.mod" ->
                    add
                        "MOD-MANIFEST-FORMAT"
                        (Some "/format")
                        "The manifest format is not supported."
                        "Set format to football-engine.mod."
                | _ -> ()

                let formatVersion =
                    match findProperty "formatVersion" with
                    | None ->
                        add
                            "MOD-MANIFEST-FORMAT-VERSION"
                            (Some "/formatVersion")
                            "formatVersion is required and must be exactly 1."
                            "Add formatVersion 1; older and newer versions are not migrated."

                        None
                    | Some property ->
                        match readInt "/formatVersion" property.Value with
                        | Some 1 -> Some 1
                        | Some _ ->
                            add
                                "MOD-MANIFEST-FORMAT-VERSION"
                                (Some "/formatVersion")
                                "Only manifest format version 1 is supported."
                                "Set formatVersion to version 1; do not rely on migration."

                            None
                        | None ->
                            add
                                "MOD-MANIFEST-FORMAT-VERSION"
                                (Some "/formatVersion")
                                "formatVersion is required and must be exactly 1."
                                "Set formatVersion to version 1."

                            None

                let modIdText =
                    match required "modId" with
                    | Some element ->
                        match readString "/modId" element with
                        | Some value when isValidModIdText value -> Some value
                        | Some value ->
                            add
                                "MOD-ID-INVALID"
                                (Some "/modId")
                                ("Invalid mod ID '" + value + "'.")
                                "Use a stable lowercase ASCII mod ID."

                            None
                        | None -> None
                    | None -> None

                let version =
                    match required "version" with
                    | Some element ->
                        match readString "/version" element with
                        | Some value ->
                            match parseSemVersion value with
                            | Some _ -> Some value
                            | None ->
                                add
                                    "MOD-SEMVER-INVALID"
                                    (Some "/version")
                                    "The mod version must use strict SemVer 2.0 syntax."
                                    "Use strict SemVer: MAJOR.MINOR.PATCH with optional valid prerelease/build identifiers."

                                None
                        | None -> None
                    | None -> None

                let engineValues =
                    match required "engine" with
                    | Some element ->
                        match readObject "/engine" element with
                        | Some objectProperties ->
                            inspectObjectKeys "/engine" objectProperties [ "minInclusive"; "maxExclusive" ]

                            let minimum =
                                match requiredNested "/engine" objectProperties "minInclusive" with
                                | Some value -> readString "/engine/minInclusive" value
                                | None -> None

                            let maximum =
                                match requiredNested "/engine" objectProperties "maxExclusive" with
                                | Some value -> readString "/engine/maxExclusive" value
                                | None -> None

                            minimum, maximum
                        | None -> None, None
                    | None -> None, None

                let engineMinimum =
                    match fst engineValues with
                    | Some value ->
                        match parseSemVersion value with
                        | Some parsed -> Some(parsed, value)
                        | None ->
                            add
                                "MOD-SEMVER-INVALID"
                                (Some "/engine/minInclusive")
                                "The engine minimum must use strict SemVer 2.0 syntax."
                                "Use strict SemVer with a complete MAJOR.MINOR.PATCH engine version."

                            None
                    | None -> None

                let engineMaximum =
                    match snd engineValues with
                    | Some value ->
                        match parseSemVersion value with
                        | Some parsed -> Some(parsed, value)
                        | None ->
                            add
                                "MOD-SEMVER-INVALID"
                                (Some "/engine/maxExclusive")
                                "The engine maximum must use strict SemVer 2.0 syntax."
                                "Use strict SemVer with a complete MAJOR.MINOR.PATCH engine version."

                            None
                    | None -> None

                match engineMinimum, engineMaximum with
                | Some(minimum, _), Some(maximum, _) ->
                    if compareSemVersion minimum maximum >= 0 then
                        add
                            "MOD-ENGINE-RANGE-INVALID"
                            (Some "/engine")
                            "The engine minimum must be lower than maxExclusive."
                            "Choose a non-empty engine version interval."

                    if
                        compareSemVersion currentEngineVersion minimum < 0
                        || compareSemVersion currentEngineVersion maximum >= 0
                    then
                        add
                            "MOD-ENGINE-INCOMPATIBLE"
                            (Some "/engine")
                            "The package engine range does not include engine version 2026.1.0."
                            "Set minInclusive at or below 2026.1.0 and maxExclusive above it."
                | _ -> ()

                let apiValues =
                    match required "api" with
                    | Some element ->
                        match readObject "/api" element with
                        | Some objectProperties ->
                            inspectObjectKeys "/api" objectProperties [ "content"; "scripts" ]

                            let content =
                                match requiredNested "/api" objectProperties "content" with
                                | Some value -> readInt "/api/content" value
                                | None -> None

                            let scripts =
                                match requiredNested "/api" objectProperties "scripts" with
                                | Some value -> readInt "/api/scripts" value
                                | None -> None

                            content, scripts
                        | None -> None, None
                    | None -> None, None

                match fst apiValues with
                | Some value when value <> CurrentContentApi ->
                    add
                        "MOD-MANIFEST-CONTENT-API"
                        (Some "/api/content")
                        "The content API is incompatible with this reader."
                        "Use content API 1 or publish a package for this reader."
                | None -> ()
                | _ -> ()

                match snd apiValues with
                | Some value when value <> CurrentScriptsApi ->
                    add
                        "MOD-MANIFEST-SCRIPTS-API"
                        (Some "/api/scripts")
                        "Scripts are not enabled by content API 1."
                        "Set scripts API 0; executable mod code is not accepted."
                | None -> ()
                | _ -> ()

                let namespaceValue =
                    match required "namespace" with
                    | Some element ->
                        match readString "/namespace" element with
                        | Some value when isValidNamespaceText value -> Some value
                        | Some value ->
                            add
                                "MOD-NAMESPACE-INVALID"
                                (Some "/namespace")
                                ("Invalid namespace '" + value + "'.")
                                "Use a stable lowercase ASCII namespace."

                            None
                        | None -> None
                    | None -> None

                match modIdText, namespaceValue with
                | Some modId, Some namespaceId when modId <> namespaceId ->
                    add
                        "MOD-NAMESPACE-MISMATCH"
                        (Some "/namespace")
                        "namespace must equal modId in format version 1."
                        "Set namespace to the declared lowercase modId."
                | _ -> ()

                let title =
                    match required "title" with
                    | Some element -> readString "/title" element
                    | None -> None

                let description =
                    match required "description" with
                    | Some element -> readString "/description" element
                    | None -> None

                match required "authors" with
                | Some element ->
                    match readArray "/authors" element with
                    | Some authors ->
                        authors
                        |> List.iteri (fun index author ->
                            let path = "/authors/" + string index

                            match readObject path author with
                            | Some authorProperties ->
                                inspectObjectKeys path authorProperties [ "id"; "displayName" ]

                                match requiredNested path authorProperties "id" with
                                | Some idElement ->
                                    match readString (appendJsonPath path "id") idElement with
                                    | Some value when not (isValidModIdText value) ->
                                        add
                                            "MOD-ID-INVALID"
                                            (Some(appendJsonPath path "id"))
                                            ("Invalid author ID '" + value + "'.")
                                            "Use a stable lowercase ASCII author ID."
                                    | _ -> ()
                                | None -> ()

                                match requiredNested path authorProperties "displayName" with
                                | Some displayName ->
                                    readString (appendJsonPath path "displayName") displayName |> ignore
                                | None -> ()
                            | None -> ())
                    | None -> ()
                | None -> ()

                let dependencies = ResizeArray<ModId * string>()

                match required "dependencies" with
                | Some element ->
                    match readArray "/dependencies" element with
                    | Some dependencyItems ->
                        dependencyItems
                        |> List.iteri (fun index dependency ->
                            let path = "/dependencies/" + string index

                            match readObject path dependency with
                            | Some dependencyProperties ->
                                inspectObjectKeys path dependencyProperties [ "modId"; "range" ]

                                let dependencyId =
                                    match requiredNested path dependencyProperties "modId" with
                                    | Some value ->
                                        match readString (appendJsonPath path "modId") value with
                                        | Some text when isValidModIdText text -> Some(ModId text)
                                        | Some text ->
                                            add
                                                "MOD-ID-INVALID"
                                                (Some(appendJsonPath path "modId"))
                                                ("Invalid dependency mod ID '" + text + "'.")
                                                "Use a stable lowercase ASCII mod ID."

                                            None
                                        | None -> None
                                    | None -> None

                                let dependencyRange =
                                    match requiredNested path dependencyProperties "range" with
                                    | Some value ->
                                        match readString (appendJsonPath path "range") value with
                                        | Some text ->
                                            match parseRange text with
                                            | Some _ -> Some text
                                            | None ->
                                                add
                                                    "MOD-RANGE-INVALID"
                                                    (Some(appendJsonPath path "range"))
                                                    "Dependency range must contain strict >= and/or < SemVer comparisons."
                                                    "Use a range such as >=1.0.0 <2.0.0."

                                                None
                                        | None -> None
                                    | None -> None

                                match dependencyId, dependencyRange with
                                | Some id, Some range -> dependencies.Add(id, range)
                                | _ -> ()
                            | None -> ())
                    | None -> ()
                | None -> ()

                let precedenceValues =
                    match required "precedence" with
                    | Some element ->
                        match readObject "/precedence" element with
                        | Some objectProperties ->
                            inspectObjectKeys "/precedence" objectProperties [ "layer"; "after"; "before" ]

                            let layer =
                                match requiredNested "/precedence" objectProperties "layer" with
                                | Some value -> readInt "/precedence/layer" value
                                | None -> None

                            let after =
                                match requiredNested "/precedence" objectProperties "after" with
                                | Some value -> readArray "/precedence/after" value
                                | None -> None

                            let before =
                                match requiredNested "/precedence" objectProperties "before" with
                                | Some value -> readArray "/precedence/before" value
                                | None -> None

                            layer, after, before
                        | None -> None, None, None
                    | None -> None, None, None

                let parseModIdArray path items =
                    let values = ResizeArray<ModId>()

                    items
                    |> List.iteri (fun index item ->
                        let itemPath = path + "/" + string index

                        match readString itemPath item with
                        | Some value when isValidModIdText value -> values.Add(ModId value)
                        | Some value ->
                            add
                                "MOD-ID-INVALID"
                                (Some itemPath)
                                ("Invalid precedence mod ID '" + value + "'.")
                                "Use a stable lowercase ASCII mod ID."
                        | None -> ())

                    List.ofSeq values

                let priority, afterItems, beforeItems = precedenceValues

                let after =
                    match afterItems with
                    | Some items -> parseModIdArray "/precedence/after" items
                    | None -> []

                let before =
                    match beforeItems with
                    | Some items -> parseModIdArray "/precedence/before" items
                    | None -> []

                let declaredFiles = ResizeArray<string * string>()

                match required "files" with
                | Some element ->
                    match readArray "/files" element with
                    | Some fileItems ->
                        fileItems
                        |> List.iteri (fun index fileItem ->
                            let path = "/files/" + string index

                            match readObject path fileItem with
                            | Some fileProperties ->
                                inspectObjectKeys path fileProperties [ "path"; "sha256" ]

                                let filePath =
                                    match requiredNested path fileProperties "path" with
                                    | Some value ->
                                        match readString (appendJsonPath path "path") value with
                                        | Some text ->
                                            if not (isSafeRelativePath text) then
                                                let code =
                                                    if pathLooksLikeTraversal text then
                                                        "MOD-PATH-TRAVERSAL"
                                                    else
                                                        "MOD-PATH-INVALID"

                                                addDiagnostic
                                                    diagnostics
                                                    code
                                                    None
                                                    None
                                                    (Some text)
                                                    None
                                                    None
                                                    ("Unsafe relative package path '" + text + "'.")
                                                    "Use a normalized relative path without traversal segments."

                                            if hasExecutableExtension text then
                                                addDiagnostic
                                                    diagnostics
                                                    "MOD-PATH-EXTENSION"
                                                    None
                                                    None
                                                    (Some text)
                                                    None
                                                    None
                                                    "Executable and script files are not accepted in a declarative mod package."
                                                    "Remove the executable file; content API 1 accepts declarative data only."

                                            Some text
                                        | None -> None
                                    | None -> None

                                let fileHash =
                                    match requiredNested path fileProperties "sha256" with
                                    | Some value ->
                                        match readString (appendJsonPath path "sha256") value with
                                        | Some text when isSha256 text -> Some text
                                        | Some text ->
                                            add
                                                "MOD-SHA256-INVALID"
                                                (Some(appendJsonPath path "sha256"))
                                                "File hash must be a lowercase hexadecimal SHA-256 value."
                                                "recalculate the SHA-256 hash and write 64 lowercase hex digits."

                                            None
                                        | None -> None
                                    | None -> None

                                match filePath, fileHash with
                                | Some pathValue, Some hashValue -> declaredFiles.Add(pathValue, hashValue)
                                | _ -> ()
                            | None -> ())
                    | None -> ()
                | None -> ()

                let declaredFileList = List.ofSeq declaredFiles
                let declaredPaths = declaredFileList |> List.map fst

                declaredPaths
                |> List.groupBy id
                |> List.iter (fun (path, occurrences) ->
                    if occurrences.Length > 1 then
                        add
                            "MOD-MANIFEST-DUPLICATE-FILE"
                            None
                            ("File path '" + path + "' is declared more than once.")
                            "Declare each package path exactly once.")

                if
                    declaredPaths
                    <> (declaredPaths
                        |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right)))
                then
                    add
                        "MOD-MANIFEST-FILES-ORDER"
                        (Some "/files")
                        "Manifest files must be ordered by canonical relative path."
                        "Sort the complete files array by path using ordinal order."

                let packageSha256 =
                    match required "packageSha256" with
                    | Some element ->
                        match readString "/packageSha256" element with
                        | Some value when isSha256 value -> Some value
                        | Some value ->
                            add
                                "MOD-PACKAGE-HASH-INVALID"
                                (Some "/packageSha256")
                                "packageSha256 must be a lowercase hexadecimal SHA-256 value."
                                "Recalculate the package hash and write 64 lowercase hex digits."

                            None
                        | None -> None
                    | None -> None

                match required "signature" with
                | Some element when element.ValueKind <> JsonValueKind.Null ->
                    add
                        "MOD-SIGNATURE-UNSUPPORTED"
                        (Some "/signature")
                        "Signatures are reserved but not supported by this reader."
                        "Set signature to null until a supported verifier is available."
                | _ -> ()

                let validEngine = engineMinimum.IsSome && engineMaximum.IsSome

                let validCore =
                    modIdText.IsSome && version.IsSome && namespaceValue.IsSome && priority.IsSome

                if
                    diagnostics.Count = 0
                    && formatVersion.IsSome
                    && validCore
                    && validEngine
                    && packageSha256.IsSome
                then
                    let manifest =
                        { Id = ModId modIdText.Value
                          Version = version.Value
                          Dependencies = List.ofSeq dependencies
                          Priority = priority.Value
                          Before = before
                          After = after }

                    let canonicalResult = canonicalizeElement root "" true

                    match canonicalResult with
                    | Ok canonical ->
                        success <-
                            Some(
                                Ok
                                    { Manifest = manifest
                                      PackageSha256 = packageSha256.Value
                                      DeclaredFiles = declaredFileList
                                      CanonicalWithoutPackageSha256 = canonical }
                            )
                    | Error error -> error.Diagnostics |> List.iter diagnostics.Add
        with ex ->
            add
                "MOD-MANIFEST-JSON"
                None
                ("Manifest JSON could not be parsed: " + ex.Message)
                "Fix the manifest JSON syntax and encode it as UTF-8."

        match success with
        | Some result when diagnostics.Count = 0 -> result
        | None -> Error(errorFromDiagnostics diagnostics)
        | Some _ -> Error(errorFromDiagnostics diagnostics)

    and private canonicalNumber (raw: string) =
        let sign, unsigned =
            if raw.StartsWith("-", StringComparison.Ordinal) then
                "-", raw.Substring(1)
            else
                "", raw

        let exponentIndex = unsigned.IndexOfAny([| 'e'; 'E' |])

        let mantissa, exponent =
            if exponentIndex >= 0 then
                let exponentText = unsigned.Substring(exponentIndex + 1)
                let mutable parsed = 0

                if
                    Int32.TryParse(exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, &parsed)
                then
                    unsigned.Substring(0, exponentIndex), parsed
                else
                    unsigned, 0
            else
                unsigned, 0

        let decimalIndex = mantissa.IndexOf('.')

        let integerPart, fractionalPart =
            if decimalIndex >= 0 then
                mantissa.Substring(0, decimalIndex), mantissa.Substring(decimalIndex + 1)
            else
                mantissa, ""

        let mutable digits = integerPart + fractionalPart
        let mutable decimalPosition = integerPart.Length + exponent

        while digits.Length > 0 && digits[0] = '0' do
            digits <- digits.Substring(1)
            decimalPosition <- decimalPosition - 1

        if digits.Length = 0 || digits |> Seq.forall (fun character -> character = '0') then
            "0"
        else
            while digits.Length > 1 && digits[digits.Length - 1] = '0' do
                digits <- digits.Substring(0, digits.Length - 1)

            let result =
                if decimalPosition <= 0 then
                    "0." + String.replicate (-decimalPosition) "0" + digits
                elif decimalPosition >= digits.Length then
                    digits + String.replicate (decimalPosition - digits.Length) "0"
                else
                    digits.Substring(0, decimalPosition) + "." + digits.Substring(decimalPosition)

            if sign = "-" then "-" + result else result

    and private canonicalizeElement
        (element: JsonElement)
        (path: string)
        (skipPackageSha256: bool)
        : Result<string, ModError> =
        let makeJsonError code jsonPath message action =
            Error(errorFromDiagnostic (diagnostic code None None None (Some jsonPath) None message action))

        match element.ValueKind with
        | JsonValueKind.Null -> Ok "null"
        | JsonValueKind.True -> Ok "true"
        | JsonValueKind.False -> Ok "false"
        | JsonValueKind.String -> Ok(JsonSerializer.Serialize(element.GetString()))
        | JsonValueKind.Number -> Ok(canonicalNumber (element.GetRawText()))
        | JsonValueKind.Array ->
            let values = ResizeArray<string>()
            let mutable error = None

            element.EnumerateArray()
            |> Seq.iteri (fun index child ->
                if error.IsNone then
                    match canonicalizeElement child (path + "/" + string index) false with
                    | Ok value -> values.Add value
                    | Error item -> error <- Some item)

            match error with
            | Some item -> Error item
            | None -> Ok("[" + String.concat "," (List.ofSeq values) + "]")
        | JsonValueKind.Object ->
            let properties = element.EnumerateObject() |> Seq.toList

            let duplicate =
                properties
                |> List.groupBy (fun property -> property.Name)
                |> List.tryFind (fun (_, occurrences) -> occurrences.Length > 1)

            match duplicate with
            | Some(name, _) ->
                makeJsonError
                    "MOD-JSON-DUPLICATE-KEY"
                    (appendJsonPath path name)
                    ("JSON property '" + name + "' occurs more than once.")
                    "Remove duplicate JSON properties before canonicalization."
            | None ->
                let canonicalProperties = ResizeArray<string>()
                let mutable error = None

                properties
                |> List.filter (fun property -> not (skipPackageSha256 && path = "" && property.Name = "packageSha256"))
                |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left.Name, right.Name))
                |> List.iter (fun property ->
                    if error.IsNone then
                        match canonicalizeElement property.Value (appendJsonPath path property.Name) false with
                        | Ok value -> canonicalProperties.Add(JsonSerializer.Serialize(property.Name) + ":" + value)
                        | Error item -> error <- Some item)

                match error with
                | Some item -> Error item
                | None -> Ok("{" + String.concat "," (List.ofSeq canonicalProperties) + "}")
        | _ -> makeJsonError "MOD-JSON-INVALID" path "Unsupported JSON token." "Use only valid JSON values."

    let canonicalizeJson (json: string) : Result<string, ModError> =
        try
            use document =
                JsonDocument.Parse(
                    json,
                    JsonDocumentOptions(
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow,
                        MaxDepth = 128
                    )
                )

            canonicalizeElement document.RootElement "" false
        with ex ->
            Error
                { Diagnostics =
                    [ diagnostic
                          "MOD-JSON-INVALID"
                          None
                          None
                          None
                          None
                          None
                          ("JSON could not be parsed: " + ex.Message)
                          "Fix the JSON syntax and encode the document as UTF-8." ] }

    let hashSha256 (bytes: byte[]) =
        use sha256 = SHA256.Create()

        sha256.ComputeHash(bytes)
        |> Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    let parseManifest (json: string) : Result<ModManifest, ModError> =
        match parseManifestDocument json with
        | Ok parsed -> Ok parsed.Manifest
        | Error error -> Error error

    let parseContentId (value: string) : Result<ContentId, ModError> =
        match parseContentIdValue value with
        | Some contentId -> Ok contentId
        | None ->
            Error
                { Diagnostics =
                    [ diagnostic
                          "MOD-CONTENT-ID-INVALID"
                          None
                          None
                          None
                          None
                          (if isNull value then None else Some value)
                          "Content ID must use namespace:kind/local-id with lowercase stable identifiers."
                          "Use a lowercase qualified content ID such as community.example:club/river." ] }

    let private packageHash (parsed: ParsedManifest) (actualFiles: (string * byte[]) list) =
        let canonicalFiles =
            actualFiles
            |> List.sortWith (fun (left, _) (right, _) -> StringComparer.Ordinal.Compare(left, right))
            |> List.map (fun (path, bytes) -> path + "\n" + hashSha256 bytes)
            |> String.concat "\n"

        hashSha256 (Encoding.UTF8.GetBytes(parsed.CanonicalWithoutPackageSha256 + "\n" + canonicalFiles))

    let private knownContentKinds =
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

    let private allowedContentKeys kind =
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

    let private validateContentFile
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

    let private selectionContext (selection: PackageSelection) =
        let manifest = selection.Package.Manifest
        Some manifest.ModId, Some manifest.Version, Some selection.RelativePath

    let private validateModIdForResolution diagnostics selection path modId =
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

    let private validateContentIdForResolution diagnostics selection path contentId =
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

    let private compareSelections left right =
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

    let private buildOrder (selections: PackageSelection list) : Result<PackageSelection list, ModError> =
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

    let private mergeContentMap
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

    let private canonicalContentMap label (content: Map<ContentId, string>) =
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

    let private lockText (value: string) =
        if isNull value then
            "-1:"
        else
            string value.Length + ":" + value

    let private lockInt (value: int) =
        value.ToString(CultureInfo.InvariantCulture)

    let private canonicalDependencies (manifest: ModManifest) =
        manifest.Dependencies
        |> List.map (fun (dependencyId, range) -> modIdValue dependencyId, range)
        |> List.sortWith (fun (leftId, leftRange) (rightId, rightRange) ->
            let idComparison = StringComparer.Ordinal.Compare(leftId, rightId)

            if idComparison <> 0 then
                idComparison
            else
                StringComparer.Ordinal.Compare(leftRange, rightRange))

    let private canonicalPrecedenceIds ids =
        ids
        |> List.map modIdValue
        |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right))

    let private canonicalDependencyLock (packages: FootballEngine.Domain.DataLockPackage list) =
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

    let private runtimeIdTable (sports: Map<ContentId, string>) =
        sports
        |> Map.toList
        |> List.map (fun (contentId, _) -> contentIdValue contentId)
        |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right))
        |> List.mapi (fun runtimeId contentId ->
            { ContentId = contentId
              RuntimeId = runtimeId })

    let private requiredAssets (assets: Map<ContentId, string>) =
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

    let private makeDataLock
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

    let private validatePhysicalDataLock (resolved: ResolvedDataSet) : Result<ResolvedDataSet, ModError> =
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

    let private duplicateContentDiagnostics (selections: PackageSelection list) =
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

    let private manifestFromDataLock (package: FootballEngine.Domain.DataLockPackage) : ModManifest =
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

    type private ParsedContent =
        | SportsContentEntry of ContentId * string
        | TranslationEntry of ContentId * string
        | AssetEntry of ContentId * string
        | PatchEntries of PatchOperation list

    type private WorkspaceScan =
        { ManifestJson: string option
          Files: Map<string, byte[]>
          SymlinkPaths: Set<string>
          Diagnostics: ModDiagnostic list }

    type private InstalledWorkspace =
        { RelativePath: string
          FullPath: string
          RootOrder: int }

    [<Literal>]
    let private MaxWorkspaceFileBytes = 10L * 1024L * 1024L

    let private hasFileAttribute (attributes: FileAttributes) flag = (attributes &&& flag) = flag

    let private ioDiagnostic (code: string) (relativePath: string) (message: string) (suggestedAction: string) =
        diagnostic code None None (Some relativePath) None None message suggestedAction

    let private contentDiagnostic
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

    let private contextualizeContentError (manifest: ModManifest) (relativePath: string) (error: ModError) : ModError =
        { Diagnostics =
            error.Diagnostics
            |> List.map (fun item ->
                { item with
                    ModId = Some manifest.ModId
                    Version = Some manifest.Version
                    RelativePath = Some relativePath }) }

    let private findReparseComponent (path: string) : Result<string option, exn> =
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

    let private validateDirectoryRoot relativePath root : Result<string, ModError> =
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

    let private readWorkspaceFile (fullPath: string) (relativePath: string) : Result<byte[], ModError> =
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

    let private decodeManifest (bytes: byte[]) : Result<string, ModError> =
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

    let private scanWorkspace (relativePath: string) (workspace: string) : WorkspaceScan =
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

    let private isValidJsonPointer (value: string) =
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

    let private parsePatchOperations
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

    let rec private parsePackageContents
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

    and private workspaceRelativeName (workspace: string) : Result<string, ModError> =
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

    and private loadPackageAt (relativePath: string) (workspace: string) : Result<PackageSelection, ModError> =
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

    and private discoverInstalledRoot
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

    and private installedSelectionOrder (left: PackageSelection) (right: PackageSelection) =
        let pathComparison =
            StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath)

        if pathComparison <> 0 then
            pathComparison
        else
            let idComparison =
                StringComparer.Ordinal.Compare(left.Package.Manifest.ModId, right.Package.Manifest.ModId)

            if idComparison <> 0 then idComparison else 0

    and private duplicateInstalledDiagnostics (selections: PackageSelection list) =
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

    and private parseContentFile
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

    [<Literal>]
    let private MaxPackageFileBytes = 10L * 1024L * 1024L

    [<Literal>]
    let private MaxPackageBytes = 64L * 1024L * 1024L

    [<Literal>]
    let private MaxPackageUncompressedBytes = 128L * 1024L * 1024L

    let private packageIoError code path message action =
        { Diagnostics = [ ioDiagnostic code path message action ] }

    let private readZipPackage (relativePath: string) (packagePath: string) : Result<PackageSelection, ModError> =
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

    let private writeZipPackage
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

    type private InstalledPackageFile =
        { RelativePath: string
          FullPath: string
          RootOrder: int }

    let private discoverFemodRoot
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

    let private applyModOrder (modOrder: string list) (selections: PackageSelection list) =
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
