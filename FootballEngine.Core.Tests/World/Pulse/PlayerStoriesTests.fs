namespace FootballEngine.Core.Tests.World.Pulse

open System
open Expecto
open FootballEngine.Domain
open FootballEngine.Domain.LineupOps
open FootballEngine.World
open FootballEngine.World.Phases
open FootballEngine.World.Pulse
open FootballEngine.Core.Tests.World

module PlayerStoriesTests =

    let private config = WorldPulseConfig.defaultWorldPulse

    // Every budget is wide so a test asserts the detector, not the daily news cap.
    let private wide =
        { config with
            DailyBudgetUserClub = 100
            DailyBudgetUserLeague = 100
            DailyBudgetWorld = 100 }

    let private world = lazy (WorldGenerationTests.generate 0xC02UL [])

    let private start (gs: GameState) = gs.CurrentDate.Date

    let private squadOf club (gs: GameState) =
        gs.Clubs[club].PlayerIds |> List.choose gs.Players.TryFind

    let private ranked club gs =
        squadOf club gs
        |> List.filter (fun p -> (Player.contractOf p).IsSome)
        |> PlayerStories.bySkill

    let private elevenOf club (gs: GameState) =
        (GameState.getLineup club gs).Value.Slots
        |> List.choose _.PlayerId
        |> Set.ofList

    let private setPlayer (id: PlayerId) (f: Player -> Player) (gs: GameState) =
        { gs with
            Players = Map.add id (f gs.Players[id]) gs.Players }

    /// Lineup complete, every contract long, everybody fit and calm, so only the scenario's own cause can act.
    let private prepared (club: ClubId) (gs: GameState) : GameState =
        let gs = LineupOps.ensureForClub club gs

        squadOf club gs
        |> List.fold
            (fun state p ->
                setPlayer
                    p.Id
                    (fun x ->
                        { x with
                            Affiliation =
                                Contracted(
                                    club,
                                    { Salary = 1000m
                                      ExpiryYear = (start gs).Year + 5 }
                                )
                            Status = Available
                            Morale = 60
                            Unrest = None
                            BenchWeeks = 0
                            UnrestWeeks = 0
                            TransferListed = false })
                    state)
            gs

    let private aiClub (gs: GameState) =
        gs.Clubs.Values
        |> Seq.filter (fun c -> c.Id <> gs.UserClubId)
        |> Seq.sortBy _.Id
        |> Seq.find (fun c -> (squadOf c.Id gs).Length >= 16 && (GameState.headCoach c.Id gs).IsSome)
        |> _.Id

    let private swap club (out: PlayerId) (inn: PlayerId) (gs: GameState) =
        GameState.updateLineup
            club
            (Option.map (fun lineup ->
                { lineup with
                    Slots =
                        lineup.Slots
                        |> List.map (fun slot ->
                            if slot.PlayerId = Some out then
                                { slot with PlayerId = Some inn }
                            else
                                slot) }))
            gs

    type private Scene =
        {
            State: GameState
            Club: ClubId
            /// A top-13 player who starts, to be benched at will.
            Star: PlayerId
            /// The player who takes his place and is outside the top 13.
            Sub: PlayerId
        }

    let private scene (club: ClubId) (gs: GameState) : Scene =
        let gs = prepared club gs
        let eleven = elevenOf club gs
        let table = ranked club gs

        let regulars =
            table |> List.truncate config.RegularStarterRank |> List.map _.Id |> Set.ofList

        let star =
            table |> List.find (fun p -> regulars.Contains p.Id && eleven.Contains p.Id)

        let sub = table |> List.rev |> List.find (fun p -> not (eleven.Contains p.Id))

        Expect.isFalse (regulars.Contains sub.Id) "the substitute is not a regular"

        { State = gs
          Club = club
          Star = star.Id
          Sub = sub.Id }

    let private benched (s: Scene) = swap s.Club s.Star s.Sub s.State

    /// One pulse per week, exactly as the scheduler's weekly cadence would run it.
    let private weeks (n: int) (gs: GameState) =
        let first = start gs

        [ 1..n ]
        |> List.fold (fun state i -> WorldPulse.runOn true config (first.AddDays(7.0 * float i)) state) gs

    let private newsOf pick (gs: GameState) =
        gs.News |> List.choose (fun n -> pick n.Kind)

    let private requests pid gs =
        newsOf
            (function
            | NewsKind.TransferRequest(p, _, reason) when p = pid -> Some reason
            | _ -> None)
            gs

    let private running pid gs =
        newsOf
            (function
            | NewsKind.ContractRunningOut(p, c, months) when p = pid -> Some(c, months)
            | _ -> None)
            gs

    let private minutesTests =
        testList
            "lack of minutes"
            [ testCase "a regular out of the XI for the configured weeks gets LackOfMinutes, not before"
              <| fun _ ->
                  let s = scene (aiClub world.Value) world.Value
                  let before = weeks (config.UnrestBenchWeeks - 1) (benched s)
                  Expect.equal before.Players[s.Star].BenchWeeks (config.UnrestBenchWeeks - 1) "counting"
                  Expect.isNone before.Players[s.Star].Unrest "one week short"

                  let at = weeks config.UnrestBenchWeeks (benched s)
                  Expect.equal at.Players[s.Star].Unrest (Some UnrestReason.LackOfMinutes) "at the threshold"
                  Expect.equal at.Players[s.Star].UnrestWeeks 1 "unrest starts counting"

              testCase "starting again resets the count so the threshold needs a fresh run"
              <| fun _ ->
                  let s = scene (aiClub world.Value) world.Value
                  let sat = weeks 2 (benched s)
                  Expect.equal sat.Players[s.Star].BenchWeeks 2 "two weeks out"

                  let back = weeks 1 (swap s.Club s.Sub s.Star sat)
                  Expect.equal back.Players[s.Star].BenchWeeks 0 "reset by starting"

                  let again = weeks (config.UnrestBenchWeeks - 1) (swap s.Club s.Star s.Sub back)
                  Expect.isNone again.Players[s.Star].Unrest "the earlier two weeks do not carry over"

              testCase "a player outside the top ranks never gets unrest however long he sits"
              <| fun _ ->
                  let s = scene (aiClub world.Value) world.Value
                  let gs = setPlayer s.Sub (fun p -> { p with BenchWeeks = 10 }) s.State
                  let after = weeks (config.UnrestBenchWeeks * 2) gs
                  Expect.isNone after.Players[s.Sub].Unrest "not a regular"
                  Expect.equal after.Players[s.Sub].BenchWeeks 0 "benching is normal for him"
                  Expect.equal after.Players[s.Sub].Morale 60 "no morale cost"

              testCase "an injured regular is not 'frozen out'"
              <| fun _ ->
                  let s = scene (aiClub world.Value) world.Value

                  let hurt =
                      setPlayer
                          s.Star
                          (fun p ->
                              { p with
                                  Status = Injured(InjurySeverity.Minor, (start s.State).AddDays 60.0) })
                          (benched s)

                  let after = weeks (config.UnrestBenchWeeks + 2) hurt
                  Expect.isNone after.Players[s.Star].Unrest "unavailable players do not count their absence" ]

    let private unrestTests =
        let scenario (club: ClubId) =
            let s = scene club world.Value
            s, benched s

        testList
            "unrest effects"
            [ testCase "each week of unrest lowers morale by the configured amount and clamps at zero"
              <| fun _ ->
                  let s, gs = scenario (aiClub world.Value)
                  let begins = config.UnrestBenchWeeks
                  let one = weeks begins gs
                  let two = weeks (begins + 1) gs
                  Expect.equal one.Players[s.Star].Morale (60 - config.UnrestMoralePerWeek) "first unrest week"
                  Expect.equal two.Players[s.Star].Morale (60 - 2 * config.UnrestMoralePerWeek) "second unrest week"

                  let low = setPlayer s.Star (fun p -> { p with Morale = 1 }) gs
                  Expect.equal (weeks (begins + 1) low).Players[s.Star].Morale 0 "never below the morale floor"

              testCase "a transfer request is made once, at the configured week, and the player becomes listed"
              <| fun _ ->
                  let s, gs = scenario (aiClub world.Value)
                  let begins = config.UnrestBenchWeeks
                  let early = weeks (begins + config.TransferRequestWeeks - 2) gs
                  Expect.isEmpty (requests s.Star early) "not yet"
                  Expect.isFalse early.Players[s.Star].TransferListed "not listed yet"

                  let at = weeks (begins + config.TransferRequestWeeks - 1) gs
                  Expect.equal (requests s.Star at) [ UnrestReason.LackOfMinutes ] "one request with its cause"
                  Expect.isTrue at.Players[s.Star].TransferListed "listed for the market"

                  let later = weeks (begins + config.TransferRequestWeeks + 5) gs
                  Expect.equal (requests s.Star later).Length 1 "never repeated while the unrest lasts"
                  Expect.isTrue later.Players[s.Star].TransferListed "stays listed"

              testCase "the user's inbox hears about his own player's request, an AI club's request stays out of it"
              <| fun _ ->
                  let user = world.Value.UserClubId
                  let s, gs = scenario user
                  let after = weeks (config.UnrestBenchWeeks + config.TransferRequestWeeks - 1) gs

                  // Other regulars the lineup leaves out may ask too; this player's own message must exist exactly once.
                  let mails =
                      after.Inbox
                      |> List.filter (fun m ->
                          m.Category = InboxMessageCategory.Transfer
                          && m.Subject.Contains after.Players[s.Star].Name)

                  Expect.equal mails.Length 1 "one request message for him"
                  Expect.isFalse mails.Head.RequiresAction "informational: the model has no action for it"

                  let ai, aiGs = scenario (aiClub world.Value)
                  // Generated names can collide with the user's squad, so the AI player gets a name nobody else has.
                  let aiGs = setPlayer ai.Star (fun p -> { p with Name = "Nobody Else Zzyzx" }) aiGs
                  let aiAfter = weeks (config.UnrestBenchWeeks + config.TransferRequestWeeks - 1) aiGs
                  Expect.isNonEmpty (requests ai.Star aiAfter) "the AI player did ask"

                  Expect.isFalse
                      (aiAfter.Inbox |> List.exists (fun m -> m.Subject.Contains "Zzyzx"))
                      "but the user is not told about it"

              testCase "a listed player can be bought by a club the seller would otherwise refuse"
              <| fun _ ->
                  let s, gs = scenario (aiClub world.Value)

                  let listed =
                      (weeks (config.UnrestBenchWeeks + config.TransferRequestWeeks - 1) gs).Players[s.Star]

                  Expect.isTrue listed.TransferListed "precondition"

                  let seller =
                      { gs.Clubs[s.Club] with
                          Reputation = 6000 }

                  let buyer =
                      { gs.Clubs[s.Club] with
                          Id = 9999
                          Reputation = 1000 }

                  let fee = Player.playerValue listed.CurrentSkill

                  Expect.equal
                      (TransferNegotiation.clubResponse buyer seller { listed with TransferListed = false } fee)
                      OfferStatus.RejectedByClub
                      "a settled player stays out of reach"

                  Expect.equal
                      (TransferNegotiation.clubResponse buyer seller listed fee)
                      OfferStatus.AcceptedByClub
                      "a player who asked to leave is a sale candidate"

              testCase "unrest, listing and counters clear when the cause disappears"
              <| fun _ ->
                  let s, gs = scenario (aiClub world.Value)
                  let unhappy = weeks (config.UnrestBenchWeeks + config.TransferRequestWeeks - 1) gs
                  Expect.isTrue unhappy.Players[s.Star].TransferListed "precondition"

                  let calm = weeks 1 (swap s.Club s.Sub s.Star unhappy)
                  let p = calm.Players[s.Star]
                  Expect.isNone p.Unrest "unrest gone"
                  Expect.equal p.UnrestWeeks 0 "counter reset"
                  Expect.equal p.BenchWeeks 0 "bench counter reset"
                  Expect.isFalse p.TransferListed "no longer for sale" ]

    let private contractTests =
        let expiring (s: Scene) pid =
            let year = (start s.State).Year - 1

            setPlayer
                pid
                (fun p ->
                    { p with
                        Affiliation = Contracted(s.Club, { Salary = 1000m; ExpiryYear = year }) })
                s.State

        testList
            "contract in the final months"
            [ testCase "a regular inside the warning window gets ContractStalled and one ContractRunningOut"
              <| fun _ ->
                  let s = scene (aiClub world.Value) world.Value
                  let gs = expiring s s.Star
                  let contract = (Player.contractOf gs.Players[s.Star]).Value
                  let months = PlayerStories.monthsLeft (start gs) contract
                  Expect.isLessThanOrEqual months config.ContractWarningMonths "scenario really is inside the window"

                  let after = weeks 4 gs
                  Expect.equal after.Players[s.Star].Unrest (Some UnrestReason.ContractStalled) "stalled"
                  Expect.equal (running s.Star after).Length 1 "announced once over four weeks"
                  Expect.equal (fst (running s.Star after).Head) s.Club "for his club"

              testCase "outside the window, or for a non-regular, nothing happens"
              <| fun _ ->
                  let s = scene (aiClub world.Value) world.Value
                  let year = (start s.State).Year + 1

                  let far =
                      setPlayer
                          s.Star
                          (fun p ->
                              { p with
                                  Affiliation = Contracted(s.Club, { Salary = 1000m; ExpiryYear = year }) })
                          s.State

                  Expect.isTrue
                      (PlayerStories.monthsLeft (start far) (Player.contractOf far.Players[s.Star]).Value > config.ContractWarningMonths)
                      "scenario is outside the window"

                  Expect.isNone (weeks 2 far).Players[s.Star].Unrest "plenty of contract left"
                  let reserve = weeks 2 (expiring s s.Sub)
                  Expect.isNone reserve.Players[s.Sub].Unrest "a reserve is not worried"
                  Expect.isEmpty (running s.Sub reserve) "and is not news"

              testCase "renewing before the unrest turns into anything clears it"
              <| fun _ ->
                  let s = scene (aiClub world.Value) world.Value
                  let stalled = weeks 1 (expiring s s.Star)
                  Expect.equal stalled.Players[s.Star].Unrest (Some UnrestReason.ContractStalled) "precondition"

                  let renewed =
                      setPlayer
                          s.Star
                          (fun p ->
                              { p with
                                  Affiliation =
                                      Contracted(
                                          s.Club,
                                          { Salary = 2000m
                                            ExpiryYear = (start s.State).Year + 4 }
                                      ) })
                          stalled

                  let after = weeks 1 renewed
                  Expect.isNone after.Players[s.Star].Unrest "renewal ends the grievance"
                  Expect.equal after.Players[s.Star].UnrestWeeks 0 "counter reset"

              testCase "the user's inbox gets an informational contract message"
              <| fun _ ->
                  let user = world.Value.UserClubId
                  let s = scene user world.Value
                  let after = weeks 3 (expiring s s.Star)

                  let mails =
                      after.Inbox
                      |> List.filter (fun m ->
                          m.Category = InboxMessageCategory.Contract
                          && m.Subject.Contains after.Players[s.Star].Name)

                  Expect.equal mails.Length 1 "once"
                  Expect.isFalse mails.Head.RequiresAction "no natural action" ]

    let private milestoneTests =
        /// Appearance number `matches` of a player who has scored `goals` in total, `inMatch` of them today.
        let appearance matches goals inMatch =
            { Player = 0
              Matches = matches
              GoalsAfter = goals
              GoalsInMatch = inMatch }

        /// Milestone news produced for a player of the given age whose queue holds `appearances`.
        let fired (age: int) (appearances: PlayerAppearance list) =
            let gs = world.Value
            let pid = squadOf (aiClub gs) gs |> List.head |> _.Id
            let d = start gs
            let gs = setPlayer pid (fun p -> { p with Birthday = d.AddYears(-age) }) gs

            let gs =
                { gs with
                    PulseQueues =
                        { gs.PulseQueues with
                            Appearances = appearances |> List.map (fun a -> { a with Player = pid }) } }

            let after = WorldPulse.runOn false wide d gs

            after,
            newsOf
                (function
                | NewsKind.Milestone(p, kind, value) when p = pid -> Some(kind, value)
                | _ -> None)
                after

        testList
            "milestones"
            [ testCase "the 100th appearance fires; the 99th and the 101st do not"
              <| fun _ ->
                  Expect.equal (snd (fired 27 [ appearance 100 5 0 ])) [ MilestoneKind.Appearances, 100 ] "100th"
                  Expect.isEmpty (snd (fired 27 [ appearance 99 5 0 ])) "99th"
                  Expect.isEmpty (snd (fired 27 [ appearance 101 5 0 ])) "101st"

              testCase "the 50th goal fires on the match that reaches it, not the ones either side"
              <| fun _ ->
                  Expect.equal (snd (fired 27 [ appearance 80 50 1 ])) [ MilestoneKind.Goals, 50 ] "50th"
                  Expect.isEmpty (snd (fired 27 [ appearance 80 49 1 ])) "49 goals"
                  Expect.isEmpty (snd (fired 27 [ appearance 80 51 1 ])) "51 goals, 50 was last match"

                  Expect.equal
                      (snd (fired 27 [ appearance 80 51 2 ]))
                      [ MilestoneKind.Goals, 50 ]
                      "a brace from 49 passes 50"

              testCase "a debut is news only for the young"
              <| fun _ ->
                  Expect.equal
                      (snd (fired config.DebutMaxAge [ appearance 1 0 0 ]))
                      [ MilestoneKind.Debut, 1 ]
                      "teenager"

                  Expect.isEmpty (snd (fired (config.DebutMaxAge + 1) [ appearance 1 0 0 ])) "too old"
                  Expect.isEmpty (snd (fired 18 [ appearance 2 0 0 ])) "not the first match"

              testCase "the queue is consumed so a milestone is never announced twice"
              <| fun _ ->
                  let after, news = fired 27 [ appearance 200 10 0 ]
                  Expect.equal news [ MilestoneKind.Appearances, 200 ] "announced"
                  Expect.isEmpty after.PulseQueues.Appearances "drained"

                  let again = WorldPulse.runOn false wide ((start after).AddDays 20.0) after
                  Expect.equal again.News.Length after.News.Length "nothing new on a later day"

              testCase "a played match records exactly the appearances the experience counters count"
              <| fun _ ->
                  let gs = world.Value

                  let fixtures =
                      gs.Competitions.Values
                      |> Seq.collect (fun c -> c.Fixtures |> Map.toSeq)
                      |> Seq.filter (fun (_, f) ->
                          MatchFixture.isPending f && not (MatchFixture.involves gs.UserClubId f))
                      |> Seq.sortBy fst
                      |> Seq.truncate 1
                      |> List.ofSeq

                  let played, _ =
                      MatchPhase.runFixtureExecutionsWithBalance
                          FootballEngine.Core.Match.BalanceConfig.defaultConfig
                          fixtures
                          gs

                  let recorded = played.PulseQueues.Appearances
                  Expect.isGreaterThanOrEqual recorded.Length 22 "both starting elevens"

                  Expect.equal
                      (recorded |> List.map _.Player |> List.distinct).Length
                      recorded.Length
                      "one entry per player"

                  for a in recorded do
                      let experience = played.PlayerExperiences[a.Player]
                      Expect.equal a.Matches experience.TotalMatchesPlayed "appearance count mirrors the counter"
                      Expect.equal a.Matches 1 "first match of a fresh world"
                      Expect.equal a.GoalsAfter experience.GoalsScored "goal total mirrors the counter" ]

    let private breakoutTests =
        let youngster (age: int) (gain: int) =
            let gs = world.Value
            let club = aiClub gs
            let pid = (ranked club gs |> List.last).Id
            let d = start gs

            pid,
            setPlayer
                pid
                (fun p ->
                    { p with
                        Birthday = d.AddYears(-age)
                        CurrentSkill = 70
                        SeasonStartSkill = 70 - gain })
                gs

        let breakouts pid gs =
            newsOf
                (function
                | NewsKind.Breakout(p, _) when p = pid -> Some()
                | _ -> None)
                gs

        testList
            "breakout"
            [ testCase "a youngster who gains the configured amount is announced once, not every week"
              <| fun _ ->
                  let pid, gs = youngster 19 config.BreakoutGain
                  Expect.equal (breakouts pid (weeks 4 gs)).Length 1 "once over four weeks"

              testCase "a smaller gain, or a player past the age limit, is not a breakout"
              <| fun _ ->
                  let pid, short = youngster 19 (config.BreakoutGain - 1)
                  Expect.isEmpty (breakouts pid (weeks 2 short)) "one point short"
                  let pid, old = youngster 23 (config.BreakoutGain + 10)
                  Expect.isEmpty (breakouts pid (weeks 2 old)) "23 years old"
                  let pid, edge = youngster config.BreakoutMaxAge config.BreakoutGain
                  Expect.isEmpty (breakouts pid (weeks 2 edge)) "the limit itself is exclusive"

              testCase "the season roll still sees a gain made during the roll, then resets every baseline"
              <| fun _ ->
                  let pid, gs = youngster 19 config.BreakoutGain

                  let rolled =
                      { gs with
                          CurrentDate = DateTime(gs.Season + 1, 7, 1) }

                  let after = PlayerStories.seasonRoll wide gs rolled
                  Expect.equal (breakouts pid after).Length 1 "announced at the roll"

                  Expect.all
                      (after.Players.Values |> Seq.filter (fun p -> p.Affiliation <> Retired))
                      (fun p -> p.SeasonStartSkill = p.CurrentSkill)
                      "new season, new baseline" ]

    let private retirementTests =
        let rollDate (gs: GameState) = DateTime(gs.Season + 1, 7, 1)

        /// Rolls the season with the chosen player forced to `age`/`skill`; returns the state after the roll.
        let roll (age: int) (pick: Player list -> Set<PlayerId> -> Player) (skill: int option) =
            let gs0 = prepared (aiClub world.Value) world.Value
            let club = aiClub world.Value
            let target = pick (ranked club gs0) (elevenOf club gs0)
            let d = rollDate gs0

            let gs =
                setPlayer
                    target.Id
                    (fun p ->
                        { p with
                            Birthday = d.AddYears(-age)
                            CurrentSkill = skill |> Option.defaultValue p.CurrentSkill })
                    gs0

            target.Id, club, gs, PlayerStories.seasonRoll wide gs { gs with CurrentDate = d }

        let retiredNews pid gs =
            newsOf
                (function
                | NewsKind.Retirement(p, age) when p = pid -> Some age
                | _ -> None)
                gs

        testList
            "retirement"
            [ testCase "past the hard age a player retires: out of the club and lineup, with news"
              <| fun _ ->
                  let pid, club, before, after =
                      roll
                          config.RetireHardAge
                          (fun table eleven -> table |> List.find (fun p -> eleven.Contains p.Id))
                          None

                  Expect.equal after.Players[pid].Affiliation Retired "marked retired"
                  Expect.isFalse (List.contains pid after.Clubs[club].PlayerIds) "left the squad"
                  Expect.isFalse ((elevenOf club after).Contains pid) "and the lineup"
                  Expect.equal (elevenOf club after).Count 11 "whose hole is refilled so the club can still play"
                  Expect.equal (retiredNews pid after) [ config.RetireHardAge ] "announced with his age"
                  Expect.isNone after.Players[pid].Unrest "no lingering grievance"
                  Expect.isTrue (List.contains pid before.Clubs[club].PlayerIds) "he was in the squad before"

              testCase "an ageing player below his club's median retires; the club's best does not"
              <| fun _ ->
                  let pid, _, _, after =
                      roll config.RetireMinAge (fun table _ -> List.last table) (Some 1)

                  Expect.equal after.Players[pid].Affiliation Retired "declined veteran retires"

                  let pid, _, _, kept =
                      roll config.RetireMinAge (fun table _ -> List.head table) (Some 200)

                  Expect.notEqual kept.Players[pid].Affiliation Retired "star veteran carries on"
                  Expect.isEmpty (retiredNews pid kept) "and is not news"

              testCase "a player below the minimum age never retires, however weak"
              <| fun _ ->
                  let pid, _, _, after =
                      roll (config.RetireMinAge - 1) (fun table _ -> List.last table) (Some 1)

                  Expect.notEqual after.Players[pid].Affiliation Retired "33 and weak still plays"

                  let pid, _, _, young = roll 30 (fun table _ -> List.last table) (Some 1)
                  Expect.notEqual young.Players[pid].Affiliation Retired "30 and weak still plays"

              testCase "retired players are gone from the market: not free agents, no open negotiation survives"
              <| fun _ ->
                  let gs0 = prepared (aiClub world.Value) world.Value
                  let club = aiClub world.Value
                  let target = (ranked club gs0).Head
                  let d = rollDate gs0

                  let negotiation =
                      { Id = 77
                        BuyerClubId = gs0.UserClubId
                        SellerClubId = club
                        PlayerId = target.Id
                        Stage = Approaching
                        Deadline = d.AddDays 20.0 }

                  let gs =
                      { setPlayer
                            target.Id
                            (fun p ->
                                { p with
                                    Birthday = d.AddYears(-config.RetireHardAge) })
                            gs0 with
                          PendingNegotiations = Map.ofList [ 77, negotiation ] }

                  let after = PlayerStories.seasonRoll wide gs { gs with CurrentDate = d }

                  Expect.isFalse
                      (GameState.freeAgents after |> Seq.exists (fun p -> p.Id = target.Id))
                      "not a free agent"

                  Expect.isFalse (after.PendingNegotiations.ContainsKey 77) "negotiation cancelled"

                  Expect.isNone (Player.clubOf after.Players[target.Id]) "no club"

                  Expect.isEmpty
                      (Selection.select after.Players[target.Id].Nationality d 1000 after
                       |> List.filter (fun c -> c.PlayerId = target.Id))
                      "not selectable for the national team" ]

    let private configTests =
        testList
            "config"
            [ testCase "defaults validate and each new tunable rejects nonsense"
              <| fun _ ->
                  Expect.isEmpty (WorldPulseConfig.validate config) "defaults are valid"

                  let bad =
                      [ "rank", { config with RegularStarterRank = 0 }
                        "bench weeks", { config with UnrestBenchWeeks = 0 }
                        "warning months",
                        { config with
                            ContractWarningMonths = 0 }
                        "morale", { config with UnrestMoralePerWeek = -1 }
                        "request weeks", { config with TransferRequestWeeks = 0 }
                        "breakout age", { config with BreakoutMaxAge = 10 }
                        "breakout gain", { config with BreakoutGain = 0 }
                        "debut age", { config with DebutMaxAge = 5 }
                        "appearances unsorted",
                        { config with
                            AppearanceMilestones = [ 200; 100 ] }
                        "goals non-positive",
                        { config with
                            GoalMilestones = [ 0; 50 ] }
                        "retire ages",
                        { config with
                            RetireHardAge = config.RetireMinAge }
                        "fee", { config with TransferNewsMinFee = -1m }
                        "top rank", { config with StarSaleTopRank = 0 }
                        "penalty",
                        { config with
                            FansStarSalePenalty = 101 }
                        "boost", { config with FansMarqueeBoost = -1 }
                        "chance", { config with RumourChance = 1.5 } ]

                  for name, c in bad do
                      Expect.isNonEmpty (WorldPulseConfig.validate c) $"{name} must be rejected" ]

    let private rollTests =
        /// Clubs that appear in a pending fixture the match builder cannot set up.
        let unplayable (gs: GameState) =
            gs.Competitions.Values
            |> Seq.collect (fun c -> c.Fixtures.Values)
            |> Seq.filter MatchFixture.isPending
            |> Seq.filter (fun f ->
                (MatchContextBuilder.build gs f FootballEngine.Core.Match.BalanceConfig.defaultConfig).IsNone)
            |> Seq.collect (fun f -> [ f.HomeClubId; f.AwayClubId ])
            |> Set.ofSeq

        testList
            "season turnover"
            [ testCase "injuries that end during the break are healed for the next kickoff, longer ones are kept"
              <| fun _ ->
                  let gs = world.Value
                  let balance = FootballEngine.Core.Match.BalanceConfig.defaultConfig
                  let kickoff = DateTime(gs.Season + 1, 7, 1)

                  let contracted =
                      gs.Players.Values
                      |> Seq.filter (fun p -> (Player.clubOf p).IsSome)
                      |> Seq.truncate 2
                      |> Seq.toList

                  let healing, longTerm = contracted[0], contracted[1]

                  let injure (until: DateTime) (p: Player) =
                      { p with
                          Status = Injured(InjurySeverity.Minor, until) }

                  let injured =
                      { gs with
                          Players =
                              gs.Players
                              |> Map.add healing.Id (injure (gs.CurrentDate.AddDays 14.0) healing)
                              |> Map.add longTerm.Id (injure (kickoff.AddDays 30.0) longTerm) }

                  let next = SeasonPhase.runEndOfSeason balance injured

                  Expect.equal next.Players[healing.Id].Status Available "fit again before the season starts"

                  Expect.equal
                      next.Players[longTerm.Id].Status
                      (Injured(InjurySeverity.Minor, kickoff.AddDays 30.0))
                      "an injury running into the new season is kept"

              testCase "the roll retires people without leaving any club with a lineup it did not already lack"
              <| fun _ ->
                  let gs = world.Value
                  let balance = FootballEngine.Core.Match.BalanceConfig.defaultConfig

                  let season =
                      WorldRunner.advanceToNextSeasonWithBalance balance (WorldClockOps.init gs.Season) gs

                  let next = season.NextSeasonGs

                  let retired =
                      next.Players.Values
                      |> Seq.filter (fun p -> p.Affiliation = Retired)
                      |> Seq.length

                  Expect.isGreaterThan retired 0 "scenario retires someone at the roll"

                  // Injuries carried into a new season already break some lineups; the roll must add no club to them.
                  let baseline =
                      unplayable
                          { SeasonPhase.runEndOfSeason balance gs with
                              PulseProcessed = Set.empty }

                  let added = Set.difference (unplayable next) baseline
                  Expect.isEmpty added $"retirements left new holes at {added}" ]

    let tests =
        testList
            "PlayerStories"
            [ configTests
              rollTests
              minutesTests
              unrestTests
              contractTests
              milestoneTests
              breakoutTests
              retirementTests ]
