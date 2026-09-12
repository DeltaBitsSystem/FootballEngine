namespace FootballEngine.Domain

open System


type LeagueLevel = LeagueLevel of int

type TiebreakerRule =
    | GoalDifference
    | GoalsScored
    | HeadToHead
    | HeadToHeadGoalDifference

type PromotionRule =
    | AutomaticPromotion of slots: int
    | PlayoffPromotion of slots: int

type RelegationRule =
    | AutomaticRelegation of slots: int
    | PlayoffRelegation of slots: int

type LeagueRules =
    { PointsForWin: int
      PointsForDraw: int
      Tiebreakers: TiebreakerRule list
      Promotion: PromotionRule list
      Relegation: RelegationRule list }

type TieResolution =
    | ReplayAtNeutral
    | ExtraTimeThenPenalties
    | AwayGoals
    | PenaltiesOnly

type LegFormat =
    | SingleLeg of TieResolution
    | TwoLegs of TieResolution

type GroupRules =
    { GroupCount: int
      TeamsPerGroup: int
      QualifyPerGroup: int
      PointsForWin: int
      PointsForDraw: int
      Tiebreakers: TiebreakerRule list }

type CupFormat =
    | StraightKnockout of LegFormat
    | GroupThenKnockout of GroupRules * LegFormat

type QualificationSlot =
    | LeaguePosition of level: LeagueLevel * fromPos: int * toPos: int
    | CupWinner of competitionName: string
    | TitleHolder
    | ConfederationSlot of slots: int

type CompetitionType =
    | NationalLeague of level: LeagueLevel * rules: LeagueRules
    | NationalCup of format: CupFormat * qualification: QualificationSlot list
    | InternationalCup of
        confederation: Confederation option *
        format: CupFormat *
        qualification: QualificationSlot list

type LeagueStanding =
    { ClubId: ClubId
      Played: int
      Won: int
      Drawn: int
      Lost: int
      GoalsFor: int
      GoalsAgainst: int
      Points: int }

type KnockoutTie =
    { TieId: int
      Round: Round
      HomeClubId: ClubId
      AwayClubId: ClubId
      Leg1FixtureId: MatchId option
      Leg2FixtureId: MatchId option
      AggregateScore: (int * int) option
      WinnerId: ClubId option }

type Competition =
    { Id: CompetitionId
      Name: string
      Type: CompetitionType
      Country: CountryCode option
      Season: int
      ClubIds: ClubId list
      Fixtures: Map<MatchId, MatchFixture>
      Standings: Map<ClubId, LeagueStanding>
      KnockoutTies: Map<int, KnockoutTie> }

type CompetitionConflict =
    | DuplicateFixtureId of MatchId
    | UnknownParticipant of MatchId * ClubId
    | TeamHasSimultaneousFixtures of DateTime * ClubId
    | InvalidParticipantCount of int
    | InvalidGroupFormat
    | InvalidFixtureDate of MatchId

type CompetitionCalendarError =
    | CalendarConflict of CompetitionConflict list
    | FixtureNotFound of MatchId
    | FixtureAlreadyPlayed of MatchId
    | RescheduleDateNotLater of MatchId * DateTime

type SeasonRecord =
    { Season: int
      CompetitionIds: CompetitionId list
      Winners: (CompetitionId * ClubId) list
      Promoted: ClubId list
      Relegated: ClubId list
      CompletedOn: DateTime }

type ManagerCareerEvent =
    | ManagerHired of DateTime * ClubId
    | ManagerDismissed of DateTime * ClubId * string
    | ManagerResigned of DateTime * ClubId
    | ObjectiveReviewed of DateTime * ClubId * bool * string

type BoardObjectiveReview =
    { ClubId: ClubId
      Objective: LeagueObjective
      Rank: int option
      Met: bool
      Explanation: string }

type PlayoffResolution =
    { Promoted: ClubId list
      Relegated: ClubId list
      PromotionCandidates: ClubId list
      RelegationCandidates: ClubId list }

module Competition =

    let private emptyStanding clubId =
        { ClubId = clubId
          Played = 0
          Won = 0
          Drawn = 0
          Lost = 0
          GoalsFor = 0
          GoalsAgainst = 0
          Points = 0 }

    let private rulesOf comp =
        match comp.Type with
        | NationalLeague(_, rules) -> rules.Tiebreakers
        | NationalCup(GroupThenKnockout(group, _), _)
        | InternationalCup(_, GroupThenKnockout(group, _), _) -> group.Tiebreakers
        | _ -> []

    let private headToHead (clubIds: Set<ClubId>) (comp: Competition) (clubId: ClubId) =
        comp.Fixtures
        |> Map.values
        |> Seq.filter (fun fixture ->
            fixture.Played
            && Set.contains fixture.HomeClubId clubIds
            && Set.contains fixture.AwayClubId clubIds)
        |> Seq.fold
            (fun (points, goalsFor, goalsAgainst) fixture ->
                match fixture.HomeScore, fixture.AwayScore with
                | Some home, Some away when fixture.HomeClubId = clubId ->
                    points
                    + (if home > away then 3
                       elif home = away then 1
                       else 0),
                    goalsFor + home,
                    goalsAgainst + away
                | Some home, Some away when fixture.AwayClubId = clubId ->
                    points
                    + (if away > home then 3
                       elif home = away then 1
                       else 0),
                    goalsFor + away,
                    goalsAgainst + home
                | _ -> points, goalsFor, goalsAgainst)
            (0, 0, 0)

    let rankedStandings (comp: Competition) =
        let tiebreakers = rulesOf comp

        let rankGroup entries =
            let tied = entries |> List.map fst |> Set.ofList

            entries
            |> List.sortWith (fun (leftId, left) (rightId, right) ->
                let compareRule rule =
                    match rule with
                    | GoalDifference ->
                        compare (right.GoalsFor - right.GoalsAgainst) (left.GoalsFor - left.GoalsAgainst)
                    | GoalsScored -> compare right.GoalsFor left.GoalsFor
                    | HeadToHead ->
                        compare
                            (headToHead tied comp rightId |> fun (p, _, _) -> p)
                            (headToHead tied comp leftId |> fun (p, _, _) -> p)
                    | HeadToHeadGoalDifference ->
                        let _, leftFor, leftAgainst = headToHead tied comp leftId
                        let _, rightFor, rightAgainst = headToHead tied comp rightId
                        compare (rightFor - rightAgainst) (leftFor - leftAgainst)

                let rec first =
                    function
                    | [] -> compare leftId rightId
                    | rule :: tail ->
                        let result = compareRule rule
                        if result = 0 then first tail else result

                let points = compare right.Points left.Points
                if points = 0 then first tiebreakers else points)

        comp.ClubIds
        |> List.map (fun id ->
            let standing =
                comp.Standings
                |> Map.tryFind id
                |> Option.defaultWith (fun () -> emptyStanding id)

            id, standing)
        |> rankGroup

    let leader (comp: Competition) : ClubId option =
        rankedStandings comp |> List.tryHead |> Option.map fst

    let bottomN (n: int) (comp: Competition) : ClubId list =
        comp.ClubIds
        |> List.map (fun id ->
            let standing =
                comp.Standings
                |> Map.tryFind id
                |> Option.defaultWith (fun () -> emptyStanding id)

            id, standing)
        |> List.sortBy (fun (_, s) -> s.Points, s.Won, s.GoalsFor - s.GoalsAgainst)
        |> List.truncate n
        |> List.map fst

    let private automaticPromotionSlots rules =
        rules
        |> List.sumBy (function
            | AutomaticPromotion n -> n
            | PlayoffPromotion _ -> 0)

    let private playoffPromotionSlots rules =
        rules
        |> List.sumBy (function
            | AutomaticPromotion _ -> 0
            | PlayoffPromotion n -> n)

    let private automaticRelegationSlots rules =
        rules
        |> List.sumBy (function
            | AutomaticRelegation n -> n
            | PlayoffRelegation _ -> 0)

    let private playoffRelegationSlots rules =
        rules
        |> List.sumBy (function
            | AutomaticRelegation _ -> 0
            | PlayoffRelegation n -> n)

    /// Resolves movement from the final table. Playoff places are selected from
    /// the positions around the configured line, so a changed table changes the
    /// participants without changing the competition definition.
    let resolvePlayoffs (comp: Competition) : PlayoffResolution option =
        match comp.Type with
        | NationalLeague(_, rules) ->
            let ranked = rankedStandings comp |> List.map fst
            let autoPromotion = automaticPromotionSlots rules.Promotion
            let playoffPromotion = playoffPromotionSlots rules.Promotion
            let autoRelegation = automaticRelegationSlots rules.Relegation
            let playoffRelegation = playoffRelegationSlots rules.Relegation

            let promotionCandidates =
                ranked |> List.skip autoPromotion |> List.truncate (playoffPromotion + 1)

            let relegationStart = max 0 (ranked.Length - autoRelegation - playoffRelegation - 1)

            let relegationCandidates =
                ranked |> List.skip relegationStart |> List.truncate (playoffRelegation + 1)

            if playoffPromotion = 0 && playoffRelegation = 0 then
                None
            else
                Some
                    { Promoted = promotionCandidates |> List.truncate playoffPromotion
                      Relegated = relegationCandidates |> List.rev |> List.truncate playoffRelegation
                      PromotionCandidates = promotionCandidates
                      RelegationCandidates = relegationCandidates }
        | _ -> None

    let unplayedFixtures (comp: Competition) =
        comp.Fixtures |> Map.filter (fun _ f -> MatchFixture.isPending f)

    let isComplete (comp: Competition) =
        comp.Fixtures |> Map.forall (fun _ fixture -> fixture.Played)

    let fixturesFor (clubId: ClubId) (comp: Competition) =
        comp.Fixtures |> Map.filter (fun _ f -> MatchFixture.involves clubId f)

    let validateCalendar (comp: Competition) : Result<unit, CompetitionConflict list> =
        let participantSet = comp.ClubIds |> Set.ofList

        let duplicateIds =
            comp.Fixtures
            |> Map.toList
            |> List.groupBy fst
            |> List.choose (fun (id, fixtures) ->
                if fixtures.Length > 1 then
                    Some(DuplicateFixtureId id)
                else
                    None)

        let invalidParticipants =
            comp.Fixtures
            |> Map.toList
            |> List.collect (fun (id, fixture) ->
                [ if not (Set.contains fixture.HomeClubId participantSet) then
                      yield UnknownParticipant(id, fixture.HomeClubId)
                  if not (Set.contains fixture.AwayClubId participantSet) then
                      yield UnknownParticipant(id, fixture.AwayClubId) ])

        let invalidDates =
            comp.Fixtures
            |> Map.toList
            |> List.choose (fun (id, fixture) ->
                if
                    fixture.ScheduledDate.Year <> comp.Season
                    && fixture.ScheduledDate.Year <> comp.Season + 1
                then
                    Some(InvalidFixtureDate id)
                else
                    None)

        let simultaneous =
            comp.Fixtures
            |> Map.toList
            |> List.filter (fun (_, fixture) -> not fixture.Played)
            |> List.collect (fun (id, fixture) ->
                [ fixture.ScheduledDate.Date, fixture.HomeClubId, id
                  fixture.ScheduledDate.Date, fixture.AwayClubId, id ])
            |> List.groupBy (fun (date, clubId, _) -> date, clubId)
            |> List.choose (fun ((date, clubId), values) ->
                if values.Length > 1 then
                    Some(TeamHasSimultaneousFixtures(date, clubId))
                else
                    None)

        let formatErrors =
            match comp.Type with
            | NationalLeague(_, rules) when rules.PointsForWin < rules.PointsForDraw || rules.Tiebreakers.IsEmpty ->
                [ InvalidParticipantCount comp.ClubIds.Length ]
            | NationalCup(GroupThenKnockout(group, _), _)
            | InternationalCup(_, GroupThenKnockout(group, _), _) when group.GroupCount < 1 || group.TeamsPerGroup < 2 ->
                [ InvalidGroupFormat ]
            | _ -> []

        match duplicateIds @ invalidParticipants @ invalidDates @ simultaneous @ formatErrors with
        | [] -> Ok()
        | errors -> Error errors

    let reschedule
        (newDate: DateTime)
        (matchId: MatchId)
        (comp: Competition)
        : Result<Competition, CompetitionCalendarError> =
        match comp.Fixtures |> Map.tryFind matchId with
        | None -> Error(FixtureNotFound matchId)
        | Some fixture when fixture.Played -> Error(FixtureAlreadyPlayed matchId)
        | Some fixture when newDate <= fixture.ScheduledDate ->
            Error(RescheduleDateNotLater(matchId, fixture.ScheduledDate))
        | Some fixture ->
            let updated = { fixture with ScheduledDate = newDate }

            let next =
                { comp with
                    Fixtures = comp.Fixtures |> Map.add matchId updated }

            match validateCalendar next with
            | Ok() -> Ok next
            | Error conflicts -> Error(CalendarConflict conflicts)
