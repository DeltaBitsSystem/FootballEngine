namespace FootballEngine.Data

open System
open System.Globalization
open System.Text.Json
open FootballEngine.Domain

type MaterializedCountry =
    { ContentId: ContentId
      Data: CountryData }

type MaterializedCompetition =
    { ContentId: ContentId
      Name: string
      CompetitionType: CompetitionType }

type ResolvedWorldData =
    { Countries: CountryData list
      InternationalCompetitions: (string * CompetitionType) list
      CountryEntities: MaterializedCountry list
      CompetitionEntities: MaterializedCompetition list }

module ResolvedWorldData =
    type private Context =
        { ContentId: ContentId
          Diagnostics: ResizeArray<ModDiagnostic> }

    type private Entity =
        | CountryEntity of MaterializedCountry
        | CompetitionEntity of MaterializedCompetition

    let private countryEnvelopeKeys =
        set [ "id"; "kind"; "schema"; "code"; "confederation"; "nameKey"; "domain" ]

    let private competitionEnvelopeKeys =
        set [ "id"; "kind"; "schema"; "nameKey"; "domain" ]

    let private countryDomainKeys =
        set [ "Country"; "Names"; "LeagueNames"; "LeagueRules"; "Clubs"; "Cups" ]

    let private countryKeys = set [ "Code"; "Name"; "Confederation" ]
    let private namePoolKeys = set [ "FirstNames"; "LastNames" ]

    let private leagueRuleKeys =
        set [ "PointsForWin"; "PointsForDraw"; "Tiebreakers"; "Promotion"; "Relegation" ]

    let private clubKeys = set [ "Name"; "LeagueLevel"; "Reputation"; "Kit"; "Players" ]

    let private playerKeys =
        set
            [ "Name"
              "Position"
              "Age"
              "Rating"
              "Potential"
              "PreferredFoot"
              "Nationality" ]

    let private kitKeys = set [ "Primary"; "Secondary"; "Pattern" ]
    let private cupKeys = set [ "Name"; "Format" ]

    let private competitionDomainKeys =
        set [ "Name"; "Confederation"; "Format"; "Qualification" ]

    let private qualificationKeys =
        set [ "Kind"; "Level"; "FromPos"; "ToPos"; "CompetitionName"; "Slots" ]

    let private contentIdValue (ContentId value) = value

    let private entityId (ctx: Context) =
        let value = contentIdValue ctx.ContentId
        if String.IsNullOrWhiteSpace value then None else Some value

    let private modId (ctx: Context) =
        if String.IsNullOrWhiteSpace ctx.ContentId.Namespace then
            None
        else
            Some ctx.ContentId.Namespace

    let private addDiagnostic
        (ctx: Context)
        (code: string)
        (jsonPath: string)
        (message: string)
        (suggestedAction: string)
        =
        ctx.Diagnostics.Add(
            { Code = code
              ModId = modId ctx
              Version = None
              RelativePath = None
              JsonPath = Some jsonPath
              EntityId = entityId ctx
              Message = message
              SuggestedAction = suggestedAction }
        )

    let private childPath (path: string) (child: string) =
        if String.IsNullOrEmpty path then
            "/" + child
        else
            path + "/" + child

    let private arrayPath path index = childPath path (string index)

    let private objectProperties (ctx: Context) (path: string) (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then
            addDiagnostic
                ctx
                "MOD-CONTENT-TYPE"
                path
                "The value must be a JSON object."
                "Use an object with the fields declared by the content schema."

            None
        else
            let properties = element.EnumerateObject() |> Seq.toList

            properties
            |> List.groupBy (fun property -> property.Name)
            |> List.iter (fun (name, occurrences) ->
                if occurrences.Length > 1 then
                    addDiagnostic
                        ctx
                        "MOD-CONTENT-DUPLICATE-KEY"
                        (childPath path name)
                        "The JSON object contains a duplicate property."
                        "Keep exactly one occurrence of each property.")

            Some properties

    let private rejectUnknownProperties
        (ctx: Context)
        (path: string)
        (allowed: Set<string>)
        (properties: JsonProperty list)
        =
        properties
        |> List.iter (fun property ->
            if not (Set.contains property.Name allowed) then
                addDiagnostic
                    ctx
                    "MOD-CONTENT-UNKNOWN-KEY"
                    (childPath path property.Name)
                    ("Unknown field '" + property.Name + "'.")
                    "Remove the field or publish a schema that explicitly supports it.")

    let private findProperty (properties: JsonProperty list) name =
        properties
        |> List.tryFind (fun property -> property.Name = name)
        |> Option.map _.Value

    let private requiredProperty (ctx: Context) (path: string) (properties: JsonProperty list) (name: string) =
        match findProperty properties name with
        | Some value -> Some value
        | None ->
            addDiagnostic
                ctx
                "MOD-CONTENT-MISSING-FIELD"
                (childPath path name)
                ("Required field '" + name + "' is missing.")
                ("Add the required field '" + name + "'.")

            None

    let private stringValue (ctx: Context) (path: string) (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.String then
            addDiagnostic
                ctx
                "MOD-CONTENT-TYPE"
                path
                "The value must be a string."
                "Use the string type declared by the content schema."

            None
        else
            let value = element.GetString()

            if isNull value then
                addDiagnostic
                    ctx
                    "MOD-CONTENT-VALUE"
                    path
                    "A JSON string value cannot be null."
                    "Provide a non-null string."

                None
            else
                Some value

    let private nonEmptyString (ctx: Context) (path: string) (value: string) =
        if String.IsNullOrWhiteSpace value then
            addDiagnostic
                ctx
                "MOD-CONTENT-VALUE"
                path
                "The string must not be empty."
                "Provide a non-empty stable value."

            None
        else
            Some value

    let private requiredString ctx path properties name =
        requiredProperty ctx path properties name
        |> Option.bind (stringValue ctx (childPath path name))

    let private requiredNonEmptyString ctx path properties name =
        requiredString ctx path properties name
        |> Option.bind (nonEmptyString ctx (childPath path name))

    let private intValue (ctx: Context) (path: string) (element: JsonElement) =
        let mutable value = 0

        if element.ValueKind = JsonValueKind.Number && element.TryGetInt32(&value) then
            Some value
        else
            addDiagnostic
                ctx
                "MOD-CONTENT-TYPE"
                path
                "The value must be a 32-bit JSON integer."
                "Use an integer value without a fractional part."

            None

    let private requiredInt ctx path properties name =
        requiredProperty ctx path properties name
        |> Option.bind (intValue ctx (childPath path name))

    let private nullableInt (ctx: Context) (path: string) (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Null -> Some None
        | JsonValueKind.Number ->
            let mutable value = 0

            if element.TryGetInt32(&value) then
                Some(Some value)
            else
                addDiagnostic
                    ctx
                    "MOD-CONTENT-TYPE"
                    path
                    "The value must be a 32-bit JSON integer or null."
                    "Use an integer value or explicit null."

                None
        | _ ->
            addDiagnostic
                ctx
                "MOD-CONTENT-TYPE"
                path
                "The value must be a 32-bit JSON integer or null."
                "Use an integer value or explicit null."

            None

    let private requiredNullableInt ctx path properties name =
        requiredProperty ctx path properties name
        |> Option.bind (nullableInt ctx (childPath path name))

    let private nullableString (ctx: Context) (path: string) (element: JsonElement) =
        match element.ValueKind with
        | JsonValueKind.Null -> Some None
        | JsonValueKind.String ->
            let value = element.GetString()

            if isNull value then
                addDiagnostic
                    ctx
                    "MOD-CONTENT-VALUE"
                    path
                    "A JSON string value cannot be null."
                    "Use explicit JSON null for an optional string."

                None
            elif String.IsNullOrWhiteSpace value then
                addDiagnostic
                    ctx
                    "MOD-CONTENT-VALUE"
                    path
                    "An optional string, when present, must not be empty."
                    "Use a non-empty string or explicit null."

                None
            else
                Some(Some value)
        | _ ->
            addDiagnostic
                ctx
                "MOD-CONTENT-TYPE"
                path
                "The value must be a string or null."
                "Use a string value or explicit JSON null."

            None

    let private requiredNullableString ctx path properties name =
        requiredProperty ctx path properties name
        |> Option.bind (nullableString ctx (childPath path name))

    let private arrayElements (ctx: Context) (path: string) (element: JsonElement) =
        if element.ValueKind = JsonValueKind.Array then
            Some(element.EnumerateArray() |> Seq.toList)
        else
            addDiagnostic
                ctx
                "MOD-CONTENT-TYPE"
                path
                "The value must be a JSON array."
                "Use an array with the elements declared by the content schema."

            None

    let private requiredArray ctx path properties name =
        requiredProperty ctx path properties name
        |> Option.bind (arrayElements ctx (childPath path name))

    let private stringArray (ctx: Context) (path: string) (element: JsonElement) =
        match arrayElements ctx path element with
        | None -> None
        | Some elements ->
            let values = ResizeArray<string>()
            let mutable valid = true

            elements
            |> List.iteri (fun index item ->
                match stringValue ctx (arrayPath path index) item with
                | Some value -> values.Add value
                | None -> valid <- false)

            if valid then Some(List.ofSeq values) else None

    let private requiredStringArray ctx path properties name =
        requiredProperty ctx path properties name
        |> Option.bind (stringArray ctx (childPath path name))

    let private parsedArray
        (ctx: Context)
        (path: string)
        (element: JsonElement)
        (parse: string -> JsonElement -> 'a option)
        =
        match arrayElements ctx path element with
        | None -> None
        | Some elements ->
            let values = ResizeArray<'a>()
            let mutable valid = true

            elements
            |> List.iteri (fun index item ->
                match parse (arrayPath path index) item with
                | Some value -> values.Add value
                | None -> valid <- false)

            if valid then Some(List.ofSeq values) else None

    let private parsedStringArray
        (ctx: Context)
        (path: string)
        (element: JsonElement)
        (parse: string -> string -> 'a option)
        =
        match arrayElements ctx path element with
        | None -> None
        | Some elements ->
            let values = ResizeArray<'a>()
            let mutable valid = true

            elements
            |> List.iteri (fun index item ->
                let itemPath = arrayPath path index

                match stringValue ctx itemPath item with
                | None -> valid <- false
                | Some value ->
                    match parse itemPath value with
                    | Some parsed -> values.Add parsed
                    | None -> valid <- false)

            if valid then Some(List.ofSeq values) else None

    let private parseConfederation (ctx: Context) (path: string) value =
        match value with
        | "UEFA" -> Some UEFA
        | "CONMEBOL" -> Some CONMEBOL
        | "CONCACAF" -> Some CONCACAF
        | "CAF" -> Some CAF
        | "AFC" -> Some AFC
        | "OFC" -> Some OFC
        | _ ->
            addDiagnostic
                ctx
                "MOD-ENUM-UNKNOWN"
                path
                ("Unknown confederation '" + value + "'.")
                "Use UEFA, CONMEBOL, CONCACAF, CAF, AFC or OFC."

            None

    let private requiredConfederation ctx path properties name =
        requiredString ctx path properties name
        |> Option.bind (parseConfederation ctx (childPath path name))

    let private requiredNullableConfederation ctx path properties name =
        requiredProperty ctx path properties name
        |> Option.bind (fun element ->
            let fieldPath = childPath path name

            match element.ValueKind with
            | JsonValueKind.Null -> Some None
            | JsonValueKind.String ->
                let value = element.GetString()

                if isNull value then
                    addDiagnostic
                        ctx
                        "MOD-CONTENT-VALUE"
                        fieldPath
                        "A JSON string value cannot be null."
                        "Use explicit JSON null for an optional confederation."

                    None
                else
                    parseConfederation ctx fieldPath value |> Option.map Some
            | _ ->
                addDiagnostic
                    ctx
                    "MOD-CONTENT-TYPE"
                    fieldPath
                    "The value must be a confederation string or null."
                    "Use a supported confederation string or explicit JSON null."

                None)

    let private parseTiebreaker (ctx: Context) (path: string) (value: string) =
        match value with
        | "GD" -> Some GoalDifference
        | "GS" -> Some GoalsScored
        | "H2H" -> Some HeadToHead
        | "H2HGD" -> Some HeadToHeadGoalDifference
        | _ ->
            addDiagnostic
                ctx
                "MOD-ENUM-UNKNOWN"
                path
                ("Unknown tiebreaker '" + value + "'.")
                "Use GD, GS, H2H or H2HGD."

            None

    let private parseIntegerText (ctx: Context) (path: string) (text: string) =
        let mutable value = 0

        if Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, &value) then
            Some value
        else
            addDiagnostic
                ctx
                "MOD-CONTENT-FIELD"
                path
                ("'" + text + "' is not a valid integer.")
                "Use a base-10 32-bit integer."

            None

    let private nonNegative (ctx: Context) (path: string) (value: int) =
        if value < 0 then
            addDiagnostic ctx "MOD-CONTENT-FIELD" path "The value must not be negative." "Use a non-negative integer."

        value

    let private atLeast (ctx: Context) (path: string) (minimum: int) (value: int) =
        if value < minimum then
            addDiagnostic
                ctx
                "MOD-CONTENT-FIELD"
                path
                ("The value must be at least " + string minimum + ".")
                ("Use an integer greater than or equal to " + string minimum + ".")

        value

    let private parsePromotion (ctx: Context) (path: string) (value: string) =
        let parts = value.Split([| ':' |], StringSplitOptions.None)

        if parts.Length <> 2 then
            addDiagnostic
                ctx
                "MOD-ENUM-UNKNOWN"
                path
                ("Invalid promotion rule '" + value + "'.")
                "Use AP:n for automatic promotion or PP:n for a promotion playoff."

            None
        else
            match parseIntegerText ctx path parts[1] with
            | None -> None
            | Some slots ->
                let slots = nonNegative ctx path slots

                match parts[0] with
                | "AP" -> Some(AutomaticPromotion slots)
                | "PP" -> Some(PlayoffPromotion slots)
                | _ ->
                    addDiagnostic
                        ctx
                        "MOD-ENUM-UNKNOWN"
                        path
                        ("Unknown promotion rule '" + parts[0] + "'.")
                        "Use AP or PP as the promotion rule code."

                    None

    let private parseRelegation (ctx: Context) (path: string) (value: string) =
        let parts = value.Split([| ':' |], StringSplitOptions.None)

        if parts.Length <> 2 then
            addDiagnostic
                ctx
                "MOD-ENUM-UNKNOWN"
                path
                ("Invalid relegation rule '" + value + "'.")
                "Use AR:n for automatic relegation or PR:n for a relegation playoff."

            None
        else
            match parseIntegerText ctx path parts[1] with
            | None -> None
            | Some slots ->
                let slots = nonNegative ctx path slots

                match parts[0] with
                | "AR" -> Some(AutomaticRelegation slots)
                | "PR" -> Some(PlayoffRelegation slots)
                | _ ->
                    addDiagnostic
                        ctx
                        "MOD-ENUM-UNKNOWN"
                        path
                        ("Unknown relegation rule '" + parts[0] + "'.")
                        "Use AR or PR as the relegation rule code."

                    None

    let private parseDelimitedStringEnum
        (ctx: Context)
        (path: string)
        (text: string)
        (parse: string -> string -> 'a option)
        =
        if text = "" then
            Some []
        else
            text.Split([| ',' |], StringSplitOptions.None)
            |> Array.toList
            |> List.mapi (fun index value -> parse (childPath path (string index)) value)
            |> fun results ->
                if results |> List.forall Option.isSome then
                    results |> List.choose id |> Some
                else
                    None

    let private parseTieResolution (ctx: Context) (path: string) (value: string) =
        match value with
        | "ReplayAtNeutral" -> Some ReplayAtNeutral
        | "ExtraTimeThenPenalties" -> Some ExtraTimeThenPenalties
        | "AwayGoals" -> Some AwayGoals
        | "PenaltiesOnly" -> Some PenaltiesOnly
        | _ ->
            addDiagnostic
                ctx
                "MOD-ENUM-UNKNOWN"
                path
                ("Unknown tie resolution '" + value + "'.")
                "Use ReplayAtNeutral, ExtraTimeThenPenalties, AwayGoals or PenaltiesOnly."

            None

    let private parseLegFormat (ctx: Context) (path: string) (value: string) =
        if value.StartsWith("SingleLeg:", StringComparison.Ordinal) then
            parseTieResolution ctx path (value.Substring("SingleLeg:".Length))
            |> Option.map SingleLeg
        elif value.StartsWith("TwoLegs:", StringComparison.Ordinal) then
            parseTieResolution ctx path (value.Substring("TwoLegs:".Length))
            |> Option.map TwoLegs
        else
            addDiagnostic
                ctx
                "MOD-ENUM-UNKNOWN"
                path
                ("Unknown leg format '" + value + "'.")
                "Use SingleLeg:<resolution> or TwoLegs:<resolution>."

            None

    let private parseGroupRules (ctx: Context) (path: string) (value: string) =
        let parts = value.Split([| ';' |], StringSplitOptions.None)

        if parts.Length <> 6 then
            addDiagnostic
                ctx
                "MOD-CONTENT-FIELD"
                path
                ("Invalid group format '" + value + "'.")
                "Use groups;teams;qualifiers;points-for-win;points-for-draw;tiebreakers."

            None
        else
            let numbers =
                parts[0..4]
                |> Array.toList
                |> List.mapi (fun index text -> parseIntegerText ctx (childPath path (string index)) text)

            let tiebreakers =
                parseDelimitedStringEnum ctx (childPath path "5") parts[5] (parseTiebreaker ctx)

            match numbers, tiebreakers with
            | [ Some groupCount; Some teamsPerGroup; Some qualifyPerGroup; Some pointsForWin; Some pointsForDraw ],
              Some tiebreakers ->
                let groupCount = atLeast ctx (childPath path "0") 1 groupCount
                let teamsPerGroup = atLeast ctx (childPath path "1") 2 teamsPerGroup
                let qualifyPerGroup = atLeast ctx (childPath path "2") 1 qualifyPerGroup
                let pointsForWin = nonNegative ctx (childPath path "3") pointsForWin
                let pointsForDraw = nonNegative ctx (childPath path "4") pointsForDraw

                if qualifyPerGroup > teamsPerGroup then
                    addDiagnostic
                        ctx
                        "MOD-CONTENT-FIELD"
                        (childPath path "2")
                        "QualifyPerGroup must not exceed TeamsPerGroup."
                        "Use a qualifying count within the declared group size."

                Some
                    { GroupCount = groupCount
                      TeamsPerGroup = teamsPerGroup
                      QualifyPerGroup = qualifyPerGroup
                      PointsForWin = pointsForWin
                      PointsForDraw = pointsForDraw
                      Tiebreakers = tiebreakers }
            | _ -> None

    let private parseCupFormat (ctx: Context) (path: string) (value: string) =
        if value.StartsWith("SK:", StringComparison.Ordinal) then
            parseLegFormat ctx path (value.Substring(3)) |> Option.map StraightKnockout
        elif value.StartsWith("GTK:", StringComparison.Ordinal) then
            let inner = value.Substring(4)
            let separator = inner.IndexOf('~')

            if
                separator <= 0
                || separator = inner.Length - 1
                || inner.IndexOf('~', separator + 1) >= 0
            then
                addDiagnostic
                    ctx
                    "MOD-CONTENT-FIELD"
                    path
                    ("Invalid group-then-knockout format '" + value + "'.")
                    "Use GTK:<group-rules>~<leg-format>."

                None
            else
                let group = parseGroupRules ctx path (inner.Substring(0, separator))
                let legs = parseLegFormat ctx path (inner.Substring(separator + 1))

                match group, legs with
                | Some group, Some legs -> Some(GroupThenKnockout(group, legs))
                | _ -> None
        else
            addDiagnostic
                ctx
                "MOD-ENUM-UNKNOWN"
                path
                ("Unknown cup format '" + value + "'.")
                "Use SK:<leg-format> or GTK:<group-rules>~<leg-format>."

            None

    let private parseLeagueRule (ctx: Context) (path: string) (element: JsonElement) =
        match objectProperties ctx path element with
        | None -> None
        | Some properties ->
            rejectUnknownProperties ctx path leagueRuleKeys properties

            let pointsForWin = requiredInt ctx path properties "PointsForWin"
            let pointsForDraw = requiredInt ctx path properties "PointsForDraw"

            let tiebreakers =
                requiredProperty ctx path properties "Tiebreakers"
                |> Option.bind (fun element ->
                    parsedStringArray ctx (childPath path "Tiebreakers") element (parseTiebreaker ctx))

            let promotion =
                requiredProperty ctx path properties "Promotion"
                |> Option.bind (fun element ->
                    parsedStringArray ctx (childPath path "Promotion") element (parsePromotion ctx))

            let relegation =
                requiredProperty ctx path properties "Relegation"
                |> Option.bind (fun element ->
                    parsedStringArray ctx (childPath path "Relegation") element (parseRelegation ctx))

            match pointsForWin, pointsForDraw, tiebreakers, promotion, relegation with
            | Some pointsForWin, Some pointsForDraw, Some tiebreakers, Some promotion, Some relegation ->
                let pointsForWin = nonNegative ctx (childPath path "PointsForWin") pointsForWin
                let pointsForDraw = nonNegative ctx (childPath path "PointsForDraw") pointsForDraw

                Some
                    { PointsForWin = pointsForWin
                      PointsForDraw = pointsForDraw
                      Tiebreakers = tiebreakers
                      Promotion = promotion
                      Relegation = relegation }
            | _ -> None

    /// Optional shirt colours: "#RRGGBB" pair plus a pattern; absent or null leaves the UI to generate one.
    let private parseKit (ctx: Context) (path: string) (properties: JsonProperty list) : ClubKit option =
        match properties |> List.tryFind (fun property -> property.Name = "Kit") with
        | None -> None
        | Some property when property.Value.ValueKind = JsonValueKind.Null -> None
        | Some property ->
            let kitPath = childPath path "Kit"

            match objectProperties ctx kitPath property.Value with
            | None -> None
            | Some kitProperties ->
                rejectUnknownProperties ctx kitPath kitKeys kitProperties

                let color name =
                    requiredNonEmptyString ctx kitPath kitProperties name
                    |> Option.bind (fun value ->
                        if Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$") then
                            Some value
                        else
                            addDiagnostic
                                ctx
                                "MOD-CONTENT-VALUE"
                                (childPath kitPath name)
                                "Kit colours must be #RRGGBB hex values."
                                "Use a six-digit hex colour such as #E2353B."

                            None)

                let pattern =
                    requiredNonEmptyString ctx kitPath kitProperties "Pattern"
                    |> Option.bind (fun value ->
                        match value with
                        | "Plain" -> Some Plain
                        | "Sash" -> Some Sash
                        | "Stripes" -> Some Stripes
                        | "Hoops" -> Some Hoops
                        | "Band" -> Some Band
                        | "Halves" -> Some Halves
                        | _ ->
                            addDiagnostic
                                ctx
                                "MOD-CONTENT-VALUE"
                                (childPath kitPath "Pattern")
                                "Unknown kit pattern."
                                "Use Plain, Sash, Stripes, Hoops, Band or Halves."

                            None)

                match color "Primary", color "Secondary", pattern with
                | Some primary, Some secondary, Some pattern ->
                    Some
                        { Primary = primary
                          Secondary = secondary
                          Pattern = pattern }
                | _ -> None

    let private parsePlayerEntry (ctx: Context) (path: string) (element: JsonElement) : PlayerEntry option =
        match objectProperties ctx path element with
        | None -> None
        | Some properties ->
            rejectUnknownProperties ctx path playerKeys properties

            let fieldError name message hint =
                addDiagnostic ctx "MOD-CONTENT-FIELD" (childPath path name) message hint
                None

            let name = requiredNonEmptyString ctx path properties "Name"

            let position =
                requiredNonEmptyString ctx path properties "Position"
                |> Option.bind (fun value ->
                    match value with
                    | "GK" -> Some GK
                    | "DC" -> Some DC
                    | "DL" -> Some DL
                    | "DR" -> Some DR
                    | "WBL" -> Some WBL
                    | "WBR" -> Some WBR
                    | "DM" -> Some DM
                    | "MC" -> Some MC
                    | "ML" -> Some ML
                    | "MR" -> Some MR
                    | "AML" -> Some AML
                    | "AMR" -> Some AMR
                    | "AMC" -> Some AMC
                    | "ST" -> Some ST
                    | _ ->
                        fieldError
                            "Position"
                            "Unknown player position."
                            "Use GK, DC, DL, DR, WBL, WBR, DM, MC, ML, MR, AML, AMR, AMC or ST.")

            let age =
                requiredInt ctx path properties "Age"
                |> Option.bind (fun value ->
                    if value < 15 || value > 45 then
                        fieldError "Age" "Player age must be between 15 and 45." "Use an age from 15 to 45."
                    else
                        Some value)

            let rating =
                requiredInt ctx path properties "Rating"
                |> Option.bind (fun value ->
                    if value < 1 || value > 100 then
                        fieldError "Rating" "Player rating must be between 1 and 100." "Use a rating from 1 to 100."
                    else
                        Some value)

            let potential =
                requiredInt ctx path properties "Potential"
                |> Option.bind (fun value ->
                    match rating with
                    | Some r when value < r || value > 100 ->
                        fieldError
                            "Potential"
                            "Player potential must be between the rating and 100."
                            "Use a potential from the player's rating up to 100."
                    | _ when value < 1 || value > 100 ->
                        fieldError
                            "Potential"
                            "Player potential must be between 1 and 100."
                            "Use a potential from 1 to 100."
                    | _ -> Some value)

            let optionalProperty key =
                properties
                |> List.tryFind (fun property -> property.Name = key)
                |> Option.filter (fun property -> property.Value.ValueKind <> JsonValueKind.Null)

            let foot =
                match optionalProperty "PreferredFoot" with
                | None -> Some None
                | Some property ->
                    stringValue ctx (childPath path "PreferredFoot") property.Value
                    |> Option.bind (fun value ->
                        match value with
                        | "Left" -> Some(Some Left)
                        | "Right" -> Some(Some Right)
                        | "Both" -> Some(Some Both)
                        | _ -> fieldError "PreferredFoot" "Unknown preferred foot." "Use Left, Right or Both.")

            let nationality =
                match optionalProperty "Nationality" with
                | None -> Some None
                | Some property -> stringValue ctx (childPath path "Nationality") property.Value |> Option.map Some

            match name, position, age, rating, potential, foot, nationality with
            | Some name, Some position, Some age, Some rating, Some potential, Some foot, Some nationality ->
                Some
                    { Name = name
                      Position = position
                      Age = age
                      Rating = rating
                      Potential = potential
                      PreferredFoot = foot
                      Nationality = nationality }
            | _ -> None

    let private parseClubEntry (ctx: Context) (path: string) (element: JsonElement) =
        match objectProperties ctx path element with
        | None -> None
        | Some properties ->
            rejectUnknownProperties ctx path clubKeys properties

            let name = requiredNonEmptyString ctx path properties "Name"
            let level = requiredInt ctx path properties "LeagueLevel"
            let reputation = requiredNullableInt ctx path properties "Reputation"

            match name, level, reputation with
            | Some name, Some level, Some reputation ->
                let level = nonNegative ctx (childPath path "LeagueLevel") level

                reputation
                |> Option.iter (fun value ->
                    if value < 0 then
                        addDiagnostic
                            ctx
                            "MOD-CONTENT-FIELD"
                            (childPath path "Reputation")
                            "Club reputation must not be negative."
                            "Use a non-negative reputation or explicit null.")

                let players =
                    match properties |> List.tryFind (fun property -> property.Name = "Players") with
                    | None -> Some []
                    | Some property -> parsedArray ctx (childPath path "Players") property.Value (parsePlayerEntry ctx)

                players
                |> Option.map (fun players ->
                    { Name = name
                      LeagueLevel = level
                      Reputation = reputation
                      Kit = parseKit ctx path properties
                      Players = players })
            | _ -> None

    let private parseCupEntry (ctx: Context) (path: string) (element: JsonElement) =
        match objectProperties ctx path element with
        | None -> None
        | Some properties ->
            rejectUnknownProperties ctx path cupKeys properties

            let name = requiredNonEmptyString ctx path properties "Name"
            let formatText = requiredNonEmptyString ctx path properties "Format"

            let format =
                formatText |> Option.bind (parseCupFormat ctx (childPath path "Format"))

            match name, format with
            | Some name, Some format -> Some(name, format)
            | _ -> None

    let private parseCountry (ctx: Context) (properties: JsonProperty list) =
        rejectUnknownProperties ctx "" countryEnvelopeKeys properties

        let topCode = requiredNonEmptyString ctx "" properties "code"
        let topConfederation = requiredConfederation ctx "" properties "confederation"
        let nameKey = requiredNonEmptyString ctx "" properties "nameKey"
        let domainElement = requiredProperty ctx "" properties "domain"

        let domainProperties = domainElement |> Option.bind (objectProperties ctx "/domain")

        domainProperties
        |> Option.iter (rejectUnknownProperties ctx "/domain" countryDomainKeys)

        let domainCountryProperties =
            domainProperties
            |> Option.bind (fun domain -> requiredProperty ctx "/domain" domain "Country")
            |> Option.bind (objectProperties ctx "/domain/Country")

        domainCountryProperties
        |> Option.iter (rejectUnknownProperties ctx "/domain/Country" countryKeys)

        let domainCode =
            domainCountryProperties
            |> Option.bind (fun country -> requiredNonEmptyString ctx "/domain/Country" country "Code")

        let domainName =
            domainCountryProperties
            |> Option.bind (fun country -> requiredNonEmptyString ctx "/domain/Country" country "Name")

        let domainConfederation =
            domainCountryProperties
            |> Option.bind (fun country -> requiredConfederation ctx "/domain/Country" country "Confederation")

        let namesProperties =
            domainProperties
            |> Option.bind (fun domain -> requiredProperty ctx "/domain" domain "Names")
            |> Option.bind (objectProperties ctx "/domain/Names")

        namesProperties
        |> Option.iter (rejectUnknownProperties ctx "/domain/Names" namePoolKeys)

        let firstNames =
            namesProperties
            |> Option.bind (fun names -> requiredStringArray ctx "/domain/Names" names "FirstNames")

        let lastNames =
            namesProperties
            |> Option.bind (fun names -> requiredStringArray ctx "/domain/Names" names "LastNames")

        let leagueNames =
            domainProperties
            |> Option.bind (fun domain -> requiredStringArray ctx "/domain" domain "LeagueNames")

        let leagueRules =
            domainProperties
            |> Option.bind (fun domain -> requiredProperty ctx "/domain" domain "LeagueRules")
            |> Option.bind (fun element -> parsedArray ctx "/domain/LeagueRules" element (parseLeagueRule ctx))

        let clubs =
            domainProperties
            |> Option.bind (fun domain -> requiredProperty ctx "/domain" domain "Clubs")
            |> Option.bind (fun element -> parsedArray ctx "/domain/Clubs" element (parseClubEntry ctx))

        let cups =
            domainProperties
            |> Option.bind (fun domain -> requiredProperty ctx "/domain" domain "Cups")
            |> Option.bind (fun element -> parsedArray ctx "/domain/Cups" element (parseCupEntry ctx))

        match
            topCode,
            topConfederation,
            nameKey,
            domainCode,
            domainName,
            domainConfederation,
            firstNames,
            lastNames,
            leagueNames,
            leagueRules,
            clubs,
            cups
        with
        | Some topCode,
          Some topConfederation,
          Some _,
          Some domainCode,
          Some domainName,
          Some domainConfederation,
          Some firstNames,
          Some lastNames,
          Some leagueNames,
          Some leagueRules,
          Some clubs,
          Some cups ->
            if
                topCode.Length <> 3
                || not (topCode |> Seq.forall (fun character -> character >= 'A' && character <= 'Z'))
            then
                addDiagnostic
                    ctx
                    "MOD-CONTENT-FIELD"
                    "/code"
                    "Country code must contain three uppercase ASCII letters."
                    "Use the canonical three-letter country code."

            if domainCode <> topCode then
                addDiagnostic
                    ctx
                    "MOD-CONTENT-REFERENCE"
                    "/domain/Country/Code"
                    "The domain country code does not match the envelope code."
                    "Keep the envelope and domain country identity equal."

            if domainConfederation <> topConfederation then
                addDiagnostic
                    ctx
                    "MOD-CONTENT-REFERENCE"
                    "/domain/Country/Confederation"
                    "The domain confederation does not match the envelope confederation."
                    "Keep the envelope and domain confederation equal."

            Some
                { ContentId = ctx.ContentId
                  Data =
                    { Country =
                        { Code = domainCode
                          Name = domainName
                          Confederation = domainConfederation }
                      Names =
                        { FirstNames = firstNames
                          LastNames = lastNames }
                      LeagueNames = leagueNames
                      LeagueRules = leagueRules
                      Clubs = clubs
                      Cups = cups |> List.map snd
                      CupNames = cups |> List.map fst } }
        | _ -> None

    let private parseQualificationSlot ctx path element =
        match objectProperties ctx path element with
        | None -> None
        | Some properties ->
            rejectUnknownProperties ctx path qualificationKeys properties

            let kind = requiredNonEmptyString ctx path properties "Kind"
            let level = requiredNullableInt ctx path properties "Level"
            let fromPosition = requiredNullableInt ctx path properties "FromPos"
            let toPosition = requiredNullableInt ctx path properties "ToPos"
            let competitionName = requiredNullableString ctx path properties "CompetitionName"
            let slots = requiredNullableInt ctx path properties "Slots"

            let checkNonNegative name value =
                value
                |> Option.iter (fun number ->
                    if number < 0 then
                        addDiagnostic
                            ctx
                            "MOD-CONTENT-FIELD"
                            (childPath path name)
                            (name + " must not be negative.")
                            "Use a non-negative integer or explicit null.")

            level |> Option.iter (checkNonNegative "Level")
            fromPosition |> Option.iter (checkNonNegative "FromPos")
            toPosition |> Option.iter (checkNonNegative "ToPos")
            slots |> Option.iter (checkNonNegative "Slots")

            let requireNull name value =
                match value with
                | Some _ ->
                    addDiagnostic
                        ctx
                        "MOD-CONTENT-FIELD"
                        (childPath path name)
                        (name + " must be null for this qualification kind.")
                        "Set fields not used by the selected qualification kind to null."
                | _ -> ()

            match kind, level, fromPosition, toPosition, competitionName, slots with
            | Some "LeaguePosition",
              Some(Some level),
              Some(Some fromPosition),
              Some(Some toPosition),
              Some None,
              Some None ->
                let fromPosition = atLeast ctx (childPath path "FromPos") 1 fromPosition
                let toPosition = atLeast ctx (childPath path "ToPos") 1 toPosition

                if fromPosition > toPosition then
                    addDiagnostic
                        ctx
                        "MOD-CONTENT-FIELD"
                        (childPath path "FromPos")
                        "FromPos must not be greater than ToPos."
                        "Use an inclusive ascending qualification range."

                Some(LeaguePosition(LeagueLevel level, fromPosition, toPosition))
            | Some "LeaguePosition", Some level, Some fromPosition, Some toPosition, competitionName, slots ->
                requireNull "CompetitionName" competitionName
                requireNull "Slots" slots

                if level.IsNone || fromPosition.IsNone || toPosition.IsNone then
                    addDiagnostic
                        ctx
                        "MOD-CONTENT-FIELD"
                        path
                        "LeaguePosition requires Level, FromPos and ToPos as integers."
                        "Provide all three numeric fields and keep unrelated fields null."

                None
            | Some "CupWinner", Some None, Some None, Some None, Some(Some name), Some None -> Some(CupWinner name)
            | Some "CupWinner", Some level, Some fromPosition, Some toPosition, competitionName, slots ->
                requireNull "Level" level
                requireNull "FromPos" fromPosition
                requireNull "ToPos" toPosition
                requireNull "Slots" slots

                if competitionName.IsNone then
                    addDiagnostic
                        ctx
                        "MOD-CONTENT-FIELD"
                        (childPath path "CompetitionName")
                        "CupWinner requires CompetitionName as a string."
                        "Provide the referenced cup name."

                None
            | Some "TitleHolder", Some None, Some None, Some None, Some None, Some None -> Some TitleHolder
            | Some "TitleHolder", Some level, Some fromPosition, Some toPosition, competitionName, slots ->
                requireNull "Level" level
                requireNull "FromPos" fromPosition
                requireNull "ToPos" toPosition
                requireNull "CompetitionName" competitionName
                requireNull "Slots" slots
                None
            | Some "ConfederationSlot", Some None, Some None, Some None, Some None, Some(Some slots) ->
                Some(ConfederationSlot(atLeast ctx (childPath path "Slots") 1 slots))
            | Some "ConfederationSlot", Some level, Some fromPosition, Some toPosition, competitionName, slots ->
                requireNull "Level" level
                requireNull "FromPos" fromPosition
                requireNull "ToPos" toPosition
                requireNull "CompetitionName" competitionName

                if slots.IsNone then
                    addDiagnostic
                        ctx
                        "MOD-CONTENT-FIELD"
                        (childPath path "Slots")
                        "ConfederationSlot requires Slots as an integer."
                        "Provide the number of slots and keep unrelated fields null."

                None
            | Some unknown, _, _, _, _, _ ->
                addDiagnostic
                    ctx
                    "MOD-ENUM-UNKNOWN"
                    (childPath path "Kind")
                    ("Unknown qualification slot kind '" + unknown + "'.")
                    "Use LeaguePosition, CupWinner, TitleHolder or ConfederationSlot."

                None
            | _ -> None

    let private parseCompetition (ctx: Context) (properties: JsonProperty list) =
        rejectUnknownProperties ctx "" competitionEnvelopeKeys properties

        let nameKey = requiredNonEmptyString ctx "" properties "nameKey"
        let domainElement = requiredProperty ctx "" properties "domain"

        let domainProperties = domainElement |> Option.bind (objectProperties ctx "/domain")

        domainProperties
        |> Option.iter (rejectUnknownProperties ctx "/domain" competitionDomainKeys)

        let name =
            domainProperties
            |> Option.bind (fun domain -> requiredNonEmptyString ctx "/domain" domain "Name")

        let confederation =
            domainProperties
            |> Option.bind (fun domain -> requiredNullableConfederation ctx "/domain" domain "Confederation")

        let formatText =
            domainProperties
            |> Option.bind (fun domain -> requiredNonEmptyString ctx "/domain" domain "Format")

        let format = formatText |> Option.bind (parseCupFormat ctx "/domain/Format")

        let qualification =
            domainProperties
            |> Option.bind (fun domain -> requiredProperty ctx "/domain" domain "Qualification")
            |> Option.bind (fun element -> parsedArray ctx "/domain/Qualification" element (parseQualificationSlot ctx))

        match nameKey, name, confederation, format, qualification with
        | Some _, Some name, Some confederation, Some format, Some qualification ->
            Some
                { ContentId = ctx.ContentId
                  Name = name
                  CompetitionType = InternationalCup(confederation, format, qualification) }
        | _ -> None

    let private parseEntity (ctx: Context) (json: string) =
        if isNull json then
            addDiagnostic
                ctx
                "MOD-CONTENT-VALUE"
                ""
                "Resolved sports content cannot have a null value."
                "Provide canonical JSON text for the content ID."

            None
        else
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

                match objectProperties ctx "" document.RootElement with
                | None -> None
                | Some properties ->
                    let id = requiredNonEmptyString ctx "" properties "id"
                    let kind = requiredNonEmptyString ctx "" properties "kind"
                    let schema = requiredInt ctx "" properties "schema"

                    match id with
                    | Some id when id <> contentIdValue ctx.ContentId ->
                        addDiagnostic
                            ctx
                            "MOD-CONTENT-ID-MISMATCH"
                            "/id"
                            "The JSON id does not match the resolved ContentId key."
                            "Use the canonical ContentId as the JSON id."
                    | _ -> ()

                    match kind with
                    | Some kind when kind <> ctx.ContentId.Kind ->
                        addDiagnostic
                            ctx
                            "MOD-CONTENT-KIND-MISMATCH"
                            "/kind"
                            "The JSON kind does not match the kind encoded in the ContentId."
                            "Keep the qualified ContentId and kind equal."
                    | _ -> ()

                    schema
                    |> Option.iter (fun version ->
                        if version <> 1 then
                            addDiagnostic
                                ctx
                                "MOD-CONTENT-SCHEMA"
                                "/schema"
                                "Only declarative content schema 1 is supported."
                                "Publish the entity with schema 1; no migration is performed.")

                    match kind with
                    | Some "country" -> parseCountry ctx properties |> Option.map CountryEntity
                    | Some "competition" -> parseCompetition ctx properties |> Option.map CompetitionEntity
                    | Some unknown ->
                        addDiagnostic
                            ctx
                            "MOD-ENUM-UNKNOWN"
                            "/kind"
                            ("World generation does not materialize content kind '" + unknown + "'.")
                            "Publish a country or international competition entity for world generation."

                        None
                    | None -> None
            with ex ->
                addDiagnostic
                    ctx
                    "MOD-CONTENT-JSON"
                    ""
                    ("Content JSON could not be parsed: " + ex.Message)
                    "Fix the JSON syntax and publish canonical UTF-8 JSON."

                None

    let private validateCountry (ctx: Context) (country: CountryData) =
        if country.Names.FirstNames.IsEmpty then
            addDiagnostic
                ctx
                "MOD-CONTENT-FIELD"
                "/domain/Names/FirstNames"
                "FirstNames must contain at least one name."
                "Provide a non-empty first-name pool."

        if country.Names.LastNames.IsEmpty then
            addDiagnostic
                ctx
                "MOD-CONTENT-FIELD"
                "/domain/Names/LastNames"
                "LastNames must contain at least one name."
                "Provide a non-empty last-name pool."

        if country.LeagueNames.Length <> country.LeagueRules.Length then
            addDiagnostic
                ctx
                "MOD-CONTENT-REFERENCE"
                "/domain"
                "LeagueNames and LeagueRules must have the same number of levels."
                "Declare one name and one rule object for every league level."

        country.Clubs
        |> List.iteri (fun index club ->
            if club.LeagueLevel >= country.LeagueRules.Length then
                addDiagnostic
                    ctx
                    "MOD-CONTENT-REFERENCE"
                    ("/domain/Clubs/" + string index + "/LeagueLevel")
                    "A club refers to a league level without a corresponding league rule."
                    "Declare the referenced league level in LeagueRules and LeagueNames.")

    let private qualificationSlots competitionType =
        match competitionType with
        | InternationalCup(_, _, slots) -> slots
        | NationalCup(_, slots) -> slots
        | NationalLeague _ -> []

    let private validateCompetitionReferences
        (ctx: Context)
        (knownCupNames: Set<string>)
        (competitionType: CompetitionType)
        =
        qualificationSlots competitionType
        |> List.iter (function
            | CupWinner name when not (Set.contains name knownCupNames) ->
                addDiagnostic
                    ctx
                    "MOD-CONTENT-REFERENCE"
                    "/domain/Qualification"
                    ("CupWinner refers to unknown cup '" + name + "'.")
                    "Use the exact declared cup name as the qualification reference."
            | _ -> ())

    let materialize (resolved: ResolvedDataSet) : Result<ResolvedWorldData, ModError> =
        let diagnostics = ResizeArray<ModDiagnostic>()

        let content =
            resolved.SportsContent
            |> Map.toList
            |> List.sortWith (fun (left, _) (right, _) ->
                StringComparer.Ordinal.Compare(contentIdValue left, contentIdValue right))

        let entities = ResizeArray<Entity>()

        for contentId, json in content do
            let ctx =
                { ContentId = contentId
                  Diagnostics = diagnostics }

            match parseEntity ctx json with
            | Some entity -> entities.Add entity
            | None -> ()

        let countries =
            entities
            |> Seq.choose (function
                | CountryEntity country -> Some country
                | CompetitionEntity _ -> None)
            |> List.ofSeq

        let competitions =
            entities
            |> Seq.choose (function
                | CountryEntity _ -> None
                | CompetitionEntity competition -> Some competition)
            |> List.ofSeq

        countries
        |> List.groupBy (fun country -> country.Data.Country.Code)
        |> List.iter (fun (code, group) ->
            if group.Length > 1 then
                group
                |> List.iter (fun country ->
                    let ctx =
                        { ContentId = country.ContentId
                          Diagnostics = diagnostics }

                    addDiagnostic
                        ctx
                        "MOD-CONTENT-DUPLICATE"
                        "/domain/Country/Code"
                        ("Country code '" + code + "' is declared by more than one ContentId.")
                        "Keep one country identity per code and patch the existing ContentId instead."))

        competitions
        |> List.groupBy _.Name
        |> List.iter (fun (name, group) ->
            if group.Length > 1 then
                group
                |> List.iter (fun competition ->
                    let ctx =
                        { ContentId = competition.ContentId
                          Diagnostics = diagnostics }

                    addDiagnostic
                        ctx
                        "MOD-CONTENT-DUPLICATE"
                        "/domain/Name"
                        ("Competition name '" + name + "' is declared by more than one ContentId.")
                        "Keep one international competition identity per name."))

        countries
        |> List.iter (fun country ->
            let ctx =
                { ContentId = country.ContentId
                  Diagnostics = diagnostics }

            validateCountry ctx country.Data)

        let knownCupNames =
            countries |> List.collect (fun country -> country.Data.CupNames) |> Set.ofList

        competitions
        |> List.iter (fun competition ->
            let ctx =
                { ContentId = competition.ContentId
                  Diagnostics = diagnostics }

            validateCompetitionReferences ctx knownCupNames competition.CompetitionType)

        if diagnostics.Count > 0 then
            Error { Diagnostics = List.ofSeq diagnostics }
        else
            Ok
                { Countries = countries |> List.map _.Data
                  InternationalCompetitions =
                    competitions
                    |> List.map (fun competition -> competition.Name, competition.CompetitionType)
                  CountryEntities = countries
                  CompetitionEntities = competitions }
