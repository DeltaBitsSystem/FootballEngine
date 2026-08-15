namespace FootballEngine.Generation

open System
open FootballEngine.Domain
open FootballEngine.Domain.LineupOps
open FootballEngine.Stats
open FootballEngine.Types

module ClubGen =

    let private minimumSquad =
        [ GK, 3
          DC, 5
          DL, 3
          DR, 3
          WBL, 2
          WBR, 2
          DM, 3
          MC, 5
          ML, 2
          MR, 2
          AML, 3
          AMR, 3
          AMC, 3
          ST, 5 ]
        |> List.collect (fun (position, count) -> List.replicate count position)

    let private extraPositionWeights =
        [ 1.0, GK
          2.0, DC
          1.5, DL
          1.5, DR
          1.0, DM
          2.0, MC
          1.0, AML
          1.0, AMR
          1.0, AMC
          2.0, ST ]

    let private generateSquadPositions () =
        let extraCount = normalInt 3.0 1.5 0 6

        let extraPositions =
            List.init extraCount (fun _ -> pickWeighted extraPositionWeights)

        minimumSquad @ extraPositions

    let private reputationFor (entry: ClubEntry) (rankPercentile: float) =
        match entry.Reputation with
        | Some reputation -> reputation
        | None ->
            let mean = 9500.0 - rankPercentile * 8500.0
            let sigma = 300.0 + rankPercentile * 400.0

            betaInt 1.5 4.0 0 9999
            |> fun betaSample ->
                let blended = int mean * 7 / 10 + betaSample * 3 / 10
                Math.Clamp(blended + normalInt 0.0 sigma -600 600, 500, 9999)

    let private budgetForLevel (reputation: int) =
        function
        | 0 ->
            normalFloat 50_000_000.0 8_000_000.0 20_000_000.0 120_000_000.0
            |> fun baseBudget ->
                Math.Clamp(baseBudget * (0.7 + float reputation / 9999.0 * 0.6), 1_000_000.0, 200_000_000.0)
            |> decimal
        | 1 -> normalFloat 15_000_000.0 3_000_000.0 5_000_000.0 35_000_000.0 |> decimal
        | _ -> normalFloat 3_000_000.0 800_000.0 500_000.0 8_000_000.0 |> decimal

    let private moraleForReputation (reputation: int) =
        let baseMorale = 45.0 + float reputation / 9999.0 * 30.0
        normalInt baseMorale 8.0 30 90

    let private boardObjectiveFor (leagueLevel: int) (reputation: int) =
        match leagueLevel, reputation with
        | 0, r when r >= 8000 -> LeagueObjective WinLeague
        | 0, r when r >= 6000 -> LeagueObjective TopFour
        | 0, r when r >= 4000 -> LeagueObjective TopHalf
        | 0, _ -> LeagueObjective MidTable
        | 1, r when r >= 7000 -> Promotion
        | 1, _ -> LeagueObjective MidTable
        | _, _ -> LeagueObjective Survival

    let create
        (clubId: ClubId)
        (entry: ClubEntry)
        (rankPercentile: float)
        (countryData: CountryData)
        (year: int)
        (nextPlayerId: unit -> int)
        (nextStaffId: unit -> int)
        : Club * Map<PlayerId, Player> * Map<StaffId, Staff> =
        let reputation = reputationFor entry rankPercentile

        let players =
            generateSquadPositions ()
            |> List.map (fun position ->
                PlayerGen.create (nextPlayerId ()) position clubId countryData entry.LeagueLevel year)

        let staff =
            StaffGen.rolesForClub
            |> List.map (fun role ->
                let generatedStaff =
                    StaffGen.create (nextStaffId ()) role clubId countryData entry.LeagueLevel year

                match role with
                | HeadCoach -> autoLineup generatedStaff players (bestFormation players)
                | _ -> generatedStaff)

        let club =
            { Id = clubId
              Name = entry.Name
              Nationality = countryData.Country.Code
              Reputation = reputation
              PlayerIds = players |> List.map _.Id
              StaffIds = staff |> List.map _.Id
              Budget = budgetForLevel reputation entry.LeagueLevel
              Morale = moraleForReputation reputation
              BoardObjective = boardObjectiveFor entry.LeagueLevel reputation
              CoordinationMemory = CoordinationMemory.defaultMemory }


        let playerMap = players |> List.map (fun p -> p.Id, p) |> Map.ofList
        let staffMap = staff |> List.map (fun s -> s.Id, s) |> Map.ofList

        club, playerMap, staffMap
