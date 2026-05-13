namespace FootballEngine.Database

open FootballEngine.Data
open FootballEngine.Domain

module Serializers =

    let parsePosition (s: string) =
        match s.Trim().ToUpper() with
        | "GK" -> GK
        | "DL" -> DL
        | "DC" -> DC
        | "DR" -> DR
        | "WBL" -> WBL
        | "WBR" -> WBR
        | "DM" -> DM
        | "ML" -> ML
        | "MC" -> MC
        | "MR" -> MR
        | "AML" -> AML
        | "AMC" -> AMC
        | "AMR" -> AMR
        | "ST" -> ST
        | _ -> MC

    let parseFoot (s: string) =
        match s.Trim().ToUpper() with
        | "LEFT" -> Left
        | "RIGHT" -> Right
        | "BOTH" -> Both
        | _ -> Right

    let formationToString =
        function
        | F442 -> "4-4-2"
        | F442Diamond -> "4-4-2 Diamond"
        | F433 -> "4-3-3"
        | F433Flat -> "4-3-3 Flat"
        | F451 -> "4-5-1"
        | F4141 -> "4-1-4-1"
        | F4231 -> "4-2-3-1"
        | F4312 -> "4-3-1-2"
        | F4321 -> "4-3-2-1"
        | F352 -> "3-5-2"
        | F343 -> "3-4-3"
        | F3421 -> "3-4-2-1"
        | F532 -> "5-3-2"
        | F541 -> "5-4-1"
        | F523 -> "5-2-3"

    let parseFormation =
        function
        | "4-4-2" -> F442
        | "4-4-2 Diamond" -> F442Diamond
        | "4-3-3" -> F433
        | "4-3-3 Flat" -> F433Flat
        | "4-5-1" -> F451
        | "4-1-4-1" -> F4141
        | "4-2-3-1" -> F4231
        | "4-3-1-2" -> F4312
        | "4-3-2-1" -> F4321
        | "3-5-2" -> F352
        | "3-4-3" -> F343
        | "3-4-2-1" -> F3421
        | "5-3-2" -> F532
        | "5-4-1" -> F541
        | "5-2-3" -> F523
        | _ -> F442

    let parseTactics =
        function
        | "Balanced" -> Balanced
        | "Attacking" -> Attacking
        | "Defensive" -> Defensive
        | "Pressing" -> Pressing
        | "Counter" -> Counter
        | _ -> Balanced

    let tacticsToString =
        function
        | Balanced -> "Balanced"
        | Attacking -> "Attacking"
        | Defensive -> "Defensive"
        | Pressing -> "Pressing"
        | Counter -> "Counter"

    let roundToString =
        function
        | GroupStage groupIndex -> $"GroupStage:{groupIndex}"
        | KnockoutRound teamsRemaining -> $"KnockoutRound:{teamsRemaining}"
        | ThirdPlace -> "ThirdPlace"
        | Final -> "Final"

    let parseRound (s: string) : Round =
        if s.StartsWith("GroupStage:") then
            GroupStage(int (s.Substring(11)))
        elif s.StartsWith("KnockoutRound:") then
            KnockoutRound(int (s.Substring(14)))
        else
            match s with
            | "ThirdPlace" -> ThirdPlace
            | _ -> Final

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
        | "CONMEBOL" -> CONMEBOL
        | "CONCACAF" -> CONCACAF
        | "CAF" -> CAF
        | "AFC" -> AFC
        | "OFC" -> OFC
        | _ -> UEFA

    let staffRoleToString =
        function
        | HeadCoach -> "HeadCoach"
        | AssistantManager -> "AssistantManager"
        | FirstTeamCoach -> "FirstTeamCoach"
        | GoalkeeperCoach -> "GoalkeeperCoach"
        | FitnessCoach -> "FitnessCoach"
        | HeadOfYouthDevelopment -> "HeadOfYouthDevelopment"
        | Scout -> "Scout"
        | Physio -> "Physio"
        | SportsScientist -> "SportsScientist"
        | PerformanceAnalyst -> "PerformanceAnalyst"
        | RecruitmentAnalyst -> "RecruitmentAnalyst"
        | LoanManager -> "LoanManager"
        | TechnicalDirector -> "TechnicalDirector"

    let parseStaffRole (s: string) : StaffRole =
        match s with
        | "HeadCoach" -> HeadCoach
        | "AssistantManager" -> AssistantManager
        | "FirstTeamCoach" -> FirstTeamCoach
        | "GoalkeeperCoach" -> GoalkeeperCoach
        | "FitnessCoach" -> FitnessCoach
        | "HeadOfYouthDevelopment" -> HeadOfYouthDevelopment
        | "Scout" -> Scout
        | "Physio" -> Physio
        | "SportsScientist" -> SportsScientist
        | "PerformanceAnalyst" -> PerformanceAnalyst
        | "RecruitmentAnalyst" -> RecruitmentAnalyst
        | _ -> LoanManager

    let coachingBadgeToString =
        function
        | NoneBadge -> "None"
        | NationalC -> "NationalC"
        | NationalB -> "NationalB"
        | NationalA -> "NationalA"
        | ProLicense -> "ProLicense"
        | ContinentalPro -> "ContinentalPro"

    let parseCoachingBadge (s: string) : CoachingBadge =
        match s with
        | "NationalC" -> NationalC
        | "NationalB" -> NationalB
        | "NationalA" -> NationalA
        | "ProLicense" -> ProLicense
        | "ContinentalPro" -> ContinentalPro
        | _ -> NoneBadge

    let staffStatusToString =
        function
        | Active -> "Active"
        | UnderPressure -> "UnderPressure"
        | Sacked -> "Sacked"
        | Resigned -> "Resigned"
        | ContractExpired -> "ContractExpired"
        | Unemployed -> "Unemployed"
        | StaffRetired -> "StaffRetired"

    let parseStaffStatus (s: string) : StaffStatus =
        match s with
        | "Active" -> Active
        | "UnderPressure" -> UnderPressure
        | "Sacked" -> Sacked
        | "Resigned" -> Resigned
        | "ContractExpired" -> ContractExpired
        | _ -> Unemployed

    let boardObjectiveToString =
        function
        | LeagueObjective Survival -> "League:Survival"
        | LeagueObjective MidTable -> "League:MidTable"
        | LeagueObjective TopHalf -> "League:TopHalf"
        | LeagueObjective TopFour -> "League:TopFour"
        | LeagueObjective WinLeague -> "League:WinLeague"
        | CupObjective WinDomesticCup -> "Cup:Domestic"
        | CupObjective WinContinentalCup -> "Cup:Continental"
        | CupObjective WinChampionsLeague -> "Cup:Champions"
        | Promotion -> "Promotion"
        | Relegation -> "Relegation"

    let parseBoardObjective (s: string) : BoardObjective =
        match s with
        | "League:Survival" -> LeagueObjective Survival
        | "League:MidTable" -> LeagueObjective MidTable
        | "League:TopHalf" -> LeagueObjective TopHalf
        | "League:TopFour" -> LeagueObjective TopFour
        | "League:WinLeague" -> LeagueObjective WinLeague
        | "Cup:Domestic" -> CupObjective WinDomesticCup
        | "Cup:Continental" -> CupObjective WinContinentalCup
        | "Cup:Champions" -> CupObjective WinChampionsLeague
        | "Promotion" -> Promotion
        | _ -> Relegation

    let private tieResolutionToString =
        function
        | ReplayAtNeutral -> "ReplayAtNeutral"
        | ExtraTimeThenPenalties -> "ExtraTimeThenPenalties"
        | AwayGoals -> "AwayGoals"
        | PenaltiesOnly -> "PenaltiesOnly"

    let private parseTieResolution =
        function
        | "ReplayAtNeutral" -> ReplayAtNeutral
        | "ExtraTimeThenPenalties" -> ExtraTimeThenPenalties
        | "AwayGoals" -> AwayGoals
        | _ -> PenaltiesOnly

    let private legFormatToString =
        function
        | SingleLeg resolution -> $"SingleLeg:{tieResolutionToString resolution}"
        | TwoLegs resolution -> $"TwoLegs:{tieResolutionToString resolution}"

    let private parseLegFormat (s: string) : LegFormat =
        if s.StartsWith("SingleLeg:") then
            SingleLeg(parseTieResolution (s.Substring(10)))
        else
            TwoLegs(parseTieResolution (s.Substring(8)))

    let private tiebreakerToString =
        function
        | GoalDifference -> "GD"
        | GoalsScored -> "GS"
        | HeadToHead -> "H2H"
        | HeadToHeadGoalDifference -> "H2HGD"

    let private parseTiebreaker =
        function
        | "GD" -> GoalDifference
        | "GS" -> GoalsScored
        | "H2H" -> HeadToHead
        | _ -> HeadToHeadGoalDifference

    let private parseList (sep: char) f (s: string) =
        if s = "" then
            []
        else
            s.Split(sep) |> Array.map f |> List.ofArray

    let private leagueRulesToString (rules: LeagueRules) =
        let tiebreakers =
            rules.Tiebreakers |> List.map tiebreakerToString |> String.concat ","

        let promotions =
            rules.Promotion
            |> List.map (function
                | AutomaticPromotion n -> $"AP:{n}"
                | PlayoffPromotion n -> $"PP:{n}")
            |> String.concat ","

        let relegations =
            rules.Relegation
            |> List.map (function
                | AutomaticRelegation n -> $"AR:{n}"
                | PlayoffRelegation n -> $"PR:{n}")
            |> String.concat ","

        $"{rules.PointsForWin}|{rules.PointsForDraw}|{tiebreakers}|{promotions}|{relegations}"

    let private parseLeagueRules (s: string) : LeagueRules =
        let parts = s.Split('|')

        let parsePromotion (x: string) =
            let p = x.Split(':')

            if p[0] = "AP" then
                AutomaticPromotion(int p[1])
            else
                PlayoffPromotion(int p[1])

        let parseRelegation (x: string) =
            let p = x.Split(':')

            if p[0] = "AR" then
                AutomaticRelegation(int p[1])
            else
                PlayoffRelegation(int p[1])

        { PointsForWin = int parts[0]
          PointsForDraw = int parts[1]
          Tiebreakers = parseList ',' parseTiebreaker parts[2]
          Promotion = parseList ',' parsePromotion parts[3]
          Relegation = parseList ',' parseRelegation parts[4] }

    let private groupRulesToString (rules: GroupRules) =
        let tiebreakers =
            rules.Tiebreakers |> List.map tiebreakerToString |> String.concat ","

        $"{rules.GroupCount};{rules.TeamsPerGroup};{rules.QualifyPerGroup};{rules.PointsForWin};{rules.PointsForDraw};{tiebreakers}"

    let private parseGroupRules (s: string) : GroupRules =
        let parts = s.Split(';')

        { GroupCount = int parts[0]
          TeamsPerGroup = int parts[1]
          QualifyPerGroup = int parts[2]
          PointsForWin = int parts[3]
          PointsForDraw = int parts[4]
          Tiebreakers = parseList ',' parseTiebreaker parts[5] }

    let private cupFormatToString =
        function
        | StraightKnockout legFormat -> $"SK:{legFormatToString legFormat}"
        | GroupThenKnockout(groupRules, legFormat) ->
            $"GTK:{groupRulesToString groupRules}~{legFormatToString legFormat}"

    let private parseCupFormat (s: string) : CupFormat =
        if s.StartsWith("GTK:") then
            let inner = s.Substring(4)
            let idx = inner.IndexOf('~')
            GroupThenKnockout(parseGroupRules inner[.. idx - 1], parseLegFormat inner[idx + 1 ..])
        else
            StraightKnockout(parseLegFormat (s.Substring(3)))

    let private qualificationSlotToString =
        function
        | LeaguePosition(LeagueLevel lvl, fromPos, toPos) -> $"LP:{lvl}:{fromPos}:{toPos}"
        | CupWinner name -> $"CW:{name}"
        | TitleHolder -> "TH"
        | ConfederationSlot n -> $"CS:{n}"

    let private parseQualificationSlot (s: string) : QualificationSlot =
        if s.StartsWith("LP:") then
            let p = s.Substring(3).Split(':')
            LeaguePosition(LeagueLevel(int p[0]), int p[1], int p[2])
        elif s.StartsWith("CW:") then
            CupWinner(s.Substring(3))
        elif s = "TH" then
            TitleHolder
        else
            ConfederationSlot(int (s.Substring(3)))

    let competitionTypeToStrings (competitionType: CompetitionType) : string * string * string =
        match competitionType with
        | NationalLeague(LeagueLevel lvl, rules) -> "NationalLeague", $"{lvl}", leagueRulesToString rules
        | NationalCup(format, slots) ->
            let slotStr = slots |> List.map qualificationSlotToString |> String.concat ","
            "NationalCup", cupFormatToString format, slotStr
        | InternationalCup(confOpt, format, slots) ->
            let confStr = confOpt |> Option.map confederationToString |> Option.defaultValue ""
            let slotStr = slots |> List.map qualificationSlotToString |> String.concat ","
            "InternationalCup", $"{cupFormatToString format}~{confStr}", slotStr

    let parseCompetitionType (tag: string) (param1: string) (param2: string) : CompetitionType =
        let defaultLeague =
            NationalLeague(
                LeagueLevel 0,
                { PointsForWin = 3
                  PointsForDraw = 1
                  Tiebreakers = [ GoalDifference ]
                  Promotion = []
                  Relegation = [] }
            )

        match tag with
        | "NationalLeague" -> NationalLeague(LeagueLevel(int param1), parseLeagueRules param2)
        | "NationalCup" -> NationalCup(parseCupFormat param1, parseList ',' parseQualificationSlot param2)
        | "InternationalCup" ->
            let idx = param1.LastIndexOf('~')

            let conf =
                let s = param1[idx + 1 ..] in if s = "" then None else Some(parseConfederation s)

            InternationalCup(conf, parseCupFormat param1[.. idx - 1], parseList ',' parseQualificationSlot param2)
        | _ -> defaultLeague

    let trainingFocusToString =
        function
        | TrainingFocus.TrainingPhysical -> "Physical"
        | TrainingFocus.TrainingTechnical -> "Technical"
        | TrainingFocus.TrainingMental -> "Mental"
        | TrainingFocus.TrainingGoalkeeping -> "Goalkeeping"
        | TrainingFocus.TrainingAllRound -> "AllRound"

    let parseTrainingFocus (s: string) : TrainingFocus =
        match s with
        | "Physical" -> TrainingFocus.TrainingPhysical
        | "Technical" -> TrainingFocus.TrainingTechnical
        | "Mental" -> TrainingFocus.TrainingMental
        | "Goalkeeping" -> TrainingFocus.TrainingGoalkeeping
        | _ -> TrainingFocus.TrainingAllRound

    let trainingIntensityToString =
        function
        | TrainingIntensity.TrainingLight -> "Light"
        | TrainingIntensity.TrainingNormal -> "Normal"
        | TrainingIntensity.TrainingHeavy -> "Heavy"

    let parseTrainingIntensity (s: string) : TrainingIntensity =
        match s with
        | "Light" -> TrainingIntensity.TrainingLight
        | "Normal" -> TrainingIntensity.TrainingNormal
        | _ -> TrainingIntensity.TrainingHeavy

    let inboxCategoryToString =
        function
        | InboxMessageCategory.Development -> "Development"
        | InboxMessageCategory.Transfer -> "Transfer"
        | InboxMessageCategory.BoardUpdate -> "BoardUpdate"
        | InboxMessageCategory.MatchReport -> "MatchReport"
        | InboxMessageCategory.InjuryMessage -> "Injury"
        | InboxMessageCategory.Contract -> "Contract"

    let parseInboxCategory (s: string) : InboxMessageCategory =
        match s with
        | "Development" -> InboxMessageCategory.Development
        | "Transfer" -> InboxMessageCategory.Transfer
        | "BoardUpdate" -> InboxMessageCategory.BoardUpdate
        | "MatchReport" -> InboxMessageCategory.MatchReport
        | "Injury" -> InboxMessageCategory.InjuryMessage
        | "Contract" -> InboxMessageCategory.Contract
        | _ -> InboxMessageCategory.Development

    let behavioralProfileToString (p: BehavioralProfile) =
        $"{p.PositionalFreedom:F3}|{p.AttackingDepth:F3}|{p.LateralTendency:F3}|{p.DefensiveHeight:F3}|{p.PressingIntensity:F3}|{p.RiskAppetite:F3}|{p.Directness:F3}|{p.CreativityWeight:F3}|{p.AerialThreat:F3}|{p.HoldUpPlay:F3}"

    let parseBehavioralProfile (s: string) : BehavioralProfile =
        let parts = s.Split('|')

        { PositionalFreedom = float parts[0]
          AttackingDepth = float parts[1]
          LateralTendency = float parts[2]
          DefensiveHeight = float parts[3]
          PressingIntensity = float parts[4]
          RiskAppetite = float parts[5]
          Directness = float parts[6]
          CreativityWeight = float parts[7]
          AerialThreat = float parts[8]
          HoldUpPlay = float parts[9] }

    let serializeCountryData (cd: CountryData) : string =
        let dto = JsonConverters.countryDataToDto cd
        System.Text.Json.JsonSerializer.Serialize(dto)

    let deserializeCountryData (json: string) : CountryData =
        let dto = System.Text.Json.JsonSerializer.Deserialize<CountryDataDto>(json)

        match JsonConverters.countryDataFromDto dto with
        | Ok cd -> cd
        | Error msg -> failwithf $"deserializeCountryData: %s{msg}"
