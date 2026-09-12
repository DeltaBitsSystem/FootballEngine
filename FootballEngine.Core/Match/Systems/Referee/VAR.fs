namespace FootballEngine.Core.Match

/// An offside review must be deterministic and reproducible: instead of
/// re-simulating the scoring moment, the shot system stores a snapshot marker
/// (Phase = 250) in the scoring team's RunLookup, and the referee reads that
/// snapshot at review expiry to give the verdict.
module RefereeVAR =

    let private markerKey (slot: int) : int = slot + 1000

    let markOffsideSnapshot
        (world: World)
        (side: ClubSide)
        (shooterSlot: int)
        (scorerX: float<meter>)
        (defLineX: float<meter>)
        (tick: int<subtick>)
        : unit =
        let team = MatchWorld.team world side

        team.Frame.RunLookup.[markerKey shooterSlot] <-
            { Slot = shooterSlot
              TargetX = float32 scorerX
              TargetY = float32 defLineX
              Phase = 250
              ExpiresAt = tick }

    let clearMarkers (world: World) (side: ClubSide) : unit =
        let team = MatchWorld.team world side

        let keys =
            team.Frame.RunLookup
            |> Seq.filter (fun kv -> kv.Value.Phase = 250)
            |> Seq.map (fun kv -> kv.Key)
            |> Array.ofSeq

        for k in keys do
            team.Frame.RunLookup.Remove k |> ignore

    /// The verdict is read, not re-simulated: the same snapshot always gives the
    /// same overturn, and only positions beyond the margin (VarOffsideMargin)
    /// overturn the goal — marginal calls stand.
    let isOffsideFor (ctx: MatchContext) (world: World) (scoringSide: ClubSide) (scorer: PlayerId) : bool =
        let team = MatchWorld.team world scoringSide
        let dir = MatchMath.goalDirection scoringSide
        let mutable verdict = false

        for kv in team.Frame.RunLookup do
            if
                kv.Value.Phase = 250
                && MatchWorld.slotOf ctx scoringSide scorer = Some kv.Value.Slot
            then
                let scorerX = float kv.Value.TargetX * 1.0<meter>
                let defLine = float kv.Value.TargetY * 1.0<meter>

                let off =
                    if dir > 0.0 then
                        scorerX > defLine + ctx.Config.Referee.VarOffsideMargin
                    else
                        scorerX < defLine - ctx.Config.Referee.VarOffsideMargin

                if off then
                    verdict <- true

        verdict

    let isOffside (ctx: MatchContext) (world: World) (scoringSide: ClubSide) : bool =
        let team = MatchWorld.team world scoringSide
        let dir = MatchMath.goalDirection scoringSide
        let mutable verdict = false

        for kv in team.Frame.RunLookup do
            if kv.Value.Phase = 250 then
                let scorerX = float kv.Value.TargetX * 1.0<meter>
                let defLine = float kv.Value.TargetY * 1.0<meter>

                if
                    (dir > 0.0 && scorerX > defLine + ctx.Config.Referee.VarOffsideMargin)
                    || (dir < 0.0 && scorerX < defLine - ctx.Config.Referee.VarOffsideMargin)
                then
                    verdict <- true

        verdict
