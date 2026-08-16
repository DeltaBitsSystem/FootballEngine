namespace FootballEngine.Core.Match

/// Manager decisions: substitutions by score window, injury and red-card
/// reactions, and late-game directive pushes. Runs on the minute.
module ManagerSystem =

    /// Sub a player whose condition is below `threshold` if the bench has a
    /// better option and the sub windows allow it.
    let private subForCondition
        (ctx: SystemContext)
        (world: World)
        (buffer: EventBuffer)
        (side: ClubSide)
        (minute: int<matchMin>)
        (threshold: float32)
        : unit =
        let team = MatchWorld.team world side
        let frame = team.Frame

        if team.SubsUsed < ctx.Ctx.Config.Manager.MaxSubs then
            let windows = ctx.Ctx.Config.Manager

            let inWindow =
                (minute >= windows.FirstSubWindowMin)
                || (minute >= windows.SecondSubWindowMin)
                || (minute >= windows.ThirdSubWindowMin)

            if inWindow then
                let mutable worstSlot = -1
                let mutable worstCond = threshold

                for slot in MatchWorld.activeSlots frame do
                    if slot <> 0 && frame.Condition.[slot] < worstCond then
                        worstCond <- frame.Condition.[slot]
                        worstSlot <- slot

                if worstSlot >= 0 then
                    let lineup = MatchWorld.lineup ctx.Ctx side
                    let mutable bestBench = -1
                    let mutable bestScore = 0.0f

                    for slot in 0 .. lineup.SlotCount - 1 do
                        match frame.Occupancy.[slot] with
                        | OccupancyKind.Sidelined pOut when pOut.Reason = PlayerOutReason.Substituted ->
                            let p = lineup.Profiles.[slot]
                            let score = p.Pace + p.Passing + p.Finishing + p.Dribbling

                            if score > bestScore then
                                bestScore <- score
                                bestBench <- slot
                        | _ -> ()

                    if bestBench >= 0 then
                        let out = lineup.Profiles.[worstSlot].Id
                        let inn = lineup.Profiles.[bestBench].Id

                        buffer.Events.Add
                            { Tick = world.State.Tick
                              Data = EventData.Substitution(out, inn) }

    /// Immediate reaction to a red card: compact the shape if configured.
    let private reactToRedCard (ctx: SystemContext) (world: World) (side: ClubSide) : unit =
        let team = MatchWorld.team world side

        if ctx.Ctx.Config.Manager.RedCardCompact then
            let candidate =
                match team.Directive.Kind with
                | DirectiveKind.Balanced
                | DirectiveKind.Press -> DirectiveKind.Defend
                | DirectiveKind.Attack -> DirectiveKind.Balanced
                | other -> other

            if candidate <> team.Directive.Kind then
                team.Directive.Kind <- candidate
                team.Directive.SinceTick <- world.State.Tick

                team.Directive.Params.LineHeight <- max 15.0f (float32 (float team.Directive.Params.LineHeight - 4.0))
                team.Directive.Params.Width <- max 0.35f (float32 (float team.Directive.Params.Width - 0.1))

    let private manageTeam
        (ctx: SystemContext)
        (world: World)
        (buffer: EventBuffer)
        (side: ClubSide)
        (tick: int<subtick>)
        : unit =
        let minute = SimulationClock.toMatchMinute tick
        let diff = world.State.HomeScore - world.State.AwayScore
        let myDiff = if side = ClubSide.Home then diff else -diff
        let cfg = ctx.Ctx.Config.Manager
        let w = ctx.Ctx.Config.Weights.Manager

        // R10 — score-based substitution thresholds (condition is 0..1).
        let winT = float32 (float w.ConditionThresholdWinning / 100.0)
        let drawT = float32 (float w.ConditionThresholdDrawing / 100.0)
        let loseT = float32 (float w.ConditionThresholdLosing / 100.0)

        let threshold =
            if myDiff < 0 then loseT
            elif myDiff = 0 then drawT
            else winT

        subForCondition ctx world buffer side minute threshold

        // Late-game directive pushes from the scoreboard.
        let late = minute >= cfg.DrawAttackingMinute

        if late && myDiff <= -1 then
            let team = MatchWorld.team world side

            if team.Directive.Kind <> DirectiveKind.Attack then
                team.Directive.Kind <- DirectiveKind.Attack
                team.Directive.SinceTick <- tick
        elif minute >= cfg.WinningDefensiveMinute && myDiff >= 1 then
            let team = MatchWorld.team world side

            if team.Directive.Kind <> DirectiveKind.Defend then
                team.Directive.Kind <- DirectiveKind.Defend
                team.Directive.SinceTick <- tick

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick

            for side in [ ClubSide.Home; ClubSide.Away ] do
                // R10 — event reactions: red card compacts the shape.
                let redFor =
                    buffer.Applied
                    |> Seq.filter (fun ev -> MatchEvent.kind ev = EventKind.RedCard)
                    |> Seq.choose (fun ev ->
                        match ev.Data with
                        | EventData.RedCardIssued pid ->
                            MatchWorld.teamOfPlayer ctx.Ctx pid |> Option.filter (fun s -> s = side)
                        | _ -> None)
                    |> Seq.toList

                if not redFor.IsEmpty then
                    reactToRedCard ctx world side

                manageTeam ctx world buffer side tick

            rng
