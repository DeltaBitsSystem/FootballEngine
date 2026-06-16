namespace FootballEngine.Player.Decision

open FootballEngine.Domain


open FootballEngine.Types
open FootballEngine.Types.SchedulingTypes
open PlayerPersonality

module PlayerDecision =
    let inline ds v =
        LanguagePrimitives.FloatWithMeasure<decisionScore> v

    let decide
        (ctx: AgentContext)
        (scores: ActionScores)
        (mask: EligibilityMask)
        : OnBallIntent option * DecisionTrace option =
        let t = ctx.Tactics
        let d = ctx.Decision

        let shootThreshold =
            ds (d.ShootMinThreshold - t.Directness * d.ShootDirectnessThresholdMod)

        let shootBlocked = not mask.CanShoot || scores.Shoot < shootThreshold

        let shootBlockReason =
            if not mask.CanShoot then "angle"
            elif scores.Shoot < shootThreshold then "threshold"
            else ""

        let crossBlocked = not mask.CanCross
        let crossBlockReason = if not mask.CanCross then "position" else ""

        let passThreshold =
            ds (d.PassMinThreshold - t.Directness * d.PassDirectnessThresholdMod)

        let passBlocked =
            not mask.CanPass
            || match ctx.BestPassTargetIdx with
               | ValueNone -> true
               | ValueSome _ -> scores.Pass < passThreshold

        let passBlockReason =
            if not mask.CanPass then "no_target"
            elif ctx.BestPassTargetIdx = ValueNone then "no_target"
            elif scores.Pass < passThreshold then "threshold"
            else ""

        let dribbleThreshold = ds d.DribbleMinThreshold

        let dribbleBlocked =
            not mask.CanDribble
            || scores.Dribble < dribbleThreshold
            || ctx.ImmediatePressure > d.DribblePressureBlockThreshold
            || (ctx.Zone = DefensiveZone
                && ctx.Profile.Directness < d.DribbleDefZoneDirectnessMin)
            || (t.Tempo > d.DribbleHighTempo
                && ctx.Profile.Directness < d.DribbleHighTempoDirectnessMin)

        let dribbleBlockReason =
            if not mask.CanDribble then
                "pressure"
            elif scores.Dribble < dribbleThreshold then
                "threshold"
            elif ctx.ImmediatePressure > d.DribblePressureBlockThreshold then
                "pressure"
            elif
                ctx.Zone = DefensiveZone
                && ctx.Profile.Directness < d.DribbleDefZoneDirectnessMin
            then
                "zone"
            elif
                t.Tempo > d.DribbleHighTempo
                && ctx.Profile.Directness < d.DribbleHighTempoDirectnessMin
            then
                "tempo"
            else
                ""

        let longBallThreshold =
            d.LongBallMinThreshold - t.Directness * d.LongBallDirectnessThresholdMod

        let longBallBlocked = not mask.CanLongBall
        let longBallBlockReason = if not mask.CanLongBall then "mask" else ""

        let passMod =
            if ctx.Phase = BuildUp then
                1.0 + ctx.Profile.CreativityWeight * 0.15
            else
                1.0

        let spacePassCandidate =
            if passBlocked then
                None
            else
                match SpaceScorer.findThroughBallTarget ctx with
                | ValueNone -> None
                | ValueSome spaceTarget ->
                    let spaceScore = SpaceScorer.spacePassScore ctx spaceTarget.Cell

                    if float spaceScore > d.SpacePassMinScore then
                        Some(float spaceScore, OnBallIntent.PassIntoSpace spaceTarget.Cell)
                    else
                        None

        let personality = PlayerPersonality.derive ctx.Me ctx.Weights.Personality

        let candidates: (float * OnBallIntent) list =
            [ if not shootBlocked then
                  let flairBonus = personality.Flair * 0.2
                  let s = float scores.Shoot * (1.0 + ctx.Urgency * 0.2 + flairBonus)
                  yield s, OnBallIntent.Shoot

              if not passBlocked then
                  let teamworkBonus = personality.Teamwork * 0.15
                  let s = float scores.Pass * passMod * (1.0 + teamworkBonus)

                  match ctx.BestPassTargetIdx with
                  | ValueSome idx ->
                      let targetPid = ctx.Team.OwnRoster.Players[idx].Id
                      yield s, OnBallIntent.Pass targetPid
                  | ValueNone -> ()

              match spacePassCandidate with
              | Some(s, intent) -> yield s, intent
              | None -> ()

              if not dribbleBlocked then
                  let consistencyMod = 1.0 - personality.Consistency * 0.1
                  let s = float scores.Dribble * consistencyMod
                  yield s, OnBallIntent.Dribble

              if not crossBlocked then
                  let s = float scores.Cross * (1.0 + ctx.Urgency * 0.1)
                  yield s, OnBallIntent.Cross

              match ctx.BestPassTargetIdx with
              | ValueSome idx when mask.CanLongBall ->
                  let s = float scores.LongBall * (1.0 + ctx.Urgency * 0.15)

                  if s > float longBallThreshold then
                      let targetPid = ctx.Team.OwnRoster.Players[idx].Id
                      yield s, OnBallIntent.LongBall targetPid
              | _ -> () ]

        let effectiveTemp =
            ctx.Decision.DecisionTemperature
            * (2.0 - ctx.MentalState.ComposureLevel)
            * (1.0 + ctx.Urgency * 0.5)
            * (2.0 - ctx.MentalState.FocusLevel)

        let intent = ActionMath.softmaxSample effectiveTemp candidates

        let chosenActionName =
            match intent with
            | Some OnBallIntent.Shoot -> "Shoot"
            | Some(OnBallIntent.Pass _) -> "Pass"
            | Some OnBallIntent.Dribble -> "Dribble"
            | Some OnBallIntent.Cross -> "Cross"
            | Some(OnBallIntent.LongBall _) -> "LongBall"
            | Some(OnBallIntent.PassIntoSpace _) -> "PassIntoSpace"
            | Some(OnBallIntent.Tackle _) -> "Tackle"
            | None -> "None"

        let trace =
            if ctx.Weights.Diagnostics.TraceDecisions then
                let minuteNorm = float ctx.Minute / 90.0

                let isDrawLate = ctx.GoalDiff = 0 && ctx.Minute >= 75

                let isLosingLate = ctx.GoalDiff < 0 && ctx.Minute >= 75

                let mkTrace (blocked: bool) (reason: string) (raw: float) =
                    { RawScore = raw
                      SituationalMod = 1.0
                      FinalScore = if blocked then 0.0 else raw
                      Blocked = blocked
                      BlockReason = reason }

                Some
                    { SubTick = ctx.CurrentSubTick
                      PlayerId = ctx.Me.Id
                      Shoot = mkTrace shootBlocked shootBlockReason (float scores.Shoot)
                      Pass = mkTrace passBlocked passBlockReason (float scores.Pass)
                      Dribble = mkTrace dribbleBlocked dribbleBlockReason (float scores.Dribble)
                      Cross = mkTrace crossBlocked crossBlockReason (float scores.Cross)
                      LongBall = mkTrace longBallBlocked longBallBlockReason (float scores.LongBall)
                      ChosenAction = chosenActionName
                      SoftmaxTemperature = ctx.Weights.Decision.DecisionTemperature
                      MatchSituationSnapshot =
                        { MinuteNorm = minuteNorm
                          GoalDiff = ctx.GoalDiff
                          IsDrawLate = isDrawLate
                          IsLosingLate = isLosingLate
                          MomentumNorm = 0.0
                          PossessionPressure = ctx.ImmediatePressure
                          TeamInTransition = ctx.TransitionPressExpiry > 0
                          TeamPressingHigh = false
                          ShootMod = 1.0
                          PassMod = passMod
                          DribbleMod = 1.0
                          LongBallMod = 1.0 } }
            else
                None

        intent, trace
