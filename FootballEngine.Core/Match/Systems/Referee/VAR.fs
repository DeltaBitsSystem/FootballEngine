namespace FootballEngine.Core.Match

/// VAR: offside snapshot markers + review verdict.
/// The shot system leaves a marker (Phase = 250) in the scoring team's RunLookup;
/// the referee uses it to decide overturns deterministically at review expiry.
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

    /// Deterministic verdict from the last snapshot. True = attacker was offside.
    let isOffside (ctx: MatchContext) (world: World) (scoringSide: ClubSide) : bool =
        let team = MatchWorld.team world scoringSide
        let dir = MatchMath.goalDirection scoringSide
        let mutable verdict = false

        for kv in team.Frame.RunLookup do
            if kv.Value.Phase = 250 then
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
