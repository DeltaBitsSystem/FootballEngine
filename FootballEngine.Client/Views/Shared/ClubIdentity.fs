namespace FootballEngine

open FootballEngine.Domain

/// Presentation identity for a club: kit colours for crests and shirts, and the league line under
/// its name. Clubs without a kit in their data mod get one derived from the name with a stable hash
/// (String.GetHashCode is randomized per process), so it never changes between runs or machines.
module ClubIdentity =

    type Kit = ClubKit

    let private kits =
        [| { Primary = "#FFFFFF"; Secondary = "#E2353B"; Pattern = Sash }
           { Primary = "#1C3F94"; Secondary = "#FFD200"; Pattern = Hoops }
           { Primary = "#6CABDD"; Secondary = "#FFFFFF"; Pattern = Stripes }
           { Primary = "#E2353B"; Secondary = "#FFFFFF"; Pattern = Plain }
           { Primary = "#0B6E3F"; Secondary = "#FFFFFF"; Pattern = Halves }
           { Primary = "#111111"; Secondary = "#FFFFFF"; Pattern = Stripes }
           { Primary = "#7A1F3D"; Secondary = "#4DA3FF"; Pattern = Plain }
           { Primary = "#F28C28"; Secondary = "#111111"; Pattern = Plain }
           { Primary = "#FFFFFF"; Secondary = "#1C3F94"; Pattern = Hoops }
           { Primary = "#B3121B"; Secondary = "#111111"; Pattern = Stripes }
           { Primary = "#1F7AE0"; Secondary = "#FFFFFF"; Pattern = Sash }
           { Primary = "#5B2A86"; Secondary = "#FFFFFF"; Pattern = Halves }
           { Primary = "#FFD200"; Secondary = "#0B4F9C"; Pattern = Plain }
           { Primary = "#0E7C86"; Secondary = "#F2F2F2"; Pattern = Stripes } |]

    let private stableHash (text: string) =
        let mutable hash = 2166136261u

        for character in text do
            hash <- (hash ^^^ uint32 character) * 16777619u

        hash

    /// Stand-in kit for clubs whose data mod has none yet.
    let generatedKit (clubName: string) : Kit =
        kits[int (stableHash clubName % uint32 kits.Length)]

    /// The club's real kit from its data mod, or a stable generated one.
    let kitOf (gs: GameState) (clubId: ClubId) : Kit =
        match gs.Clubs |> Map.tryFind clubId with
        | Some club -> club.Kit |> Option.defaultWith (fun () -> generatedKit club.Name)
        | None -> generatedKit ""

    let keeperKit: Kit =
        { Primary = "#FFC83D"
          Secondary = "#0A1020"
          Pattern = Plain }

    /// The user's top-flight league and their place in it, as shown under the club name.
    let leagueStanding (gs: GameState) (clubId: ClubId) : (string * int option) option =
        gs.Competitions
        |> Map.toSeq
        |> Seq.tryPick (fun (_, competition) ->
            match competition.Type with
            | NationalLeague(LeagueLevel 0, _) when List.contains clubId competition.ClubIds ->
                let position =
                    Competition.rankedStandings competition
                    |> List.tryFindIndex (fun (standingClub, _) -> standingClub = clubId)
                    |> Option.map ((+) 1)

                Some(competition.Name, position)
            | _ -> None)
