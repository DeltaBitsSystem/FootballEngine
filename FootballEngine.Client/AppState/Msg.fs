namespace FootballEngine

open FootballEngine.Data
open FootballEngine.Domain

open AppTypes
open FootballEngine.Settings
open FootballEngine.World
open FootballEngine.World.WorldRunner
open FootballEngine.Routing
open FootballEngine.LiveMatch

module AppMsgs =

    type SetupMsg =
        | GoToStep of SetupStep
        | SelectPrimaryCountry of CountryCode
        | SelectLeague of int
        | ToggleSecondaryCountry of CountryCode
        | UpdateManagerName of string
        | SelectManagementStyle of TeamEmphasis
        | SelectClub of string
        | SelectDifficulty of DifficultyPreset option
        | OpenLoadGame
        | CloseLoadGame
        | CancelSetup
        | StartNewGame

    type PersistenceMsg =
        | ListSaveSlots
        | SaveSlotsLoaded of requestId: uint64 * Result<SaveSlotInfo list, string>
        | LoadSlot of slot: string
        | SaveCurrent
        | SaveCompleted of requestId: uint64 * slot: string * revision: uint64 * Result<unit, string>

    type SimMsg =
        | ContinueCareer
        | AdvanceToNextEvent
        | CancelAdvance
        | Advance of days: int
        | AdvanceDone of DayResult
        | AdvanceFailed of string
        | AdvanceSeason
        | SeasonAdvanceDone of Result<SeasonResult, SeasonError>
        | SimulateSeason
        | SimulateUserFixture
        | UserMatchDone of Result<UserMatchResult, string>
        | SimulateAiFixtures
        | AiMatchesDone of DayResult

    type TransferMsg =
        | Load
        | Loaded of players: Player list * clubNames: Map<PlayerId, string>
        | TabChange of TransferTab
        | Search of string
        | ApplySearch of string
        | FilterChange of TransferFilter
        | SortChange of SortField
        | PlayerSelect of PlayerId
        | WatchToggle of PlayerId
        | PageChange of int
        | MakeOffer of playerId: PlayerId * fee: decimal * salary: decimal
        | SubmitOffer
        | WithdrawOffer of negotiationId: int
        | ClearNegotiation of negotiationId: int
        | CounterOffer of negotiationId: int * newFee: decimal * newSalary: decimal option
        | SelectScoutingPlayer of PlayerId option
        | SelectScoutingScout of StaffId option
        | SetScoutingCost of string
        | SetScoutingDays of string
        | CreateScoutingAssignment
        | ToggleShortlist of PlayerId
        | OpenScoutingOffer of PlayerId

    type NotificationMsg =
        | DismissNotification of id: int
        | DismissAll
        | PushNotification of Notification

    type FinanceMsg =
        | SelectOperation of operationId: string option
        | SetLedgerFilter of FinanceLedgerFilter
        | SetLedgerSort of FinanceLedgerSort
        | ToggleObligations

    type CalendarMsg =
        | SetView of CalendarView
        | SetDateFilter of CalendarDateFilter
        | SetTeamFilter of CalendarTeamFilter
        | SetCompetitionFilter of CompetitionId option
        | MoveMonth of int
        | SelectFixture of MatchId option

    type NewsMsg = SetNewsFilter of NewsFilter

    type CareerMsg =
        | AcceptJobOffer of offerId: int
        | DeclineJobOffer of offerId: int

    type SquadMsg =
        | SetSort of SortField
        | SelectPlayer of PlayerId
        | ClearSelection
        | StartDrag of PlayerId
        | EndDrag

    type TacticsMsg =
        | SelectSlot of int option
        | SetFormation of Formation
        | SetTeamTactics of TeamTactics
        | SetInstruction of (TacticalInstructions -> TacticalInstructions)
        | DropPlayerInSlot of slotIndex: int * playerId: PlayerId
        | StartDrag of PlayerId
        | EndDrag
        | QuickPick
        | UndoTactics

    type TrainingMsg =
        | SelectPlayer of PlayerId
        | ClearSelection
        | SetPlan of playerId: PlayerId * plan: IndividualPlan
        | SetTeamEmphasis of TeamEmphasis
        | BulkApply of TrainingBulk

    type InboxMsg =
        | SelectMessage of messageId: int
        | MarkAsRead of messageId: int
        | MarkActionTaken of messageId: int
        | SetRequiresActionOnly of bool
        | SetUnreadOnly of bool
        | SetCategoryFilter of InboxCategoryFilter
        | ClearSelection

    type LiveMsg =
        | RuntimePublicationAvailable of runtimeId: MatchRuntimeId
        | SessionConnected of LiveMatchSessionId * FixtureContext * revision: uint64 * LiveMatchSource
        | SemanticReceived of
            revision: uint64 *
            LiveClock *
            (int * int) *
            FootballEngine.Core.Match.TeamStats *
            FootballEngine.Core.Match.TeamStats *
            FootballEngine.Core.Match.MatchEvent list
        | CommandSubmitted of LiveMatchCommand
        | CommandAccepted of MatchCommandId * ApplicationPoint
        | CommandApplied of MatchCommandId * revision: uint64 * appliedAt: int<FootballEngine.Core.Match.subtick>
        | CommandRejected of MatchCommandId * CommandRejection
        | RuntimeStatusChanged of LiveMatchStatus
        | SelectManagerWorkspace of MatchDayWorkspace
        | SelectViewerCamera of FootballEngine.Client.Match3D.ViewerCamera
        | SelectIntelligenceWorkspace of IntelligenceWorkspace
        | SelectPlayer of FootballEngine.Domain.PlayerId option
        | SetEvidenceQuery of FootballEngine.Core.Match.ObservationQuery
        | SetDraftSubstitutions of SubstitutionChange list
        | SubmitDraftSubstitutions
        | DismissError
        /// Opens a recorded match at the tick: the match is re-simulated silently and the viewer lands there.
        | OpenReplay of MatchId * startAt: int<FootballEngine.Core.Match.subtick>
        /// Moves a recording to the tick: forward through the live worker, backward by reopening it.
        | SeekReplay of int<FootballEngine.Core.Match.subtick>
        | TogglePause
        | ConfirmFinish
        | Close

    type ModEditorMsg =
        | SetTab of ModEditorTypes.ModEditorTab
        | SetSubTab of ModEditorTypes.ModEditorSubTab
        /// The string names the edited field; consecutive edits of one field form one undo step.
        | UpdateManifest of string * (ModManifestDto -> ModManifestDto)
        | SelectCountry of string option
        | SelectEditorClub of int option
        | AddCountry of CountryDataDto
        | UpdateCountry of string * (CountryDataDto -> CountryDataDto)
        | EditCountry of code: string * field: string * (CountryDataDto -> CountryDataDto)
        | RemoveCountry of string
        | AddCup of string * CupEntryDto
        | UpdateCup of string * int * (CupEntryDto -> CupEntryDto)
        | RemoveCup of string * int
        | AddInternationalComp of InternationalCompDto
        | UpdateInternationalComp of int * (InternationalCompDto -> InternationalCompDto)
        | RemoveInternationalComp of int
        | UndoMod
        | RedoMod
        | Validate
        | PublishMod of enable: bool
        | SearchMod of string
        | SearchPlayers of string
        | SetSquadPaste of string
        | ImportSquad of code: string * clubIndex: int
        | RefreshLibrary
        | NewMod
        | ResumeDraft
        | OpenPackage of path: string * asOverride: bool
        | ImportCountry of path: string * countryCode: string
        | CloseDocument

    type SettingsMsg =
        | SetSearch of string
        | SelectCategory of SettingsCategory
        | UpdateGlobal of (GlobalSettings -> GlobalSettings)
        | UpdateSave of (SaveSettings -> SaveSettings)
        | ApplyPreset of DifficultyPreset
        | ResetCategory of SettingsCategory
        | ResetAll
        | SaveSettings
        | LoadSettings
        | Undo
        | Redo
        | Import of string
        | Export

    type Msg =
        | SetupMsg of SetupMsg
        | PersistenceMsg of PersistenceMsg
        | SimMsg of SimMsg
        | TransferMsg of TransferMsg
        | FinanceMsg of FinanceMsg
        | CalendarMsg of CalendarMsg
        | NewsMsg of NewsMsg
        | CareerMsg of CareerMsg
        | NotificationMsg of NotificationMsg
        | ModEditorMsg of ModEditorMsg
        | SettingsMsg of SettingsMsg
        | StartupReady
        | GameLoaded of requestId: uint64 * slot: string * Result<Db.LoadedCareer, LoadError>
        | Navigate of Route
        | GoBack
        | GoForward
        | SearchGlobal of string
        | ChangeLeague of CompetitionId
        | SetProcessing of bool
        | NoOp
        | ReloadMods

        | SquadMsg of SquadMsg
        | TacticsMsg of TacticsMsg
        | TrainingMsg of TrainingMsg
        | InboxMsg of InboxMsg
        | LiveMsg of LiveMsg

module SimHelpers =
    let leagueOf (gs: GameState) (clubId: ClubId) : CompetitionId option =
        gs.Competitions
        |> Map.tryFindKey (fun _ comp ->
            match comp.Type with
            | NationalLeague _ -> List.contains clubId comp.ClubIds
            | _ -> false)

    let primaryLeagueId (gs: GameState) =
        gs.Competitions
        |> Map.tryFindKey (fun _ comp ->
            match comp.Type, comp.Country with
            | NationalLeague(LeagueLevel 0, _), Some c when c = gs.PrimaryCountry -> true
            | _ -> false)
        |> Option.defaultWith (fun () ->
            gs.Competitions
            |> Map.tryFindKey (fun _ comp ->
                match comp.Type with
                | NationalLeague _ -> true
                | _ -> false)
            |> Option.defaultValue 1)

    let private saveCmdInSlot
        (slot: string)
        (gs: GameState)
        (clock: WorldClock)
        (saveSettings: SaveSettings)
        : Elmish.Cmd<AppMsgs.Msg> =
        let save = Db.prepareSaveInSlot slot gs clock saveSettings false

        Elmish.Cmd.OfTask.either save () (fun () -> AppMsgs.NoOp) (fun ex ->
            AppMsgs.NotificationMsg(
                AppMsgs.PushNotification
                    { Id = 0
                      Icon = Material.Icons.MaterialIconKind.AlertCircleOutline
                      Title = "Save failed"
                      Body = ex.Message
                      IsRead = false }
            ))

    let saveCmd (gs: GameState) (clock: WorldClock) (saveSettings: SaveSettings) : Elmish.Cmd<AppMsgs.Msg> =
        saveCmdInSlot Db.DefaultSlot gs clock saveSettings

    let saveCmdForState
        (state: AppTypes.State)
        (gs: GameState)
        (clock: WorldClock)
        (saveSettings: SaveSettings)
        : Elmish.Cmd<AppMsgs.Msg> =
        let slot = state.ActiveSlot |> Option.defaultValue Db.DefaultSlot
        saveCmdInSlot slot gs clock saveSettings

    let saveCmdWithRequest
        (state: AppTypes.State)
        (requestId: uint64)
        (gs: GameState)
        (clock: WorldClock)
        (saveSettings: SaveSettings)
        : Elmish.Cmd<AppMsgs.Msg> =
        let slot = state.ActiveSlot |> Option.defaultValue Db.DefaultSlot
        let save = Db.prepareSaveInSlot slot gs clock saveSettings false

        Elmish.Cmd.OfTask.either
            save
            ()
            (fun () -> AppMsgs.PersistenceMsg(AppMsgs.SaveCompleted(requestId, slot, clock.Revision, Ok())))
            (fun ex -> AppMsgs.PersistenceMsg(AppMsgs.SaveCompleted(requestId, slot, clock.Revision, Error ex.Message)))

    let replaceSaveCmdWithRequest
        (state: AppTypes.State)
        (requestId: uint64)
        (gs: GameState)
        (clock: WorldClock)
        (saveSettings: SaveSettings)
        : Elmish.Cmd<AppMsgs.Msg> =
        let slot = state.ActiveSlot |> Option.defaultValue Db.DefaultSlot
        let save = Db.prepareSaveInSlot slot gs clock saveSettings true

        Elmish.Cmd.OfTask.either
            save
            ()
            (fun () -> AppMsgs.PersistenceMsg(AppMsgs.SaveCompleted(requestId, slot, clock.Revision, Ok())))
            (fun ex -> AppMsgs.PersistenceMsg(AppMsgs.SaveCompleted(requestId, slot, clock.Revision, Error ex.Message)))
