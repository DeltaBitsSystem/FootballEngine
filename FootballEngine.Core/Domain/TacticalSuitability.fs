namespace FootballEngine.Domain

/// Role suitability: how natural a position is for a player, 0..5 stars from
/// real attributes. One function feeds the selection table, Quick Pick and
/// advice, so the three can never disagree. Weights sum to 1 per position.
module TacticalSuitability =

    let private w (weight: float) (value: int) : float = weight * float value

    let private rating (p: Player) (pos: Position) : float =
        let ph = p.Physical
        let te = p.Technical
        let me = p.Mental
        let gk = p.Goalkeeping

        match pos with
        | GK ->
            w 0.25 gk.Reflexes
            + w 0.20 gk.Handling
            + w 0.15 gk.OneOnOne
            + w 0.10 me.Positioning
            + w 0.10 me.Composure
            + w 0.10 gk.Kicking
            + w 0.10 gk.AerialReach
        | DC ->
            w 0.20 te.Marking
            + w 0.20 te.Tackling
            + w 0.15 ph.Strength
            + w 0.15 te.Heading
            + w 0.15 me.Positioning
            + w 0.05 ph.Pace
            + w 0.10 me.Composure
        | DL
        | DR ->
            w 0.15 ph.Pace
            + w 0.15 te.Tackling
            + w 0.15 te.Marking
            + w 0.15 te.Crossing
            + w 0.15 ph.Stamina
            + w 0.10 me.Positioning
            + w 0.05 te.Dribbling
            + w 0.10 me.Composure
        | WBL
        | WBR ->
            w 0.20 ph.Stamina
            + w 0.15 ph.Pace
            + w 0.15 te.Crossing
            + w 0.10 te.Tackling
            + w 0.10 te.Marking
            + w 0.10 te.Dribbling
            + w 0.10 me.Positioning
            + w 0.10 me.WorkRate
        | DM ->
            w 0.20 me.Positioning
            + w 0.15 te.Tackling
            + w 0.10 te.Marking
            + w 0.15 te.Passing
            + w 0.10 ph.Strength
            + w 0.10 me.Vision
            + w 0.10 me.WorkRate
            + w 0.10 me.Composure
        | MC ->
            w 0.20 te.Passing
            + w 0.15 me.Vision
            + w 0.10 te.BallControl
            + w 0.10 me.WorkRate
            + w 0.10 me.Positioning
            + w 0.10 te.Tackling
            + w 0.10 ph.Stamina
            + w 0.05 me.Composure
            + w 0.10 te.Dribbling
        | MR
        | ML ->
            w 0.15 ph.Pace
            + w 0.20 te.Crossing
            + w 0.15 te.Dribbling
            + w 0.10 te.Passing
            + w 0.15 ph.Stamina
            + w 0.10 te.BallControl
            + w 0.05 me.WorkRate
            + w 0.10 me.Positioning
        | AMC ->
            w 0.20 te.Passing
            + w 0.20 me.Vision
            + w 0.15 te.Dribbling
            + w 0.15 te.Finishing
            + w 0.10 te.BallControl
            + w 0.10 me.Composure
            + w 0.10 ph.Agility
        | AML
        | AMR ->
            w 0.20 ph.Pace
            + w 0.20 te.Dribbling
            + w 0.15 te.Finishing
            + w 0.10 te.Crossing
            + w 0.10 te.Passing
            + w 0.10 te.BallControl
            + w 0.10 ph.Agility
            + w 0.05 me.Composure
        | ST ->
            w 0.25 te.Finishing
            + w 0.15 ph.Pace
            + w 0.10 ph.Strength
            + w 0.10 te.Heading
            + w 0.10 te.BallControl
            + w 0.10 me.Composure
            + w 0.10 me.Positioning
            + w 0.10 te.LongShots

    /// 0..5 stars. Outfield players in goal (and keepers outfield) collapse
    /// naturally: their keeping attributes sit near minimum.
    let stars (p: Player) (pos: Position) : float = rating p pos / 20.0 * 5.0

    let private score (p: Player) (pos: Position) : float =
        stars p pos * (0.7 + 0.3 * float p.MatchFitness / 100.0)

    /// Greedy best XI for the given slots: each slot takes the best remaining
    /// player by suitability blended with sharpness. Deterministic by slot order.
    let quickPick (slots: LineupSlot list) (players: Player list) : Map<int, PlayerId> =
        let _, picks =
            ((Set.ofList (players |> List.map _.Id), Map.empty), slots)
            ||> List.fold (fun (remaining, acc) slot ->
                let best =
                    players
                    |> List.filter (fun p -> remaining.Contains p.Id)
                    |> List.sortByDescending (fun p -> score p slot.Role)
                    |> List.tryHead

                match best with
                | None -> remaining, acc
                | Some p -> remaining |> Set.remove p.Id, acc |> Map.add slot.Index p.Id)

        picks
