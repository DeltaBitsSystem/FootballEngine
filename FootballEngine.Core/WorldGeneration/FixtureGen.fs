namespace FootballEngine.Generation

open System
open FootballEngine.Domain

module FixtureGen =

    let private makeFixture
        (firstMatchId: int)
        (idx: int)
        (compId: CompetitionId)
        (startDate: DateTime)
        (roundIdx: int)
        (homeId: ClubId)
        (awayId: ClubId)
        : MatchFixture =
        { Id = firstMatchId + idx
          CompetitionId = compId
          Round = None
          HomeClubId = homeId
          AwayClubId = awayId
          ScheduledDate = startDate.AddDays(float roundIdx * 7.0)
          Played = false
          HomeScore = None
          AwayScore = None
          Events = [] }

    let private singleRoundRobinPairs (clubIds: ClubId list) : (int * ClubId * ClubId) list =
        let teams =
            if clubIds.Length % 2 = 0 then
                Array.ofList clubIds
            else
                Array.append (Array.ofList clubIds) [| -1 |]

        let n = teams.Length
        let mutable rotating = teams[1..]
        let mutable result = []

        for round in 0 .. n - 2 do
            let pool = Array.append [| teams[0] |] rotating

            let pairs =
                [ for i in 0 .. n / 2 - 1 do
                      let h, a = pool[i], pool[n - 1 - i]

                      if h <> -1 && a <> -1 then
                          yield round, h, a ]

            result <- result @ pairs
            rotating <- Array.append [| rotating[rotating.Length - 1] |] rotating[.. rotating.Length - 2]

        result

    let private roundRobinPairs (clubIds: ClubId list) : (int * ClubId * ClubId) list =
        let firstLeg = singleRoundRobinPairs clubIds

        let rounds =
            (if clubIds.Length % 2 = 0 then
                 clubIds.Length
             else
                 clubIds.Length + 1)
            - 1

        [ for round, h, a in firstLeg do
              yield round, h, a
              yield round + rounds, a, h ]
        |> List.sortBy (fun (r, _, _) -> r)

    let forLeague
        (compId: CompetitionId)
        (clubIds: ClubId list)
        (startDate: DateTime)
        (firstMatchId: int)
        : MatchFixture list * int =
        let fixtures =
            roundRobinPairs clubIds
            |> List.mapi (fun i (roundIdx, h, a) -> makeFixture firstMatchId i compId startDate roundIdx h a)

        fixtures, firstMatchId + fixtures.Length

    let forGroupStage
        (compId: CompetitionId)
        (clubIds: ClubId list)
        (groupRules: GroupRules)
        (startDate: DateTime)
        (firstMatchId: int)
        : MatchFixture list * int =
        let groups =
            clubIds
            |> List.chunkBySize (max 1 (clubIds.Length / groupRules.GroupCount))
            |> List.truncate groupRules.GroupCount

        let mutable nextId = firstMatchId
        let mutable allFixtures = []

        for groupIdx, group in groups |> List.indexed do
            for roundIdx, h, a in singleRoundRobinPairs group do
                let f =
                    { makeFixture nextId 0 compId (startDate.AddDays(float groupIdx)) roundIdx h a with
                        Round = Some(GroupStage groupIdx) }

                allFixtures <- f :: allFixtures
                nextId <- nextId + 1

        List.rev allFixtures, nextId

    let forCupFormat
        (compId: CompetitionId)
        (clubIds: ClubId list)
        (format: CupFormat)
        (startDate: DateTime)
        (firstMatchId: int)
        : MatchFixture list * int =
        match format with
        | StraightKnockout _ -> forLeague compId clubIds startDate firstMatchId
        | GroupThenKnockout(groupRules, _) -> forGroupStage compId clubIds groupRules startDate firstMatchId
