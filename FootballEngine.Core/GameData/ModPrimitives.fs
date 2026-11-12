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

/// Identifier, version and path rules every mod stage checks against, and the diagnostics they report.
module internal ModPrimitives =
    [<Literal>]
    let internal CurrentFormatVersion = 1

    [<Literal>]
    let internal CurrentContentApi = 1

    [<Literal>]
    let internal CurrentScriptsApi = 0

    type internal SemVersion =
        { Major: int
          Minor: int
          Patch: int
          PreRelease: string list }

    let internal currentEngineVersion =
        { Major = 2026
          Minor = 1
          Patch = 0
          PreRelease = [] }

    type internal RangeComparison =
        | AtLeast of SemVersion
        | Below of SemVersion

    type internal ParsedManifest =
        { Manifest: ModManifest
          PackageSha256: string
          DeclaredFiles: (string * string) list
          CanonicalWithoutPackageSha256: string }

    let internal modIdValue (ModId value) = value

    let internal contentIdValue (ContentId value) = value

    let internal diagnostic code modId version relativePath jsonPath entityId message suggestedAction =
        { Code = code
          ModId = modId
          Version = version
          RelativePath = relativePath
          JsonPath = jsonPath
          EntityId = entityId
          Message = message
          SuggestedAction = suggestedAction }

    let internal addDiagnostic
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

    let internal errorFromDiagnostics (diagnostics: ResizeArray<ModDiagnostic>) =
        { Diagnostics = List.ofSeq diagnostics }

    let internal errorFromDiagnostic item = { Diagnostics = [ item ] }

    let internal isAsciiLowerLetter character = character >= 'a' && character <= 'z'

    let internal isAsciiDigit character = character >= '0' && character <= '9'

    let internal isAsciiAlphaNumeric character =
        isAsciiLowerLetter character || isAsciiDigit character

    let internal isAsciiSemVerCharacter character =
        isAsciiAlphaNumeric character || (character >= 'A' && character <= 'Z')

    let internal isValidNamespaceText (value: string) =
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

    let internal isValidKindText (value: string) =
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

    let internal isValidLocalIdText (value: string) =
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

    let internal isValidModIdText (value: string) =
        isValidNamespaceText value
        && not (value.Contains(':', StringComparison.Ordinal))

    let internal isValidContentIdText (value: string) =
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

    let internal isSha256 (value: string) =
        not (String.IsNullOrEmpty value)
        && value.Length = 64
        && (value
            |> Seq.forall (fun character -> isAsciiDigit character || (character >= 'a' && character <= 'f')))

    let internal normalizedPath (path: string) =
        if isNull path then "" else path.Replace('\\', '/')

    let internal isSafeRelativePath (path: string) =
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

    let internal pathLooksLikeTraversal (path: string) =
        if String.IsNullOrEmpty path then
            false
        else
            let normalized = normalizedPath path
            let segments = normalized.Split('/')

            normalized.StartsWith("/", StringComparison.Ordinal)
            || (normalized.Length >= 2 && normalized[1] = ':')
            || normalized.Contains('\u0000')
            || (segments |> Array.exists (fun segment -> segment = ".."))

    let internal hasExecutableExtension (path: string) =
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

    let internal jsonPathSegment (segment: string) =
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)

    let internal appendJsonPath path segment = path + "/" + jsonPathSegment segment

    let internal parseSemVersion (value: string) : SemVersion option =
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

    let internal compareIdentifier (left: string) (right: string) =
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

    let internal compareSemVersion left right =
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

    let internal parseRange (value: string) : RangeComparison list option =
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

    let internal satisfiesRange version range =
        range
        |> List.forall (function
            | AtLeast minimum -> compareSemVersion version minimum >= 0
            | Below maximum -> compareSemVersion version maximum < 0)

    let internal parseContentIdValue value =
        if isNull value || not (isValidContentIdText value) then
            None
        else
            Some(ContentId value)
