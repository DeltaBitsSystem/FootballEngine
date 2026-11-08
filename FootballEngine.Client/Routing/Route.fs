namespace FootballEngine.Routing

open System
open FootballEngine.Domain

type SquadRoute =
    | All
    | ByPosition
    | Detail of PlayerId

type StaffRoute =
    | AllStaff
    | Detail of StaffId

type TacticsRoute =
    | Overview
    | Draft
    | Plan of int

type TrainingRoute =
    | Overview
    | Player of PlayerId
    | Group of int

type ScoutingRoute =
    | Overview
    | Assignment of ScoutingAssignmentId
    | Report of ScoutingAssignmentId

type TransferRoute =
    | Market
    | Player of PlayerId
    | Watchlist
    | IncomingOffers
    | Negotiations
    | Negotiation of int
    | Offer of int
    | History

type FinanceRoute =
    | Overview
    | Operation of string

type SettingsCategory =
    | Appearance
    | MatchView
    | Simulation
    | Career
    | Data
    | Scouting
    | Advanced
    | All

type SettingsRoute =
    | Overview
    | Category of SettingsCategory

type InboxRoute = InboxRoute of int option

type MatchPhase =
    | Prematch
    | Live
    | Replay
    | Postmatch

[<RequireQualifiedAccess>]
type Route =
    | Loading
    | Setup
    | Home
    | Inbox of InboxRoute
    | Squad of SquadRoute
    | Club of ClubId
    | Staff of StaffRoute
    | Tactics of TacticsRoute
    | Training of TrainingRoute
    | Scouting of ScoutingRoute
    | Transfers of TransferRoute
    | Finances of FinanceRoute
    | Calendar
    | Competitions
    | Competition of CompetitionId
    | Fixture of MatchId
    | Match of MatchId * MatchPhase
    | MatchEvent of int
    | Board
    | Youth
    | News
    | SeasonClose
    | Settings of SettingsRoute
    | ModEditor

module Route =
    let defaultRoute = Route.Home

    let requiresEmployment (route: Route) =
        match route with
        // Pages that act on or show the club as "yours"; with no club they would edit someone else's team.
        | Route.Squad _
        | Route.Tactics _
        | Route.Training _
        | Route.Staff _
        | Route.Scouting _
        | Route.Transfers _
        | Route.Finances _
        | Route.Board
        | Route.Youth
        | Route.Calendar -> true
        | _ -> false

    let canNavigate (isEmployed: bool) (route: Route) =
        isEmployed || not (requiresEmployment route)

    let private parseInt (value: string) =
        match Int32.TryParse value with
        | true, parsed -> Some parsed
        | _ -> None

    let toPath (route: Route) : string =
        match route with
        | Route.Loading -> "/loading"
        | Route.Setup -> "/setup"
        | Route.Home -> "/"
        | Route.Inbox(InboxRoute None) -> "/inbox"
        | Route.Inbox(InboxRoute(Some id)) -> $"/inbox/%d{id}"
        | Route.Squad SquadRoute.All -> "/squad"
        | Route.Squad SquadRoute.ByPosition -> "/squad/by-position"
        | Route.Squad(SquadRoute.Detail id) -> $"/squad/%d{id}"
        | Route.Club id -> $"/club/%d{id}"
        | Route.Staff StaffRoute.AllStaff -> "/staff"
        | Route.Staff(StaffRoute.Detail id) -> $"/staff/%d{id}"
        | Route.Tactics TacticsRoute.Overview -> "/tactics"
        | Route.Tactics TacticsRoute.Draft -> "/tactics/draft"
        | Route.Tactics(TacticsRoute.Plan id) -> $"/tactics/plan/%d{id}"
        | Route.Training TrainingRoute.Overview -> "/training"
        | Route.Training(TrainingRoute.Player id) -> $"/training/player/%d{id}"
        | Route.Training(TrainingRoute.Group id) -> $"/training/group/%d{id}"
        | Route.Scouting ScoutingRoute.Overview -> "/scouting"
        | Route.Scouting(ScoutingRoute.Assignment(ScoutingAssignmentId id)) -> $"/scouting/assignment/%d{id}"
        | Route.Scouting(ScoutingRoute.Report(ScoutingAssignmentId id)) -> $"/scouting/report/%d{id}"
        | Route.Transfers TransferRoute.Market -> "/transfers"
        | Route.Transfers(TransferRoute.Player id) -> $"/transfers/player/%d{id}"
        | Route.Transfers TransferRoute.Watchlist -> "/transfers/watchlist"
        | Route.Transfers TransferRoute.IncomingOffers -> "/transfers/incoming"
        | Route.Transfers TransferRoute.Negotiations -> "/transfers/negotiations"
        | Route.Transfers(TransferRoute.Negotiation id) -> $"/transfers/negotiation/%d{id}"
        | Route.Transfers(TransferRoute.Offer id) -> $"/transfers/offer/%d{id}"
        | Route.Transfers TransferRoute.History -> "/transfers/history"
        | Route.Finances FinanceRoute.Overview -> "/finances"
        | Route.Finances(FinanceRoute.Operation id) -> "/finances/operation/" + Uri.EscapeDataString id
        | Route.Calendar -> "/calendar"
        | Route.Competitions -> "/competitions"
        | Route.Competition id -> $"/competition/%d{id}"
        | Route.Fixture id -> $"/fixture/%d{id}"
        | Route.Match(id, Prematch) -> $"/match/%d{id}/prematch"
        | Route.Match(id, Live) -> $"/match/%d{id}/live"
        | Route.Match(id, Replay) -> $"/match/%d{id}/replay"
        | Route.Match(id, Postmatch) -> $"/match/%d{id}/postmatch"
        | Route.MatchEvent id -> $"/match-event/%d{id}"
        | Route.Board -> "/board"
        | Route.Youth -> "/youth"
        | Route.News -> "/news"
        | Route.SeasonClose -> "/season-close"
        | Route.Settings SettingsRoute.Overview -> "/settings"
        | Route.Settings(SettingsRoute.Category category) -> $"/settings/%A{category}"
        | Route.ModEditor -> "/mods"

    let fromPath (path: string) : Route option =
        let segments =
            if String.IsNullOrWhiteSpace path then
                []
            else
                path.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                |> Array.toList

        match segments with
        | [] -> Some Route.Home
        | [ "loading" ] -> Some Route.Loading
        | [ "setup" ] -> Some Route.Setup
        | [ "inbox" ] -> Some(Route.Inbox(InboxRoute None))
        | [ "inbox"; id ] -> parseInt id |> Option.map (Some >> InboxRoute >> Route.Inbox)
        | [ "squad" ] -> Some(Route.Squad SquadRoute.All)
        | [ "squad"; "by-position" ] -> Some(Route.Squad SquadRoute.ByPosition)
        | [ "squad"; id ] -> parseInt id |> Option.map (SquadRoute.Detail >> Route.Squad)
        | [ "club"; id ] -> parseInt id |> Option.map Route.Club
        | [ "staff" ] -> Some(Route.Staff StaffRoute.AllStaff)
        | [ "staff"; id ] -> parseInt id |> Option.map (StaffRoute.Detail >> Route.Staff)
        | [ "tactics" ] -> Some(Route.Tactics TacticsRoute.Overview)
        | [ "tactics"; "draft" ] -> Some(Route.Tactics TacticsRoute.Draft)
        | [ "tactics"; "plan"; id ] -> parseInt id |> Option.map (TacticsRoute.Plan >> Route.Tactics)
        | [ "training" ] -> Some(Route.Training TrainingRoute.Overview)
        | [ "training"; "player"; id ] -> parseInt id |> Option.map (TrainingRoute.Player >> Route.Training)
        | [ "training"; "group"; id ] -> parseInt id |> Option.map (TrainingRoute.Group >> Route.Training)
        | [ "scouting" ] -> Some(Route.Scouting ScoutingRoute.Overview)
        | [ "scouting"; "assignment"; id ] ->
            parseInt id
            |> Option.map (ScoutingAssignmentId >> ScoutingRoute.Assignment >> Route.Scouting)
        | [ "scouting"; "report"; id ] ->
            parseInt id
            |> Option.map (ScoutingAssignmentId >> ScoutingRoute.Report >> Route.Scouting)
        | [ "transfers" ] -> Some(Route.Transfers TransferRoute.Market)
        | [ "transfers"; "player"; id ] -> parseInt id |> Option.map (TransferRoute.Player >> Route.Transfers)
        | [ "transfers"; "watchlist" ] -> Some(Route.Transfers TransferRoute.Watchlist)
        | [ "transfers"; "incoming" ] -> Some(Route.Transfers TransferRoute.IncomingOffers)
        | [ "transfers"; "negotiations" ] -> Some(Route.Transfers TransferRoute.Negotiations)
        | [ "transfers"; "negotiation"; id ] -> parseInt id |> Option.map (TransferRoute.Negotiation >> Route.Transfers)
        | [ "transfers"; "history" ] -> Some(Route.Transfers TransferRoute.History)
        | [ "transfers"; "offer"; id ] -> parseInt id |> Option.map (TransferRoute.Offer >> Route.Transfers)
        | [ "finances" ] -> Some(Route.Finances FinanceRoute.Overview)
        | [ "finances"; "operation"; id ] -> Some(Route.Finances(FinanceRoute.Operation(Uri.UnescapeDataString id)))
        | [ "calendar" ] -> Some Route.Calendar
        | [ "competitions" ] -> Some Route.Competitions
        | [ "competition"; id ] -> parseInt id |> Option.map Route.Competition
        | [ "fixture"; id ] -> parseInt id |> Option.map Route.Fixture
        | [ "match"; id ] -> parseInt id |> Option.map (fun value -> Route.Match(value, Replay))
        | [ "match"; id; phase ] ->
            match parseInt id, phase with
            | Some value, "prematch" -> Some(Route.Match(value, Prematch))
            | Some value, "live" -> Some(Route.Match(value, Live))
            | Some value, "replay" -> Some(Route.Match(value, Replay))
            | Some value, "postmatch" -> Some(Route.Match(value, Postmatch))
            | _ -> None
        | [ "match-event"; id ] -> parseInt id |> Option.map Route.MatchEvent
        | [ "board" ] -> Some Route.Board
        | [ "youth" ] -> Some Route.Youth
        | [ "news" ] -> Some Route.News
        | [ "season-close" ] -> Some Route.SeasonClose
        | [ "settings" ] -> Some(Route.Settings SettingsRoute.Overview)
        | [ "settings"; category ] ->
            let parsed =
                match category with
                | "Appearance" -> Some SettingsCategory.Appearance
                | "MatchView" -> Some SettingsCategory.MatchView
                | "Simulation" -> Some SettingsCategory.Simulation
                | "Career" -> Some SettingsCategory.Career
                | "Data" -> Some SettingsCategory.Data
                | "Scouting" -> Some SettingsCategory.Scouting
                | "Advanced" -> Some SettingsCategory.Advanced
                | "All" -> Some SettingsCategory.All
                | _ -> None

            parsed |> Option.map (SettingsRoute.Category >> Route.Settings)
        | [ "mods" ] -> Some Route.ModEditor
        | _ -> None
