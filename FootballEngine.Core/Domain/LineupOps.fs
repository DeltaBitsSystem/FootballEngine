namespace FootballEngine.Domain

open FootballEngine.Domain.TacticalInstructions


module FormationLineups =
    let all =
        [ F442
          F442Diamond
          F433
          F433Flat
          F451
          F4141
          F4231
          F4312
          F4321
          F352
          F343
          F3421
          F532
          F541
          F523 ]

module LineupOps =

    let private availablePlayers (players: Player list) =
        players |> List.filter (fun p -> p.Status = Available)

    let bestForPosition (players: Player list) (pos: Position) : Player option =
        match players |> List.filter (fun p -> p.Position = pos) with
        | [] -> players |> List.sortByDescending _.CurrentSkill |> List.tryHead
        | exact -> exact |> List.sortByDescending _.CurrentSkill |> List.tryHead

    let bestFormation (players: Player list) : Formation =
        let available = availablePlayers players

        FormationLineups.all
        |> List.maxBy (fun formation ->
            FormationData.getFormation formation
            |> List.sumBy (fun slot ->
                available
                |> List.filter (fun p -> p.Position = slot.Role)
                |> List.sortByDescending _.CurrentSkill
                |> List.tryHead
                |> Option.map _.CurrentSkill
                |> Option.defaultValue 0))

    let private countActiveSlots (players: Player list) (lineup: Lineup) =
        lineup.Slots
        |> List.filter (fun s ->
            s.PlayerId
            |> Option.map (fun pid -> players |> List.exists (fun p -> p.Id = pid && p.Status = Available))
            |> Option.defaultValue false)
        |> List.length

    let private buildSlots (formation: Formation) (players: Player list) : LineupSlot list =
        let formationSlots = FormationData.getFormation formation
        let available = availablePlayers players

        let rec assign (remaining: LineupSlot list) (pool: Player list) (acc: LineupSlot list) =
            match remaining with
            | [] -> List.rev acc
            | slot :: rest ->
                let lineupSlot =
                    { Index = slot.Index
                      Role = slot.Role
                      X = slot.X
                      Y = slot.Y
                      PlayerId = None }

                match bestForPosition pool slot.Role with
                | None -> assign rest pool (lineupSlot :: acc)
                | Some p ->
                    assign
                        rest
                        (pool |> List.filter (fun x -> x.Id <> p.Id))
                        ({ lineupSlot with PlayerId = Some p.Id } :: acc)

        assign formationSlots available []

    let autoLineup (coach: Staff) (players: Player list) (formation: Formation) : Staff =
        let current = coach.Attributes.Coaching.Lineup

        let needsRegen =
            match current with
            | None -> true
            | Some lu -> countActiveSlots players lu < 11

        if not needsRegen then
            coach
        else


            let lineup =
                { Formation = formation
                  Tactics = Balanced
                  Instructions = Some defaultInstructions
                  Slots = buildSlots formation players }

            { coach with
                Attributes.Coaching.Lineup = Some lineup }

    let ensureForClub (clubId: ClubId) (gs: GameState) : GameState =
        let club = gs.Clubs[clubId]

        match GameState.headCoach clubId gs with
        | None -> gs
        | Some coach ->
            let isComplete =
                coach.Attributes.Coaching.Lineup
                |> Option.map (fun lu -> lu.Slots |> List.filter (fun s -> s.PlayerId.IsSome) |> List.length = 11)
                |> Option.defaultValue false

            if isComplete then
                gs
            else
                let squad = club.PlayerIds |> List.choose gs.Players.TryFind
                let updatedCoach = autoLineup coach squad (bestFormation squad)

                { gs with
                    Staff = gs.Staff |> Map.add coach.Id updatedCoach }

    let ensureForFixtures (fixtures: (MatchId * MatchFixture) list) (gs: GameState) : GameState =
        let clubIds =
            fixtures
            |> List.collect (fun (_, f) -> [ f.HomeClubId; f.AwayClubId ])
            |> List.distinct

        (gs, clubIds)
        ||> List.fold (fun currentGs clubId -> ensureForClub clubId currentGs)

    let swapPlayer (targetIdx: int) (pId: PlayerId) (lineup: Lineup) : Lineup =
        let slotsMap = lineup.Slots |> List.map (fun s -> s.Index, s.PlayerId) |> Map.ofList

        let sourceIdx =
            slotsMap
            |> Map.tryPick (fun idx idOpt -> if idOpt = Some pId then Some idx else None)

        let occupant = slotsMap |> Map.tryFind targetIdx |> Option.flatten

        let newSlotsMap =
            match sourceIdx with
            | Some src -> slotsMap |> Map.add targetIdx (Some pId) |> Map.add src occupant
            | None ->
                slotsMap
                |> Map.add targetIdx (Some pId)
                |> Map.map (fun idx idOpt -> if idx <> targetIdx && idOpt = Some pId then None else idOpt)

        let newSlots =
            newSlotsMap
            |> Map.toList
            |> List.map (fun (idx, pidOpt) ->
                let original = lineup.Slots |> List.find (fun s -> s.Index = idx)

                { Index = idx
                  Role = original.Role
                  X = original.X
                  Y = original.Y
                  PlayerId = pidOpt })
            |> List.sortBy _.Index

        { lineup with Slots = newSlots }
