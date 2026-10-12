namespace FootballEngine.Core.Tests.Systems.Team

open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module CollectiveAttackFixtures =
    let buffer () : EventBuffer = { Events = ResizeArray(); Applied = ResizeArray(); Flags = { Bits = 0UL } }
    let systemContext ctx = { Ctx = ctx; Clock = SimulationClock.defaultClock }

    let scenarioWithContext (ctx: MatchContext) side =
        let world = MatchFactory.create ctx
        let team = MatchWorld.team world side
        let opponents = MatchWorld.team world (MatchWorld.oppOf side)
        let dir = MatchMath.goalDirection side
        let positions = [| -48.0, 0.0; -25.0, -16.0; -22.0, -5.0; -22.0, 5.0; -9.0, 8.0; 0.0, 0.0; 6.0, -8.0; 13.0, 8.0; 6.0, -24.0; 23.0, 0.0; 6.0, 24.0 |]
        for slot in 0 .. team.Frame.SlotCount - 1 do
            let x, y = positions[slot % positions.Length]
            team.Frame.PosX[slot] <- float32 (x * dir)
            team.Frame.PosY[slot] <- float32 y
            team.Frame.Cognitive[slot] <- 1.0f
            team.Frame.Condition[slot] <- 1.0f
            team.Frame.LaneClear[slot] <- true
            team.Frame.FaceCone[slot] <- true
        for slot in 0 .. opponents.Frame.SlotCount - 1 do
            opponents.Frame.PosX[slot] <- float32 ((if slot = 0 then 49.0 else 38.0) * dir)
            opponents.Frame.PosY[slot] <- if slot = 0 then 0.0f else float32 (if slot % 2 = 0 then 26.0 else -26.0)
        world.State <- { world.State with Tick = 400<subtick>; Flow = MatchFlow.Live; Ball = TestBuilders.emptyBall }
        EventApply.apply ctx world { Tick = 400<subtick>; Data = EventData.Possession(side, team.RosterIds[5]) }
        world

    let scenario side =
        let ctx = TestBuilders.formationCtx ()
        ctx, scenarioWithContext ctx side

    let start ctx world =
        let events = buffer ()
        TeamAttack.step (systemContext ctx) (Rng.ofSeed 42UL) world events |> ignore
        events

    let episode (world: World) side =
        match (MatchWorld.team world side).Attack.Episode with
        | Some episode -> episode
        | None -> failwith "scenario did not produce an attacking episode"
