namespace FootballEngine

open System
open FootballEngine.AppTypes
open FootballEngine.Domain
open FootballEngine.Routing

/// Shared decisions keep the header, home and preparation screen consistent without advancing the world in a view.
module CareerFlow =
    type Continuation =
        | Unavailable
        | Working
        | ReturnToMatch of MatchId * MatchPhase
        | PrepareMatch of MatchId
        | AdvanceDay of DateTime

    type LineupReadiness =
        { SlotCount: int
          EmptySlots: int
          UnavailableStarters: string list
          HasDuplicates: bool }

        member this.IsReady =
            this.SlotCount = 11
            && this.EmptySlots = 0
            && this.UnavailableStarters.IsEmpty
            && not this.HasDuplicates

    let lineupReadiness (gs: GameState) =
        let slots =
            GameState.headCoach gs.UserClubId gs
            |> Option.bind (fun coach -> coach.Attributes.Coaching.Lineup)
            |> Option.map _.Slots
            |> Option.defaultValue []

        let ids = slots |> List.choose _.PlayerId

        { SlotCount = slots.Length
          EmptySlots = slots |> List.filter (fun slot -> slot.PlayerId.IsNone) |> List.length
          HasDuplicates = (ids |> List.distinct |> List.length) <> ids.Length
          UnavailableStarters =
            ids
            |> List.choose (fun id ->
                match Map.tryFind id gs.Players with
                | Some player when Player.clubOf player = Some gs.UserClubId && player.Status = Available -> None
                | Some player -> Some player.Name
                | None -> Some(string id)) }

    let todayFixture (gs: GameState) =
        GameState.getUserNextFixture gs
        |> Option.filter (fun (_, fixture) -> fixture.ScheduledDate.Date = gs.CurrentDate.Date)

    let continuation (state: State) =
        match state.Mode with
        | InGame(gs, _) when state.IsProcessing -> Working
        | InGame(gs, _) ->
            match state.Match.RequestedMatchId with
            | Some id when state.Match.PendingResult.IsSome -> ReturnToMatch(id, Postmatch)
            | Some id when state.Match.RuntimeId.IsSome -> ReturnToMatch(id, Live)
            | _ ->
                match todayFixture gs with
                | Some(id, _) -> PrepareMatch id
                | None -> AdvanceDay(gs.CurrentDate.AddDays 1.0)
        | _ -> Unavailable

    let lastResult (gs: GameState) =
        gs.Competitions
        |> Map.toSeq
        |> Seq.collect (fun (_, competition) -> competition.Fixtures |> Map.toSeq)
        |> Seq.filter (fun (_, fixture) -> fixture.Played && MatchFixture.involves gs.UserClubId fixture)
        |> Seq.sortByDescending (fun (id, fixture) -> fixture.ScheduledDate, id)
        |> Seq.tryHead

    let newAttention (before: GameState) (after: GameState) =
        let pending gs =
            gs.Inbox
            |> List.filter (fun message -> message.RequiresAction && message.ActionTaken <> Some true)
            |> List.map _.Id
            |> Set.ofList

        let unavailable gs =
            GameState.getUserSquad gs
            |> List.filter (fun player -> player.Status <> Available)
            |> List.map _.Id
            |> Set.ofList

        not (Set.isEmpty (Set.difference (pending after) (pending before)))
        || not (Set.isEmpty (Set.difference (unavailable after) (unavailable before)))

    let shotFacts (state: State) matchId (events: FootballEngine.Core.Match.MatchEvent list) =
        let input =
            state.Match.PendingResult
            |> Option.filter (fun result -> result.MatchId = matchId)
            |> Option.map _.Input
            |> Option.orElseWith (fun () -> state.WorldClock.MatchExecutions |> Map.tryFind matchId |> Option.map fst)

        input
        |> Option.map (fun input ->
            let ids (lineup: FootballEngine.Core.Match.Lineup) =
                lineup.Profiles |> Array.map _.Id |> Set.ofArray

            let home, away = ids input.Context.HomeLineup, ids input.Context.AwayLineup
            let mutable homeShots, awayShots = 0, 0
            let mutable homeXg, awayXg = 0.0, 0.0

            for event in events do
                match event.Data with
                | FootballEngine.Core.Match.EventData.Shot(actor, _, xg, _, _, _) when Set.contains actor home ->
                    homeShots <- homeShots + 1
                    homeXg <- homeXg + xg
                | FootballEngine.Core.Match.EventData.Shot(actor, _, xg, _, _, _) when Set.contains actor away ->
                    awayShots <- awayShots + 1
                    awayXg <- awayXg + xg
                | _ -> ()

            homeShots, awayShots, homeXg, awayXg)
