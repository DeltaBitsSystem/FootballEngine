namespace FootballEngine.Data

open FootballEngine.Domain

/// Result-returning parsers and encoders for JSON serialization.
/// Extracted from Database/Serializers.fs patterns but with proper error handling.
module DomainEncoders =

    // ─── Helper: Result utilities ───
    let sequenceList<'T, 'E> (results: Result<'T, 'E> list) : Result<'T list, 'E> =
        let rec loop acc =
            function
            | [] -> Ok(List.rev acc)
            | Ok x :: xs -> loop (x :: acc) xs
            | Error e :: _ -> Error e

        loop [] results

    let map2 f r1 r2 =
        match r1, r2 with
        | Ok a, Ok b -> Ok(f a b)
        | Error e, _
        | _, Error e -> Error e

    let map3 f r1 r2 r3 =
        match r1, r2, r3 with
        | Ok a, Ok b, Ok c -> Ok(f a b c)
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e

    // ─── Confederation ───

    let confederationToString =
        function
        | UEFA -> "UEFA"
        | CONMEBOL -> "CONMEBOL"
        | CONCACAF -> "CONCACAF"
        | CAF -> "CAF"
        | AFC -> "AFC"
        | OFC -> "OFC"

    let parseConfederation =
        function
        | "CONMEBOL" -> Ok CONMEBOL
        | "CONCACAF" -> Ok CONCACAF
        | "CAF" -> Ok CAF
        | "AFC" -> Ok AFC
        | "OFC" -> Ok OFC
        | "UEFA" -> Ok UEFA
        | s -> Error $"Unknown confederation: '{s}'"

    // ─── TiebreakerRule ───

    let tiebreakerToString =
        function
        | GoalDifference -> "GD"
        | GoalsScored -> "GS"
        | HeadToHead -> "H2H"
        | HeadToHeadGoalDifference -> "H2HGD"

    let parseTiebreaker =
        function
        | "GD" -> Ok GoalDifference
        | "GS" -> Ok GoalsScored
        | "H2H" -> Ok HeadToHead
        | "H2HGD" -> Ok HeadToHeadGoalDifference
        | s -> Error $"Unknown tiebreaker: '{s}'"

    // ─── TieResolution ───

    let tieResolutionToString =
        function
        | ReplayAtNeutral -> "ReplayAtNeutral"
        | ExtraTimeThenPenalties -> "ExtraTimeThenPenalties"
        | AwayGoals -> "AwayGoals"
        | PenaltiesOnly -> "PenaltiesOnly"

    let parseTieResolution =
        function
        | "ReplayAtNeutral" -> Ok ReplayAtNeutral
        | "ExtraTimeThenPenalties" -> Ok ExtraTimeThenPenalties
        | "AwayGoals" -> Ok AwayGoals
        | "PenaltiesOnly" -> Ok PenaltiesOnly
        | s -> Error $"Unknown tie resolution: '{s}'"

    // ─── LegFormat ───

    let legFormatToString =
        function
        | SingleLeg resolution -> $"SingleLeg:{tieResolutionToString resolution}"
        | TwoLegs resolution -> $"TwoLegs:{tieResolutionToString resolution}"

    let parseLegFormat (s: string) : Result<LegFormat, string> =
        if s.StartsWith "SingleLeg:" then
            parseTieResolution (s.Substring 10) |> Result.map SingleLeg
        elif s.StartsWith "TwoLegs:" then
            parseTieResolution (s.Substring 8) |> Result.map TwoLegs
        else
            Error $"Unknown leg format: '{s}'"

    // ─── PromotionRule ───

    let promotionToString =
        function
        | AutomaticPromotion n -> $"AP:{n}"
        | PlayoffPromotion n -> $"PP:{n}"

    let parsePromotion (s: string) : Result<PromotionRule, string> =
        let parts = s.Split(':')

        if parts.Length <> 2 then
            Error $"Invalid promotion rule: '{s}'"
        else
            match parts[0], System.Int32.TryParse(parts[1]) with
            | "AP", (true, n) -> Ok(AutomaticPromotion n)
            | "PP", (true, n) -> Ok(PlayoffPromotion n)
            | _ -> Error $"Invalid promotion rule: '{s}'"

    // ─── RelegationRule ───

    let relegationToString =
        function
        | AutomaticRelegation n -> $"AR:{n}"
        | PlayoffRelegation n -> $"PR:{n}"

    let parseRelegation (s: string) : Result<RelegationRule, string> =
        let parts = s.Split(':')

        if parts.Length <> 2 then
            Error $"Invalid relegation rule: '{s}'"
        else
            match parts[0], System.Int32.TryParse(parts[1]) with
            | "AR", (true, n) -> Ok(AutomaticRelegation n)
            | "PR", (true, n) -> Ok(PlayoffRelegation n)
            | _ -> Error $"Invalid relegation rule: '{s}'"

    // ─── LeagueRules ───

    let leagueRulesToString (rules: LeagueRules) : string =
        let tiebreakers =
            rules.Tiebreakers |> List.map tiebreakerToString |> String.concat ","

        let promotions = rules.Promotion |> List.map promotionToString |> String.concat ","

        let relegations =
            rules.Relegation |> List.map relegationToString |> String.concat ","

        $"{rules.PointsForWin}|{rules.PointsForDraw}|{tiebreakers}|{promotions}|{relegations}"

    let parseLeagueRules (s: string) : Result<LeagueRules, string> =
        let parts = s.Split('|')

        if parts.Length <> 5 then
            Error $"Invalid league rules: '{s}'"
        else
            match System.Int32.TryParse(parts[0]), System.Int32.TryParse(parts[1]) with
            | (true, pw), (true, pd) ->
                let tbs =
                    if parts[2] = "" then
                        Ok []
                    else
                        parts[2].Split(',') |> Array.toList |> List.map parseTiebreaker |> sequenceList

                let promos =
                    if parts[3] = "" then
                        Ok []
                    else
                        parts[3].Split(',') |> Array.toList |> List.map parsePromotion |> sequenceList

                let relegs =
                    if parts[4] = "" then
                        Ok []
                    else
                        parts[4].Split(',') |> Array.toList |> List.map parseRelegation |> sequenceList

                map3
                    (fun tbs promos relegs ->
                        { PointsForWin = pw
                          PointsForDraw = pd
                          Tiebreakers = tbs
                          Promotion = promos
                          Relegation = relegs })
                    tbs
                    promos
                    relegs
            | _ -> Error $"Invalid league rules (points): '{s}'"

    // ─── GroupRules ───

    let groupRulesToString (rules: GroupRules) : string =
        let tiebreakers =
            rules.Tiebreakers |> List.map tiebreakerToString |> String.concat ","

        $"{rules.GroupCount};{rules.TeamsPerGroup};{rules.QualifyPerGroup};{rules.PointsForWin};{rules.PointsForDraw};{tiebreakers}"

    let parseGroupRules (s: string) : Result<GroupRules, string> =
        let parts = s.Split(';')

        if parts.Length <> 6 then
            Error $"Invalid group rules: '{s}'"
        else
            match
                System.Int32.TryParse(parts[0]),
                System.Int32.TryParse(parts[1]),
                System.Int32.TryParse(parts[2]),
                System.Int32.TryParse(parts[3]),
                System.Int32.TryParse(parts[4])
            with
            | (true, gc), (true, tpg), (true, qpg), (true, pw), (true, pd) ->
                let tbs =
                    if parts[5] = "" then
                        Ok []
                    else
                        parts[5].Split(',') |> Array.toList |> List.map parseTiebreaker |> sequenceList

                Result.map
                    (fun tbs ->
                        { GroupCount = gc
                          TeamsPerGroup = tpg
                          QualifyPerGroup = qpg
                          PointsForWin = pw
                          PointsForDraw = pd
                          Tiebreakers = tbs })
                    tbs
            | _ -> Error $"Invalid group rules (numbers): '{s}'"

    // ─── CupFormat ───

    let cupFormatToString =
        function
        | StraightKnockout legFormat -> $"SK:{legFormatToString legFormat}"
        | GroupThenKnockout(groupRules, legFormat) ->
            $"GTK:{groupRulesToString groupRules}~{legFormatToString legFormat}"

    let parseCupFormat (s: string) : Result<CupFormat, string> =
        if s.StartsWith "GTK:" then
            let inner = s.Substring 4
            let idx = inner.IndexOf '~'

            if idx < 0 then
                Error $"Invalid GTK format (no '~'): '{s}'"
            else
                match parseGroupRules inner[.. idx - 1], parseLegFormat inner[idx + 1 ..] with
                | Ok gr, Ok lf -> Ok(GroupThenKnockout(gr, lf))
                | Error e, _
                | _, Error e -> Error e
        elif s.StartsWith "SK:" then
            parseLegFormat (s.Substring 3) |> Result.map StraightKnockout
        else
            Error $"Unknown cup format: '{s}'"

    // ─── QualificationSlot ───

    let qualificationSlotToString =
        function
        | LeaguePosition(LeagueLevel lvl, fromPos, toPos) -> $"LP:{lvl}:{fromPos}:{toPos}"
        | CupWinner name -> $"CW:{name}"
        | TitleHolder -> "TH"
        | ConfederationSlot n -> $"CS:{n}"

    let parseQualificationSlot (s: string) : Result<QualificationSlot, string> =
        if s.StartsWith "LP:" then
            let p = s.Substring(3).Split(':')

            if p.Length <> 3 then
                Error $"Invalid LeaguePosition slot: '{s}'"
            else
                match System.Int32.TryParse(p[0]), System.Int32.TryParse(p[1]), System.Int32.TryParse(p[2]) with
                | (true, lvl), (true, fp), (true, tp) -> Ok(LeaguePosition(LeagueLevel lvl, fp, tp))
                | _ -> Error $"Invalid LeaguePosition numbers: '{s}'"
        elif s.StartsWith "CW:" then
            Ok(CupWinner(s.Substring 3))
        elif s = "TH" then
            Ok TitleHolder
        elif s.StartsWith "CS:" then
            match System.Int32.TryParse(s.Substring 3) with
            | (true, n) -> Ok(ConfederationSlot n)
            | _ -> Error $"Invalid ConfederationSlot number: '{s}'"
        else
            Error $"Unknown qualification slot: '{s}'"

    // ─── CompetitionType ───

    let competitionTypeToString (ct: CompetitionType) : string * string * string =
        match ct with
        | NationalLeague(LeagueLevel lvl, rules) -> "NationalLeague", string lvl, leagueRulesToString rules
        | NationalCup(format, slots) ->
            let slotStr = slots |> List.map qualificationSlotToString |> String.concat ","
            "NationalCup", cupFormatToString format, slotStr
        | InternationalCup(confOpt, format, slots) ->
            let confStr = confOpt |> Option.map confederationToString |> Option.defaultValue ""
            let slotStr = slots |> List.map qualificationSlotToString |> String.concat ","
            "InternationalCup", $"{cupFormatToString format}~{confStr}", slotStr

    let parseCompetitionType (tag: string) (param1: string) (param2: string) : Result<CompetitionType, string> =
        match tag with
        | "NationalLeague" ->
            match System.Int32.TryParse(param1) with
            | false, _ -> Error $"Invalid league level: '{param1}'"
            | true, lvl ->
                match parseLeagueRules param2 with
                | Ok rules -> Ok(NationalLeague(LeagueLevel lvl, rules))
                | Error e -> Error e
        | "NationalCup" ->
            match parseCupFormat param1 with
            | Error e -> Error e
            | Ok fmt ->
                let slots =
                    if param2 = "" then
                        Ok []
                    else
                        param2.Split(',')
                        |> Array.toList
                        |> List.map parseQualificationSlot
                        |> sequenceList

                Result.map (fun slots -> NationalCup(fmt, slots)) slots
        | "InternationalCup" ->
            let idx = param1.LastIndexOf '~'

            if idx < 0 then
                Error $"Invalid InternationalCup param1 (no '~'): '{param1}'"
            else
                let confStr = param1[idx + 1 ..]

                let conf =
                    if confStr = "" then
                        Ok None
                    else
                        parseConfederation confStr |> Result.map Some

                match parseCupFormat param1[.. idx - 1], conf with
                | Error e, _
                | _, Error e -> Error e
                | Ok fmt, Ok confOpt ->
                    let slots =
                        if param2 = "" then
                            Ok []
                        else
                            param2.Split(',')
                            |> Array.toList
                            |> List.map parseQualificationSlot
                            |> sequenceList

                    Result.map (fun slots -> InternationalCup(confOpt, fmt, slots)) slots
        | _ -> Error $"Unknown competition type: '{tag}'"
