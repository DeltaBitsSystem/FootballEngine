namespace FootballEngine.Domain

open System

type NationalCallUp =
    { PlayerId: PlayerId
      Country: CountryCode
      ReleasedOn: DateTime
      Reason: string }

module Selection =

    let private positionOrder = [ GK; DC; DL; DR; DM; MC; ML; MR; AMC; AML; AMR; ST ]

    let private eligible (country: CountryCode) (p: Player) =
        p.Nationality = country
        && Player.clubOf p |> Option.isSome
        && p.Status = Available
        && p.Affiliation <> Retired

    /// Selects one best available player per position before filling the squad.
    /// The result is pure, deterministic and does not alter the player's club.
    let select (country: CountryCode) (releasedOn: DateTime) (maximum: int) (state: GameState) : NationalCallUp list =
        let candidates =
            state.Players
            |> Map.values
            |> Seq.filter (eligible country)
            |> Seq.sortByDescending (fun p -> p.CurrentSkill, p.Reputation, -p.Id)
            |> List.ofSeq

        let starters =
            positionOrder
            |> List.choose (fun position -> candidates |> List.tryFind (fun p -> p.Position = position))

        (starters @ candidates)
        |> List.distinctBy _.Id
        |> List.truncate (max 1 maximum)
        |> List.map (fun p ->
            { PlayerId = p.Id
              Country = country
              ReleasedOn = releasedOn
              Reason = $"available:{p.CurrentSkill}:club:{Player.clubOf p |> Option.get}" })

    let toWorldEvents (date: DateTime) (callUps: NationalCallUp list) : WorldEventPayload list =
        callUps |> List.map (fun c -> PlayerCalledUp(c.PlayerId, c.Country))
