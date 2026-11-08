namespace FootballEngine.Core.Tests.World.Pulse

open System
open Expecto
open FootballEngine.Domain
open FootballEngine.World.Pulse
open FootballEngine.Core.Tests.World

module MarketStoriesTests =

    let private config = WorldPulseConfig.defaultWorldPulse

    // Every budget is wide so a test asserts the detector, not the daily news cap.
    let private wide =
        { config with
            DailyBudgetUserClub = 100
            DailyBudgetUserLeague = 100
            DailyBudgetWorld = 100 }

    let private world = lazy (WorldGenerationTests.generate 0xC02UL [])

    let private day (gs: GameState) = gs.CurrentDate.Date

    let private squadOf club (gs: GameState) =
        gs.Clubs[club].PlayerIds |> List.choose gs.Players.TryFind

    let private ranked club gs =
        squadOf club gs |> PlayerStories.bySkill

    /// AI clubs with a real squad, weakest reputation first.
    let private aiClubs (gs: GameState) =
        gs.Clubs.Values
        |> Seq.filter (fun c -> c.Id <> gs.UserClubId && (squadOf c.Id gs).Length >= 16)
        |> Seq.sortBy (fun c -> c.Reputation, c.Id)
        |> List.ofSeq

    let private setPlayer id (f: Player -> Player) (gs: GameState) =
        { gs with
            Players = Map.add id (f gs.Players[id]) gs.Players }

    let private setClub id (f: Club -> Club) (gs: GameState) =
        { gs with
            Clubs = Map.add id (f gs.Clubs[id]) gs.Clubs }

    /// What the market leaves behind when a transfer completes: new affiliation, both squads and the pulse record.
    let private moved (pid: PlayerId) (from: ClubId) (toClub: ClubId) (fee: decimal) (gs: GameState) =
        { gs with
            Clubs =
                gs.Clubs
                |> Map.add from (Club.removePlayer pid gs.Clubs[from])
                |> Map.add toClub (Club.addPlayer pid gs.Clubs[toClub])
            Players =
                Map.add
                    pid
                    { gs.Players[pid] with
                        Affiliation = Contracted(toClub, { Salary = 1000m; ExpiryYear = 2040 }) }
                    gs.Players
            PulseQueues =
                PulseQueues.recordTransfer
                    { Player = pid
                      From = Some from
                      To = toClub
                      Fee = fee }
                    gs.PulseQueues }

    let private transferred pid gs =
        gs.News
        |> List.choose (fun n ->
            match n.Kind with
            | NewsKind.TransferDone(p, from, toClub, fee) when p = pid -> Some(from, toClub, fee)
            | _ -> None)

    let private run (cfg: WorldPulseConfig) (date: DateTime) (gs: GameState) = WorldPulse.runWith [] cfg date gs

    let private transferTests =
        let scene (rankOfPlayer: int) =
            let gs = world.Value
            let sellers = aiClubs gs
            let seller, buyer = sellers[0].Id, sellers[1].Id
            let pid = (ranked seller gs |> List.item rankOfPlayer).Id

            let calm =
                gs
                |> setClub seller (fun c -> { c with FansMood = 50 })
                |> setClub buyer (fun c -> { c with FansMood = 50 })

            seller, buyer, pid, calm

        testList
            "completed transfers"
            [ testCase "a fee at the threshold is announced once with its real parties"
              <| fun _ ->
                  let seller, buyer, pid, gs = scene 6
                  let gs = moved pid seller buyer config.TransferNewsMinFee gs
                  let after = run wide (day gs) gs
                  Expect.equal (transferred pid after) [ Some seller, buyer, config.TransferNewsMinFee ] "announced"

                  let later = run wide ((day gs).AddDays 30.0) after
                  Expect.equal (transferred pid later).Length 1 "never again"
                  Expect.isEmpty later.PulseQueues.Transfers "consumed"

              testCase "a fee under the threshold makes no news"
              <| fun _ ->
                  let seller, buyer, pid, gs = scene 6
                  let gs = moved pid seller buyer (config.TransferNewsMinFee - 1m) gs
                  let after = run wide (day gs) gs
                  Expect.isEmpty (transferred pid after) "too small to matter"
                  Expect.isEmpty after.PulseQueues.Transfers "still consumed"

              testCase "selling a top-three player angers the seller's fans; selling a squad player does not"
              <| fun _ ->
                  let seller, buyer, star, gs = scene 0
                  let after = run wide (day gs) (moved star seller buyer 1m gs)
                  Expect.equal after.Clubs[seller].FansMood (50 - config.FansStarSalePenalty) "star sale"

                  let seller, buyer, filler, gs = scene config.StarSaleTopRank
                  let after = run wide (day gs) (moved filler seller buyer 1m gs)
                  Expect.equal after.Clubs[seller].FansMood 50 "outside the top three"

              testCase "signing someone better than the buyer's top three delights its fans; a lesser signing does not"
              <| fun _ ->
                  let seller, buyer, pid, gs = scene 8
                  let marquee = setPlayer pid (fun p -> { p with CurrentSkill = 200 }) gs
                  let after = run wide (day gs) (moved pid seller buyer 1m marquee)
                  Expect.equal after.Clubs[buyer].FansMood (50 + config.FansMarqueeBoost) "marquee signing"

                  let weak = setPlayer pid (fun p -> { p with CurrentSkill = 1 }) gs
                  let after = run wide (day gs) (moved pid seller buyer 1m weak)
                  Expect.equal after.Clubs[buyer].FansMood 50 "ordinary signing"

              testCase "a transferred player starts clean: unrest, counters and listing are dropped"
              <| fun _ ->
                  let seller, buyer, pid, gs = scene 8

                  let unhappy =
                      setPlayer
                          pid
                          (fun p ->
                              { p with
                                  Unrest = Some UnrestReason.LackOfMinutes
                                  UnrestWeeks = 5
                                  BenchWeeks = 6
                                  TransferListed = true })
                          gs

                  let p = (run wide (day gs) (moved pid seller buyer 1m unhappy)).Players[pid]
                  Expect.isNone p.Unrest "no unrest"
                  Expect.equal (p.UnrestWeeks, p.BenchWeeks, p.TransferListed) (0, 0, false) "counters"

              testCase "the user transfer path records the completed move for the pulse"
              <| fun _ ->
                  let gs = world.Value
                  let seller, buyer = List.item 0 (aiClubs gs), List.item 1 (aiClubs gs)
                  let player = ranked seller.Id gs |> List.item 4

                  let done' = Transfer.completeUserTransfer buyer seller player 1000m 500m 3 gs

                  Expect.equal
                      done'.PulseQueues.Transfers
                      [ { Player = player.Id
                          From = Some seller.Id
                          To = buyer.Id
                          Fee = 1000m } ]
                      "recorded with parties and fee" ]

    let private rumourTests =
        // The best player of the weakest club is courted by the strongest one.
        let scene () =
            let gs = world.Value
            let clubs = aiClubs gs
            let weak, strong = clubs.Head, List.last clubs
            let star = (ranked weak.Id gs).Head
            let reserve = (ranked weak.Id gs) |> List.last

            let negotiation (buyer: Club) (seller: Club) (pid: PlayerId) : Negotiation =
                { Id = 5
                  BuyerClubId = buyer.Id
                  SellerClubId = seller.Id
                  PlayerId = pid
                  Stage = Approaching
                  Deadline = (day gs).AddDays 30.0 }

            let withNegotiation (n: Negotiation) =
                { gs with
                    PendingNegotiations = Map.ofList [ n.Id, n ] }

            gs, weak, strong, star, reserve, negotiation, withNegotiation

        let always = { wide with RumourChance = 1.0 }

        let rumours pid gs =
            gs.News
            |> List.choose (fun n ->
                match n.Kind with
                | NewsKind.Rumour(p, from, interested) when p = pid -> Some(from, interested)
                | _ -> None)

        testList
            "rumours"
            [ testCase "a bigger club approaching a smaller club's player can leak, naming both clubs"
              <| fun _ ->
                  let gs, weak, strong, star, _, negotiation, withN = scene ()
                  let after = run always (day gs) (withN (negotiation strong weak star.Id))
                  Expect.equal (rumours star.Id after) [ weak.Id, strong.Id ] "from the owner, to the suitor"

              testCase "no rumour when the buyer is not bigger than the seller"
              <| fun _ ->
                  let gs, weak, strong, star, _, negotiation, withN = scene ()
                  let down = withN (negotiation weak strong star.Id)
                  Expect.isEmpty (rumours star.Id (run always (day gs) down)) "smaller buying from bigger"

                  let level =
                      setClub
                          strong.Id
                          (fun c -> { c with Reputation = weak.Reputation })
                          (withN (negotiation strong weak star.Id))

                  Expect.isEmpty (rumours star.Id (run always (day gs) level)) "equal reputation is not bigger"

              testCase "the chance gates the rumour and the roll is reproducible for the same seed"
              <| fun _ ->
                  let gs, weak, strong, star, _, negotiation, withN = scene ()
                  let n = withN (negotiation strong weak star.Id)
                  Expect.isEmpty (rumours star.Id (run { wide with RumourChance = 0.0 } (day gs) n)) "never at zero"
                  Expect.equal (run wide (day gs) n) (run wide (day gs) n) "same inputs, same world"

                  let published seed =
                      rumours star.Id (run wide (day gs) { n with ExecutionSeed = seed })
                      |> List.length

                  let hits = [ 1UL .. 80UL ] |> List.sumBy published
                  Expect.isGreaterThan hits 10 "a 35% chance does leak"
                  Expect.isLessThan hits 55 "but not every time"

              testCase "a negotiation is considered once, however the first roll went"
              <| fun _ ->
                  let gs, weak, strong, star, _, negotiation, withN = scene ()
                  let n = withN (negotiation strong weak star.Id)
                  let first = run { wide with RumourChance = 0.0 } (day gs) n
                  Expect.isTrue (first.PulseQueues.NegotiationsSeen.Contains 5) "remembered"
                  let second = run always ((day gs).AddDays 30.0) first
                  Expect.isEmpty (rumours star.Id second) "a lost roll is not rerolled"

              testCase "a rumour unsettles a regular who has no other grievance, and nobody else"
              <| fun _ ->
                  let gs, weak, strong, star, reserve, negotiation, withN = scene ()
                  let after = run always (day gs) (withN (negotiation strong weak star.Id))
                  Expect.equal after.Players[star.Id].Unrest (Some UnrestReason.BiggerClubInterest) "regular"

                  let reserved = run always (day gs) (withN (negotiation strong weak reserve.Id))
                  Expect.isNonEmpty (rumours reserve.Id reserved) "the whisper still happens"
                  Expect.isNone reserved.Players[reserve.Id].Unrest "but a reserve is not unsettled"

                  let busy =
                      withN (negotiation strong weak star.Id)
                      |> setPlayer star.Id (fun p ->
                          { p with
                              Unrest = Some UnrestReason.ContractStalled })

                  Expect.equal
                      (run always (day gs) busy).Players[star.Id].Unrest
                      (Some UnrestReason.ContractStalled)
                      "existing grievance kept"

              testCase "interest lasts while the bid is alive and clears at the next weekly pass once it ends"
              <| fun _ ->
                  let gs, weak, strong, star, _, negotiation, withN = scene ()
                  let live = withN (negotiation strong weak star.Id)
                  let rumoured = run always (day gs) live
                  let nextWeek = (day gs).AddDays 7.0

                  let stillOn =
                      WorldPulse.runOn
                          true
                          always
                          nextWeek
                          { rumoured with
                              PendingNegotiations =
                                  Map.ofList
                                      [ 5,
                                        { negotiation strong weak star.Id with
                                            Stage = OfferMade 100m } ] }

                  Expect.equal stillOn.Players[star.Id].Unrest (Some UnrestReason.BiggerClubInterest) "bid alive"

                  Expect.isLessThan
                      stillOn.Players[star.Id].Morale
                      rumoured.Players[star.Id].Morale
                      "and it costs morale"

                  let ended =
                      WorldPulse.runOn
                          true
                          always
                          nextWeek
                          { rumoured with
                              PendingNegotiations =
                                  Map.ofList
                                      [ 5,
                                        { negotiation strong weak star.Id with
                                            Stage = RejectedByClub(100m, "no") } ] }

                  Expect.isNone ended.Players[star.Id].Unrest "bid failed, interest gone"
                  Expect.equal ended.Players[star.Id].UnrestWeeks 0 "counter reset" ]

    let tests = testList "MarketStories" [ transferTests; rumourTests ]
