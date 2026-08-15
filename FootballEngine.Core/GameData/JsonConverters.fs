namespace FootballEngine.Data

open FootballEngine.Domain

/// Bidirectional converters between DTO types and domain types.
module JsonConverters =

    // ─── Country ───

    let countryToDto (c: Country) : CountryDto =
        { Code = c.Code
          Name = c.Name
          Confederation = DomainEncoders.confederationToString c.Confederation }

    let countryFromDto (d: CountryDto) : Result<Country, string> =
        match DomainEncoders.parseConfederation d.Confederation with
        | Error e -> Error e
        | Ok conf ->
            Ok
                { Code = d.Code
                  Name = d.Name
                  Confederation = conf }

    // ─── NamePool ───

    let namePoolToDto (np: NamePool) : NamePoolDto =
        { FirstNames = np.FirstNames
          LastNames = np.LastNames }

    let namePoolFromDto (d: NamePoolDto) : NamePool =
        { FirstNames = d.FirstNames
          LastNames = d.LastNames }

    // ─── ClubEntry ───

    let clubEntryToDto (ce: ClubEntry) : ClubEntryDto =
        { Name = ce.Name
          LeagueLevel = ce.LeagueLevel
          Reputation = ce.Reputation }

    let clubEntryFromDto (d: ClubEntryDto) : ClubEntry =
        { Name = d.Name
          LeagueLevel = d.LeagueLevel
          Reputation = d.Reputation }

    // ─── CupEntry ───

    let cupEntryToDto (name: string) (cf: CupFormat) : CupEntryDto =
        { Name = name
          Format = DomainEncoders.cupFormatToString cf }

    let cupFormatFromDto (d: CupEntryDto) : Result<CupFormat, string> = DomainEncoders.parseCupFormat d.Format

    // ─── LeagueRules ───

    let leagueRulesToDto (lr: LeagueRules) : LeagueRulesDto =
        { PointsForWin = lr.PointsForWin
          PointsForDraw = lr.PointsForDraw
          Tiebreakers = lr.Tiebreakers |> List.map DomainEncoders.tiebreakerToString
          Promotion = lr.Promotion |> List.map DomainEncoders.promotionToString
          Relegation = lr.Relegation |> List.map DomainEncoders.relegationToString }

    let leagueRulesFromDto (d: LeagueRulesDto) : Result<LeagueRules, string> =
        let tbs =
            d.Tiebreakers
            |> List.map DomainEncoders.parseTiebreaker
            |> DomainEncoders.sequenceList

        let promos =
            d.Promotion
            |> List.map DomainEncoders.parsePromotion
            |> DomainEncoders.sequenceList

        let relegs =
            d.Relegation
            |> List.map DomainEncoders.parseRelegation
            |> DomainEncoders.sequenceList

        DomainEncoders.map3
            (fun tbs promos relegs ->
                { PointsForWin = d.PointsForWin
                  PointsForDraw = d.PointsForDraw
                  Tiebreakers = tbs
                  Promotion = promos
                  Relegation = relegs })
            tbs
            promos
            relegs

    // ─── CountryData ───

    /// Derive cup name from country code + index (same logic as WorldGen hardcoded fallback).
    let private defaultCupName (countryCode: string) (idx: int) : string =
        match countryCode, idx with
        | "ARG", 0 -> "Copa Argentina"
        | "ENG", 0 -> "FA Cup"
        | "ESP", 0 -> "Copa del Rey"
        | "BRA", 0 -> "Copa do Brasil"
        | code, 0 -> $"Cup {code}"
        | code, i -> $"Cup {code} {i}"

    let countryDataToDto (cd: CountryData) : CountryDataDto =
        let cups =
            cd.Cups
            |> List.mapi (fun i cf ->
                let name =
                    match cd.CupNames |> List.tryItem i with
                    | Some n -> n
                    | None -> defaultCupName cd.Country.Code i

                cupEntryToDto name cf)

        { Country = countryToDto cd.Country
          Names = namePoolToDto cd.Names
          LeagueNames = cd.LeagueNames
          LeagueRules = cd.LeagueRules |> List.map leagueRulesToDto
          Clubs = cd.Clubs |> List.map clubEntryToDto
          Cups = cups }

    let countryDataFromDto (d: CountryDataDto) : Result<CountryData, string> =
        let country = countryFromDto d.Country

        let leagueRules =
            d.LeagueRules |> List.map leagueRulesFromDto |> DomainEncoders.sequenceList

        let cups = d.Cups |> List.map cupFormatFromDto |> DomainEncoders.sequenceList
        let cupNames = d.Cups |> List.map _.Name

        DomainEncoders.map3
            (fun country leagueRules cups ->
                { Country = country
                  Names = namePoolFromDto d.Names
                  LeagueNames = d.LeagueNames
                  LeagueRules = leagueRules
                  Clubs = d.Clubs |> List.map clubEntryFromDto
                  Cups = cups
                  CupNames = cupNames })
            country
            leagueRules
            cups

    // ─── QualificationSlot ───

    let qualSlotToDto (qs: QualificationSlot) : QualificationSlotDto =
        match qs with
        | LeaguePosition(LeagueLevel lvl, fromPos, toPos) ->
            { Kind = "LeaguePosition"
              Level = Some lvl
              FromPos = Some fromPos
              ToPos = Some toPos
              CompetitionName = None
              Slots = None }
        | CupWinner name ->
            { Kind = "CupWinner"
              Level = None
              FromPos = None
              ToPos = None
              CompetitionName = Some name
              Slots = None }
        | TitleHolder ->
            { Kind = "TitleHolder"
              Level = None
              FromPos = None
              ToPos = None
              CompetitionName = None
              Slots = None }
        | ConfederationSlot n ->
            { Kind = "ConfederationSlot"
              Level = None
              FromPos = None
              ToPos = None
              CompetitionName = None
              Slots = Some n }

    let qualSlotFromDto (d: QualificationSlotDto) : Result<QualificationSlot, string> =
        match d.Kind with
        | "LeaguePosition" ->
            match d.Level, d.FromPos, d.ToPos with
            | Some lvl, Some fp, Some tp -> Ok(LeaguePosition(LeagueLevel lvl, fp, tp))
            | _ -> Error "LeaguePosition requires Level, FromPos, ToPos"
        | "CupWinner" ->
            match d.CompetitionName with
            | Some name -> Ok(CupWinner name)
            | None -> Error "CupWinner requires CompetitionName"
        | "TitleHolder" -> Ok TitleHolder
        | "ConfederationSlot" ->
            match d.Slots with
            | Some n -> Ok(ConfederationSlot n)
            | None -> Error "ConfederationSlot requires Slots"
        | _ -> Error $"Unknown QualificationSlot kind: '{d.Kind}'"

    // ─── InternationalComp ───

    let intlCompToDto (name: string) (compType: CompetitionType) : Result<InternationalCompDto, string> =
        match compType with
        | InternationalCup(confOpt, fmt, slots) ->
            Ok
                { Name = name
                  Confederation = confOpt |> Option.map DomainEncoders.confederationToString
                  Format = DomainEncoders.cupFormatToString fmt
                  Qualification = slots |> List.map qualSlotToDto }
        | _ -> Error $"Only InternationalCup supported for serialization, got: {compType}"

    let intlCompFromDto (d: InternationalCompDto) : Result<(string * CompetitionType), string> =
        let fmt = DomainEncoders.parseCupFormat d.Format

        let conf =
            match d.Confederation with
            | None -> Ok None
            | Some s -> DomainEncoders.parseConfederation s |> Result.map Some

        let slots =
            d.Qualification |> List.map qualSlotFromDto |> DomainEncoders.sequenceList

        DomainEncoders.map3 (fun fmt conf slots -> d.Name, InternationalCup(conf, fmt, slots)) fmt conf slots
