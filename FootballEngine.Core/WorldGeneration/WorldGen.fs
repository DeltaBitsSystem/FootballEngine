namespace FootballEngine.Generation

open System
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Data
open FootballEngine.Stats

module WorldGen =

    type private Counters = { mutable NextId: int }

    let private nextId (counters: Counters) =
        let id = counters.NextId
        counters.NextId <- counters.NextId + 1
        id

    type private WorldAcc =
        { Clubs: Map<ClubId, Club>
          Players: Map<PlayerId, Player>
          Staff: Map<StaffId, Staff>
          Competitions: Map<CompetitionId, Competition> }

    let private emptyWorld =
        { Clubs = Map.empty
          Players = Map.empty
          Staff = Map.empty
          Competitions = Map.empty }

    let private toFixtureMap (fixtures: MatchFixture list) =
        fixtures |> List.map (fun f -> f.Id, f) |> Map.ofList

    let private emptyComp id name compType country season clubIds fixtures =
        { Id = id
          Name = name
          BroadcastIdentity = BroadcastIdentity.generated ($"builtin/competition/{id}")
          Type = compType
          Country = country
          Season = season
          ClubIds = clubIds
          Fixtures = toFixtureMap fixtures
          Standings = Map.empty
          KnockoutTies = Map.empty }

    let resolveSlots
        (slots: QualificationSlot list)
        (confOpt: Confederation option)
        (allCountryData: CountryData list)
        (competitions: Map<CompetitionId, Competition>)
        : ClubId list =
        let countryInConf (code: CountryCode) =
            match confOpt with
            | None -> true
            | Some conf ->
                allCountryData
                |> List.exists (fun c -> c.Country.Code = code && c.Country.Confederation = conf)

        let cupWinnerIds competitionName =
            competitions
            |> Map.toList
            |> List.choose (fun (_, comp) ->
                match comp.Type, comp.Country with
                | NationalCup _, Some code when countryInConf code && comp.Name = competitionName ->
                    comp.ClubIds |> List.tryHead
                | _ -> None)

        let titleHolderIds =
            competitions
            |> Map.toList
            |> List.choose (fun (_, comp) ->
                match comp.Type, comp.Country with
                | NationalLeague(LeagueLevel 0, _), Some code when countryInConf code -> comp.ClubIds |> List.tryHead
                | _ -> None)

        slots
        |> List.collect (fun slot ->
            match slot with
            | LeaguePosition(LeagueLevel lvl, fromPos, toPos) ->
                competitions
                |> Map.toList
                |> List.choose (fun (_, comp) ->
                    match comp.Type, comp.Country with
                    | NationalLeague(LeagueLevel l, _), Some code when l = lvl && countryInConf code ->
                        comp.ClubIds
                        |> List.mapi (fun index clubId -> index, clubId)
                        |> List.choose (fun (index, clubId) ->
                            if index >= fromPos - 1 && index < toPos then
                                Some clubId
                            else
                                None)
                        |> Some
                    | _ -> None)
                |> List.concat
            | TitleHolder -> titleHolderIds
            | CupWinner competitionName -> cupWinnerIds competitionName
            | ConfederationSlot _ -> [])
        |> List.distinct

    let private generateLeagues (year: int) (countryData: CountryData) (counters: Counters) (acc: WorldAcc) : WorldAcc =
        let clubsByLevel =
            countryData.Clubs |> List.groupBy _.LeagueLevel |> List.sortBy fst

        let totalClubs = float countryData.Clubs.Length

        (acc, clubsByLevel)
        ||> List.fold (fun acc (levelIdx, entries) ->
            let compId = nextId counters

            let accAfterClubs, leagueClubIds =
                ((acc, []), entries)
                ||> List.fold (fun (innerAcc, ids) entry ->
                    let clubId = nextId counters

                    let rankPercentile =
                        countryData.Clubs
                        |> List.tryFindIndex (fun c -> c.Name = entry.Name)
                        |> Option.map (fun i -> float i / totalClubs)
                        |> Option.defaultValue 0.5

                    let club, players, staff =
                        ClubGen.create
                            clubId
                            entry
                            rankPercentile
                            countryData
                            year
                            (fun () -> nextId counters)
                            (fun () -> nextId counters)

                    { innerAcc with
                        Clubs = innerAcc.Clubs |> Map.add clubId club
                        Players = Map.foldBack Map.add players innerAcc.Players
                        Staff = Map.foldBack Map.add staff innerAcc.Staff },
                    ids @ [ clubId ])

            let fixtures, nextMatchId =
                FixtureGen.forLeague compId leagueClubIds (DateTime(year, 8, 1)) counters.NextId

            counters.NextId <- nextMatchId

            { acc with
                Clubs = accAfterClubs.Clubs
                Players = accAfterClubs.Players
                Staff = accAfterClubs.Staff
                Competitions =
                    accAfterClubs.Competitions
                    |> Map.add
                        compId
                        (emptyComp
                            compId
                            countryData.LeagueNames[levelIdx]
                            (NationalLeague(LeagueLevel levelIdx, countryData.LeagueRules[levelIdx]))
                            (Some countryData.Country.Code)
                            year
                            leagueClubIds
                            fixtures) })

    let private generateNationalCups
        (year: int)
        (countryData: CountryData)
        (counters: Counters)
        (acc: WorldAcc)
        : WorldAcc =
        let leagueClubIds =
            acc.Competitions
            |> Map.toList
            |> List.collect (fun (_, comp) ->
                match comp.Type, comp.Country with
                | NationalLeague _, Some code when code = countryData.Country.Code -> comp.ClubIds
                | _ -> [])

        if leagueClubIds.IsEmpty then
            acc
        else
            (acc, countryData.Cups |> List.indexed)
            ||> List.fold (fun acc (cupIdx, cupFormat) ->
                let compId = nextId counters

                let cupName = countryData.CupNames[cupIdx]

                let fixtures, nextMatchId =
                    FixtureGen.forCupFormat
                        compId
                        leagueClubIds
                        cupFormat
                        (DateTime(year, 10, 1).AddDays(float cupIdx * 3.0))
                        counters.NextId

                counters.NextId <- nextMatchId

                { acc with
                    Competitions =
                        acc.Competitions
                        |> Map.add
                            compId
                            (emptyComp
                                compId
                                cupName
                                (NationalCup(cupFormat, [ LeaguePosition(LeagueLevel 0, 1, leagueClubIds.Length) ]))
                                (Some countryData.Country.Code)
                                year
                                leagueClubIds
                                fixtures) })

    let private generateInternational
        (year: int)
        (name: string)
        (compType: CompetitionType)
        (allCountryData: CountryData list)
        (counters: Counters)
        (acc: WorldAcc)
        : WorldAcc =
        let compId = nextId counters

        let qualifyingClubIds, format =
            match compType with
            | InternationalCup(confOpt, fmt, slots) -> resolveSlots slots confOpt allCountryData acc.Competitions, fmt
            | _ -> [], StraightKnockout(SingleLeg ExtraTimeThenPenalties)

        if qualifyingClubIds.IsEmpty then
            acc
        else
            let fixtures, nextMatchId =
                FixtureGen.forCupFormat compId qualifyingClubIds format (DateTime(year, 9, 1)) counters.NextId

            counters.NextId <- nextMatchId

            { acc with
                Competitions =
                    acc.Competitions
                    |> Map.add compId (emptyComp compId name compType None year qualifyingClubIds fixtures) }

    let private createUserManager
        (staffId: StaffId)
        (managerName: string)
        (nationality: CountryCode)
        (clubId: ClubId)
        (year: int)
        : Staff =
        let attr = normalInt 10.0 3.0 1 20

        { Id = staffId
          Name = managerName
          Nationality = nationality
          Birthday = DateTime(year - normalInt 42.0 6.0 30 65, normalInt 6.0 3.0 1 12, normalInt 15.0 8.0 1 28)
          Role = HeadCoach
          Attributes =
            { Coaching =
                { Attacking = attr
                  Defending = attr
                  Fitness = attr
                  Goalkeeping = attr
                  Mental = attr
                  SetPieces = attr
                  Tactical = attr
                  Technical = attr
                  WorkingWithYoungsters = attr
                  PreferredFormation = None
                  Lineup = None }
              Scouting =
                { NetworkReach = attr
                  DataAnalysis = attr
                  MarketKnowledge = attr }
              Medical =
                { Physiotherapy = attr
                  SportsScience = attr }
              Analysis =
                { PerformanceAnalysis = attr
                  RecruitmentAnalysis = attr } }
          Knowledge =
            { JudgingPlayerAbility = attr
              JudgingPlayerPotential = attr
              JudgingStaffAbility = attr
              Negotiating = attr
              TacticalKnowledge = attr }
          Mental =
            { Adaptability = attr
              Determination = attr
              LevelOfDiscipline = attr
              PeopleManagement = attr
              Motivating = attr }
          CurrentSkill = 100
          PotentialSkill = 200
          Badge = NationalB
          Reputation = normalInt 3000.0 800.0 500 6000
          Contract =
            Some
                { ClubId = clubId
                  Salary = 50_000m
                  ExpiryYear = year + 3 }
          Status = Active
          TrophiesWon = 0
          SeasonsManaged = 0
          Mind = ManagerMind.neutral }

    let private generationError code entityId message suggestedAction : ModError =
        { Diagnostics =
            [ { Code = code
                ModId = None
                Version = None
                RelativePath = None
                JsonPath = None
                EntityId = entityId
                Message = message
                SuggestedAction = suggestedAction } ] }

    let private validateCountries
        (countries: CountryData list)
        (primaryCountry: CountryCode)
        (secondaryCountries: CountryCode list)
        : Result<CountryData list, ModError> =
        let byCode =
            countries
            |> List.map (fun country -> country.Country.Code, country)
            |> Map.ofList

        let diagnostics = ResizeArray<ModDiagnostic>()

        let add code entityId message suggestedAction =
            diagnostics.Add(
                { Code = code
                  ModId = None
                  Version = None
                  RelativePath = None
                  JsonPath = None
                  EntityId = entityId
                  Message = message
                  SuggestedAction = suggestedAction }
            )

        let primary =
            match Map.tryFind primaryCountry byCode with
            | None ->
                add
                    "WORLD-COUNTRY-MISSING"
                    (Some primaryCountry)
                    ("The primary country '"
                     + primaryCountry
                     + "' is not present in the resolved dataset.")
                    "Choose a country materialized from ResolvedDataSet.SportsContent."

                None
            | Some country ->
                if
                    country.LeagueRules.IsEmpty
                    || country.LeagueNames.IsEmpty
                    || not (country.Clubs |> List.exists (fun club -> club.LeagueLevel = 0))
                then
                    add
                        "WORLD-COUNTRY-NO-FIRST-DIVISION"
                        (Some primaryCountry)
                        ("The primary country '" + primaryCountry + "' has no level-0 league with clubs.")
                        "Declare a level-0 league and at least one level-0 club."

                    None
                else
                    Some country

        let secondary =
            secondaryCountries
            |> List.distinct
            |> List.choose (fun code ->
                match Map.tryFind code byCode with
                | Some country -> Some country
                | None ->
                    add
                        "WORLD-SECONDARY-COUNTRY-MISSING"
                        (Some code)
                        ("The secondary country '" + code + "' is not present in the resolved dataset.")
                        "Remove the country or include its canonical country entity in SportsContent."

                    None)

        if diagnostics.Count > 0 then
            Error { Diagnostics = List.ofSeq diagnostics }
        else
            match primary with
            | Some primary -> Ok(primary :: secondary |> List.distinctBy (fun country -> country.Country.Code))
            | None ->
                Error(
                    generationError
                        "WORLD-COUNTRY-INVALID"
                        (Some primaryCountry)
                        "The primary country could not be materialized for generation."
                        "Repair the resolved country entity before starting a game."
                )

    let generateNewGame
        (resolved: ResolvedDataSet)
        (seed: uint64)
        (startDate: DateTime)
        (primaryCountry: CountryCode)
        (managerName: string)
        (secondaryCountries: CountryCode list)
        : Result<GameState, ModError> =
        match ResolvedWorldData.materialize resolved with
        | Error error -> Error error
        | Ok materialized ->
            match validateCountries materialized.Countries primaryCountry secondaryCountries with
            | Error error -> Error error
            | Ok allCountryData ->
                let year = startDate.Year

                let allCountries =
                    materialized.Countries
                    |> List.map (fun country -> country.Country.Code, country)
                    |> Map.ofList

                RNG.withSeed seed (fun () ->
                    let counters = { NextId = 1 }

                    let world =
                        (emptyWorld, allCountryData)
                        ||> List.fold (fun acc country -> generateLeagues year country counters acc)
                        |> fun world ->
                            (world, allCountryData)
                            ||> List.fold (fun acc country -> generateNationalCups year country counters acc)

                    let world =
                        (world, materialized.InternationalCompetitions)
                        ||> List.fold (fun acc (name, competitionType) ->
                            generateInternational year name competitionType allCountryData counters acc)

                    let userClubId =
                        world.Competitions
                        |> Map.toSeq
                        |> Seq.tryPick (fun (_, competition) ->
                            match competition.Type, competition.Country with
                            | NationalLeague(LeagueLevel 0, _), Some country when country = primaryCountry ->
                                competition.ClubIds |> List.tryHead
                            | _ -> None)

                    match userClubId with
                    | None ->
                        Error(
                            generationError
                                "WORLD-COUNTRY-NO-FIRST-DIVISION"
                                (Some primaryCountry)
                                ("No generated level-0 competition exists for '" + primaryCountry + "'.")
                                "Repair the primary country's level-0 league data before starting a game."
                        )
                    | Some userClubId ->
                        let userStaffId = counters.NextId

                        let userManager =
                            createUserManager userStaffId managerName primaryCountry userClubId year

                        let profileCache =
                            world.Players
                            |> Map.map (fun _ player -> Player.profile player ProfileWeightDefaults.defaults)

                        Ok
                            { CurrentDate = startDate
                              Season = year
                              TrainingWeeksApplied = 0
                              Clubs = world.Clubs |> Map.map (fun _ club -> { club with ManagerSince = Some startDate })
                              Players = world.Players
                              Staff = world.Staff |> Map.add userStaffId userManager
                              Competitions = world.Competitions
                              Countries = allCountries
                              DataLock = resolved.DataLock
                              UserClubId = userClubId
                              UserStaffId = userStaffId
                              PrimaryCountry = primaryCountry
                              Inbox = []
                              NextInboxId = 1
                              PendingNegotiations = Map.empty
                              NextNegotiationId = 1
                              Market = MarketState.empty
                              ProfileCache = profileCache
                              PlayerExperiences = Map.empty
                              TeamEmphasis = EmPossession
                              SeasonHistory = []
                              CareerHistory = []
                              News = []
                              NextNewsId = 1
                              PulseProcessed = Set.empty
                              PulseQueues = PulseQueues.empty
                              JobOffers = []
                              NextJobOfferId = 1
                              UserStudy = []
                              ExecutionSeed = seed
                              MatchSeeds = Map.empty })
