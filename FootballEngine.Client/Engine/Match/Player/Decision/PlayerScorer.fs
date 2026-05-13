namespace FootballEngine.Player.Decision

open System
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Player.Actions
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract

[<Struct>]
type ActionScores =
    { Shoot: float<decisionScore>
      Pass: float<decisionScore>
      Dribble: float<decisionScore>
      Cross: float<decisionScore>
      LongBall: float<decisionScore> }

module PlayerScorer =

    let private normStat (v: int) = float v / 20.0
    let private condFactor (c: float32) = 0.90 + 0.10 * (float c / 100.0)

    let private directnessFactor (t: TacticsConfig) (profile: BehavioralProfile) =
        t.Directness * 0.6 + profile.Directness * 0.4

    let private buildUpSideBonus (_pos: Position) = 0.0

    let private shootScore (ctx: AgentContext) (matchMemory: MatchMemory) : float<decisionScore> =
        let me = ctx.Me
        let d = ctx.Decision
        let t = ctx.Tactics
        let finishing = normStat me.Technical.Finishing * d.ShootFinishingWeight
        let longShots = normStat me.Technical.LongShots * d.ShootLongShotsWeight
        let composure = normStat me.Mental.Composure * d.ShootComposureWeight

        let distNorm =
            (1.0 - clampFloat (float ctx.DistToGoal / d.ShootDistNormDivisor) 0.0 1.0)
            * d.ShootDistNormWeight

        let posBonus =
            ctx.Profile.Directness * d.ShootPosDirectnessWeight
            + ctx.Profile.AttackingDepth * d.ShootPosDepthWeight
            + if me.Position = ST then d.ShootSTBonus else 0.0

        let distPenalty =
            clampFloat (float ctx.DistToGoal / d.ShootDistPenaltyDivisor) 0.0 d.ShootDistPenaltyMax

        let df = directnessFactor t ctx.Profile
        let mentalityBonus = t.UrgencyMultiplier * 0.1
        let directnessBonus = df * d.ShootDirectnessBonus

        let composureStateMod = ctx.MentalState.ComposureLevel * 0.12
        let confidenceMod = ctx.MentalState.ConfidenceLevel * 0.08
        let focusMod = ctx.MentalState.FocusLevel * 0.06
        let riskBonus = ctx.MentalState.RiskTolerance * d.ShootDirectnessBonus * 0.5

        let streakMod =
            MatchMemory.successStreakModifier ctx.Team.ClubSide ctx.MeIdx matchMemory

        let decisionsWeight = normStat me.Mental.Positioning * d.ShootDecisionsWeight
        let braveryMod = normStat me.Mental.Bravery * d.ShootBraveryMod
        let concentrationMod = normStat me.Mental.Concentration * d.ShootConcentrationMod

        let goalX =
            if ctx.Team.AttackDir = LeftToRight then
                PitchLength
            else
                0.0<meter>

        let goalNearY = PostNearY
        let goalFarY = PostFarY
        let dx = float (abs (goalX - ctx.MyPos.X))
        let dy1 = float (goalNearY - ctx.MyPos.Y)
        let dy2 = float (goalFarY - ctx.MyPos.Y)

        let angleToGoal =
            if dx < 0.01 then
                90.0
            else
                abs (atan2 dy2 dx - atan2 dy1 dx) * 180.0 / System.Math.PI

        let pressureLevel =
            match ctx.NearestOpponentIdx with
            | ValueNone -> 0.0
            | ValueSome oppIdx ->
                let ox = float ctx.Team.OppFrame.Physics.PosX[oppIdx]
                let oy = float ctx.Team.OppFrame.Physics.PosY[oppIdx]
                let dx = float ctx.MyPos.X - ox
                let dy = float ctx.MyPos.Y - oy
                let dist = sqrt (dx * dx + dy * dy)
                1.0 - clampFloat (dist / 10.0) 0.0 1.0

        let isOneOnOne =
            let mutable gkCount = 0

            for i = 0 to ctx.Team.OppFrame.SlotCount - 1 do
                match ctx.Team.OppFrame.Physics.Occupancy[i] with
                | OccupancyKind.Active rosterIdx ->
                    if ctx.Team.OppRoster.Players[rosterIdx].Position = GK then
                        let ox = float ctx.Team.OppFrame.Physics.PosX[i] * 1.0<meter>

                        let inPath =
                            (ctx.Team.AttackDir = LeftToRight && ox > ctx.MyPos.X)
                            || (ctx.Team.AttackDir = RightToLeft && ox < ctx.MyPos.X)

                        if inPath then
                            gkCount <- gkCount + 1
                | _ -> ()

            gkCount = 1

        let xgModel =
            { DistanceToGoal = ctx.DistToGoal
              AngleToGoal = angleToGoal
              ShotType = ShotType.PlacedShot
              BodyPart = "foot"
              AssistType = ""
              PressureLevel = pressureLevel
              IsOneOnOne = isOneOnOne
              IsSetPiece = false }

        let xgValue = xGCalculator.calculate xgModel |> float

        let pressureResistance =
            let bravery = normStat me.Mental.Bravery
            bravery * pressureLevel

        let scoreRaw =
            finishing + longShots + composure + distNorm + posBonus - distPenalty
            + mentalityBonus
            + directnessBonus
            + composureStateMod
            + confidenceMod
            + focusMod
            + riskBonus
            + streakMod
            + decisionsWeight
            + braveryMod
            + concentrationMod
            + (xgValue * d.ShootXGWeight)
            + (pressureResistance * d.ShootBraveryPressureMod)

        let maxPossible =
            d.ShootFinishingWeight
            + d.ShootLongShotsWeight
            + d.ShootComposureWeight
            + d.ShootDistNormWeight
            + d.ShootPosDirectnessWeight
            + d.ShootPosDepthWeight
            + d.ShootSTBonus
            + d.ShootDecisionsWeight
            + d.ShootBraveryMod
            + d.ShootConcentrationMod
            + d.ShootXGWeight
            + d.ShootBraveryPressureMod
            + 1.0

        ((scoreRaw / maxPossible) * condFactor ctx.MyCondition)
        |> LanguagePrimitives.FloatWithMeasure<decisionScore>

    let private passScore (ctx: AgentContext) (matchMemory: MatchMemory) : float<decisionScore> =
        let me = ctx.Me
        let d = ctx.Decision
        let t = ctx.Tactics
        let passing = normStat me.Technical.Passing * d.PassPassingWeight
        let vision = normStat me.Mental.Vision * d.PassVisionWeight

        let targetBonus =
            match ctx.BestPassTargetIdx with
            | ValueNone -> 0.0
            | ValueSome targetIdx ->
                let target = ctx.Team.OwnRoster.Players[targetIdx]
                let receiverQuality = float target.Technical.BallControl / 20.0 * d.PassTargetBonus

                let targetMarkingPressure =
                    let targetX = ctx.Team.OwnFrame.Physics.PosX[targetIdx]
                    let targetY = ctx.Team.OwnFrame.Physics.PosY[targetIdx]
                    let mutable minDistSq = Single.MaxValue

                    for i = 0 to ctx.Team.OppFrame.SlotCount - 1 do
                        match ctx.Team.OppFrame.Physics.Occupancy[i] with
                        | OccupancyKind.Active _ ->
                            let dx = targetX - ctx.Team.OppFrame.Physics.PosX[i]
                            let dy = targetY - ctx.Team.OppFrame.Physics.PosY[i]
                            let d = dx * dx + dy * dy

                            if d < minDistSq then
                                minDistSq <- d
                        | _ -> ()

                    let nearestOppDist = MathF.Sqrt minDistSq
                    max 0.0f (1.0f - nearestOppDist / 5.0f) * 0.15f

                receiverQuality - float targetMarkingPressure

        let phaseBonusMax =
            max (ctx.BuildUp.PassSuccessBonus + ctx.BuildUp.GKDistributionBonus) 0.0

        let creativityBonusMax = d.CreativityWeight + d.DirectnessWeight

        let phaseMod =
            match ctx.Phase with
            | BuildUp ->
                let posBonus =
                    match me.Position with
                    | GK -> ctx.BuildUp.GKDistributionBonus
                    | DC
                    | DM -> ctx.BuildUp.DCPassingBonus
                    | _ -> 0.0

                ctx.BuildUp.PassSuccessBonus + posBonus
            | Midfield -> 0.0
            | Attack -> d.PassAttackPhasePenalty

        let creativityMod =
            ctx.Profile.CreativityWeight * d.CreativityWeight
            + (1.0 - ctx.Profile.Directness) * d.DirectnessWeight

        let df = directnessFactor t ctx.Profile
        let tempoBias = t.Tempo * 0.15
        let directBias = -(df * 0.20)

        let busMod = buildUpSideBonus me.Position

        let confidenceMod = ctx.MentalState.ConfidenceLevel * 0.08
        let riskMod = ctx.MentalState.RiskTolerance * d.PassTargetBonus * 0.3
        let focusMod = ctx.MentalState.FocusLevel * 0.05

        let passMemMod =
            MatchMemory.passFailureModifier ctx.Team.ClubSide ctx.MeIdx matchMemory

        let passDecisionsWeight = normStat me.Mental.Positioning * d.PassDecisionsWeight
        let passConcentrationMod = normStat me.Mental.Concentration * d.PassConcentrationMod

        let trajectoryBonus =
            match ctx.BestPassTargetIdx with
            | ValueSome targetIdx ->
                let rvx = float ctx.Team.OwnFrame.Physics.VelX[targetIdx] * 1.0<meter / second>
                let forwardDir = if ctx.Team.AttackDir = LeftToRight then 1.0 else -1.0
                let runningIntoSpace = forwardDir * rvx > 2.0<meter / second>
                if runningIntoSpace then d.PassTrajectoryBonus else 0.0
            | ValueNone -> 0.0

        let anticipationBonus =
            match ctx.BestPassTargetIdx with
            | ValueSome targetIdx ->
                let rvx = float ctx.Team.OwnFrame.Physics.VelX[targetIdx] * 1.0<meter / second>
                let forwardDir = if ctx.Team.AttackDir = LeftToRight then 1.0 else -1.0
                let runningIntoSpace = forwardDir * rvx > 2.0<meter / second>
                let anticipation = normStat me.Mental.Positioning * d.PassAnticipationBonus

                if runningIntoSpace then
                    anticipation
                else
                    anticipation * 0.3
            | ValueNone -> 0.0

        let influenceSpaceBonus =
            match ctx.BestPassTargetIdx with
            | ValueNone -> 0.0
            | ValueSome targetIdx ->
                let targetCell =
                    InfluenceTypes.posToCell
                        ctx.Team.OwnFrame.Physics.PosX[targetIdx]
                        ctx.Team.OwnFrame.Physics.PosY[targetIdx]

                let passSafety = float ctx.Influence.AttackerPassSafety[targetCell]
                let defCoverage = float ctx.Influence.DefenderCoverage[targetCell]
                (passSafety - 0.5) * 0.15 + (1.0 - defCoverage) * 0.10

        let scoreRaw =
            passing
            + vision
            + targetBonus
            + phaseMod
            + creativityMod
            + passMemMod
            + tempoBias
            + directBias
            + busMod
            + confidenceMod
            + riskMod
            + focusMod
            + influenceSpaceBonus
            + passDecisionsWeight
            + passConcentrationMod
            + trajectoryBonus
            + anticipationBonus

        let maxPossible =
            d.PassPassingWeight
            + d.PassVisionWeight
            + d.PassTargetBonus
            + phaseBonusMax
            + creativityBonusMax
            + tempoBias
            + abs directBias
            + busMod
            + confidenceMod
            + riskMod
            + focusMod
            + 0.25
            + d.PassDecisionsWeight
            + d.PassConcentrationMod
            + d.PassTrajectoryBonus
            + d.PassAnticipationBonus
            + d.PassHighPressPenalty

        ((scoreRaw / maxPossible) * condFactor ctx.MyCondition)
        |> LanguagePrimitives.FloatWithMeasure<decisionScore>

    let private dribbleScore (ctx: AgentContext) (matchMemory: MatchMemory) : float<decisionScore> =
        let me = ctx.Me
        let d = ctx.Decision
        let t = ctx.Tactics
        let dribbling = normStat me.Technical.Dribbling * ctx.Dribble.TechnicalWeight
        let agility = normStat me.Physical.Agility * ctx.Dribble.AgilityWeight
        let balance = normStat me.Physical.Balance * ctx.Dribble.BalanceWeight

        let zoneBonus =
            match ctx.Zone with
            | AttackingZone -> d.DribbleZoneBonusAttacking
            | MidfieldZone -> d.DribbleZoneBonusMidfield
            | DefensiveZone -> 0.0

        let phaseMod =
            match ctx.Phase with
            | BuildUp -> -ctx.BuildUp.DribblePenalty
            | Midfield -> 0.0
            | Attack -> d.DribbleAttackPhaseBonus

        let df = directnessFactor t ctx.Profile
        let tempoPenalty = t.Tempo * (1.0 - df) * d.DribbleTempoPenalty

        let zoneBonusMax = max d.DribbleZoneBonusAttacking d.DribbleZoneBonusMidfield
        let phaseBonusMax = d.DribbleAttackPhaseBonus

        let maxPossible =
            ctx.Dribble.TechnicalWeight
            + ctx.Dribble.AgilityWeight
            + ctx.Dribble.BalanceWeight
            + zoneBonusMax
            + phaseBonusMax
            + tempoPenalty

        let memMod =
            match ctx.NearestOpponentIdx with
            | ValueSome oppIdx ->
                let oppId = ctx.Team.OppRoster.Players[oppIdx].Id
                MatchMemory.duelHistoryModifier ctx.Team.ClubSide ctx.MeIdx matchMemory
            | ValueNone -> 0.0

        let streakMod =
            MatchMemory.successStreakModifier ctx.Team.ClubSide ctx.MeIdx matchMemory

        let scoreRaw =
            dribbling + agility + balance + zoneBonus + phaseMod + memMod + streakMod
            - tempoPenalty

        ((scoreRaw / maxPossible) * condFactor ctx.MyCondition)
        |> LanguagePrimitives.FloatWithMeasure<decisionScore>

    let private crossScore (ctx: AgentContext) (matchMemory: MatchMemory) : float<decisionScore> =
        let me = ctx.Me
        let d = ctx.Decision
        let t = ctx.Tactics
        let crossing = normStat me.Technical.Crossing * d.CrossCrossingWeight

        let posBonus =
            abs (ctx.Profile.LateralTendency - 0.5) * d.CrossLateralTendencyWeight

        let zoneBonus = if ctx.Zone = AttackingZone then d.CrossZoneBonus else 0.0
        let widthBonus = t.Width * ctx.Profile.LateralTendency * d.CrossWidthBonus
        let scoreRaw = crossing + posBonus + zoneBonus + widthBonus

        let maxPossible =
            d.CrossCrossingWeight
            + d.CrossLateralTendencyWeight
            + d.CrossZoneBonus
            + widthBonus

        ((scoreRaw / maxPossible) * condFactor ctx.MyCondition)
        |> LanguagePrimitives.FloatWithMeasure<decisionScore>

    let private longBallScore (ctx: AgentContext) (matchMemory: MatchMemory) : float<decisionScore> =
        let me = ctx.Me
        let d = ctx.Decision
        let t = ctx.Tactics
        let passing = normStat me.Technical.Passing * d.LongBallPassingWeight
        let vision = normStat me.Mental.Vision * d.LongBallVisionWeight

        let df = directnessFactor t ctx.Profile
        let directnessBonus = df * d.LongBallDirectnessBonus

        let pressureMod =
            match ctx.NearestOpponentIdx with
            | ValueSome oppIdx ->
                let oppX = float ctx.Team.OppFrame.Physics.PosX[oppIdx] * 1.0<meter>
                let oppY = float ctx.Team.OppFrame.Physics.PosY[oppIdx] * 1.0<meter>
                let dx = ctx.MyPos.X - oppX
                let dy = ctx.MyPos.Y - oppY
                let dist = sqrt (dx * dx + dy * dy)
                clampFloat (float dist / d.LongBallPressDistBase) d.LongBallPressMin d.LongBallPressMax
            | ValueNone -> d.LongBallPressNoOpponent

        let phaseMod =
            match ctx.Phase with
            | BuildUp -> -ctx.BuildUp.LongBallPenalty
            | Midfield -> 0.0
            | Attack -> d.LongBallAttackPhaseBonus

        let phaseBonusMax = d.LongBallAttackPhaseBonus

        let maxPossible =
            d.LongBallPassingWeight
            + d.LongBallVisionWeight
            + phaseBonusMax
            + directnessBonus

        let scoreRaw = (passing + vision) * pressureMod + phaseMod + directnessBonus

        ((scoreRaw / maxPossible) * condFactor ctx.MyCondition)
        |> LanguagePrimitives.FloatWithMeasure<decisionScore>

    let computeAll (ctx: AgentContext) (matchMemory: MatchMemory) : ActionScores =
        { Shoot = shootScore ctx matchMemory
          Pass = passScore ctx matchMemory
          Dribble = dribbleScore ctx matchMemory
          Cross = crossScore ctx matchMemory
          LongBall = longBallScore ctx matchMemory }
