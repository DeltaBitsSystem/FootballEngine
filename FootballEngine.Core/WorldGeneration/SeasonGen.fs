namespace FootballEngine.Generation

open System
open FootballEngine.Domain
open FootballEngine.Data

module SeasonGen =

    let private toFixtureMap (fixtures: MatchFixture list) =
        fixtures |> List.map (fun f -> f.Id, f) |> Map.ofList

    let private movement (comp: Competition) (rules: LeagueRules) =
        let ranked = Competition.rankedStandings comp |> List.map fst

        let automaticPromotion =
            rules.Promotion
            |> List.sumBy (function
                | AutomaticPromotion n -> n
                | PlayoffPromotion _ -> 0)

        let automaticRelegation =
            rules.Relegation
            |> List.sumBy (function
                | AutomaticRelegation n -> n
                | PlayoffRelegation _ -> 0)

        let playoff = Competition.resolvePlayoffs comp
        let promoted = ranked |> List.truncate automaticPromotion
        let relegated = ranked |> List.rev |> List.truncate automaticRelegation

        let promoted =
            promoted @ (playoff |> Option.map _.Promoted |> Option.defaultValue [])

        let relegated =
            relegated @ (playoff |> Option.map _.Relegated |> Option.defaultValue [])

        promoted |> List.distinct, relegated |> List.distinct

    let private applyPromotionRelegation (comps: Map<CompetitionId, Competition>) : Map<CompetitionId, Competition> =
        let leaguesByCountry =
            comps
            |> Map.toList
            |> List.choose (fun (id, comp) ->
                match comp.Type, comp.Country with
                | NationalLeague(LeagueLevel lvl, rules), Some code -> Some(code, lvl, id, comp, rules)
                | _ -> None)
            |> List.groupBy (fun (code, _, _, _, _) -> code)

        (comps, leaguesByCountry)
        ||> List.fold (fun acc (_, byCountry) ->
            byCountry
            |> List.sortBy (fun (_, lvl, _, _, _) -> lvl)
            |> List.pairwise
            |> List.fold
                (fun acc ((_, _, highId, highComp, highRules), (_, _, lowId, lowComp, lowRules)) ->
                    if highComp.Standings.IsEmpty || lowComp.Standings.IsEmpty then
                        acc
                    else
                        let _, relegated = movement highComp highRules
                        let promoted, _ = movement lowComp lowRules

                        if relegated.IsEmpty || promoted.IsEmpty then
                            acc
                        else
                            acc
                            |> Map.add
                                highId
                                { highComp with
                                    ClubIds =
                                        highComp.ClubIds
                                        |> List.filter (fun id -> not (List.contains id relegated))
                                        |> fun ids -> ids @ promoted }
                            |> Map.add
                                lowId
                                { lowComp with
                                    ClubIds =
                                        lowComp.ClubIds
                                        |> List.filter (fun id -> not (List.contains id promoted))
                                        |> fun ids -> ids @ relegated })
                acc)

    let regenerateFixtures (state: GameState) : GameState =
        let mutable nextMatchId =
            state.Competitions
            |> Map.toList
            |> List.collect (fun (_, c) -> c.Fixtures |> Map.toList |> List.map fst)
            |> fun ids -> if ids.IsEmpty then 1 else List.max ids + 1

        let comps = applyPromotionRelegation state.Competitions

        let allCountryData = state.Countries |> Map.toList |> List.map snd

        let regenerated =
            comps
            |> Map.map (fun _ comp ->
                match comp.Type with
                | NationalLeague _ ->
                    let fixtures, next =
                        FixtureGen.forLeague comp.Id comp.ClubIds (DateTime(state.Season, 8, 1)) nextMatchId

                    nextMatchId <- next

                    { comp with
                        Season = state.Season
                        Fixtures = toFixtureMap fixtures
                        Standings = Map.empty }

                | NationalCup(fmt, _) ->
                    let allLeagueClubs =
                        comps
                        |> Map.toList
                        |> List.choose (fun (_, c) ->
                            match c.Type, c.Country with
                            | NationalLeague _, Some code when Some code = comp.Country -> Some c.ClubIds
                            | _ -> None)
                        |> List.concat
                        |> List.distinct

                    let fixtures, next =
                        FixtureGen.forCupFormat comp.Id allLeagueClubs fmt (DateTime(state.Season, 10, 1)) nextMatchId

                    nextMatchId <- next

                    { comp with
                        Season = state.Season
                        ClubIds = allLeagueClubs
                        Fixtures = toFixtureMap fixtures
                        Standings = Map.empty }

                | InternationalCup(confOpt, fmt, slots) ->
                    let qualifyingClubIds = WorldGen.resolveSlots slots confOpt allCountryData comps

                    if qualifyingClubIds.IsEmpty then
                        comp
                    else
                        let fixtures, next =
                            FixtureGen.forCupFormat
                                comp.Id
                                qualifyingClubIds
                                fmt
                                (DateTime(state.Season, 9, 1))
                                nextMatchId

                        nextMatchId <- next

                        { comp with
                            Season = state.Season
                            ClubIds = qualifyingClubIds
                            Fixtures = toFixtureMap fixtures
                            Standings = Map.empty })

        { state with
            Competitions = regenerated }
