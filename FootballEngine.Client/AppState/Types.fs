namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Simulation
open FootballEngine.World
open ModEditorTypes

module AppTypes =

    type Page =
        | Loading
        | Setup
        | HomePage
        | Inbox
        | Squad
        | Tactics
        | Training
        | Scouting
        | Transfers
        | Finances
        | Settings
        | Match
        | ModEditor

    type SetupStep =
        | MainMenu
        | CountrySelection
        | ClubSelection
        | ManagerNaming

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
          ManagerName: string }

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
          PendingOffer: PendingOffer option }

    type MatchLabState =
        { HomeClubId: ClubId option
          AwayClubId: ClubId option
          Result: MatchReplay option
          Snapshot: int }

    type SquadState = {
        SortBy:         SortField
        SelectedPlayer: PlayerId option
        DraggedPlayer:  PlayerId option
    }

    type TacticsState = {
        DraggedPlayer: PlayerId option
    }

    type TrainingState = {
        SelectedPlayer: PlayerId option
    }

    type InboxState = {
        SelectedMessageId: int option
    }

    type MatchState = {
        ActiveReplay:   MatchReplay option
        Snapshot:       int
        IsPlaying:      bool
        PlaybackSpeed:  int
        Accumulator:    float
        InterpolationT: float
    }

    type ManagerEmployment =
        | Employed of clubId: ClubId
        | NotEmployed

    type AppMode =
        | Initializing
        | NoSave
        | InGame of GameState * ManagerEmployment

    type State =
        { Mode:                AppMode
          CurrentPage:         Page
          IsProcessing:        bool

          Setup:               SetupState
          Squad:               SquadState
          Tactics:             TacticsState
          Training:            TrainingState
          Inbox:               InboxState
          Match:               MatchState
          Transfer:            TransferState
          ModEditor:           ModEditorState

          Notifications:       Notification list
          NextNotificationId:  int
          LogMessages:         string list
          SelectedLeagueId:    CompetitionId
          SelectedTactics:     Formation
          WorldClock:          WorldClock
          ModLoadErrors:       string list

          PrevUserClubSkills:  Map<PlayerId, int> option
          PrevUserClubStatus:  Map<PlayerId, PlayerStatus> option }

    let initSetupState =
        { Step = MainMenu
          SelectedCountry = None
          SecondaryCountries = []
          ManagerName = "" }

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
          PendingOffer = None }

    let initMatchLabState =
        { HomeClubId = None
          AwayClubId = None
          Result = None
          Snapshot = 0 }

    let initInboxState = { SelectedMessageId = None }

    let initialState () : State =
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
