namespace FootballEngine.Core.Tests.Systems.Team

open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module DefensiveTransitionFixtures =
    let buffer () : EventBuffer =
        { Events = ResizeArray()
          Applied = ResizeArray()
          Flags = { Bits = 0UL } }

    let systemContext ctx =
        { Ctx = ctx
          Clock = SimulationClock.defaultClock }

    let scenarioWithContext (ctx: MatchContext) side opened condition =
        let world = MatchFactory.create ctx
        let dir = MatchMath.goalDirection side
        let team = MatchWorld.team world side
        let opponent = MatchWorld.team world (MatchWorld.oppOf side)

        let positions =
            [| -48.0, 0.0
               -24.0, -16.0
               -27.0, -5.0
               -27.0, 5.0
               -24.0, 16.0
               -5.0, 0.0
               -2.0, -4.0
               -2.0, 4.0
               8.0, -18.0
               2.0, 0.0
               8.0, 18.0 |]

        for slot in 0 .. team.Frame.SlotCount - 1 do
            let x, y = positions[slot % positions.Length]
            team.Frame.PosX[slot] <- float32 ((if opened && slot > 0 then x + 30.0 else x) * dir)
            team.Frame.PosY[slot] <- float32 y
            team.Frame.Condition[slot] <- condition

        for slot in 0 .. opponent.Frame.SlotCount - 1 do
            opponent.Frame.PosX[slot] <-
                float32 (
                    (if slot = 9 then 0.0
                     elif opened && slot = 8 then -22.0
                     else 28.0)
                    * dir
                )

            opponent.Frame.PosY[slot] <-
                if slot = 9 || slot = 8 then
                    0.0f
                else
                    float32 (snd positions[slot % positions.Length])

        world.State <-
            { world.State with
                Tick = 401<subtick>
                Flow = MatchFlow.Live
                Ball = TestBuilders.emptyBall }

        EventApply.apply
            ctx
            world
            { Tick = 400<subtick>
              Data = EventData.Possession(side, team.RosterIds[9]) }

        EventApply.apply
            ctx
            world
            { Tick = 401<subtick>
              Data = EventData.Possession(MatchWorld.oppOf side, opponent.RosterIds[9]) }

        ctx, world

    let scenario side opened condition =
        scenarioWithContext (TestBuilders.formationCtx ()) side opened condition
