namespace FootballEngine

open FootballEngine.Core.Match
open FootballEngine.World
open ModEditorTypes
open FootballEngine.Domain
open FootballEngine.Data
open FootballEngine.Settings
open FootballEngine.Routing

module AppTypes =

    type SetupStep =
        | MainMenu
        | CountrySelection
        | ManagerNaming
        | ClubSelection
        | OptionalSettings
        | Summary
        | LoadGame

    type SetupLoadState =
        | LoadClosed
        | LoadingSlots of requestId: uint64
        | SlotsReady of SaveSlotInfo list
        | LoadingSlot of slot: string * requestId: uint64
        | SlotsLoadFailed of message: string
        | LoadFailed of slot: string * error: LoadError

    type TransferTab =
        | MarketSearch
        | MyWatchlist
        | IncomingOffers
        | OutgoingOffers
        | TransferHistory

    type TransferFilter =
        | AllPositions
        | Goalkeepers
        | Defenders
        | Midfielders
        | Attackers

    type SortField =
        | ByName
        | ByCA
        | ByValue
        | ByAge
        | ByPosition

    type NotificationKind =
        | MatchResult
        | SeasonEnd
        | Transfer
        | Info

    type Notification =
        { Id: int
          Icon: Material.Icons.MaterialIconKind
          Title: string
          Body: string
          IsRead: bool }

    type SetupState =
        { Step: SetupStep
          SelectedCountry: CountryCode option
          SecondaryCountries: CountryCode list
          SelectedLeagueLevel: int option
          ManagerName: string
          ManagementStyle: TeamEmphasis option
          SelectedClubName: string option
          Difficulty: DifficultyPreset option
          Seed: uint64
          SaveSlots: SaveSlotInfo list
          LoadState: SetupLoadState }

    type PendingOffer =
        { PlayerId: PlayerId
          Fee: decimal
          Salary: decimal }

    type TransferState =
        { ActiveTab: TransferTab
          SearchQuery: string
          PositionFilter: TransferFilter
          SortBy: SortField
          SortAsc: bool
          SelectedPlayerId: PlayerId option
          WatchlistIds: PlayerId list
          CachedPlayers: Player list
          FilteredPlayers: Player list
          ClubNameCache: Map<PlayerId, string>
          IsLoading: bool
          Page: int
          TransferHistory: TransferRecord list
          ActiveNegotiationId: int option
          PendingOffer: PendingOffer option
          ScoutingPlayerId: PlayerId option
          ScoutingScoutId: StaffId option
          ScoutingCostText: string
          ScoutingDaysText: string }

    type SquadState =
        { SortBy: SortField
          SelectedPlayer: PlayerId option
          DraggedPlayer: PlayerId option }

    type TacticsState =
        { DraggedPlayer: PlayerId option
          History: Lineup list }

    type TrainingState = { SelectedPlayer: PlayerId option }

    type FinanceLedgerFilter =
        | AllLedgerEntries
        | IncomeEntries
        | ExpenseEntries
        | CommitmentEntries

    type FinanceLedgerSort =
        | NewestLedgerFirst
        | OldestLedgerFirst
        | LargestLedgerFirst
        | SmallestLedgerFirst

    type FinanceState =
        { SelectedOperationId: string option
          ShowObligations: bool
          LedgerFilter: FinanceLedgerFilter
          LedgerSort: FinanceLedgerSort }

    type CalendarView =
        | MonthView
        | AgendaView

    type CalendarDateFilter =
        | AllCalendarDates
        | PastCalendarDates
        | FutureCalendarDates

    type CalendarTeamFilter =
        | MyTeam
        | AllTeams
        | SelectedTeam of ClubId

    type CalendarState =
        { View: CalendarView
          DateFilter: CalendarDateFilter
          TeamFilter: CalendarTeamFilter
          CompetitionFilter: CompetitionId option
          AnchorMonth: System.DateTime option
          SelectedFixtureId: MatchId option }

    [<Struct>]
    type MatchRuntimeId = MatchRuntimeId of uint64

    type LiveRuntimeSemantic =
        { RuntimeId: MatchRuntimeId
          Revision: uint64
          Frontier: int<subtick>
          Events: MatchEvent array
          Clock: FootballEngine.LiveMatch.LiveClock
          Score: int * int
          Stats: TeamStats * TeamStats }

    /// One-click bulk rules for the squad list.
    type TrainingBulk =
        | U21Develop
        | RestTired
        | RestStarters
        | AllPerform

    type InboxCategoryFilter =
        | AllInboxCategories
        | InboxCategory of InboxMessageCategory

    type InboxState =
        { SelectedMessageId: int option
          RequiresActionOnly: bool
          UnreadOnly: bool
          CategoryFilter: InboxCategoryFilter }

    type MatchState =
        { RuntimeId: MatchRuntimeId option
          RequestedMatchId: MatchId option
          PendingResult: MatchJobResult option
          Error: string option
          Live: FootballEngine.LiveMatch.LiveMatchState option }

    type SettingsCategory = FootballEngine.Routing.SettingsCategory

    type SettingsState =
        { Global: GlobalSettings
          Save: SaveSettings
          SearchQuery: string
          SelectedCategory: SettingsCategory
          Dirty: bool
          ValidationErrors: Map<string, string>
          History: (GlobalSettings * SaveSettings) list
          Future: (GlobalSettings * SaveSettings) list }

    type ManagerEmployment =
        | Employed of clubId: ClubId
        | NotEmployed

    type SaveStatus =
        | SaveIdle
        | Saving of requestId: uint64
        | Saved of at: System.DateTime
        | SaveFailed of message: string

    type NavigationState =
        { Current: Route
          Back: Route list
          Forward: Route list }

    type AppMode =
        | Initializing
        | NoSave
        | InGame of GameState * ManagerEmployment

    type State =
        { Mode: AppMode
          Navigation: NavigationState
          IsProcessing: bool

          Setup: SetupState
          Squad: SquadState
          Tactics: TacticsState
          Training: TrainingState
          Finance: FinanceState
          Calendar: CalendarState
          Inbox: InboxState
          Match: MatchState
          Transfer: TransferState
          ModEditor: ModEditorState

          Notifications: Notification list
          NextNotificationId: int
          LogMessages: string list
          SelectedLeagueId: CompetitionId
          SelectedTactics: Formation
          WorldClock: WorldClock
          ModLoadErrors: string list
          ResolvedData: ResolvedDataSet option
          SaveSettings: SaveSettings
          Settings: SettingsState
          ActiveSlot: string option
          PersistenceRequestId: uint64
          SaveStatus: SaveStatus
          GlobalSearchQuery: string

          PrevUserClubSkills: Map<PlayerId, int> option
          PrevUserClubStatus: Map<PlayerId, PlayerStatus> option }

    let initSetupState =
        { Step = MainMenu
          SelectedCountry = None
          SecondaryCountries = []
          SelectedLeagueLevel = None
          ManagerName = ""
          ManagementStyle = None
          SelectedClubName = None
          Difficulty = None
          Seed = 0x464F4F5442414C4CUL
          SaveSlots = []
          LoadState = LoadClosed }

    let initTransferState =
        { ActiveTab = MarketSearch
          SearchQuery = ""
          PositionFilter = AllPositions
          SortBy = ByCA
          SortAsc = false
          SelectedPlayerId = None
          WatchlistIds = []
          CachedPlayers = []
          FilteredPlayers = []
          ClubNameCache = Map.empty
          IsLoading = false
          Page = 0
          TransferHistory = []
          ActiveNegotiationId = None
          PendingOffer = None
          ScoutingPlayerId = None
          ScoutingScoutId = None
          ScoutingCostText = ""
          ScoutingDaysText = "" }

    let initInboxState =
        { SelectedMessageId = None
          RequiresActionOnly = false
          UnreadOnly = false
          CategoryFilter = AllInboxCategories }

    let private buildValidationErrors
        (globalSettings: GlobalSettings)
        (saveSettings: SaveSettings)
        : Map<string, string> =
        // why map string->string: UI binds ValidationErrors by field key, Map allows O(log n) lookup per row
        let gErrs =
            match GlobalSettings.validate globalSettings with
            | Ok _ -> []
            | Error errs -> errs

        let sErrs =
            match SaveSettings.validate saveSettings with
            | Ok _ -> []
            | Error errs -> errs

        (gErrs @ sErrs) |> List.map (fun e -> e, e) |> Map.ofList

    let initSettingsState (globalSettings: GlobalSettings) (saveSettings: SaveSettings) : SettingsState =
        { Global = globalSettings
          Save = saveSettings
          SearchQuery = ""
          SelectedCategory = All
          Dirty = false
          ValidationErrors = buildValidationErrors globalSettings saveSettings
          History = []
          Future = [] }

    let initialState () : State =
        { Mode = Initializing
          Navigation =
            { Current = Route.Loading
              Back = []
              Forward = [] }
          IsProcessing = true
          Setup = initSetupState
          Squad =
            { SortBy = ByPosition
              SelectedPlayer = None
              DraggedPlayer = None }
          Tactics = { DraggedPlayer = None; History = [] }
          Training = { SelectedPlayer = None }
          Finance =
            { SelectedOperationId = None
              ShowObligations = true
              LedgerFilter = AllLedgerEntries
              LedgerSort = NewestLedgerFirst }
          Calendar =
            { View = AgendaView
              DateFilter = FutureCalendarDates
              TeamFilter = MyTeam
              CompetitionFilter = None
              AnchorMonth = None
              SelectedFixtureId = None }
          Inbox = initInboxState
          Match =
            { RuntimeId = None
              RequestedMatchId = None
              PendingResult = None
              Error = None
              Live = None }
          Transfer = initTransferState
          ModEditor = ModEditorTypes.initModEditorState
          Notifications = []
          NextNotificationId = 1
          LogMessages = [ "Football Engine 2026 Initialized" ]
          SelectedLeagueId = 1
          SelectedTactics = F433
          WorldClock = WorldClockOps.init 1
          ModLoadErrors = []
          ResolvedData = None
          SaveSettings = SaveSettings.defaults
          Settings = initSettingsState GlobalSettings.defaults SaveSettings.defaults
          ActiveSlot = None
          PersistenceRequestId = 0UL
          SaveStatus = SaveIdle
          GlobalSearchQuery = ""
          PrevUserClubSkills = None
          PrevUserClubStatus = None }

    let managerEmployment (gs: GameState) : ManagerEmployment =
        GameState.userManager gs
        |> Option.bind (fun s -> if Staff.isEmployed s then Staff.clubId s else None)
        |> Option.map Employed
        |> Option.defaultValue NotEmployed

    let addLog msg (state: State) =
        { state with
            LogMessages = msg :: state.LogMessages |> List.truncate 30 }

    let pushNotification icon title body (state: State) =
        let note =
            { Id = state.NextNotificationId
              Icon = icon
              Title = title
              Body = body
              IsRead = false }

        { state with
            Notifications = note :: state.Notifications |> List.truncate 20
            NextNotificationId = state.NextNotificationId + 1 }
