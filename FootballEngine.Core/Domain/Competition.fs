namespace FootballEngine.Domain



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

    let rankedStandings (comp: Competition) =
        comp.ClubIds
        |> List.map (fun id ->
            let standing =
                comp.Standings
                |> Map.tryFind id
                |> Option.defaultWith (fun () -> emptyStanding id)

            id, standing)
        |> List.sortByDescending (fun (_, s) -> s.Points, s.Won, s.GoalsFor - s.GoalsAgainst)

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

    let unplayedFixtures (comp: Competition) =
        comp.Fixtures |> Map.filter (fun _ f -> MatchFixture.isPending f)

    let fixturesFor (clubId: ClubId) (comp: Competition) =
        comp.Fixtures |> Map.filter (fun _ f -> MatchFixture.involves clubId f)
