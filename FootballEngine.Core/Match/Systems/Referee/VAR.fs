namespace FootballEngine.Core.Match

/// Review consumes captured evidence, never a re-simulation of the scoring moment.
module RefereeVAR =

    let markOffsideSnapshot
        (world: World)
        (side: ClubSide)
        (shooterSlot: int)
        (scorerX: float<meter>)
        (defLineX: float<meter>)
        (tick: int<subtick>)
        : unit =
        world.Reviews.Offside[(side, shooterSlot)] <-
            { ScorerX = float32 scorerX * 1.0f<meter>
              DefensiveLineX = float32 defLineX * 1.0f<meter>
              CapturedAt = tick }

    let clearSnapshots (world: World) (side: ClubSide) : unit =
        let keys =
            world.Reviews.Offside.Keys
            |> Seq.filter (fun (snapshotSide, _) -> snapshotSide = side)
            |> Seq.toArray

        for key in keys do
            world.Reviews.Offside.Remove key |> ignore

    let private beyondMargin (ctx: MatchContext) side (snapshot: OffsideReviewSnapshot) =
        let scorerX = float snapshot.ScorerX * 1.0<meter>
        let defLine = float snapshot.DefensiveLineX * 1.0<meter>

        if MatchMath.goalDirection side > 0.0 then
            scorerX > defLine + ctx.Config.Referee.VarOffsideMargin
        else
            scorerX < defLine - ctx.Config.Referee.VarOffsideMargin

    let isOffsideFor (ctx: MatchContext) (world: World) (scoringSide: ClubSide) (scorer: PlayerId) : bool =
        match MatchWorld.slotOf ctx scoringSide scorer with
        | Some slot ->
            match world.Reviews.Offside.TryGetValue((scoringSide, slot)) with
            | true, snapshot -> beyondMargin ctx scoringSide snapshot
            | _ -> false
        | None -> false

    let isOffside (ctx: MatchContext) (world: World) (scoringSide: ClubSide) : bool =
        world.Reviews.Offside
        |> Seq.exists (fun entry ->
            let side, _ = entry.Key
            side = scoringSide && beyondMargin ctx scoringSide entry.Value)
