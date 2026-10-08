namespace FootballEngine.Core.Match

module MatchAdapter =

    let private resultOfOutcome (input: MatchInput) (outcome: MatchOutcome) : MatchResult =
        let yellowCards, redCards, fouls =
            outcome.Events
            |> List.fold
                (fun (yellow, red, fouls) event ->
                    match event.Data with
                    | EventData.Card(_, CardColor.Yellow) -> yellow + 1, red, fouls
                    | EventData.Card(_, CardColor.Red)
                    | EventData.RedCardIssued _ -> yellow, red + 1, fouls
                    | EventData.Foul _ -> yellow, red, fouls + 1
                    | _ -> yellow, red, fouls)
                (0, 0, 0)

        let homeEvents, awayEvents =
            outcome.Events
            |> List.fold
                (fun (home, away) event ->
                    match event.Data with
                    | EventData.Goal(ClubSide.Home, _, _, _) -> home + 1, away
                    | EventData.Goal(ClubSide.Away, _, _, _) -> home, away + 1
                    | _ -> home, away)
                (0, 0)

        { MatchId = input.MatchId
          EngineVersion = input.EngineVersion
          Facts =
            { HomeScore = outcome.Final.State.HomeScore
              AwayScore = outcome.Final.State.AwayScore
              EventsHash = outcome.EventsHash }
          Load =
            { HomeEvents = homeEvents
              AwayEvents = awayEvents
              TotalTicks = outcome.Final.State.Tick }
          InjuredPlayers =
            outcome.Events
            |> List.choose (fun event ->
                match event.Data with
                | EventData.Injury(player, _) -> Some player
                | _ -> None)
          Discipline =
            { YellowCards = yellowCards
              RedCards = redCards
              Fouls = fouls }
          Metrics = outcome.Final.Home.Stats, outcome.Final.Away.Stats
          Trace = None }

    let runOutcome systems input commands : Result<MatchOutcome, MatchContractError> =
        match MatchSession.create systems input MatchRunMode.Fast with
        | Error error -> Error error
        | Ok session ->
            let rec receiveAll rest =
                match rest with
                | [] -> MatchSession.finish session
                | command :: tail ->
                    match MatchSession.receive command session with
                    | Error error -> Error error
                    | Ok() -> receiveAll tail

            receiveAll commands

    let run systems input commands : Result<MatchResult, MatchContractError> =
        runOutcome systems input commands |> Result.map (resultOfOutcome input)
