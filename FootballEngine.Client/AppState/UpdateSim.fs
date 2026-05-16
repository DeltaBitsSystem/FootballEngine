namespace FootballEngine

open System.Threading.Tasks
open Elmish
open FootballEngine.Domain
open FootballEngine.Icons
open FootballEngine.Types
open AppTypes
open AppMsgs
open FootballEngine.Types.SimulationClock
open FootballEngine.World.WorldRunner


module UpdateSim =

    let private addLog = AppTypes.addLog
    let private pushNotification = AppTypes.pushNotification
    let private saveCmd = SimHelpers.saveCmd
    let private primaryLeagueId = SimHelpers.primaryLeagueId

    let private userMatchNotif (gs: GameState) (fixture: MatchFixture) (state: State) =
        match fixture.HomeScore, fixture.AwayScore with
        | Some h, Some a when MatchFixture.involves gs.UserClubId fixture ->
            let isHome = fixture.HomeClubId = gs.UserClubId
            let userScore, oppScore = if isHome then h, a else a, h

            let clubName id =
                gs.Clubs |> Map.tryFind id |> Option.map _.Name |> Option.defaultValue "?"

            let outcome, icon =
                if userScore > oppScore then
                    "WIN", NotificationIcons.win
                elif userScore < oppScore then
                    "LOSS", NotificationIcons.loss
                else
                    "DRAW", NotificationIcons.draw

            let goals =
                fixture.Events
                |> List.choose (fun ev ->
                    if ev.Type = Goal || ev.Type = OwnGoal then
                        let minutes = int (subTicksToSeconds defaultClock (ev.SubTick * 1<subtick>) / 60.0)

                        gs.Players
                        |> Map.tryFind ev.PlayerId
                        |> Option.map (fun p ->
                            let suffix = if ev.Type = OwnGoal then " (OG)" else ""
                            $"{p.Name}{suffix} {minutes}'")
                    else
                        None)

            let body =
                if goals.IsEmpty then
                    "No goal events recorded."
                else
                    String.concat " · " goals

            let title =
                $"{outcome}  {clubName fixture.HomeClubId} {h}-{a} {clubName fixture.AwayClubId}"

            state |> pushNotification icon title body
        | _ -> state

    let private applyMatchNotifs (gs: GameState) (fixtures: (MatchId * MatchFixture) list) (state: State) =
        fixtures |> List.fold (fun acc (_, f) -> userMatchNotif gs f acc) state

    let private userFinishingLine (gs: GameState) (leagueId: CompetitionId) (userClubId: ClubId) =
        gs.Competitions
        |> Map.tryFind leagueId
        |> Option.bind (fun comp ->
            let ranked = Competition.rankedStandings comp

            ranked
            |> List.tryFindIndex (fun (id, _) -> id = userClubId)
            |> Option.map (fun idx -> $"Season ended - finished {idx + 1}/{ranked.Length}"))
        |> Option.toList

    let private leagueChangeNotif (prevGs: GameState) (newGs: GameState) (userClubId: ClubId) =
        let levelOf (gs: GameState) =
            gs.Competitions
            |> Map.toList
            |> List.tryPick (fun (_, comp) ->
                match comp.Type, comp.Country with
                | NationalLeague(LeagueLevel lvl, _), Some _ when List.contains userClubId comp.ClubIds ->
                    Some(lvl, comp.Name)
                | _ -> None)

        match levelOf prevGs, levelOf newGs with
        | Some(old, _), Some(nw, league) when nw < old -> Some("Promoted!", $"Promoted to {league}")
        | Some(old, _), Some(nw, league) when nw > old -> Some("Relegated", $"Relegated to {league}")
        | _ -> None

    let private applyDayResult (result: DayResult) (state: State) =
        let logs =
            if result.Logs.IsEmpty then
                []
            else
                $"{result.PlayedMatches.Length} matches played" :: result.Logs

        let nextState =
            { state with
                Mode = InGame(result.GameState, managerEmployment result.GameState)
                IsProcessing = false
                WorldClock = result.WorldClock
                LogMessages = logs @ state.LogMessages |> List.truncate 30 }
            |> applyMatchNotifs result.GameState result.PlayedMatches

        match state.PrevUserClubSkills, state.PrevUserClubStatus with
        | Some prevSkills, Some prevStatus ->
            let userClubPlayers = GameState.getUserSquad result.GameState

            let inboxMsgs =
                InboxMessages.generateWeeklyDevelopmentMessages
                    result.GameState.CurrentDate
                    userClubPlayers
                    prevSkills
                    prevStatus

            let gs =
                inboxMsgs
                |> List.fold (fun acc msg -> GameState.addInboxMessage msg acc) result.GameState

            { nextState with
                Mode = InGame(gs, managerEmployment gs)
                PrevUserClubSkills = None
                PrevUserClubStatus = None }
        | _ -> nextState

    let private applySeasonResult (result: SeasonResult) (state: State) =
        let userLine =
            userFinishingLine result.SeasonFinalGs state.SelectedLeagueId result.NextSeasonGs.UserClubId

        let messages =
            $"Season {result.NextSeasonGs.Season} begins" :: userLine @ result.Summary

        let seasonBody = userLine |> String.concat " · "

        let seasonStartMsg =
            InboxMessages.generateSeasonStartMessage result.NextSeasonGs.CurrentDate result.NextSeasonGs.Season

        let gsAfterSeasonStart =
            GameState.addInboxMessage seasonStartMsg result.NextSeasonGs

        let stateWithInbox =
            match state.PrevUserClubSkills with
            | Some prevSkills ->
                let userClubPlayers = GameState.getUserSquad result.NextSeasonGs

                let improved =
                    userClubPlayers
                    |> List.choose (fun p ->
                        match prevSkills.TryFind p.Id with
                        | Some prev when p.CurrentSkill > prev ->
                            Some(p.Name, string p.Position, p.CurrentSkill - prev)
                        | _ -> None)

                let seasonSummaryMsg =
                    if not improved.IsEmpty then
                        let improvements =
                            improved
                            |> List.map (fun (n, p, g) -> $"• {n} ({p}): +{g}")
                            |> String.concat "\n"

                        Some(
                            Inbox.create
                                result.NextSeasonGs.CurrentDate
                                "Youth Development System"
                                $"Season {result.NextSeasonGs.Season - 1} Development Summary"
                                $"The following players showed improvement over the season:\n\n{improvements}"
                                InboxMessageCategory.Development
                                false
                        )
                    else
                        None

                let gs =
                    match seasonSummaryMsg with
                    | Some msg -> GameState.addInboxMessage msg gsAfterSeasonStart
                    | None -> gsAfterSeasonStart

                { state with
                    Mode = InGame(gs, managerEmployment gs)
                    PrevUserClubSkills = None
                    PrevUserClubStatus = None }
            | None ->
                { state with
                    Mode = InGame(gsAfterSeasonStart, managerEmployment gsAfterSeasonStart)
                    PrevUserClubSkills = None
                    PrevUserClubStatus = None }

        let prevGs =
            match state.Mode with
            | InGame(gs, _) -> gs
            | _ -> result.NextSeasonGs

        let nextState =
            { stateWithInbox with
                IsProcessing = false
                SelectedLeagueId = primaryLeagueId result.NextSeasonGs
                LogMessages = messages @ stateWithInbox.LogMessages |> List.truncate 50 }
            |> pushNotification
                NotificationIcons.seasonComplete
                $"Season {result.NextSeasonGs.Season - 1} Complete"
                seasonBody

        match leagueChangeNotif prevGs result.NextSeasonGs result.NextSeasonGs.UserClubId with
        | Some(title, body) when title = "Promoted!" ->
            nextState |> pushNotification NotificationIcons.promotion title body
        | Some(title, body) -> nextState |> pushNotification NotificationIcons.relegation title body
        | None -> nextState

    let rec handle (msg: SimMsg) (state: State) : State * Cmd<Msg> =
        let getGs () =
            match state.Mode with
            | InGame(gs, _) -> gs
            | _ -> failwith "Engine call attempted outside of InGame state"

        match msg with
        | Advance days ->


            if state.IsProcessing then

                state, Cmd.none
            else
                let gs = getGs ()

                let totalF =
                    gs.Competitions
                    |> Map.toSeq
                    |> Seq.sumBy (fun (_, c) -> c.Fixtures |> Map.count)

                let playedF =
                    gs.Competitions
                    |> Map.toSeq
                    |> Seq.sumBy (fun (_, c) -> c.Fixtures |> Map.filter (fun _ f -> f.Played) |> Map.count)



                let prevSkills =
                    GameState.getUserSquad gs
                    |> List.map (fun p -> p.Id, p.CurrentSkill)
                    |> Map.ofList

                let prevStatus =
                    GameState.getUserSquad gs |> List.map (fun p -> p.Id, p.Status) |> Map.ofList

                { state with
                    IsProcessing = true
                    PrevUserClubSkills = Some prevSkills
                    PrevUserClubStatus = Some prevStatus },
                Cmd.OfTask.either
                    (fun () -> Task.Run(fun () -> advanceDays days state.WorldClock gs))
                    ()
                    (SimMsg << AdvanceDone)
                    (fun ex ->
                        printfn $"[CRASH] Advance Error: {ex.ToString()}"
                        SetProcessing false)

        | AdvanceDone result ->

            let nextState = applyDayResult result state

            let baseCmd =
                if result.SeasonComplete then

                    Cmd.ofMsg (SimMsg AdvanceSeason)
                else
                    saveCmd result.GameState state.WorldClock

            if hasUserMatchToday result.GameState then

                nextState, Cmd.batch [ baseCmd; Cmd.ofMsg (SimMsg SimulateUserFixture) ]
            else
                nextState, baseCmd

        | SimulateUserFixture ->


            if state.IsProcessing then

                state, Cmd.none
            else
                { state with IsProcessing = true },
                Cmd.OfTask.perform
                    (fun () -> Task.Run(fun () -> simulateUserMatch state.WorldClock (getGs ())))
                    ()
                    (SimMsg << UserMatchDone)

        | UserMatchDone(Ok matchDay) ->

            let nextState = applyDayResult matchDay.DayResult state

            let withMatch =
                { nextState with
                    ActiveMatchReplay = Some matchDay.MatchReplay
                    ActiveMatchSnapshot = 0
                    CurrentPage = Match }

            if matchDay.DayResult.SeasonComplete then
                withMatch,
                Cmd.batch
                    [ Cmd.ofMsg (SimMsg AdvanceSeason)
                      saveCmd matchDay.DayResult.GameState state.WorldClock ]
            else
                withMatch, saveCmd matchDay.DayResult.GameState state.WorldClock

        | UserMatchDone(Error e) ->

            { state with IsProcessing = false }, Cmd.none

        | SimulateSeason ->


            if state.IsProcessing then

                state, Cmd.none
            else
                let gs = getGs ()



                let prevSkills =
                    GameState.getUserSquad gs
                    |> List.map (fun p -> p.Id, p.CurrentSkill)
                    |> Map.ofList

                let prevStatus =
                    GameState.getUserSquad gs |> List.map (fun p -> p.Id, p.Status) |> Map.ofList

                { state with
                    IsProcessing = true
                    PrevUserClubSkills = Some prevSkills
                    PrevUserClubStatus = Some prevStatus },
                Cmd.OfTask.either
                    (fun () -> Task.Run(fun () -> simulateFullSeasonAndAdvance state.WorldClock gs))
                    ()
                    (SimMsg << SeasonAdvanceDone)
                    (fun ex ->
                        printfn $"[CRASH] SimulateSeason Error: {ex.ToString()}"
                        SetProcessing false)


        | SeasonAdvanceDone(Ok result) ->



            let nextState = applySeasonResult result state

            match nextState.Mode with
            | InGame(gs, _) -> nextState, saveCmd gs state.WorldClock
            | _ -> nextState, Cmd.none

        | SeasonAdvanceDone(Error NoFixturesToPlay) ->

            handle AdvanceSeason { state with IsProcessing = false }

        | SeasonAdvanceDone(Error(SimulationErrors errors)) ->
            { state with IsProcessing = false }
            |> pushNotification NotificationIcons.error "Sim Season failed" (String.concat "\n" errors),
            Cmd.none

        | AdvanceSeason ->


            if state.IsProcessing then

                state, Cmd.none
            else
                let gs = getGs ()


                let prevSkills =
                    GameState.getUserSquad gs
                    |> List.map (fun p -> p.Id, p.CurrentSkill)
                    |> Map.ofList

                let prevStatus =
                    GameState.getUserSquad gs |> List.map (fun p -> p.Id, p.Status) |> Map.ofList

                { state with
                    IsProcessing = true
                    PrevUserClubSkills = Some prevSkills
                    PrevUserClubStatus = Some prevStatus },
                Cmd.OfTask.either
                    (fun () -> Task.Run(fun () -> advanceToNextSeason state.WorldClock gs))
                    ()
                    (SimMsg << SeasonAdvanceDone << Ok)
                    (fun ex ->
                        printfn $"[CRASH] AdvanceSeason Error: {ex.ToString()}"
                        SetProcessing false)

        | SaveGame ->
            match state.Mode with
            | InGame(gs, _) -> state, saveCmd gs state.WorldClock
            | _ -> state, Cmd.none
