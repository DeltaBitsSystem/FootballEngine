namespace FootballEngine.Domain

open System
open Inbox

/// A board's standing invitation to the unemployed user; accepting it replaces that club's head coach.
type JobOffer =
    { Id: int
      ClubId: ClubId
      Offered: DateTime
      Expires: DateTime
      Salary: decimal
      ContractYears: int }

/// One of the user's own engine matches as a rival can study it: the plan he took in and how it ended. Only matches
/// the user plays (and that reach TV) are here; rivals read nothing else.
type UserMatchRecord =
    { Date: DateTime
      OpponentClubId: ClubId
      Formation: Formation
      Instructions: TacticalInstructions
      Pattern: PatternVector
      GoalsFor: int
      GoalsAgainst: int }

type GameState =
    { ExecutionSeed: uint64
      MatchSeeds: Map<MatchId, uint64>
      CurrentDate: DateTime
      Season: int
      TrainingWeeksApplied: int
      Clubs: Map<ClubId, Club>
      Players: Map<PlayerId, Player>
      Staff: Map<StaffId, Staff>
      Competitions: Map<CompetitionId, Competition>
      Countries: Map<CountryCode, CountryData>
      DataLock: DataLock
      UserClubId: ClubId
      UserStaffId: StaffId
      PrimaryCountry: CountryCode
      Inbox: InboxMessage list
      NextInboxId: int
      PendingNegotiations: Map<int, Negotiation>
      NextNegotiationId: int
      Market: MarketState
      ProfileCache: Map<PlayerId, BehavioralProfile>
      PlayerExperiences: Map<PlayerId, PlayerExperience>
      TeamEmphasis: TeamEmphasis
      SeasonHistory: SeasonRecord list
      CareerHistory: ManagerCareerEvent list
      News: NewsItem list
      NextNewsId: int
      /// Played fixtures the pulse already absorbed; results can be committed after the day they are scheduled.
      PulseProcessed: Set<MatchId>
      PulseQueues: PulseQueues
      JobOffers: JobOffer list
      NextJobOfferId: int
      /// The user's last engine matches, newest first, capped at the configured study window.
      UserStudy: UserMatchRecord list }

module GameState =

    let getSquad (clubId: ClubId) (gs: GameState) : Player list =
        gs.Clubs
        |> Map.tryFind clubId
        |> Option.map (fun club -> club.PlayerIds |> List.choose gs.Players.TryFind)
        |> Option.defaultValue []

    let getUserSquad (gs: GameState) : Player list = getSquad gs.UserClubId gs

    let getStaff (clubId: ClubId) (gs: GameState) : Staff list =
        gs.Staff
        |> Map.values
        |> Seq.filter (fun s -> s.Contract |> Option.map _.ClubId = Some clubId)
        |> List.ofSeq

    let headCoach (clubId: ClubId) (gs: GameState) : Staff option =
        getStaff clubId gs |> List.tryFind (fun s -> s.Role = HeadCoach)

    let freeAgents (gs: GameState) : Player seq =
        gs.Players |> Map.values |> Seq.filter (fun p -> p.Affiliation = FreeAgent)

    let unemployedStaff (gs: GameState) : Staff seq =
        gs.Staff |> Map.values |> Seq.filter (fun s -> s.Status = Unemployed)

    let updatePlayer (p: Player) (gs: GameState) : GameState =
        { gs with
            Players = gs.Players |> Map.add p.Id p
            ProfileCache =
                gs.ProfileCache
                |> Map.add p.Id (Player.profile p ProfileWeightDefaults.defaults) }

    let getProfile (pid: PlayerId) (gs: GameState) : BehavioralProfile =
        gs.ProfileCache
        |> Map.tryFind pid
        |> Option.defaultWith (fun () -> Player.profile gs.Players[pid] ProfileWeightDefaults.defaults)

    let updateStaff (s: Staff) (gs: GameState) : GameState =
        { gs with
            Staff = gs.Staff |> Map.add s.Id s }

    let userManager (gs: GameState) : Staff option = gs.Staff |> Map.tryFind gs.UserStaffId

    let userManagerName (gs: GameState) : string =
        userManager gs |> Option.map _.Name |> Option.defaultValue ""

    /// `UserClubId` keeps the last club after a dismissal (history, news, the season's finishing line); only this
    /// says whether the user actually runs it, and therefore whether its matches wait for the user.
    let managedClub (gs: GameState) : ClubId option =
        userManager gs
        |> Option.filter Staff.isEmployed
        |> Option.bind Staff.clubId
        |> Option.filter ((=) gs.UserClubId)

    let isUserFixture (gs: GameState) (fixture: MatchFixture) : bool =
        managedClub gs |> Option.exists (fun club -> MatchFixture.involves club fixture)

    let getUserNextFixture (gs: GameState) : (MatchId * MatchFixture) option =
        gs.Competitions
        |> Map.toSeq
        |> Seq.collect (fun (_, comp) -> comp.Fixtures |> Map.toSeq)
        |> Seq.filter (fun (_, f) -> MatchFixture.isPending f && isUserFixture gs f)
        |> Seq.sortBy (fun (_, f) -> f.ScheduledDate)
        |> Seq.tryHead

    let getLineup (clubId: ClubId) (gs: GameState) : Lineup option =
        headCoach clubId gs |> Option.bind _.Attributes.Coaching.Lineup

    let setLineup (clubId: ClubId) (lineup: Lineup option) (gs: GameState) : GameState =
        match headCoach clubId gs with
        | None -> gs
        | Some coach ->
            let updatedCoach = Staff.setLineup lineup coach

            { gs with
                Staff = gs.Staff |> Map.add coach.Id updatedCoach }

    let updateLineup (clubId: ClubId) (updater: Lineup option -> Lineup option) (gs: GameState) : GameState =
        let newLineupOpt = gs |> getLineup clubId |> updater
        setLineup clubId newLineupOpt gs

    let addInboxMessage (message: InboxMessage) (gs: GameState) : GameState =
        { gs with
            Inbox = { message with Id = gs.NextInboxId } :: gs.Inbox
            NextInboxId = gs.NextInboxId + 1 }

    let markMessageAsRead (messageId: int) (gs: GameState) : GameState =
        { gs with
            Inbox = markAsRead messageId gs.Inbox }

    let markMessageActionTaken (messageId: int) (gs: GameState) : GameState =
        { gs with
            Inbox = markAsActionTaken messageId gs.Inbox }

    let unreadInboxCount (gs: GameState) : int = unreadCount gs.Inbox

    let pendingActionCount (gs: GameState) : int = pendingActionCount gs.Inbox
