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

        if
            team.SubsUsed < ctx.Ctx.SubstitutionRules.MaxSubstitutions
            && team.Substitution.Phase <> SubstitutionPhase.Requested
            && team.Substitution.Phase <> SubstitutionPhase.Accepted
            && team.Substitution.Phase <> SubstitutionPhase.Pending
        then
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
                              Data = EventData.SubstitutionRequested(out, inn) }

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
        (simTick: int<subtick>)
        (playClock: PlayClock)
        : unit =
        let minute = PlayClock.toMatchMinute playClock
        let diff = world.State.HomeScore - world.State.AwayScore
        let myDiff = if side = ClubSide.Home then diff else -diff
        let cfg = ctx.Ctx.Config.Manager
        let winT = float32 cfg.ConditionThresholdWinning
        let drawT = float32 cfg.ConditionThresholdDrawing
        let loseT = float32 cfg.ConditionThresholdLosing

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
                team.Directive.SinceTick <- simTick
        elif minute >= cfg.WinningDefensiveMinute && myDiff >= 1 then
            let team = MatchWorld.team world side

            if team.Directive.Kind <> DirectiveKind.Defend then
                team.Directive.Kind <- DirectiveKind.Defend
                team.Directive.SinceTick <- simTick

    // ---- The autonomous side: a rival manager with a brain ----

    let private clampF32 (lo: float32) (hi: float32) (v: float32) = max lo (min hi v)

    let private canChange (ctx: SystemContext) (team: TeamWorld) =
        team.SubsUsed < ctx.Ctx.SubstitutionRules.MaxSubstitutions
        && team.Substitution.Phase <> SubstitutionPhase.Requested
        && team.Substitution.Phase <> SubstitutionPhase.Accepted
        && team.Substitution.Phase <> SubstitutionPhase.Pending

    let private benchSlots (lineup: Lineup) (frame: TeamFrame) : int list =
        [ for slot in lineup.StarterCount .. lineup.SlotCount - 1 do
              match frame.Occupancy.[slot] with
              | OccupancyKind.Sidelined pOut when pOut.Reason = PlayerOutReason.Substituted -> yield slot
              | _ -> () ]

    let private isForwardRole (role: SlotRole) =
        match role with
        | SlotRole.Striker
        | SlotRole.AttackingMid
        | SlotRole.Winger -> true
        | _ -> false

    let private isDefensiveRole (role: SlotRole) =
        match role with
        | SlotRole.Defender
        | SlotRole.FullBack
        | SlotRole.DefensiveMid -> true
        | _ -> false

    let private attackScore (p: PlayerProfile) = p.Pace + p.Passing + p.Finishing + p.Dribbling

    let private defendScore (p: PlayerProfile) = p.Tackling + p.Marking + p.Positioning + p.Strength

    let private mostTired (frame: TeamFrame) (candidates: int list) : int voption =
        match candidates with
        | [] -> ValueNone
        | _ -> ValueSome(candidates |> List.minBy (fun slot -> frame.Condition.[slot], slot))

    let private bestOf (score: PlayerProfile -> float32) (lineup: Lineup) (slots: int list) : int voption =
        match slots with
        | [] -> ValueNone
        | _ -> ValueSome(slots |> List.maxBy (fun slot -> score lineup.Profiles.[slot], -slot))

    /// The change that goes with a move: who comes off and who comes on, by what the move is for.
    let private pickChange (ctx: SystemContext) (world: World) (side: ClubSide) (intent: SubstitutionIntent) : (int * int) voption =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let lineup = MatchWorld.lineup ctx.Ctx side
        let active = MatchWorld.activeSlots frame |> List.ofArray
        let bench = benchSlots lineup frame
        let roleOf slot = lineup.Profiles.[slot].Role

        let out, inn =
            match intent with
            | SubstitutionIntent.Attack ->
                let candidates =
                    active
                    |> List.filter (fun slot ->
                        match roleOf slot with
                        | SlotRole.DefensiveMid
                        | SlotRole.Defender -> true
                        | _ -> false)

                mostTired frame candidates,
                bestOf attackScore lineup (bench |> List.filter (fun slot -> isForwardRole (roleOf slot)))
            | SubstitutionIntent.Defend ->
                let candidates = active |> List.filter (fun slot -> isForwardRole (roleOf slot))

                mostTired frame candidates,
                bestOf defendScore lineup (bench |> List.filter (fun slot -> isDefensiveRole (roleOf slot)))
            | SubstitutionIntent.Keep -> ValueNone, ValueNone

        match out, inn with
        | ValueSome o, ValueSome i -> ValueSome(o, i)
        | _ -> ValueNone

    /// A booked defender late in the match is a red card waiting to happen: a coach who reads the game takes him off.
    let private pickCardRiskChange (ctx: SystemContext) (world: World) (side: ClubSide) : (int * int) voption =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let lineup = MatchWorld.lineup ctx.Ctx side

        let booked =
            MatchWorld.activeSlots frame
            |> List.ofArray
            |> List.filter (fun slot ->
                (lineup.Profiles.[slot].Role = SlotRole.Defender || lineup.Profiles.[slot].Role = SlotRole.FullBack)
                && team.Memory.[slot].Yellows >= 1)

        let replacements =
            benchSlots lineup frame
            |> List.filter (fun slot -> isDefensiveRole lineup.Profiles.[slot].Role)

        match booked, bestOf defendScore lineup replacements with
        | [], _ -> ValueNone
        | slots, ValueSome replacement -> ValueSome(slots |> List.maxBy (fun slot -> team.Memory.[slot].Yellows, -slot), replacement)
        | _, ValueNone -> ValueNone

    /// The tired player replaced by someone who plays his role, instead of by whoever rates best.
    let private pickRoleMatchedChange (ctx: SystemContext) (world: World) (side: ClubSide) (threshold: float32) : (int * int) voption =
        let team = MatchWorld.team world side
        let frame = team.Frame
        let lineup = MatchWorld.lineup ctx.Ctx side

        let tired =
            MatchWorld.activeSlots frame
            |> List.ofArray
            |> List.filter (fun slot -> lineup.Profiles.[slot].Role <> SlotRole.Goalkeeper && frame.Condition.[slot] < threshold)

        match mostTired frame tired with
        | ValueNone -> ValueNone
        | ValueSome out ->
            let role = lineup.Profiles.[out].Role
            let sameRole = benchSlots lineup frame |> List.filter (fun slot -> lineup.Profiles.[slot].Role = role)

            match bestOf (fun p -> attackScore p + defendScore p) lineup sameRole with
            | ValueSome inn -> ValueSome(out, inn)
            | ValueNone -> ValueNone

    let private requestChange (world: World) (buffer: EventBuffer) (ctx: SystemContext) (side: ClubSide) (out: int, inn: int) =
        let lineup = MatchWorld.lineup ctx.Ctx side

        buffer.Events.Add
            { Tick = world.State.Tick
              Data = EventData.SubstitutionRequested(lineup.Profiles.[out].Id, lineup.Profiles.[inn].Id) }

    /// Measures every shift from the plan the coach started with, so a chain of shifts never compounds.
    let private applyMove (ctx: SystemContext) (world: World) (buffer: EventBuffer) (side: ClubSide) (move: ManagerMove) =
        let cfg = ctx.Ctx.Config.RivalManager
        let team = MatchWorld.team world side
        let brain = team.Brain

        let plan =
            match brain.BasePlan with
            | ValueSome plan -> plan
            | ValueNone ->
                brain.BasePlan <- ValueSome team.Tactics
                team.Tactics

        let mentality, line =
            match move.Shift with
            | ManagerShift.ChaseGame -> cfg.ChaseMentalityShift, cfg.AttackLineShift
            | ManagerShift.AllOut -> cfg.AllOutMentalityShift, cfg.AttackLineShift
            | ManagerShift.TakeInitiative -> cfg.InitiativeMentalityShift, cfg.AttackLineShift
            | ManagerShift.ProtectLead -> -cfg.ProtectMentalityDrop, -cfg.DefendLineShift
            | ManagerShift.WeatherStorm -> -cfg.StormMentalityDrop, -cfg.DefendLineShift
            | ManagerShift.RedCardReshape -> -cfg.RedCardMentalityDrop, -cfg.DefendLineShift
            | ManagerShift.BackToPlan -> 0.0, 0.0

        let tactics =
            match move.Shift with
            | ManagerShift.BackToPlan -> plan
            | _ ->
                { plan with
                    Mentality = clampF32 0.0f 1.0f (plan.Mentality + float32 (mentality * move.Intensity))
                    DefensiveLine = clampF32 15.0f 70.0f (plan.DefensiveLine + float32 (line * move.Intensity)) }

        team.Tactics <- tactics
        team.Directive.Kind <- move.Directive
        team.Directive.SinceTick <- world.State.Tick
        team.Directive.Params.LineHeight <- tactics.DefensiveLine
        team.Directive.Params.PressIntensity <- tactics.PressIntensity
        team.Directive.Params.Width <- tactics.Width

        // The viewer only knows what an event tells it: the instruction and the reason travel together.
        buffer.Events.Add
            { Tick = world.State.Tick
              Data = EventData.TeamInstruction side }

        buffer.Events.Add
            { Tick = world.State.Tick
              Data = EventData.ManagerShift(side, move.Shift) }

        brain.Shift <-
            (if move.Shift = ManagerShift.BackToPlan then
                 ValueNone
             else
                 ValueSome move.Shift)

        brain.EventSinceMove <- false

    let private autonomousStep
        (ctx: SystemContext)
        (rng: Rng)
        (world: World)
        (buffer: EventBuffer)
        (side: ClubSide)
        (playClock: PlayClock)
        : Rng =
        let cfg = ctx.Ctx.Config.RivalManager
        let profile = (if side = ClubSide.Home then ctx.Ctx.HomeClub else ctx.Ctx.AwayClub).Manager
        let team = MatchWorld.team world side
        let opponent = MatchWorld.team world (if side = ClubSide.Home then ClubSide.Away else ClubSide.Home)
        let brain = team.Brain
        let minute = int (PlayClock.toMatchMinute playClock)
        let isMinuteTick = PlayClock.value playClock % SimulationClock.minuteSubticks = 0<subtick>

        let mutable goal = false
        let mutable ownRed = false
        let mutable halfTime = false

        for ev in buffer.Applied do
            match ev.Data with
            | EventData.Goal _ -> goal <- true
            | EventData.RedCardIssued pid ->
                if MatchWorld.sideOfPlayer ctx.Ctx pid = ValueSome side then
                    ownRed <- true
            | EventData.HalfTime -> halfTime <- true
            | _ -> ()

        let incident = goal || ownRed || halfTime

        if incident then
            let due = ManagerBrain.reviewMinuteAfter cfg profile minute

            brain.PendingReviewMinute <-
                if brain.PendingReviewMinute >= 0 then
                    min brain.PendingReviewMinute due
                else
                    due

        if goal || ownRed then
            brain.EventSinceMove <- true

        if ownRed then
            brain.OwnRedSinceReview <- true

        if halfTime then
            brain.HalfTimeSeen <- true

        let pendingDue =
            brain.PendingReviewMinute >= 0
            && minute >= brain.PendingReviewMinute
            && (minute > brain.LastReviewMinute || incident)

        let intervalDue = isMinuteTick && minute - brain.LastReviewMinute >= cfg.ReviewIntervalMin
        let mutable rng = rng
        let mutable requested = false

        // A team holds one substitution at a time: the rest of a double change waits for the first to be made.
        if brain.QueuedChanges > 0 && isMinuteTick && canChange ctx team then
            match pickChange ctx world side brain.QueuedIntent with
            | ValueSome change ->
                requestChange world buffer ctx side change
                brain.QueuedChanges <- brain.QueuedChanges - 1
                requested <- true
            | ValueNone -> brain.QueuedChanges <- 0

        if pendingDue || intervalDue then
            let noise, next = Rng.normal 0.0 1.0 rng
            rng <- next
            let diff = world.State.HomeScore - world.State.AwayScore
            let myDiff = if side = ClubSide.Home then diff else -diff
            let fade = 0.5 ** (float (max 0 (minute - brain.LastReviewMinute)) / cfg.MomentumHalfLifeMin)
            brain.RecentXgFor <- brain.RecentXgFor * fade + (team.Stats.XgSum - brain.LastXgFor)
            brain.RecentXgAgainst <- brain.RecentXgAgainst * fade + (opponent.Stats.XgSum - brain.LastXgAgainst)

            let read: ManagerRead =
                { Minute = minute
                  ScoreDiff = myDiff
                  Momentum = ManagerBrain.momentum cfg brain.RecentXgFor brain.RecentXgAgainst
                  MinutesSinceMove = minute - brain.LastMoveMinute
                  EventSinceMove = brain.EventSinceMove
                  OwnRedSinceReview = brain.OwnRedSinceReview
                  Current = brain.Shift }

            brain.LastReviewMinute <- minute
            brain.PendingReviewMinute <- -1
            brain.LastXgFor <- team.Stats.XgSum
            brain.LastXgAgainst <- opponent.Stats.XgSum
            brain.OwnRedSinceReview <- false
            let halfTimeReview = brain.HalfTimeSeen
            brain.HalfTimeSeen <- false

            match ManagerBrain.decide cfg profile read noise with
            | ValueSome move ->
                applyMove ctx world buffer side move
                brain.LastMoveMinute <- minute

                if not requested && canChange ctx team then
                    match pickChange ctx world side move.Intent with
                    | ValueSome change ->
                        requestChange world buffer ctx side change
                        requested <- true
                    | ValueNone -> ()
            | ValueNone -> ()

            // A hothead who is losing at the break cannot wait: he asks for a second change as well.
            if halfTimeReview && myDiff < 0 && float profile.Temperament >= cfg.HotheadTemperament then
                brain.QueuedChanges <- cfg.HotheadHalfTimeChanges - (if requested then 1 else 0)
                brain.QueuedIntent <- SubstitutionIntent.Attack

            if
                not requested
                && float profile.Reading >= cfg.SmartChangeReadingMin
                && minute >= cfg.CardRiskMinute
                && canChange ctx team
            then
                match pickCardRiskChange ctx world side with
                | ValueSome change ->
                    requestChange world buffer ctx side change
                    requested <- true
                | ValueNone -> ()

        // The condition change every side makes: a coach who reads the game keeps the tired player's role.
        if not requested && isMinuteTick && canChange ctx team then
            let diff = world.State.HomeScore - world.State.AwayScore
            let myDiff = if side = ClubSide.Home then diff else -diff

            let threshold =
                if myDiff < 0 then float32 ctx.Ctx.Config.Manager.ConditionThresholdLosing
                elif myDiff = 0 then float32 ctx.Ctx.Config.Manager.ConditionThresholdDrawing
                else float32 ctx.Ctx.Config.Manager.ConditionThresholdWinning

            if float profile.Reading >= cfg.SmartChangeReadingMin then
                if minute >= int ctx.Ctx.Config.Manager.FirstSubWindowMin then
                    match pickRoleMatchedChange ctx world side threshold with
                    | ValueSome change -> requestChange world buffer ctx side change
                    | ValueNone -> subForCondition ctx world buffer side (PlayClock.toMatchMinute playClock) threshold
            else
                subForCondition ctx world buffer side (PlayClock.toMatchMinute playClock) threshold

        rng

    let step: SystemStep =
        fun ctx rng world buffer ->
            let tick = world.State.Tick
            let playClock = MatchClock.playClock world.State.Clock
            // The old schedule (every minute) still governs the sides the user commands; the incident triggers
            // exist for the autonomous ones.
            let isMinuteTick = PlayClock.value playClock % SimulationClock.minuteSubticks = 0<subtick>
            let mutable rng = rng

            for side in [ ClubSide.Home; ClubSide.Away ] do
                let profile = (if side = ClubSide.Home then ctx.Ctx.HomeClub else ctx.Ctx.AwayClub).Manager

                if profile.Autonomous then
                    rng <- autonomousStep ctx rng world buffer side playClock
                elif isMinuteTick then
                    // R10 — event reactions: red card compacts the shape.
                    let redFor =
                        buffer.Applied
                        |> Seq.filter (fun ev -> MatchEvent.kind ev = EventKind.RedCard)
                        |> Seq.choose (fun ev ->
                            match ev.Data with
                            | EventData.RedCardIssued pid ->
                                MatchWorld.sideOfPlayer ctx.Ctx pid |> ValueOption.filter (fun s -> s = side)
                                |> ValueOption.toOption
                            | _ -> None)
                        |> Seq.toList

                    if not redFor.IsEmpty then
                        reactToRedCard ctx world side

                    manageTeam ctx world buffer side tick playClock

            rng
