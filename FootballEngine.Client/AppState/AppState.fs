namespace FootballEngine

open Elmish
open FootballEngine.AppMsgs
open FootballEngine.Domain
open AppTypes
open FootballEngine.Types
open FootballEngine.World
open FootballEngine.Data


module AppState =

    let private initialState () : State =
        { Mode                = Initializing
          CurrentPage         = Loading
          IsProcessing        = true
          Setup               = initSetupState
          Squad               = { SortBy = ByPosition; SelectedPlayer = None; DraggedPlayer = None }
          Tactics             = { DraggedPlayer = None }
          Training            = { SelectedPlayer = None }
          Inbox               = { SelectedMessageId = None }
          Match               = { ActiveReplay = None; Snapshot = 0; IsPlaying = false; PlaybackSpeed = 20; Accumulator = 0.0; InterpolationT = 0.0 }
          Transfer            = initTransferState
          ModEditor           = ModEditorTypes.initModEditorState
          Notifications       = []
          NextNotificationId  = 1
          LogMessages         = [ "Football Engine 2026 Initialized" ]
          SelectedLeagueId    = 1
          SelectedTactics     = F433
          WorldClock          = WorldClockOps.init 1
          ModLoadErrors       = []
          PrevUserClubSkills  = None
          PrevUserClubStatus  = None }

    let private addLog msg (state: State) =
        { state with
            LogMessages = msg :: state.LogMessages |> List.truncate 30 }

    let private headCoach (clubId: ClubId) (gs: GameState) : Staff option =
        GameState.getStaff clubId gs |> List.tryFind (fun s -> s.Role = HeadCoach)

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

    let init () =
        let loadCmd = Cmd.OfTask.perform (fun () -> Db.loadGame ()) () GameLoaded
        initialState (), loadCmd

    let update (msg: Msg) (state: State) : State * Cmd<Msg> =
        let withGame (f: GameState -> State * Cmd<Msg>) =
            match state.Mode with
            | InGame(gs, _) -> f gs
            | _ -> state, Cmd.none

        match msg with

        | SetupMsg m -> UpdateSetup.handle m state

        | ModEditorMsg m ->
            let nextEditorState, cmd = UpdateModEditor.update m state.ModEditor
            { state with ModEditor = nextEditorState }, Cmd.map ModEditorMsg cmd

        | SimMsg m ->
            let nextState, cmd = UpdateSim.handle m state
            let invalidated =
                match m with
                | AdvanceDone _ | SeasonAdvanceDone _ | UserMatchDone _ -> invalidateTransferCache nextState
                | _ -> nextState
            invalidated, cmd

        | TransferMsg m -> UpdateTransfer.handle m state

        | NotificationMsg nm ->
            match nm with
            | DismissNotification id ->
                { state with Notifications = state.Notifications |> List.filter (fun n -> n.Id <> id) }, Cmd.none
            | DismissAll -> { state with Notifications = [] }, Cmd.none
            | PushNotification note ->
                { state with
                    Notifications = note :: state.Notifications |> List.truncate 20
                    NextNotificationId = state.NextNotificationId + 1 }, Cmd.none

        | SquadMsg m ->
            { state with Squad = SquadSlice.update m state.Squad }, Cmd.none

        | TacticsMsg(TacticsMsg.SetFormation formation) ->
            withGame (fun gs ->
                let newGs =
                    GameState.updateLineup gs.UserClubId (fun currentLineupOpt ->
                        let lineup =
                            match currentLineupOpt with
                            | Some l -> l
                            | None ->
                                { Formation = formation
                                  Tactics = Balanced
                                  Instructions = Some TacticalInstructions.defaultInstructions
                                  Slots = [] }
                        Some { Formation = formation; Tactics = lineup.Tactics; Instructions = lineup.Instructions; Slots = buildNewLineupSlots formation currentLineupOpt }) gs
                { state with Mode = InGame(newGs, managerEmployment newGs); SelectedTactics = formation }, Cmd.none)

        | TacticsMsg(TacticsMsg.SetTeamTactics tactics) ->
            withGame (fun gs ->
                let newGs = GameState.updateLineup gs.UserClubId (Option.map (fun l -> { l with Tactics = tactics })) gs
                { state with Mode = InGame(newGs, managerEmployment newGs) }, Cmd.none)

        | TacticsMsg(TacticsMsg.SetInstruction f) ->
            withGame (fun gs ->
                let newGs =
                    GameState.updateLineup gs.UserClubId (Option.map (fun lineup ->
                        let instr = lineup.Instructions |> Option.defaultValue TacticalInstructions.defaultInstructions
                        { lineup with Instructions = Some (f instr) })) gs
                { state with Mode = InGame(newGs, managerEmployment newGs) }, Cmd.none)

        | TacticsMsg(TacticsMsg.DropPlayerInSlot(targetIdx, pId)) ->
            withGame (fun gs ->
                let swapPlayer (idx: int) (pid: PlayerId) (lineup: Lineup) : Lineup =
                    let updatedSlots =
                        lineup.Slots |> List.map (fun slot ->
                            if slot.Index = idx then { slot with PlayerId = Some pid }
                            elif slot.PlayerId = Some pid then { slot with PlayerId = None }
                            else slot)
                    { lineup with Slots = updatedSlots }

                let newGs =
                    GameState.updateLineup gs.UserClubId (fun currentLineupOpt ->
                        let defaultLineup =
                            { Formation = state.SelectedTactics; Tactics = Balanced
                              Instructions = Some TacticalInstructions.defaultInstructions
                              Slots = FormationData.getFormation state.SelectedTactics |> List.map (fun fs -> { Index = fs.Index; Role = fs.Role; X = fs.X; Y = fs.Y; PlayerId = None }) |> List.sortBy _.Index }
                        let current = currentLineupOpt |> Option.defaultValue defaultLineup
                        Some (swapPlayer targetIdx pId current)) gs
                { state with Mode = InGame(newGs, managerEmployment newGs); Tactics = { state.Tactics with DraggedPlayer = None } } |> addLog $"Swap made: Slot {targetIdx}", Cmd.none)

        | TacticsMsg(TacticsMsg.StartDrag id) ->
            { state with Tactics = { state.Tactics with DraggedPlayer = Some id } }, Cmd.none

        | TacticsMsg(TacticsMsg.EndDrag) ->
            { state with Tactics = { state.Tactics with DraggedPlayer = None } }, Cmd.none

        | TrainingMsg(TrainingMsg.SelectPlayer id) ->
            { state with Training = { state.Training with SelectedPlayer = Some id } }, Cmd.none

        | TrainingMsg(TrainingMsg.ClearSelection) ->
            { state with Training = { state.Training with SelectedPlayer = None } }, Cmd.none

        | TrainingMsg(TrainingMsg.SetFocus(playerId, focus)) ->
            withGame (fun gs ->
                let newGs = { gs with Players = gs.Players |> Map.add playerId { gs.Players[playerId] with TrainingSchedule = { gs.Players[playerId].TrainingSchedule with Focus = focus } } }
                { state with Mode = InGame(newGs, managerEmployment newGs) }, SimHelpers.saveCmd newGs state.WorldClock)

        | TrainingMsg(TrainingMsg.SetIntensity(playerId, intensity)) ->
            withGame (fun gs ->
                let newGs = { gs with Players = gs.Players |> Map.add playerId { gs.Players[playerId] with TrainingSchedule = { gs.Players[playerId].TrainingSchedule with Intensity = intensity } } }
                { state with Mode = InGame(newGs, managerEmployment newGs) }, SimHelpers.saveCmd newGs state.WorldClock)

        | InboxMsg(InboxMsg.SelectMessage messageId) ->
            withGame (fun gs ->
                let newGs = GameState.markMessageAsRead messageId gs
                { state with Mode = InGame(newGs, managerEmployment newGs); Inbox = { SelectedMessageId = Some messageId } }, SimHelpers.saveCmd newGs state.WorldClock)

        | InboxMsg(InboxMsg.MarkAsRead messageId) ->
            withGame (fun gs ->
                let newGs = GameState.markMessageAsRead messageId gs
                { state with Mode = InGame(newGs, managerEmployment newGs) }, SimHelpers.saveCmd newGs state.WorldClock)

        | InboxMsg(InboxMsg.MarkActionTaken messageId) ->
            withGame (fun gs ->
                let newGs = GameState.markMessageActionTaken messageId gs
                { state with Mode = InGame(newGs, managerEmployment newGs) }, SimHelpers.saveCmd newGs state.WorldClock)

        | MatchMsg m ->
            { state with Match = MatchSlice.update m state.Match }, Cmd.none

        | GameLoaded result ->
            match result with
            | Some(gs, clock) ->
                let leagueId = SimHelpers.primaryLeagueId gs
                match ModLoader.loadAll ModPaths.builtinsDir ModPaths.modsDir with
                | Ok data ->
                    DataRegistry.setLoadedData data
                    { state with Mode = InGame(gs, managerEmployment gs); CurrentPage = HomePage; SelectedLeagueId = leagueId; IsProcessing = false; WorldClock = clock; ModLoadErrors = data.Errors |> List.map string }, Cmd.none
                | Error errs ->
                    { state with Mode = InGame(gs, managerEmployment gs); CurrentPage = HomePage; SelectedLeagueId = leagueId; IsProcessing = false; WorldClock = clock; ModLoadErrors = errs |> List.map string }, Cmd.none
            | None ->
                { state with Mode = NoSave; CurrentPage = Setup; IsProcessing = false }, Cmd.none

        | ChangePage page ->
            let cmd =
                if page = Transfers && state.Transfer.CachedPlayers.IsEmpty then Cmd.ofMsg (TransferMsg Load) else Cmd.none
            let saveCmdVal =
                match state.Mode with
                | InGame(gs, _) ->
                    if state.CurrentPage = Tactics && page <> Tactics then SimHelpers.saveCmd gs state.WorldClock
                    elif state.CurrentPage = Training && page <> Training then SimHelpers.saveCmd gs state.WorldClock
                    else Cmd.none
                | _ -> Cmd.none
            { state with CurrentPage = page }, Cmd.batch [ cmd; saveCmdVal ]

        | ChangeLeague leagueId -> { state with SelectedLeagueId = leagueId }, Cmd.none
        | SetProcessing b -> { state with IsProcessing = b }, Cmd.none
        | NoOp -> state, Cmd.none

        | ReloadMods ->
            match ModLoader.loadAll ModPaths.builtinsDir ModPaths.modsDir with
            | Ok data -> DataRegistry.setLoadedData data; { state with ModLoadErrors = data.Errors |> List.map string }, Cmd.none
            | Error errs -> { state with ModLoadErrors = errs |> List.map string }, Cmd.none
