namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract
open Stats

type HandballResult =
    | NoHandball
    | HandballNoFoul of position: float<meter> * float<meter>
    | HandballFreeKick of position: float<meter> * float<meter>
    | HandballPenalty of position: float<meter> * float<meter>

type HandballContext =
    { BallPos: Spatial
      PlayerPos: Spatial
      PlayerId: PlayerId
      IsGK: bool
      ArmPosition: float
      DeliberateMovement: bool
      DistanceFromShot: float<meter>
      InPenaltyArea: bool }

module HandballDetector =

    let private isInPenaltyArea (x: float<meter>) (y: float<meter>) (side: ClubSide) : bool =
        match side with
        | HomeClub ->
            x <= PenaltyAreaDepth
            && y >= (PitchWidth / 2.0 - PenaltyAreaHalfWidth)
            && y <= (PitchWidth / 2.0 + PenaltyAreaHalfWidth)
        | AwayClub ->
            x >= PitchLength - PenaltyAreaDepth
            && y >= (PitchWidth / 2.0 - PenaltyAreaHalfWidth)
            && y <= (PitchWidth / 2.0 + PenaltyAreaHalfWidth)

    let private armUnnaturalProbability (armPos: float) (deliberate: bool) : float =
        let baseProb = 0.15 + armPos * 0.3
        if deliberate then baseProb * 2.0 else baseProb

    let private closeToShotProbability (dist: float<meter>) : float =
        let d = float dist

        if d < 5.0 then 0.8
        elif d < 10.0 then 0.5
        elif d < 15.0 then 0.2
        else 0.05

    let assess (ctx: HandballContext) (defendingSide: ClubSide) : HandballResult =
        if ctx.IsGK then
            NoHandball
        else
            let armProb = armUnnaturalProbability ctx.ArmPosition ctx.DeliberateMovement
            let shotProb = closeToShotProbability ctx.DistanceFromShot
            let handballProb = min 1.0 (armProb + shotProb * 0.5)

            if not (bernoulli handballProb) then
                NoHandball
            else
                let inArea = isInPenaltyArea ctx.PlayerPos.X ctx.PlayerPos.Y defendingSide

                if inArea && ctx.DeliberateMovement then
                    HandballPenalty(ctx.PlayerPos.X, ctx.PlayerPos.Y)
                elif inArea then
                    HandballFreeKick(ctx.PlayerPos.X, ctx.PlayerPos.Y)
                else
                    HandballFreeKick(ctx.PlayerPos.X, ctx.PlayerPos.Y)

    let detect
        (ballPos: Spatial)
        (playerPos: Spatial)
        (playerId: PlayerId)
        (isGK: bool)
        (armPosition: float)
        (deliberate: bool)
        (distanceFromShot: float<meter>)
        (defendingSide: ClubSide)
        : HandballResult =

        let ctx =
            { BallPos = ballPos
              PlayerPos = playerPos
              PlayerId = playerId
              IsGK = isGK
              ArmPosition = armPosition
              DeliberateMovement = deliberate
              DistanceFromShot = distanceFromShot
              InPenaltyArea = false }

        assess ctx defendingSide
