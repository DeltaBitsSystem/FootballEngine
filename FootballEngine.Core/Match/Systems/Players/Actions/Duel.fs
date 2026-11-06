namespace FootballEngine.Core.Match

/// A take-on begins a timed protection action; only later physical contacts
/// decide whether the carrier keeps or loses control.
module Duel =
    let adjudicate
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (_buffer: EventBuffer)
        (side: ClubSide)
        (slot: int)
        : Rng =
        let tick = world.State.Tick
        let p = MatchWorld.profileOf ctx.Ctx side slot
        let carrierAvailable =
            match MatchWorld.ballCarrier ctx.Ctx world with
            | Some(owner, carrierSlot, _) when owner = side && carrierSlot = slot ->
                BallFootContact.reachable ctx.Ctx world side slot
            | _ -> false

        if not carrierAvailable then
            world.Observation.RejectAction(tick, p.Id, DuelCarrierUnavailable) |> ignore
        else
            ContactActionPlanner.prepare ctx world side slot ContactActionKind.ContactShield None |> ignore

        rng
