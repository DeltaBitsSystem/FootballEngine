namespace FootballEngine

open System.Threading.Tasks
open Elmish
open FootballEngine.Core.Match
open FootballEngine.Domain
open FootballEngine.Icons
open FootballEngine.Routing
open AppTypes
open AppMsgs
open FootballEngine.World.WorldRunner
open FootballEngine.World.Phases
open FootballEngine.LiveMatch


module UpdateSim =

    let private addLog = AppTypes.addLog
    let private pushNotification = AppTypes.pushNotification
    let private saveCmd = SimHelpers.saveCmdForState
    let private primaryLeagueId = SimHelpers.primaryLeagueId

    let private userFixture (gs: GameState) : (MatchId * MatchFixture) option =
        gs.Competitions
        |> Map.toSeq
        |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
        |> Seq.tryFind (fun (_, fixture) ->
            fixture.ScheduledDate.Date = gs.CurrentDate.Date
            && MatchFixture.isPending fixture
            && MatchFixture.involves gs.UserClubId fixture)

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
                    match ev.Data with
                    | EventData.Goal(_, scorer, _, _) ->
                        let minutes = int (SimulationClock.toMatchMinute ev.Tick)

                        scorer
                        |> Option.bind (fun sid -> gs.Players |> Map.tryFind (MatchContextBuilder.unwrapPid sid))
                        |> Option.map (fun p -> $"{p.Name} {minutes}'")
                    | _ -> None)

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
                SaveStatus = SaveIdle
                PersistenceRequestId = state.PersistenceRequestId + 1UL
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
                WorldClock = result.NextSeasonClock
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
        let tryGetGs () =
            match state.Mode with
            | InGame(gs, _) -> Some gs
            | _ -> None

        match msg with
        | ContinueCareer ->
            match CareerFlow.continuation state with
            | CareerFlow.PrepareMatch id -> state, Cmd.ofMsg (Navigate(Route.Match(id, Prematch)))
            | CareerFlow.ReturnToMatch(id, phase) -> state, Cmd.ofMsg (Navigate(Route.Match(id, phase)))
            | CareerFlow.AdvanceDay _ -> handle (Advance 1) state
            | CareerFlow.Working
            | CareerFlow.Unavailable -> state, Cmd.none

        | CancelAdvance -> { state with AdvanceTarget = None }, Cmd.none

        | AdvanceFailed error ->
            { state with
                IsProcessing = false
                AdvanceTarget = None }
            |> pushNotification
                NotificationIcons.error
                (Localization.resolve TranslationKey.JourneyTitle [])
                (Localization.resolve TranslationKey.AdvanceFailed [ "error", error ]),
            Cmd.none

        | AdvanceToNextEvent ->
            match state.Mode, CareerFlow.continuation state with
            | InGame(gs, _), CareerFlow.AdvanceDay _ ->
                let pending =
                    gs.Inbox
                    |> List.tryFind (fun message -> message.RequiresAction && message.ActionTaken <> Some true)

                match pending, GameState.getUserNextFixture gs with
                | Some message, _ -> state, Cmd.ofMsg (Navigate(Route.Inbox(InboxRoute(Some message.Id))))
                | None, Some(_, fixture) ->
                    handle
                        (Advance 1)
                        { state with
                            AdvanceTarget = Some fixture.ScheduledDate.Date }
                | None, None -> handle ContinueCareer state
            | _ -> handle ContinueCareer state

        | Advance days ->


            if
                days <= 0
                || state.IsProcessing
                || state.Match.RuntimeId.IsSome
                || state.Match.PendingResult.IsSome
            then

                state, Cmd.none
            else
                match tryGetGs () with
                | None -> state, Cmd.none
                | Some gs when (CareerFlow.todayFixture gs).IsSome ->
                    let matchId, _ = CareerFlow.todayFixture gs |> Option.get
                    { state with AdvanceTarget = None }, Cmd.ofMsg (Navigate(Route.Match(matchId, Prematch)))
                | Some gs ->

                    // A multi-day request must stop before the user's fixture, not simulate it as a background match.
                    let days =
                        GameState.getUserNextFixture gs
                        |> Option.map (fun (_, fixture) ->
                            max 1 (min days (int (fixture.ScheduledDate.Date - gs.CurrentDate.Date).TotalDays)))
                        |> Option.defaultValue days

                    let gs = ExecutionRandomness.beginAttempt gs

                    let prevSkills =
                        GameState.getUserSquad gs
                        |> List.map (fun p -> p.Id, p.CurrentSkill)
                        |> Map.ofList

                    let prevStatus =
                        GameState.getUserSquad gs |> List.map (fun p -> p.Id, p.Status) |> Map.ofList

                    { state with
                        Mode = InGame(gs, managerEmployment gs)
                        IsProcessing = true
                        PrevUserClubSkills = Some prevSkills
                        PrevUserClubStatus = Some prevStatus },
                    Cmd.OfTask.either
                        (fun () ->
                            Task.Run(fun () -> advanceDaysWithSave (Some state.SaveSettings) days state.WorldClock gs))
                        ()
                        (SimMsg << AdvanceDone)
                        (fun ex -> SimMsg(AdvanceFailed ex.Message))

        | AdvanceDone result ->

            let nextState = applyDayResult result state

            let interrupted =
                match state.Mode with
                | InGame(before, _) -> CareerFlow.newAttention before result.GameState
                | _ -> false

            let nextState =
                if
                    interrupted
                    || result.SeasonComplete
                    || (CareerFlow.todayFixture result.GameState).IsSome
                then
                    { nextState with AdvanceTarget = None }
                else
                    nextState

            let nextState =
                if interrupted && state.AdvanceTarget.IsSome then
                    nextState
                    |> pushNotification
                        IconName.info
                        (Localization.resolve TranslationKey.JourneyTitle [])
                        (Localization.resolve TranslationKey.AdvanceAttention [])
                else
                    nextState

            let baseCmd =
                if result.SeasonComplete then

                    Cmd.ofMsg (SimMsg AdvanceSeason)
                else
                    saveCmd state result.GameState result.WorldClock state.SaveSettings

            let userMatchId = userFixture result.GameState |> Option.map fst

            match userMatchId with
            | Some matchId -> nextState, Cmd.batch [ Cmd.ofMsg (Navigate(Route.Match(matchId, Prematch))); baseCmd ]
            | None -> nextState, Cmd.batch [ baseCmd; Cmd.ofMsg (SimMsg SimulateAiFixtures) ]

        | SimulateUserFixture ->
            if state.Match.RuntimeId.IsSome || state.Match.PendingResult.IsSome then
                state, Cmd.none
            else
                state.Match.RuntimeId |> Option.iter MatchRuntime.close

                match tryGetGs () with
                | Some gs ->
                    match userFixture gs with
                    | Some(matchId, _) when
                        match state.Navigation.Current with
                        | Route.Match(requested, _) -> requested = matchId
                        | _ -> false
                        ->
                        let gs = ExecutionRandomness.beginAttempt gs

                        let runtimeId =
                            MatchRuntime.createUserMatch
                                matchId
                                gs
                                state.WorldClock
                                (Some state.SaveSettings)
                                state.Settings.Global.MatchView.HighlightFilter

                        let matchState =
                            { state.Match with
                                RuntimeId = Some runtimeId
                                RequestedMatchId = Some matchId
                                PendingResult = None
                                Error = None
                                Live = None }

                        { state with
                            Mode = InGame(gs, managerEmployment gs)
                            IsProcessing = false
                            Match = matchState },
                        Cmd.ofEffect (fun dispatch -> MatchRuntime.start runtimeId dispatch)
                    | _ ->
                        { state with
                            Match =
                                { state.Match with
                                    Error = Some "The requested fixture is not a pending match for your club today." } },
                        Cmd.none
                | None -> state, Cmd.none

        | UserMatchDone(Ok matchDay) ->

            let nextState = applyDayResult matchDay.DayResult state

            let matchId, _, _ =
                match matchDay.DayResult.PlayedMatches with
                | (fixtureId, f) :: _ -> Some fixtureId, Some f.HomeClubId, Some f.AwayClubId
                | [] -> None, None, None

            let withMatch =
                { nextState with
                    Match =
                        { nextState.Match with
                            RuntimeId = None
                            Live = nextState.Match.Live |> Option.map (fun live -> { live with Status = Finished }) } }

            let openMatchCmd =
                match matchId with
                | Some fixtureId -> Cmd.ofMsg (Navigate(Route.Match(fixtureId, Postmatch)))
                | None -> Cmd.none

            if matchDay.DayResult.SeasonComplete then
                withMatch,
                Cmd.batch
                    [ Cmd.ofMsg (SimMsg AdvanceSeason)
                      openMatchCmd
                      saveCmd state matchDay.DayResult.GameState matchDay.DayResult.WorldClock state.SaveSettings ]
            else
                withMatch,
                Cmd.batch
                    [ saveCmd state matchDay.DayResult.GameState matchDay.DayResult.WorldClock state.SaveSettings
                      openMatchCmd
                      Cmd.ofMsg (SimMsg SimulateAiFixtures) ]

        | UserMatchDone(Error _) ->

            { state with IsProcessing = false }, Cmd.none

        | SimulateAiFixtures ->

            match tryGetGs () with
            | None -> state, Cmd.none
            | Some _ when
                state.IsProcessing
                || state.Match.RuntimeId.IsSome
                || state.Match.PendingResult.IsSome
                ->
                state, Cmd.none
            | Some gs ->
                { state with IsProcessing = true },
                Cmd.OfTask.either
                    (fun () -> Task.Run(fun () -> runAiFixturesWithSave (Some state.SaveSettings) state.WorldClock gs))
                    ()
                    (SimMsg << AiMatchesDone)
                    (fun ex -> SimMsg(AdvanceFailed ex.Message))

        | AiMatchesDone result ->
            match state.Mode with
            | InGame(currentGs, employment) ->
                if
                    result.GameState.CurrentDate.Date = currentGs.CurrentDate.Date
                    && result.GameState.ExecutionSeed = currentGs.ExecutionSeed
                    && result.WorldClock.Revision = state.WorldClock.Revision
                then
                    let nextState =
                        { state with
                            Mode = InGame(result.GameState, employment)
                            IsProcessing = false
                            WorldClock = result.WorldClock }
                        |> applyMatchNotifs result.GameState result.PlayedMatches

                    let continuation =
                        match nextState.AdvanceTarget with
                        | Some target when result.GameState.CurrentDate.Date < target -> Cmd.ofMsg (SimMsg(Advance 1))
                        | _ -> Cmd.none

                    let nextState =
                        match nextState.AdvanceTarget with
                        | Some target when result.GameState.CurrentDate.Date >= target ->
                            { nextState with AdvanceTarget = None }
                        | _ -> nextState

                    nextState,
                    Cmd.batch
                        [ saveCmd state result.GameState result.WorldClock state.SaveSettings
                          continuation ]
                else
                    state, Cmd.none
            | _ -> state, Cmd.none

        | SimulateSeason ->


            if
                state.IsProcessing
                || state.Match.RuntimeId.IsSome
                || state.Match.PendingResult.IsSome
            then

                state, Cmd.none
            else
                match tryGetGs () with
                | None -> state, Cmd.none
                | Some gs ->


                    let gs = ExecutionRandomness.beginAttempt gs

                    let prevSkills =
                        GameState.getUserSquad gs
                        |> List.map (fun p -> p.Id, p.CurrentSkill)
                        |> Map.ofList

                    let prevStatus =
                        GameState.getUserSquad gs |> List.map (fun p -> p.Id, p.Status) |> Map.ofList

                    { state with
                        Mode = InGame(gs, managerEmployment gs)
                        IsProcessing = true
                        PrevUserClubSkills = Some prevSkills
                        PrevUserClubStatus = Some prevStatus },
                    Cmd.OfTask.either
                        (fun () ->
                            Task.Run(fun () ->
                                simulateFullSeasonAndAdvanceWithSave (Some state.SaveSettings) state.WorldClock gs))
                        ()
                        (SimMsg << SeasonAdvanceDone)
                        (fun ex ->
                            eprintfn $"[CRASH] SimulateSeason Error: {ex.ToString()}"
                            SetProcessing false)


        | SeasonAdvanceDone(Ok result) ->



            let nextState = applySeasonResult result state

            match nextState.Mode with
            | InGame(gs, _) -> nextState, saveCmd state gs result.NextSeasonClock state.SaveSettings
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
                match tryGetGs () with
                | None -> state, Cmd.none
                | Some gs ->


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
                        (fun () ->
                            Task.Run(fun () ->
                                advanceToNextSeasonWithSave (Some state.SaveSettings) state.WorldClock gs))
                        ()
                        (SimMsg << SeasonAdvanceDone << Ok)
                        (fun ex ->
                            eprintfn $"[CRASH] AdvanceSeason Error: {ex.ToString()}"
                            SetProcessing false)
