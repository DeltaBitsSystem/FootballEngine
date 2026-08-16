namespace FootballEngine.Core.Match

/// A foul must get one coherent referee response — advantage or restart, plus a
/// card — so a single system decides both: the Duel emits the Foul, this system
/// answers with the SetPiece, and the two can never disagree.
module RefereeFouls =

    /// Stopping play for every foul would kill attacking flow: advantage is only
    /// played when the fouled team is actually advancing with a teammate ahead.
    let private shouldPlayAdvantage
        (ctx: MatchContext)
        (world: World)
        (fouledSide: ClubSide)
        (fx: float<meter>)
        (fy: float<meter>)
        : bool =
        let cfg = ctx.Config.Referee
        let dir = MatchMath.goalDirection fouledSide

        // Advantage only pays off if the play is heading toward the fouled team's goal.
        let advancing = float fx * dir > 0.0

        // And there must be a teammate ahead — otherwise continuing play gains nothing.
        let team = MatchWorld.team world fouledSide
        let mutable bestTeammateFwd = 0.0

        for slot in MatchWorld.activeSlots team.Frame do
            if slot <> 0 then
                let tx = float (MatchWorld.posX team.Frame slot)
                let fwd = (tx - float fx) * dir

                if fwd > bestTeammateFwd then
                    bestTeammateFwd <- fwd

        advancing && bestTeammateFwd > 8.0

    /// The restart depends on the spot: inside the box it is a penalty, anywhere
    /// else a free kick — location alone decides.
    let private isFoulInBox (fouledSide: ClubSide) (fx: float<meter>) : bool =
        let goalX = MatchMath.goalX fouledSide
        abs (float fx - float goalX) < 16.5

    let private decideCard
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (buffer: EventBuffer)
        (tick: int<subtick>)
        (ev: MatchEvent)
        : Rng =
        match ev.Data with
        | EventData.Foul(fouler, fouled, severity, preCard, (fx, fy)) ->
            let cfg = ctx.Ctx.Config
            let side = MatchWorld.teamOfPlayer ctx.Ctx fouler

            let severityNum =
                match severity with
                | FoulSeverity.Minor -> 0.2
                | FoulSeverity.Tactical -> 0.4
                | FoulSeverity.YellowWorthy -> 0.7
                | FoulSeverity.RedWorthy -> 0.95

            let lastMan =
                match side with
                | Some s ->
                    let oppGoal = MatchMath.goalX s
                    let fx = float fx
                    let isNearGoal = abs (fx - float oppGoal) < 30.0

                    if isNearGoal then
                        let frame = (MatchWorld.team world s).Frame
                        let dir = MatchMath.goalDirection s
                        let mutable mostBack = if dir > 0.0 then -Pitch.HalfLength else Pitch.HalfLength

                        for slot in MatchWorld.activeSlots frame do
                            if slot <> 0 then
                                let x = MatchWorld.posX frame slot

                                if dir > 0.0 && x > mostBack then
                                    mostBack <- x

                                if dir < 0.0 && x < mostBack then
                                    mostBack <- x

                        abs (fx - float mostBack) < 4.0
                        && MatchWorld.opponentsNear ctx.Ctx world s (fx * 1.0<meter>) fy 25.0 >= 2
                    else
                        false
                | None -> false

            let pYellow =
                MatchMath.clamp01 (
                    cfg.Referee.FoulYellowWorthyChance
                    + severityNum * 0.4
                    + (if lastMan then 0.45 else 0.0)
                )

            let pRed = MatchMath.clamp01 (severityNum * cfg.Referee.FoulRedWorthyChance * 0.5)

            let roll, rng' = Rng.double rng

            let card =
                if roll < pRed then Some CardColor.Red
                elif roll < pRed + pYellow then Some CardColor.Yellow
                else None

            match card with
            | Some c ->
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.Card(fouler, c) }
            | None -> ()

            rng'
        | _ -> rng

    /// Every foul needs a visible consequence: either the game continues (advantage)
    /// or a restart event is emitted — decided once so the viewer always sees it.
    let private processFoul
        (ctx: SystemContext)
        (world: World)
        (buffer: EventBuffer)
        (tick: int<subtick>)
        (ev: MatchEvent)
        : unit =
        match ev.Data with
        | EventData.Foul(fouler, fouled, severity, _, (fx, fy)) ->
            let fouledSide =
                MatchWorld.teamOfPlayer ctx.Ctx fouled |> Option.defaultValue ClubSide.Home

            let inBox = isFoulInBox fouledSide fx
            let advantage = shouldPlayAdvantage ctx.Ctx world fouledSide fx fy

            if advantage then
                // Play-on: emit nothing for the set-piece; the game continues.
                // The advantage window is implicit — no tick overhead.
                ()
            elif inBox then
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.SetPiece(SetPieceKind.Penalty, fouledSide) }
            else
                buffer.Events.Add
                    { Tick = tick
                      Data = EventData.SetPiece(SetPieceKind.FreeKick, fouledSide) }
        | _ -> ()

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick
            let mutable r = rng

            for ev in buffer.Applied do
                if MatchEvent.kind ev = EventKind.Foul then
                    r <- decideCard ctx r world buffer tick ev
                    processFoul ctx world buffer tick ev

            r
