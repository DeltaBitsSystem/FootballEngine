namespace FootballEngine.Player.Decision

open FootballEngine.Domain


open FootballEngine.Types
open FootballEngine.Types.SchedulingTypes
open PlayerPersonality

// ------------------------------------------------------------
// PlayerDecision
//
// Single responsibility: given a scored + masked action set,
// select an intent via softmax sampling.
//
// This module must NOT invent thresholds. Every numeric gate
// lives in DecisionConfig (BalanceConfig.fs) so it can be
// tuned without touching logic.
//
// Pipeline:
//   mask (impossible?) → config threshold (worth trying?) → softmax
//
// The mask handles physical impossibility.
// Config thresholds handle "not worth attempting given context".
// Softmax handles stochastic personality expression.
// ------------------------------------------------------------

module PlayerDecision =
    let inline ds v =
        LanguagePrimitives.FloatWithMeasure<decisionScore> v

    let decide (ctx: AgentContext) (scores: ActionScores) (mask: EligibilityMask) : OnBallIntent option =
        let t = ctx.Tactics
        let d = ctx.Decision // DecisionConfig — all thresholds live here

        // ----------------------------------------------------------
        // Shoot
        // Threshold from config, scaled by directness so direct teams
        // shoot more readily. mask.CanShoot already ruled out
        // impossible angles and fully blocked lanes.
        // ----------------------------------------------------------
        let shootThreshold =
            ds (d.ShootMinThreshold - t.Directness * d.ShootDirectnessThresholdMod)

        let shootBlocked = not mask.CanShoot || scores.Shoot < shootThreshold

        // ----------------------------------------------------------
        // Cross
        // No score threshold here — if position/target are valid
        // (mask) and cross score wins softmax, it fires.
        // ----------------------------------------------------------
        let crossBlocked = not mask.CanCross

        // ----------------------------------------------------------
        // Pass
        // mask.CanPass = target exists.
        // Threshold suppresses low-quality passes in direct/tempo play.
        // ----------------------------------------------------------
        let passThreshold =
            ds (d.PassMinThreshold - t.Directness * d.PassDirectnessThresholdMod)

        let passBlocked =
            not mask.CanPass
            || match ctx.BestPassTargetIdx with
               | ValueNone -> true
               | ValueSome _ -> scores.Pass < passThreshold

        // ----------------------------------------------------------
        // Dribble
        // mask.CanDribble = not immediately dispossessed.
        // Config thresholds add tactical/personality layers on top.
        // ----------------------------------------------------------
        let dribbleThreshold = ds d.DribbleMinThreshold

        let dribbleBlocked =
            not mask.CanDribble
            || scores.Dribble < dribbleThreshold
            || (ctx.Zone = DefensiveZone
                && ctx.Profile.Directness < d.DribbleDefZoneDirectnessMin)
            || (t.Tempo > d.DribbleHighTempo
                && ctx.Profile.Directness < d.DribbleHighTempoDirectnessMin)

        // ----------------------------------------------------------
        // Long ball — config threshold + mask
        // ----------------------------------------------------------
        let longBallThreshold =
            d.LongBallMinThreshold - t.Directness * d.LongBallDirectnessThresholdMod

        // ----------------------------------------------------------
        // Pass modifiers
        // ----------------------------------------------------------
        let passMod =
            if ctx.Phase = BuildUp then
                1.0 + ctx.Profile.CreativityWeight * 0.15
            else
                1.0

        // ----------------------------------------------------------
        // PassIntoSpace
        // Only evaluated when a regular pass is already unblocked —
        // prevents it from becoming a permanent bypass for all other
        // blocked actions.
        // ----------------------------------------------------------
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

        let personality = deriveWithDefaults ctx.Me

        // ----------------------------------------------------------
        // Build candidate list — all scores are already [0..1]
        // normalized by PlayerScorer, so they are directly comparable
        // across action types in softmax.
        // ----------------------------------------------------------
        let candidates: (float * OnBallIntent) list =
            [ if not shootBlocked then
                  let flairBonus = personality.Flair * 0.2
                  let s = float scores.Shoot * (1.0 + ctx.Urgency * 0.5 + flairBonus)
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

        ActionMath.softmaxSample effectiveTemp candidates
