namespace FootballEngine

open FootballEngine.Data
open FootballEngine.Domain

open AppTypes
open FootballEngine.World
open FootballEngine.World.WorldRunner

module AppMsgs =

    type SetupMsg =
        | GoToStep of SetupStep
        | SelectPrimaryCountry of CountryCode
        | ToggleSecondaryCountry of CountryCode
        | UpdateManagerName of string
        | ConfirmClub of ClubId
        | StartNewGame

    type SimMsg =
        | Advance of days: int
        | AdvanceDone of DayResult
        | AdvanceSeason
        | SeasonAdvanceDone of Result<SeasonResult, SeasonError>
        | SaveGame
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

    type NotificationMsg =
        | DismissNotification of id: int
        | DismissAll
        | PushNotification of Notification

    type SquadMsg =
        | SetSort      of SortField
        | SelectPlayer of PlayerId
        | ClearSelection
        | StartDrag    of PlayerId
        | EndDrag

    type TacticsMsg =
        | SetFormation     of Formation
        | SetTeamTactics   of TeamTactics
        | SetInstruction   of (TacticalInstructions -> TacticalInstructions)
        | DropPlayerInSlot of slotIndex: int * playerId: PlayerId
        | StartDrag        of PlayerId
        | EndDrag

    type TrainingMsg =
        | SelectPlayer of PlayerId
        | ClearSelection
        | SetFocus     of playerId: PlayerId * TrainingFocus
        | SetIntensity of playerId: PlayerId * TrainingIntensity

    type InboxMsg =
        | SelectMessage   of messageId: int
        | MarkAsRead      of messageId: int
        | MarkActionTaken of messageId: int

    type MatchMsg =
        | Step       of delta: int
        | Close
        | TogglePlay
        | SetSpeed   of int
        | Tick

    type ModEditorMsg =
        | SetTab of ModEditorTypes.ModEditorTab
        | SetSubTab of ModEditorTypes.ModEditorSubTab
        | UpdateManifest of (ModManifestDto -> ModManifestDto)
        | SelectCountry of string option
        | AddCountry of CountryDataDto
        | UpdateCountry of string * (CountryDataDto -> CountryDataDto)
        | RemoveCountry of string
        | AddCup of string * CupEntryDto
        | UpdateCup of string * int * (CupEntryDto -> CupEntryDto)
        | RemoveCup of string * int
        | AddInternationalComp of InternationalCompDto
        | UpdateInternationalComp of int * (InternationalCompDto -> InternationalCompDto)
        | RemoveInternationalComp of int
        | UndoMod
        | RedoMod
        | Export
        | Validate
        | SearchMod of string

    type Msg =
        | SetupMsg      of SetupMsg
        | SimMsg        of SimMsg
        | TransferMsg   of TransferMsg
        | NotificationMsg of NotificationMsg
        | ModEditorMsg  of ModEditorMsg
        | GameLoaded    of (GameState * WorldClock) option
        | ChangePage    of Page
        | ChangeLeague  of CompetitionId
        | SetProcessing of bool
        | NoOp
        | ReloadMods

        | SquadMsg      of SquadMsg
        | TacticsMsg    of TacticsMsg
        | TrainingMsg   of TrainingMsg
        | InboxMsg      of InboxMsg
        | MatchMsg      of MatchMsg

module SimHelpers =
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

    let saveCmd (gs: GameState) (clock: WorldClock) : Elmish.Cmd<AppMsgs.Msg> =
        Elmish.Cmd.OfTask.either (fun () -> Db.saveGameAsync gs clock) () (fun () -> AppMsgs.NoOp) (fun ex ->
            AppMsgs.NotificationMsg(
                AppMsgs.PushNotification
                    { Id = 0
                      Icon = Material.Icons.MaterialIconKind.AlertCircleOutline
                      Title = "Save failed"
                      Body = ex.Message
                      IsRead = false }
            ))
