namespace FootballEngine.Domain

open System

module WorldNews =

    let private clubName (state: GameState) clubId =
        state.Clubs
        |> Map.tryFind clubId
        |> Option.map _.Name
        |> Option.defaultValue $"Club {clubId}"

    let private message date subject body =
        Inbox.create date "Competition desk" subject body InboxMessageCategory.BoardUpdate false

    /// Converts confirmed season facts into bounded inbox messages. Every text
    /// contains the season and entity IDs so it can be traced back to the record.
    let deriveSeasonMessages (state: GameState) (record: SeasonRecord) : InboxMessage list =
        let winnerMessages =
            record.Winners
            |> List.choose (fun (competitionId, clubId) ->
                state.Competitions
                |> Map.tryFind competitionId
                |> Option.map (fun comp ->
                    message
                        record.CompletedOn
                        $"{comp.Name}: campeón"
                        $"Temporada {record.Season}: {clubName state clubId} (club:{clubId}) ganó {comp.Name} (competición:{competitionId})."))

        let movementMessages =
            [ yield!
                  record.Promoted
                  |> List.filter (fun id -> id = state.UserClubId)
                  |> List.map (fun id ->
                      message
                          record.CompletedOn
                          "Ascenso confirmado"
                          $"Temporada {record.Season}: {clubName state id} (club:{id}) ascendió según la clasificación final.")
              yield!
                  record.Relegated
                  |> List.filter (fun id -> id = state.UserClubId)
                  |> List.map (fun id ->
                      message
                          record.CompletedOn
                          "Descenso confirmado"
                          $"Temporada {record.Season}: {clubName state id} (club:{id}) descendió según la clasificación final.") ]

        (winnerMessages @ movementMessages) |> List.truncate 20

    let publishSeasonMessages (state: GameState) (record: SeasonRecord) : GameState =
        deriveSeasonMessages state record
        |> List.fold (fun acc msg -> GameState.addInboxMessage msg acc) state
