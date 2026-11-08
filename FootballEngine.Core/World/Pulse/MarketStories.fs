namespace FootballEngine.World.Pulse

open System
open FootballEngine.Domain

/// Transfers that happened and the whispers before them.
module MarketStories =

    let private involvesUser (gs: GameState) (clubs: ClubId list) = List.contains gs.UserClubId clubs

    let private adjustMood (delta: int) (clubId: ClubId) (gs: GameState) : GameState =
        match Map.tryFind clubId gs.Clubs with
        | Some club ->
            { gs with
                Clubs =
                    Map.add
                        clubId
                        { club with
                            FansMood = Math.Clamp(club.FansMood + delta, 0, 100) }
                        gs.Clubs }
        | None -> gs

    /// Whether `player` is among the `rank` best of `squad`, counting him even when he is not listed in it.
    let private isTopOf (rank: int) (squad: Player list) (player: Player) : bool =
        (player :: (squad |> List.filter (fun p -> p.Id <> player.Id)))
        |> PlayerStories.bySkill
        |> List.truncate rank
        |> List.exists (fun p -> p.Id = player.Id)

    /// A new arrival starts clean: grievances belonged to the old club.
    let private settle (player: Player) : Player =
        { player with
            Unrest = None
            UnrestWeeks = 0
            BenchWeeks = 0
            TransferListed = false }

    let private applyTransfer
        (config: WorldPulseConfig)
        (transfer: CompletedTransfer)
        (state: GameState, news: NewsCandidate list)
        : GameState * NewsCandidate list =
        match state.Players |> Map.tryFind transfer.Player with
        | None -> state, news
        | Some player ->
            let state =
                { state with
                    Players = Map.add player.Id (settle player) state.Players }

            let squadOf clubId =
                state.Clubs
                |> Map.tryFind clubId
                |> Option.map (fun club -> club.PlayerIds |> List.choose state.Players.TryFind)
                |> Option.defaultValue []

            // The seller's squad no longer holds him, so he is re-added to judge whether he was a star there.
            let state =
                match transfer.From with
                | Some seller when isTopOf config.StarSaleTopRank (squadOf seller) player ->
                    adjustMood -config.FansStarSalePenalty seller state
                | _ -> state

            let others =
                squadOf transfer.To
                |> List.filter (fun p -> p.Id <> player.Id)
                |> PlayerStories.bySkill
                |> List.truncate config.StarSaleTopRank

            // Needs a full top group: a thin squad has no meaningful "top three" to out-class.
            let state =
                if
                    others.Length = config.StarSaleTopRank
                    && float player.CurrentSkill > (others |> List.averageBy (fun p -> float p.CurrentSkill))
                then
                    adjustMood config.FansMarqueeBoost transfer.To state
                else
                    state

            let clubs = (transfer.From |> Option.toList) @ [ transfer.To ]

            let items =
                if transfer.Fee >= config.TransferNewsMinFee then
                    let importance =
                        if involvesUser state clubs then
                            NewsImportance.Major
                        else
                            NewsImportance.Notable

                    [ NewsKind.TransferDone(player.Id, transfer.From, transfer.To, transfer.Fee),
                      importance,
                      NewsDesk.classifyScope state clubs,
                      clubs,
                      [ player.Id ] ]
                else
                    []

            state, news @ items

    /// Reputation decides who can credibly be linked with whom; the roll decides whether it leaks.
    let private rumourFor
        (config: WorldPulseConfig)
        (date: DateTime)
        (negotiation: Negotiation)
        (state: GameState, news: NewsCandidate list)
        : GameState * NewsCandidate list =
        match
            state.Clubs |> Map.tryFind negotiation.BuyerClubId,
            state.Clubs |> Map.tryFind negotiation.SellerClubId,
            state.Players |> Map.tryFind negotiation.PlayerId
        with
        | Some buyer, Some seller, Some player when buyer.Reputation > seller.Reputation ->
            let roll =
                PulseRandom.seedFor state.ExecutionSeed date "rumour" negotiation.Id
                |> PulseRandom.roll

            if roll < config.RumourChance then
                // Interest only unsettles someone the club depends on, and never overrides a grievance he already has.
                let state =
                    if player.Unrest.IsNone && PlayerStories.isRegular config state player then
                        { state with
                            Players =
                                Map.add
                                    player.Id
                                    { player with
                                        Unrest = Some UnrestReason.BiggerClubInterest }
                                    state.Players }
                    else
                        state

                let clubs = [ seller.Id; buyer.Id ]

                let importance =
                    if involvesUser state clubs then
                        NewsImportance.Notable
                    else
                        NewsImportance.Minor

                state,
                news
                @ [ NewsKind.Rumour(player.Id, seller.Id, buyer.Id),
                    importance,
                    NewsDesk.classifyScope state clubs,
                    clubs,
                    [ player.Id ] ]
            else
                state, news
        | _ -> state, news

    let apply (config: WorldPulseConfig) (date: DateTime) (gs: GameState) : GameState * NewsCandidate list =
        let transfers = gs.PulseQueues.Transfers |> List.rev

        let settled, transferNews =
            ((gs, []), transfers) ||> List.fold (fun acc t -> applyTransfer config t acc)

        let fresh =
            settled.PendingNegotiations
            |> Map.values
            |> Seq.filter (fun n -> not (settled.PulseQueues.NegotiationsSeen.Contains n.Id))
            |> Seq.sortBy _.Id
            |> List.ofSeq

        let whispered, rumourNews =
            ((settled, []), fresh) ||> List.fold (fun acc n -> rumourFor config date n acc)

        // Only live ids are kept: a finished negotiation can never reappear.
        let alive = settled.PendingNegotiations |> Map.keys |> Set.ofSeq

        let queues =
            { Appearances = whispered.PulseQueues.Appearances
              Transfers = []
              NegotiationsSeen =
                Set.union
                    (Set.intersect settled.PulseQueues.NegotiationsSeen alive)
                    (fresh |> List.map _.Id |> Set.ofList) }

        { whispered with PulseQueues = queues }, transferNews @ rumourNews
