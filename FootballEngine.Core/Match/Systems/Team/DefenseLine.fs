namespace FootballEngine.Core.Match

module DefenseLine =
    /// The defensive line is anchored on the second-deepest outfielder, not
    /// the deepest: one recovering player would drag the whole line toward
    /// his own goal.
    let secondDeepest (ctx: MatchContext) (world: World) (side: ClubSide) : float<meter> =
        let frame = (MatchWorld.team world side).Frame
        let own = MatchMath.ownGoalX side

        let mutable first = -1
        let mutable second = -1
        let mutable firstDistance = System.Double.PositiveInfinity
        let mutable secondDistance = System.Double.PositiveInfinity

        for slot in MatchWorld.activeSlots frame do
            if slot <> 0 then
                let distance = MatchMath.dist (MatchWorld.posX frame slot) (MatchWorld.posY frame slot) own 0.0<meter>
                if distance < firstDistance then
                    second <- first
                    secondDistance <- firstDistance
                    first <- slot
                    firstDistance <- distance
                elif distance < secondDistance then
                    second <- slot
                    secondDistance <- distance

        if second < 0 then own else MatchWorld.posX frame second
