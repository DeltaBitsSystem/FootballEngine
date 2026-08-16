namespace FootballEngine.Core.Match

/// Goalkeeper distribution: restart the attack from the back with the least
/// risky option. The target is the same under any pressure — the teammate who
/// is nearest and least marked — but pressure spoils the delivery: a hurried
/// GK passes worse than a calm one.
module GK =

    let distribute (ctx: SystemContext) (rng: Rng) (world: World) (buffer: EventBuffer) (side: ClubSide) : Rng =
        let frame = (MatchWorld.team world side).Frame
        let gk = MatchWorld.posX frame 0
        let gkY = MatchWorld.posY frame 0
        let cfg = ctx.Ctx.Config

        let pressure =
            MatchWorld.opponentsNear ctx.Ctx world side gk gkY (float cfg.Actions.GkDistributionPressureRadius)

        let targetSlot =
            let mutable best = -1
            let mutable bestD = 10000.0

            for s2 in MatchWorld.activeSlots frame do
                if s2 <> 0 then
                    let tx = MatchWorld.posX frame s2
                    let ty = MatchWorld.posY frame s2
                    let nearOpp = MatchWorld.opponentsNear ctx.Ctx world side tx ty 4.0
                    let d = MatchMath.dist gk gkY tx ty

                    let score = d + float nearOpp * 6.0

                    if score < bestD then
                        bestD <- score
                        best <- s2

            best

        if pressure > 0 then
            if targetSlot >= 0 then
                Pass.launch ctx rng world buffer side 0 targetSlot -0.25
            else
                rng
        else if targetSlot >= 0 then
            Pass.launch ctx rng world buffer side 0 targetSlot 0.25
        else
            rng
