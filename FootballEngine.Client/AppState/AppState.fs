namespace FootballEngine

open System
open Elmish
open FootballEngine.AppMsgs
open FootballEngine.Domain
open AppTypes
open FootballEngine.Settings
open FootballEngine.World
open FootballEngine.World.WorldRunner
open FootballEngine.Data
open FootballEngine.Routing


module AppState =

    // why mutable preloaded: Program.main loads GlobalSettings synchronously before AppBuilder, Elmish init then sees cached value without waiting for async Cmd
    let mutable initialGlobalOverride: GlobalSettings option = None

    // CLI parsing happens in Program.main before AppBuilder starts; the Avalonia App
    // reads it back in OnFrameworkInitializationCompleted.
    let mutable shotsContext: Shots.Context option = None

    // Startup resolves the immutable content profile before Avalonia starts. Keeping the
    // value here only bridges the host lifecycle; runtime consumers read State.ResolvedData.
    let mutable initialResolvedDataOverride: ResolvedDataSet option = None

    let private initialState (resolvedData: ResolvedDataSet option) : State =
        { AppTypes.initialState () with
            ResolvedData = resolvedData }

    let private addLog msg (state: State) =
        { state with
            LogMessages = msg :: state.LogMessages |> List.truncate 30 }

    let private headCoach (clubId: ClubId) (gs: GameState) : Staff option =
        GameState.getStaff clubId gs |> List.tryFind (fun s -> s.Role = HeadCoach)

    let private pushTacticsHistory (gs: GameState) (state: State) : State =
        let state =
            { state with
                SaveStatus = SaveIdle
                PersistenceRequestId = state.PersistenceRequestId + 1UL }

        match
            headCoach gs.UserClubId gs
            |> Option.bind (fun c -> c.Attributes.Coaching.Lineup)
        with
        | None -> state
        | Some lu ->
            { state with
                Tactics =
                    { state.Tactics with
                        History = TacticsSlice.recordDraft lu state.Tactics.History } }

    let private buildNewLineupSlots (formation: Formation) (existing: Lineup option) =
        let newFormationSlots = FormationData.getFormation formation

        match existing with
        | None ->
            newFormationSlots
            |> List.map (fun fs ->
                { Index = fs.Index
                  Role = fs.Role
                  X = fs.X
                  Y = fs.Y
                  PlayerId = None })
        | Some lineup ->
            let mutable remaining =
                lineup.Slots
                |> List.choose (fun s -> s.PlayerId |> Option.map (fun pid -> s.Role, pid))

            newFormationSlots
            |> List.map (fun fs ->
                match remaining |> List.tryFind (fun (role, _) -> role = fs.Role) with
                | Some(role, pid) ->
                    remaining <- remaining |> List.filter (fun x -> x <> (role, pid))

                    { Index = fs.Index
                      Role = fs.Role
                      X = fs.X
                      Y = fs.Y
                      PlayerId = Some pid }
                | None ->
                    { Index = fs.Index
                      Role = fs.Role
                      X = fs.X
                      Y = fs.Y
                      PlayerId = None })

    let private invalidateTransferCache (state: State) =
        { state with
            Transfer =
                { state.Transfer with
                    CachedPlayers = []
                    FilteredPlayers = [] } }

    let initWithGlobal (preloaded: GlobalSettings) (resolvedData: ResolvedDataSet option) () : State * Cmd<Msg> =
        let baseState = initialState resolvedData
        // why override Settings here: initialState uses defaults, but startup may have loaded user GlobalSettings from disk
        let stateWithPreloaded =
            { baseState with
                Settings = initSettingsState preloaded baseState.SaveSettings }

        // why sync Theme here: ensures currentPalette matches preloaded Appearance before first view renders
        try
            Theme.usePalette(Theme.paletteForSettings preloaded.Appearance)
        with _ ->
            ()

        match Localization.setActiveCode preloaded.Appearance.Language with
        | Ok() -> ()
        | Error _ -> ()

        FootballEngine.Client.Match3D.ViewerQualitySelection.select preloaded.MatchView.ViewerQuality
        stateWithPreloaded, Cmd.ofMsg StartupReady

    let init () =
        let resolvedData = initialResolvedDataOverride

        match initialGlobalOverride with
        | Some g -> initWithGlobal g resolvedData ()
        | None -> initialState resolvedData, Cmd.ofMsg StartupReady

    let private routeAllowed (state: State) (route: Route) =
        match state.Mode with
        | Initializing -> route = Route.Loading
        | NoSave -> route = Route.Setup || route = Route.ModEditor
        | InGame(_, _employment) when Shots.Enabled && route = Route.Setup -> true
        | InGame(_, employment) ->
            let isEmployed =
                match employment with
                | Employed _ -> true
                | NotEmployed -> false

            route <> Route.Loading
            && route <> Route.Setup
            && Route.canNavigate isEmployed route
            && (match route, state.Match.RequestedMatchId with
                | Route.Match(id, _), Some active when state.Match.RuntimeId.IsSome || state.Match.PendingResult.IsSome ->
                    id = active
                | _ -> true)

    let private applyRouteContext (route: Route) (state: State) =
        match route with
        | Route.Inbox(InboxRoute messageId) ->
            { state with
                Inbox =
                    { state.Inbox with
                        SelectedMessageId = messageId } }
        | Route.Squad(SquadRoute.Detail playerId) ->
            { state with
                Squad =
                    { state.Squad with
                        SelectedPlayer = Some playerId } }
        | Route.Squad _ ->
            { state with
                Squad =
                    { state.Squad with
                        SelectedPlayer = None } }
        | Route.Training(TrainingRoute.Player playerId) ->
            { state with
                Training = { SelectedPlayer = Some playerId } }
        | Route.Training _ ->
            { state with
                Training = { SelectedPlayer = None } }
        | Route.Transfers TransferRoute.Market ->
            { state with
                Transfer =
                    { state.Transfer with
                        ActiveTab = MarketSearch
                        SelectedPlayerId = None
                        ActiveNegotiationId = None } }
        | Route.Transfers(TransferRoute.Player playerId) ->
            { state with
                Transfer =
                    { state.Transfer with
                        SelectedPlayerId = Some playerId } }
        | Route.Transfers TransferRoute.Watchlist ->
            { state with
                Transfer =
                    { state.Transfer with
                        ActiveTab = MyWatchlist } }
        | Route.Transfers TransferRoute.IncomingOffers ->
            { state with
                Transfer =
                    { state.Transfer with
                        ActiveTab = AppTypes.IncomingOffers } }
        | Route.Transfers TransferRoute.Negotiations ->
            { state with
                Transfer =
                    { state.Transfer with
                        ActiveTab = OutgoingOffers } }
        | Route.Transfers(TransferRoute.Negotiation negotiationId)
        | Route.Transfers(TransferRoute.Offer negotiationId) ->
            let selectedPlayerId =
                match state.Mode with
                | InGame(gs, _) -> gs.PendingNegotiations |> Map.tryFind negotiationId |> Option.map _.PlayerId
                | _ -> None

            { state with
                Transfer =
                    { state.Transfer with
                        ActiveTab = OutgoingOffers
                        ActiveNegotiationId = Some negotiationId
                        SelectedPlayerId = selectedPlayerId } }
        | Route.Transfers TransferRoute.History ->
            { state with
                Transfer =
                    { state.Transfer with
                        ActiveTab = TransferHistory } }
        | Route.Finances(FinanceRoute.Operation operationId) ->
            { state with
                Finance =
                    { state.Finance with
                        SelectedOperationId = Some operationId } }
        | Route.Finances FinanceRoute.Overview ->
            { state with
                Finance =
                    { state.Finance with
                        SelectedOperationId = None } }
        | Route.Calendar ->
            { state with
                Calendar =
                    { state.Calendar with
                        SelectedFixtureId = None } }
        | Route.Settings(SettingsRoute.Category category) ->
            { state with
                Settings = SettingsSlice.selectCategory category state.Settings }
        | Route.Settings SettingsRoute.Overview ->
            { state with
                Settings = SettingsSlice.selectCategory SettingsCategory.All state.Settings }
        | _ -> state

    let private navigateTo (route: Route) (state: State) : State =
        if not (routeAllowed state route) || state.Navigation.Current = route then
            state
        else
            { state with
                Navigation =
                    { Current = route
                      Back = state.Navigation.Current :: state.Navigation.Back
                      Forward = [] } }
            |> applyRouteContext route

    let private replaceRoute (route: Route) (state: State) : State =
        { state with
            Navigation =
                { Current = route
                  Back = []
                  Forward = [] } }

    let private goBack (state: State) : State =
        match state.Navigation.Back with
        | previous :: rest when routeAllowed state previous ->
            { state with
                Navigation =
                    { Current = previous
                      Back = rest
                      Forward = state.Navigation.Current :: state.Navigation.Forward } }
            |> applyRouteContext previous
        | _ -> state

    let private goForward (state: State) : State =
        match state.Navigation.Forward with
        | next :: rest when routeAllowed state next ->
            { state with
                Navigation =
                    { Current = next
                      Back = state.Navigation.Current :: state.Navigation.Back
                      Forward = rest } }
            |> applyRouteContext next
        | _ -> state

    let private closeMatchRuntimeWhenLeaving (fromRoute: Route) (toRoute: Route) (state: State) =
        match fromRoute, toRoute with
        | Route.Match _, Route.Match _ -> state
        | Route.Match _, _ when
            state.Match.RequestedMatchId.IsSome
            && (state.Match.RuntimeId.IsSome || state.Match.PendingResult.IsSome)
            ->
            state
        | Route.Match _, _ ->
            state.Match.RuntimeId |> Option.iter MatchRuntime.close

            { state with
                Match = MatchSlice.update LiveMsg.Close state.Match }
        | _ -> state

    let private pauseMatchWhenLeaving fromRoute toRoute (state: State) =
        match fromRoute, toRoute, state.Match.Live with
        | Route.Match _, Route.Match _, _ -> Cmd.none
        | Route.Match _, _, Some live when state.Match.RuntimeId.IsSome ->
            match live.Status with
            | FootballEngine.LiveMatch.Live _
            | FootballEngine.LiveMatch.Cruising -> Cmd.ofMsg (LiveMsg LiveMsg.TogglePause)
            | _ -> Cmd.none
        | _ -> Cmd.none

    let private listSlotsCmd (requestId: uint64) : Cmd<Msg> =
        Cmd.OfTask.either
            (fun () -> Db.listSaveSlotsAsync ())
            ()
            (fun result -> AppMsgs.PersistenceMsg(AppMsgs.SaveSlotsLoaded(requestId, result)))
            (fun ex -> AppMsgs.PersistenceMsg(AppMsgs.SaveSlotsLoaded(requestId, Error ex.Message)))

    let private loadSlotCmd (requestId: uint64) (slot: string) : Cmd<Msg> =
        Cmd.OfTask.either
            (fun () -> Db.loadEnvelope slot)
            ()
            (fun result -> AppMsgs.GameLoaded(requestId, slot, result))
            (fun ex -> AppMsgs.GameLoaded(requestId, slot, Error(Corrupt(slot, ex.Message))))

    let update (msg: Msg) (state: State) : State * Cmd<Msg> =
        let withGame (f: GameState -> State * Cmd<Msg>) =
            match state.Mode with
            | InGame(gs, _) -> f gs
            | _ -> state, Cmd.none

        match msg with

        | StartupReady ->
            { state with
                Mode = NoSave
                Navigation =
                    { Current = Route.Setup
                      Back = []
                      Forward = [] }
                IsProcessing = false
                Setup = initSetupState
                ModLoadErrors =
                    if state.ResolvedData.IsSome then
                        []
                    else
                        [ "World data is unavailable. Check the installed data packages before starting a career." ] },
            Cmd.none

        | SetupMsg OpenLoadGame ->
            let requestId = state.PersistenceRequestId + 1UL

            { state with
                Mode = NoSave
                Navigation = replaceRoute Route.Setup state |> fun next -> next.Navigation
                IsProcessing = false
                PersistenceRequestId = requestId
                Setup =
                    { state.Setup with
                        Step = LoadGame
                        SaveSlots = []
                        LoadState = LoadingSlots requestId } },
            listSlotsCmd requestId

        | SetupMsg CloseLoadGame -> UpdateSetup.handle CloseLoadGame state

        | SetupMsg CancelSetup -> UpdateSetup.handle CancelSetup state

        | SetupMsg m -> UpdateSetup.handle m state

        | PersistenceMsg ListSaveSlots ->
            let requestId = state.PersistenceRequestId + 1UL

            { state with
                PersistenceRequestId = requestId
                Setup =
                    { state.Setup with
                        Step = LoadGame
                        LoadState = LoadingSlots requestId } },
            listSlotsCmd requestId

        | PersistenceMsg(SaveSlotsLoaded(requestId, Ok slots)) ->
            match state.Setup.LoadState with
            | LoadingSlots current when current = requestId ->
                { state with
                    Setup =
                        { state.Setup with
                            SaveSlots = slots
                            LoadState = SlotsReady slots } },
                Cmd.none
            | _ -> state, Cmd.none

        | PersistenceMsg(SaveSlotsLoaded(requestId, Error error)) ->
            match state.Setup.LoadState with
            | LoadingSlots current when current = requestId ->
                { state with
                    Setup =
                        { state.Setup with
                            LoadState = SlotsLoadFailed error } },
                Cmd.none
            | _ -> state, Cmd.none

        | PersistenceMsg(LoadSlot slot) ->
            let knownSlot =
                state.Setup.SaveSlots |> List.exists (fun save -> save.SlotId = slot)

            if not knownSlot then
                { state with
                    Setup =
                        { state.Setup with
                            LoadState = SlotsLoadFailed $"Save slot '{slot}' is not in the current slot list." } },
                Cmd.none
            else
                let requestId = state.PersistenceRequestId + 1UL

                { state with
                    Mode = NoSave
                    Navigation = replaceRoute Route.Setup state |> fun next -> next.Navigation
                    IsProcessing = true
                    PersistenceRequestId = requestId
                    Setup =
                        { state.Setup with
                            Step = LoadGame
                            LoadState = LoadingSlot(slot, requestId) } },
                loadSlotCmd requestId slot

        | GameLoaded(requestId, slot, result) ->
            match state.Setup.LoadState with
            | LoadingSlot(currentSlot, currentRequest) when currentSlot = slot && currentRequest = requestId ->
                match result with
                | Ok loaded ->
                    let gs = loaded.State
                    let clock = loaded.WorldClock
                    let saveSettings = loaded.SaveSettings
                    let resolvedData = loaded.ResolvedData

                    match Localization.setActiveCode state.Settings.Global.Appearance.Language with
                    | Ok() -> ()
                    | Error _ -> ()

                    let nextState =
                        { state with
                            Mode = InGame(gs, managerEmployment gs)
                            Navigation =
                                { Current = Route.Home
                                  Back = []
                                  Forward = [] }
                            IsProcessing = false
                            WorldClock = clock
                            ResolvedData = Some resolvedData
                            SaveSettings = saveSettings
                            Settings = initSettingsState state.Settings.Global saveSettings
                            ActiveSlot = Some slot
                            SaveStatus = Saved gs.CurrentDate
                            AdvanceTarget = None
                            Tactics =
                                { DraggedPlayer = None
                                  SelectedSlot = None
                                  History = [] }
                            Setup = initSetupState
                            ModLoadErrors = [] }

                    (if loaded.RecoveredFromBackup then
                         nextState
                         |> pushNotification
                             Material.Icons.MaterialIconKind.AlertCircleOutline
                             "Save recovered"
                             "The primary save was damaged. The previous backup was restored; the damaged file was kept."
                     else
                         nextState),
                    Cmd.none
                | Error error ->
                    { state with
                        Mode = NoSave
                        Navigation =
                            { Current = Route.Setup
                              Back = []
                              Forward = [] }
                        IsProcessing = false
                        Setup =
                            { state.Setup with
                                LoadState = LoadFailed(slot, error) } },
                    Cmd.none
            | _ -> state, Cmd.none

        | PersistenceMsg SaveCurrent ->
            match state.Mode with
            | InGame _ when state.Match.RuntimeId.IsSome || state.Match.PendingResult.IsSome ->
                state
                |> pushNotification
                    Material.Icons.MaterialIconKind.InformationOutline
                    (Localization.resolve TranslationKey.ManualSave [])
                    (Localization.resolve TranslationKey.SaveAfterMatch []),
                Cmd.none
            | InGame(gs, _) when not state.IsProcessing ->
                let requestId = state.PersistenceRequestId + 1UL

                let nextState =
                    { state with
                        PersistenceRequestId = requestId
                        SaveStatus = Saving requestId }

                nextState, SimHelpers.saveCmdWithRequest nextState requestId gs state.WorldClock state.SaveSettings
            | _ -> state, Cmd.none

        | PersistenceMsg(SaveCompleted(requestId, slot, revision, result)) ->
            match state.SaveStatus, state.ActiveSlot with
            | Saving currentRequest, Some activeSlot when
                currentRequest = requestId
                && activeSlot = slot
                && state.WorldClock.Revision = revision
                ->
                match result with
                | Ok() ->
                    { state with
                        SaveStatus = Saved DateTime.UtcNow },
                    Cmd.none
                | Error error ->
                    { state with
                        SaveStatus = SaveFailed error }
                    |> pushNotification Material.Icons.MaterialIconKind.AlertCircleOutline "Save failed" error,
                    Cmd.none
            | _ -> state, Cmd.none

        | ModEditorMsg m ->
            let nextEditorState, cmd = UpdateModEditor.update m state.ModEditor

            // Publishing with "enable" ends in a playable world: the package joins the active profile
            // and the content is reloaded, so the next career already uses it.
            let enableCmd =
                match m, nextEditorState.PublishStatus with
                | ModEditorMsg.PublishMod true, ModEditorTypes.Published(_, true) ->
                    let modId = nextEditorState.Document.Manifest.Id
                    // An empty profile means "everything installed"; listing only the new mod would drop the built-in world.
                    let installed = nextEditorState.Library |> List.map _.ModId

                    Cmd.batch
                        [ Cmd.ofMsg (
                              SettingsMsg(
                                  UpdateGlobal(fun gs ->
                                      let data = gs.Data

                                      { gs with
                                          Data =
                                              { data with
                                                  ActiveMods =
                                                      (if data.ActiveMods.IsEmpty then installed else data.ActiveMods)
                                                      @ [ modId ]
                                                      |> List.distinct
                                                  ModOrder =
                                                      if List.contains modId data.ModOrder then
                                                          data.ModOrder
                                                      else
                                                          data.ModOrder @ [ modId ] } })
                              )
                          )
                          Cmd.ofMsg ReloadMods ]
                | _ -> Cmd.none

            { state with
                ModEditor = nextEditorState },
            Cmd.batch [ Cmd.map ModEditorMsg cmd; enableCmd ]

        | SimMsg m ->
            let nextState, cmd = UpdateSim.handle m state

            let invalidated =
                match m with
                | AdvanceDone _
                | SeasonAdvanceDone _
                | UserMatchDone _ -> invalidateTransferCache nextState
                | _ -> nextState

            invalidated, cmd

        | TransferMsg m -> UpdateTransfer.handle m state

        | FinanceMsg m ->
            { state with
                Finance = FinanceSlice.update m state.Finance },
            Cmd.none

        | CalendarMsg m ->
            { state with
                Calendar = CalendarSlice.update m state.Calendar },
            Cmd.none

        | NewsMsg(SetNewsFilter filter) -> { state with News = { Filter = filter } }, Cmd.none

        | CareerMsg msg ->
            match state.Mode, msg with
            | InGame(gs, _), DeclineJobOffer offerId ->
                let gs = FootballEngine.World.Pulse.JobMarket.decline offerId gs

                { state with
                    Mode = InGame(gs, managerEmployment gs) },
                Cmd.none
            | InGame(gs, _), AcceptJobOffer offerId ->
                match
                    FootballEngine.World.Pulse.JobMarket.accept
                        WorldPulseConfig.defaultWorldPulse
                        gs.CurrentDate
                        offerId
                        gs
                with
                | Ok hired ->
                    let clubName = hired.Clubs[hired.UserClubId].Name

                    { state with
                        Mode = InGame(hired, managerEmployment hired)
                        SelectedLeagueId =
                            SimHelpers.leagueOf hired hired.UserClubId
                            |> Option.defaultValue state.SelectedLeagueId
                        Navigation =
                            { Current = Route.Home
                              Back = []
                              Forward = [] } }
                    |> pushNotification
                        FootballEngine.Icons.NotificationIcons.newCareer
                        (Localization.resolve TranslationKey.JobAcceptedTitle [ "club", clubName ])
                        (Localization.resolve TranslationKey.JobAcceptedBody [ "club", clubName ]),
                    Cmd.none
                | Error error -> state |> pushNotification FootballEngine.Icons.NotificationIcons.error error "", Cmd.none
            | _ -> state, Cmd.none

        | NotificationMsg nm ->
            match nm with
            | DismissNotification id ->
                { state with
                    Notifications = state.Notifications |> List.filter (fun n -> n.Id <> id) },
                Cmd.none
            | DismissAll -> { state with Notifications = [] }, Cmd.none
            | PushNotification note ->
                { state with
                    Notifications = note :: state.Notifications |> List.truncate 20
                    NextNotificationId = state.NextNotificationId + 1 },
                Cmd.none

        | SquadMsg m ->
            { state with
                Squad = SquadSlice.update m state.Squad },
            Cmd.none

        | TacticsMsg(TacticsMsg.SelectSlot slot) ->
            { state with
                Tactics = TacticsSlice.update (TacticsMsg.SelectSlot slot) state.Tactics },
            Cmd.none

        | TacticsMsg(TacticsMsg.SetFormation formation) ->
            withGame (fun gs ->
                let state = pushTacticsHistory gs state

                let newGs =
                    GameState.updateLineup
                        gs.UserClubId
                        (fun currentLineupOpt ->
                            let lineup =
                                match currentLineupOpt with
                                | Some l -> l
                                | None ->
                                    { Formation = formation
                                      Tactics = TeamTactics.Balanced
                                      Instructions = Some TacticalInstructions.defaultInstructions
                                      Slots = [] }

                            Some
                                { Formation = formation
                                  Tactics = lineup.Tactics
                                  Instructions = lineup.Instructions
                                  Slots = buildNewLineupSlots formation currentLineupOpt })
                        gs

                { state with
                    Mode = InGame(newGs, managerEmployment newGs)
                    SelectedTactics = formation },
                Cmd.none)

        | TacticsMsg(TacticsMsg.SetTeamTactics tactics) ->
            withGame (fun gs ->
                let state = pushTacticsHistory gs state

                let newGs =
                    GameState.updateLineup gs.UserClubId (Option.map (fun l -> { l with Tactics = tactics })) gs

                { state with
                    Mode = InGame(newGs, managerEmployment newGs) },
                Cmd.none)

        | TacticsMsg(TacticsMsg.SetInstruction f) ->
            withGame (fun gs ->
                let state = pushTacticsHistory gs state

                let newGs =
                    GameState.updateLineup
                        gs.UserClubId
                        (Option.map (fun lineup ->
                            let instr =
                                lineup.Instructions
                                |> Option.defaultValue TacticalInstructions.defaultInstructions

                            { lineup with
                                Instructions = Some(f instr) }))
                        gs

                { state with
                    Mode = InGame(newGs, managerEmployment newGs) },
                Cmd.none)

        | TacticsMsg(TacticsMsg.DropPlayerInSlot(targetIdx, pId)) ->
            withGame (fun gs ->
                let state = pushTacticsHistory gs state

                let swapPlayer (idx: int) (pid: PlayerId) (lineup: Lineup) : Lineup =
                    let outgoing =
                        lineup.Slots
                        |> List.tryFind (fun slot -> slot.Index = idx)
                        |> Option.bind _.PlayerId

                    let updatedSlots =
                        lineup.Slots
                        |> List.map (fun slot ->
                            if slot.Index = idx then
                                { slot with PlayerId = Some pid }
                            elif slot.PlayerId = Some pid then
                                { slot with PlayerId = outgoing }
                            else
                                slot)

                    { lineup with Slots = updatedSlots }

                let newGs =
                    GameState.updateLineup
                        gs.UserClubId
                        (fun currentLineupOpt ->
                            let defaultLineup =
                                { Formation = state.SelectedTactics
                                  Tactics = TeamTactics.Balanced
                                  Instructions = Some TacticalInstructions.defaultInstructions
                                  Slots =
                                    FormationData.getFormation state.SelectedTactics
                                    |> List.map (fun fs ->
                                        { Index = fs.Index
                                          Role = fs.Role
                                          X = fs.X
                                          Y = fs.Y
                                          PlayerId = None })
                                    |> List.sortBy _.Index }

                            let current = currentLineupOpt |> Option.defaultValue defaultLineup
                            Some(swapPlayer targetIdx pId current))
                        gs

                { state with
                    Mode = InGame(newGs, managerEmployment newGs)
                    Tactics =
                        { state.Tactics with
                            DraggedPlayer = None
                            SelectedSlot = None } }
                |> addLog $"Swap made: Slot {targetIdx}",
                Cmd.none)

        | TacticsMsg(TacticsMsg.StartDrag id) ->
            { state with
                Tactics =
                    { state.Tactics with
                        DraggedPlayer = Some id } },
            Cmd.none

        | TacticsMsg(TacticsMsg.EndDrag) ->
            { state with
                Tactics =
                    { state.Tactics with
                        DraggedPlayer = None } },
            Cmd.none

        | TacticsMsg TacticsMsg.QuickPick ->
            withGame (fun gs ->
                let state = pushTacticsHistory gs state
                let squad = GameState.getSquad gs.UserClubId gs |> List.filter (fun player -> player.Status = Available)

                let formation =
                    headCoach gs.UserClubId gs
                    |> Option.bind (fun c -> c.Attributes.Coaching.Lineup)
                    |> Option.map _.Formation
                    |> Option.defaultValue state.SelectedTactics

                let slots = FormationData.getFormation formation
                let picks = TacticalSuitability.quickPick slots squad

                let newGs =
                    GameState.updateLineup
                        gs.UserClubId
                        (fun currentOpt ->
                            let current =
                                currentOpt
                                |> Option.defaultValue
                                    { Formation = formation
                                      Tactics = TeamTactics.Balanced
                                      Instructions = Some TacticalInstructions.defaultInstructions
                                      Slots = [] }

                            let filled =
                                slots
                                |> List.map (fun fs ->
                                    { Index = fs.Index
                                      Role = fs.Role
                                      X = fs.X
                                      Y = fs.Y
                                      PlayerId = picks.TryFind fs.Index })

                            Some
                                { current with
                                    Formation = formation
                                    Slots = filled })
                        gs

                { state with
                    Mode = InGame(newGs, managerEmployment newGs)
                    SelectedTactics = formation }
                |> addLog "Quick Pick: best XI by suitability and sharpness",
                Cmd.none)

        | TacticsMsg TacticsMsg.UndoTactics ->
            withGame (fun gs ->
                match TacticsSlice.undoDraft state.Tactics.History with
                | None -> state, Cmd.none
                | Some(head, tail) ->
                    let newGs = GameState.updateLineup gs.UserClubId (fun _ -> Some head) gs

                    { state with
                        Mode = InGame(newGs, managerEmployment newGs)
                        SaveStatus = SaveIdle
                        PersistenceRequestId = state.PersistenceRequestId + 1UL
                        Tactics = { state.Tactics with History = tail } },
                    Cmd.none)

        | TrainingMsg(TrainingMsg.SelectPlayer id) ->
            { state with
                Training =
                    { state.Training with
                        SelectedPlayer = Some id } },
            Cmd.none

        | TrainingMsg(TrainingMsg.ClearSelection) ->
            { state with
                Training =
                    { state.Training with
                        SelectedPlayer = None } },
            Cmd.none

        | TrainingMsg(TrainingMsg.SetPlan(playerId, plan)) ->
            withGame (fun gs ->
                let newGs =
                    { gs with
                        Players =
                            gs.Players
                            |> Map.change playerId (Option.map (fun p -> { p with TrainingPlan = plan })) }

                { state with
                    Mode = InGame(newGs, managerEmployment newGs) },
                SimHelpers.saveCmdForState state newGs state.WorldClock state.SaveSettings)

        | TrainingMsg(TrainingMsg.SetTeamEmphasis emphasis) ->
            withGame (fun gs ->
                let newGs = { gs with TeamEmphasis = emphasis }

                { state with
                    Mode = InGame(newGs, managerEmployment newGs) },
                SimHelpers.saveCmdForState state newGs state.WorldClock state.SaveSettings)

        | TrainingMsg(TrainingMsg.BulkApply rule) ->
            withGame (fun gs ->
                let squad = GameState.getSquad gs.UserClubId gs

                let starters =
                    GameState.headCoach gs.UserClubId gs
                    |> Option.bind (fun c -> c.Attributes.Coaching.Lineup)
                    |> Option.map (fun lu -> lu.Slots |> List.choose _.PlayerId |> Set.ofList)
                    |> Option.defaultValue Set.empty

                let decide (p: Player) : IndividualPlan option =
                    match rule with
                    | U21Develop when Player.age gs.CurrentDate p <= 21 ->
                        Some(TrainingPlans.defaultPlanFor p.Position (Player.age gs.CurrentDate p))
                    | RestTired when p.Condition < 65 -> Some Rest
                    | RestStarters when starters.Contains p.Id -> Some Rest
                    | AllPerform -> Some Perform
                    | _ -> None

                let newPlayers =
                    squad
                    |> List.choose (fun p -> decide p |> Option.map (fun plan -> p.Id, { p with TrainingPlan = plan }))
                    |> List.fold (fun acc (pid, p) -> acc |> Map.add pid p) gs.Players

                let newGs = { gs with Players = newPlayers }

                { state with
                    Mode = InGame(newGs, managerEmployment newGs) },
                SimHelpers.saveCmdForState state newGs state.WorldClock state.SaveSettings)

        | InboxMsg(InboxMsg.MarkAsRead messageId) ->
            withGame (fun gs ->
                let newGs = GameState.markMessageAsRead messageId gs

                { state with
                    Mode = InGame(newGs, managerEmployment newGs) },
                SimHelpers.saveCmdForState state newGs state.WorldClock state.SaveSettings)

        | InboxMsg(InboxMsg.MarkActionTaken messageId) ->
            withGame (fun gs ->
                let newGs = GameState.markMessageActionTaken messageId gs

                { state with
                    Mode = InGame(newGs, managerEmployment newGs) },
                SimHelpers.saveCmdForState state newGs state.WorldClock state.SaveSettings)

        | InboxMsg(InboxMsg.SetRequiresActionOnly value) ->
            { state with
                Inbox = InboxSlice.update (InboxMsg.SetRequiresActionOnly value) state.Inbox },
            Cmd.none

        | InboxMsg(InboxMsg.SetUnreadOnly value) ->
            { state with
                Inbox = InboxSlice.update (InboxMsg.SetUnreadOnly value) state.Inbox },
            Cmd.none

        | InboxMsg(InboxMsg.SetCategoryFilter category) ->
            { state with
                Inbox = InboxSlice.update (InboxMsg.SetCategoryFilter category) state.Inbox },
            Cmd.none

        | InboxMsg InboxMsg.ClearSelection ->
            { state with
                Inbox = InboxSlice.update InboxMsg.ClearSelection state.Inbox },
            Cmd.none

        | InboxMsg(InboxMsg.SelectMessage messageId) ->
            withGame (fun gs ->
                let newGs = GameState.markMessageAsRead messageId gs

                let selected =
                    { state with
                        Mode = InGame(newGs, managerEmployment newGs)
                        Inbox = InboxSlice.update (InboxMsg.SelectMessage messageId) state.Inbox }

                let navigated = navigateTo (Route.Inbox(InboxRoute(Some messageId))) selected
                navigated, SimHelpers.saveCmdForState state newGs state.WorldClock state.SaveSettings)

        | LiveMsg(LiveMsg.RuntimePublicationAvailable runtimeId) ->
            // One wake drains everything published since the last one, so the view is rebuilt once per batch
            // rather than once per publication.
            let rec drain budget state commands =
                match state.Match.RuntimeId with
                // A skip publishes as fast as the worker runs; the budget hands the thread back to rendering and
                // continues on the next message instead of draining forever.
                | Some activeRuntime when activeRuntime = runtimeId && budget = 0 ->
                    state, Cmd.batch (Cmd.ofMsg (LiveMsg(LiveMsg.RuntimePublicationAvailable runtimeId)) :: commands)
                | Some activeRuntime when activeRuntime = runtimeId ->
                    match MatchRuntime.tryTake runtimeId with
                    | None -> state, Cmd.batch commands
                    | Some publication ->
                        match publication with
                        | MatchRuntimePublication.Connected(sessionId, fixture, revision, source) ->
                            drain
                                (budget - 1)
                                { state with
                                    Match =
                                        MatchSlice.update
                                            (LiveMsg.SessionConnected(sessionId, fixture, revision, source))
                                            state.Match }
                                commands
                        | MatchRuntimePublication.CommandAccepted(commandId, point) ->
                            drain
                                (budget - 1)
                                { state with
                                    Match = MatchSlice.update (LiveMsg.CommandAccepted(commandId, point)) state.Match }
                                commands
                        | MatchRuntimePublication.CommandApplied(commandId, revision, appliedAt) ->
                            drain
                                (budget - 1)
                                { state with
                                    Match =
                                        MatchSlice.update
                                            (LiveMsg.CommandApplied(commandId, revision, appliedAt))
                                            state.Match }
                                commands
                        | MatchRuntimePublication.CommandRejected(commandId, rejection) ->
                            drain
                                (budget - 1)
                                { state with
                                    Match = MatchSlice.update (LiveMsg.CommandRejected(commandId, rejection)) state.Match }
                                commands
                        | MatchRuntimePublication.StatusChanged status ->
                            let nextState =
                                { state with
                                    Match = MatchSlice.update (LiveMsg.RuntimeStatusChanged status) state.Match }

                            let pause =
                                match nextState.Navigation.Current, status with
                                | Route.Match _, _ -> Cmd.none
                                | _, FootballEngine.LiveMatch.Live _
                                | _, FootballEngine.LiveMatch.Cruising -> Cmd.ofMsg (LiveMsg LiveMsg.TogglePause)
                                | _ -> Cmd.none

                            drain (budget - 1) nextState (pause :: commands)
                        | MatchRuntimePublication.Semantic semantic ->
                            drain (budget - 1) { state with Match = MatchSlice.applySemantic semantic state.Match } commands
                        | MatchRuntimePublication.Failed error ->
                            MatchRuntime.close runtimeId

                            { state with
                                IsProcessing = false
                                Match = MatchSlice.applyFailed error state.Match },
                            Cmd.batch commands
                        | MatchRuntimePublication.Finished result ->
                            MatchRuntime.close runtimeId

                            { state with
                                Match =
                                    { MatchSlice.applyFinished runtimeId result state.Match with
                                        RuntimeId = None
                                        PendingResult = Some result } },
                            Cmd.batch commands
                | _ -> state, Cmd.batch commands

            drain 64 state []

        | LiveMsg LiveMsg.ConfirmFinish ->
            match state.Match.PendingResult with
            | None -> state, Cmd.none
            | Some result ->
                let runtimeId = state.Match.RuntimeId |> Option.defaultValue (MatchRuntimeId 0UL)

                let commit () =
                    let isReplay =
                        state.Match.Live
                        |> Option.exists (fun live ->
                            match live.Source with
                            | FootballEngine.LiveMatch.RecordedReplay -> true
                            | FootballEngine.LiveMatch.LiveSession -> false)

                    if isReplay then
                        MatchRuntime.close runtimeId

                        { state with
                            Match =
                                state.Match
                                |> MatchSlice.applyFinished runtimeId result
                                |> fun matchState ->
                                    { matchState with
                                        RuntimeId = None
                                        PendingResult = None } },
                        Cmd.none
                    else
                        match state.Mode with
                        | InGame(gs, _) ->
                            let balance =
                                FootballEngine.Core.Match.WeightMergeSettings.mergeWithOverridesSave (
                                    Some state.SaveSettings
                                )

                            match commitUserMatchJobWithBalance balance state.WorldClock gs result with
                            | Error error ->
                                MatchRuntime.close runtimeId

                                { state with
                                    IsProcessing = false
                                    Match = MatchSlice.applyFailed error state.Match },
                                Cmd.none
                            | Ok matchDay ->
                                let committed, cmd = UpdateSim.handle (UserMatchDone(Ok matchDay)) state

                                { committed with
                                    Match =
                                        MatchSlice.applyFinished runtimeId result committed.Match
                                        |> fun matchState -> { matchState with PendingResult = None } }
                                |> invalidateTransferCache,
                                cmd
                        | _ ->
                            MatchRuntime.close runtimeId
                            state, Cmd.none

                commit ()

        | LiveMsg(LiveMsg.OpenReplay) ->
            match state.Mode, state.Navigation.Current with
            | InGame(gs, _), Route.Match(matchId, _) ->
                state.Match.RuntimeId |> Option.iter MatchRuntime.close

                let runtimeId =
                    MatchRuntime.createReplay matchId gs state.WorldClock (Some state.SaveSettings)

                { state with
                    IsProcessing = false
                    Match =
                        { MatchSlice.clearState with
                            RuntimeId = Some runtimeId
                            RequestedMatchId = Some matchId
                            Live = None } },
                Cmd.ofEffect (fun dispatch -> MatchRuntime.start runtimeId dispatch)
            | _ -> state, Cmd.none

        | LiveMsg LiveMsg.TogglePause ->
            match state.Match.Live with
            | None -> state, Cmd.none
            | Some live ->
                let payload =
                    match live.Status with
                    | FootballEngine.LiveMatch.Paused
                    | FootballEngine.LiveMatch.KeyMomentStop _
                    | FootballEngine.LiveMatch.HalfTime -> FootballEngine.LiveMatch.ResumeMatch
                    | _ -> FootballEngine.LiveMatch.PauseMatch

                let command: FootballEngine.LiveMatch.LiveMatchCommand =
                    { CommandId = FootballEngine.LiveMatch.MatchCommandId live.NextCommandId
                      SessionId = live.SessionId
                      ExpectedRevision = live.Revision
                      IssuedAt = live.Clock.Elapsed
                      Payload = payload }

                state.Match.RuntimeId
                |> Option.iter (fun runtimeId -> MatchRuntime.submit runtimeId command)

                { state with
                    Match = MatchSlice.update (LiveMsg.CommandSubmitted command) state.Match },
                Cmd.none

        | LiveMsg(LiveMsg.CommandSubmitted command) ->
            state.Match.RuntimeId
            |> Option.iter (fun runtimeId -> MatchRuntime.submit runtimeId command)

            { state with
                Match = MatchSlice.update (LiveMsg.CommandSubmitted command) state.Match },
            Cmd.none

        | LiveMsg(LiveMsg.Close) ->
            state.Match.RuntimeId |> Option.iter MatchRuntime.close

            let nextState =
                { state with
                    Match = MatchSlice.update LiveMsg.Close state.Match }

            let backed = goBack nextState

            if backed.Navigation.Current = state.Navigation.Current then
                replaceRoute Route.Home backed, Cmd.none
            else
                backed, Cmd.none

        | LiveMsg m ->
            { state with
                Match = MatchSlice.update m state.Match },
            Cmd.none

        | SettingsMsg m ->
            match m with
            | SetSearch q ->
                { state with
                    Settings = SettingsSlice.setSearch q state.Settings },
                Cmd.none
            | SelectCategory cat ->
                { state with
                    Settings = SettingsSlice.selectCategory cat state.Settings },
                Cmd.none
            | UpdateGlobal f ->
                let next = SettingsSlice.updateGlobal f state.Settings
                // why sync palette: theme changes without restart via paletteForSettings
                try
                    Theme.usePalette(Theme.paletteForSettings next.Global.Appearance)
                with _ ->
                    ()

                match Localization.setActiveCode next.Global.Appearance.Language with
                | Ok() -> ()
                | Error _ -> ()

                FootballEngine.Client.Match3D.ViewerQualitySelection.select next.Global.MatchView.ViewerQuality

                // DataSettings changes apply to the next career; an active career keeps its DataLock.
                let dataChanged = next.Global.Data <> state.Settings.Global.Data
                let saveCmd = if dataChanged then SettingsSlice.saveCmd next else Cmd.none
                { state with Settings = next }, saveCmd
            | UpdateSave f ->
                let next = SettingsSlice.updateSave f state.Settings

                let perSaveCmd =
                    match state.Mode with
                    | InGame(gs, _) -> SimHelpers.saveCmdForState state gs state.WorldClock next.Save
                    | _ -> Cmd.none

                { state with
                    Settings = next
                    SaveSettings = next.Save },
                perSaveCmd
            | ApplyPreset preset ->
                let next = SettingsSlice.applyPreset preset state.Settings

                let perSaveCmd =
                    match state.Mode with
                    | InGame(gs, _) -> SimHelpers.saveCmdForState state gs state.WorldClock next.Save
                    | _ -> Cmd.none

                { state with
                    Settings = next
                    SaveSettings = next.Save },
                perSaveCmd
            | ResetCategory cat ->
                let next = SettingsSlice.resetCategory cat state.Settings
                // why sync palette: reset may affect Appearance, so palette must follow
                try
                    Theme.usePalette(Theme.paletteForSettings next.Global.Appearance)
                with _ ->
                    ()

                let perSaveCmd =
                    match state.Mode with
                    | InGame(gs, _) -> SimHelpers.saveCmdForState state gs state.WorldClock next.Save
                    | _ -> Cmd.none

                { state with
                    Settings = next
                    SaveSettings = next.Save },
                Cmd.batch [ perSaveCmd; SettingsSlice.saveCmd next ]
            | ResetAll ->
                let next = SettingsSlice.resetAll state.Settings
                // why sync palette: ResetAll restores default Appearance, so theme must reset without restart
                try
                    Theme.usePalette(Theme.paletteForSettings next.Global.Appearance)
                with _ ->
                    ()

                let perSaveCmd =
                    match state.Mode with
                    | InGame(gs, _) -> SimHelpers.saveCmdForState state gs state.WorldClock next.Save
                    | _ -> Cmd.none

                { state with
                    Settings = next
                    SaveSettings = next.Save },
                Cmd.batch [ perSaveCmd; SettingsSlice.saveCmd next ]
            | SaveSettings ->
                let globalCmd = SettingsSlice.saveCmd state.Settings

                let perSaveCmd =
                    match state.Mode with
                    | InGame(gs, _) -> SimHelpers.saveCmdForState state gs state.WorldClock state.Settings.Save
                    | _ -> Cmd.none

                { state with
                    Settings = SettingsSlice.markClean state.Settings },
                Cmd.batch [ globalCmd; perSaveCmd ]
            | LoadSettings -> state, SettingsSlice.loadCmd
            | Undo ->
                let next = SettingsSlice.undo state.Settings
                // why sync palette: undo may revert Appearance change
                try
                    Theme.usePalette(Theme.paletteForSettings next.Global.Appearance)
                with _ ->
                    ()

                { state with Settings = next }, Cmd.none
            | Redo ->
                let next = SettingsSlice.redo state.Settings
                // why sync palette: redo may reapply Appearance change
                try
                    Theme.usePalette(Theme.paletteForSettings next.Global.Appearance)
                with _ ->
                    ()

                { state with Settings = next }, Cmd.none
            | Import json ->
                match SettingsSlice.tryImport json state.Settings with
                | Ok next ->
                    // why sync palette: imported Appearance may differ, theme must update without restart
                    try
                        Theme.usePalette(Theme.paletteForSettings next.Global.Appearance)
                    with _ ->
                        ()

                    let perSaveCmd =
                        match state.Mode with
                        | InGame(gs, _) -> SimHelpers.saveCmdForState state gs state.WorldClock next.Save
                        | _ -> Cmd.none

                    { state with
                        Settings = next
                        SaveSettings = next.Save },
                    Cmd.batch [ perSaveCmd; SettingsSlice.saveCmd next ]
                | Error err ->
                    state
                    |> pushNotification Material.Icons.MaterialIconKind.AlertCircleOutline "Import failed" err,
                    Cmd.none
            | Export ->
                let json = SettingsSlice.exportState state.Settings

                state
                |> addLog $"Settings exported ({json.Length} chars)"
                |> pushNotification Material.Icons.MaterialIconKind.ContentCopy "Settings exported" "JSON copied to log",
                Cmd.none

        | Navigate route ->
            let nextState =
                let navigated = navigateTo route state

                let navigated =
                    closeMatchRuntimeWhenLeaving state.Navigation.Current navigated.Navigation.Current navigated

                match route with
                | Route.Settings _ ->
                    { navigated with
                        Settings =
                            { navigated.Settings with
                                ValidationErrors = SettingsSlice.validateState navigated.Settings } }
                | Route.ModEditor ->
                    { navigated with
                        ModEditor = UpdateModEditor.refreshLibrary navigated.ModEditor }
                | _ -> navigated

            let cmd =
                match nextState.Navigation.Current with
                | Route.Match(_, MatchPhase.Live) when nextState.Match.RuntimeId.IsNone ->
                    Cmd.ofMsg (SimMsg SimulateUserFixture)
                | Route.Transfers _ when state.Transfer.CachedPlayers.IsEmpty -> Cmd.ofMsg (TransferMsg Load)
                | _ -> Cmd.none

            nextState,
            Cmd.batch
                [ pauseMatchWhenLeaving state.Navigation.Current nextState.Navigation.Current state
                  cmd ]

        | GoBack ->
            let backed = goBack state

            closeMatchRuntimeWhenLeaving state.Navigation.Current backed.Navigation.Current backed,
            pauseMatchWhenLeaving state.Navigation.Current backed.Navigation.Current state

        | GoForward ->
            let forwarded = goForward state

            closeMatchRuntimeWhenLeaving state.Navigation.Current forwarded.Navigation.Current forwarded,
            pauseMatchWhenLeaving state.Navigation.Current forwarded.Navigation.Current state

        | SearchGlobal query -> { state with GlobalSearchQuery = query }, Cmd.none

        | ChangeLeague leagueId ->
            { state with
                SelectedLeagueId = leagueId },
            Cmd.none
        | SetProcessing b ->
            { state with
                IsProcessing = b
                AdvanceTarget = if b then state.AdvanceTarget else None },
            Cmd.none
        | NoOp -> state, Cmd.none

        | ReloadMods ->
            match state.Mode with
            | InGame _ ->
                { state with
                    ModLoadErrors =
                        [ "El contenido de una carrera activa está congelado; los cambios aplican a la próxima carrera." ] },
                Cmd.none
            | _ ->
                match
                    ModPackageFile.loadInstalledPackages
                        ModPaths.builtinsDir
                        ModPaths.modsDir
                        state.Settings.Global.Data.ActiveMods
                        state.Settings.Global.Data.ModOrder
                with
                | Ok resolved ->
                    { state with
                        ResolvedData = Some resolved
                        ModLoadErrors = [] },
                    Cmd.none
                | Error error ->
                    { state with
                        ModLoadErrors = error.Diagnostics |> List.map (fun d -> $"{d.Code}: {d.Message}") },
                    Cmd.none
