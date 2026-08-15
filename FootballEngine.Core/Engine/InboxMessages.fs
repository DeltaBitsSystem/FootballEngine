namespace FootballEngine

open System
open FootballEngine.Domain

module InboxMessages =

    let private createInjuryMessage
        (date: DateTime)
        (playerName: string)
        (position: string)
        (severity: InjurySeverity)
        (weeksOut: int)
        =
        let severityText =
            match severity with
            | Minor -> "minor"
            | Moderate -> "moderate"
            | Major -> "major"
            | Severe -> "severe"

        Inbox.create
            date
            "Medical Department"
            $"Injury Alert: {playerName}"
            $"{playerName} ({position}) has suffered a {severityText} injury and is expected to be out for approximately {weeksOut} week(s)."
            InboxMessageCategory.InjuryMessage
            true

    let private createSeasonStartMessage (date: DateTime) (season: int) =
        Inbox.create
            date
            "League Board"
            $"Season {season} Begins"
            $"The new season is about to begin. Good luck to all clubs competing. May the best team win!"
            InboxMessageCategory.BoardUpdate
            false

    let private createWeeklyTrainingSummary
        (date: DateTime)
        (improvedPlayers: (string * string * int) list)
        (injuredPlayers: (string * string * InjurySeverity * int) list)
        =
        let bodyParts =
            [ if not improvedPlayers.IsEmpty then
                  let improvements =
                      improvedPlayers
                      |> List.map (fun (name, pos, gain) -> $"• {name} ({pos}): +{gain} CA")
                      |> String.concat "\n"

                  yield $"**Players who improved:**\n{improvements}"

              if not injuredPlayers.IsEmpty then
                  let injuries =
                      injuredPlayers
                      |> List.map (fun (name, pos, sev, weeks) -> $"• {name} ({pos}): {sev} injury, {weeks} weeks")
                      |> String.concat "\n"

                  yield $"**Injuries sustained:**\n{injuries}" ]
            |> String.concat "\n\n"

        Inbox.create
            date
            "Training Department"
            $"Weekly Training Summary"
            (if bodyParts = "" then
                 "No significant events in training this week."
             else
                 bodyParts)
            InboxMessageCategory.Development
            false

    let generateWeeklyDevelopmentMessages
        (date: DateTime)
        (players: Player list)
        (prevSkills: Map<PlayerId, int>)
        (prevStatus: Map<PlayerId, PlayerStatus>)
        : InboxMessage list =
        let improved =
            players
            |> List.choose (fun p ->
                match p.Affiliation with
                | Contracted _ ->
                    match prevSkills.TryFind p.Id with
                    | Some prevSkill when p.CurrentSkill > prevSkill ->
                        let gain = p.CurrentSkill - prevSkill
                        Some(p.Name, string p.Position, gain)
                    | _ -> None
                | _ -> None)

        let newInjuries =
            players
            |> List.choose (fun p ->
                match p.Status with
                | Injured(severity, until) ->
                    let wasInjuredBefore =
                        match prevStatus.TryFind p.Id with
                        | Some(Injured _) -> true
                        | _ -> false

                    if not wasInjuredBefore then
                        let weeksOut = int (until - date).TotalDays / 7
                        Some(p.Name, string p.Position, severity, max 1 weeksOut)
                    else
                        None
                | _ -> None)

        [ if not improved.IsEmpty || not newInjuries.IsEmpty then
              yield createWeeklyTrainingSummary date improved newInjuries ]

    let generateSeasonStartMessage (date: DateTime) (season: int) : InboxMessage = createSeasonStartMessage date season

    let generateInjuryMessage (date: DateTime) (player: Player) : InboxMessage option =
        match player.Status with
        | Injured(severity, until) ->
            let weeksOut = int (until - date).TotalDays / 7
            Some(createInjuryMessage date player.Name (string player.Position) severity (max 1 weeksOut))
        | _ -> None
