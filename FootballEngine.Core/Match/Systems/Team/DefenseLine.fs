namespace FootballEngine.Core.Match

module DefenseLine =
    let secondDeepest (ctx: MatchContext) (world: World) (side: ClubSide) : float<meter> =
        let frame = (MatchWorld.team world side).Frame
        let own = MatchMath.ownGoalX side

        let outfielders =
            MatchWorld.activeSlots frame |> Array.filter (fun slot -> slot <> 0)

        if outfielders.Length < 2 then
            own
        else
            let ranked =
                outfielders
                |> Array.sortBy (fun slot ->
                    MatchMath.dist (MatchWorld.posX frame slot) (MatchWorld.posY frame slot) own 0.0<meter>)

            MatchWorld.posX frame ranked.[1]
