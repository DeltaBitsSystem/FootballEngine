namespace FootballEngine.Types

open FootballEngine.Domain
open FootballEngine.Types.PhysicsContract
open FootballEngine.Types

module ShapeEngine =

    let ShapeInterval = 15

    let computeShapeTargets
        (basePositions: Spatial[])
        (currentPositions: PhysicsFrame)
        (profiles: BehavioralProfile[])
        (dir: AttackDir)
        (phase: MatchPhase)
        (ballX: float<meter>)
        (tacticsCfg: TacticsConfig)
        (targetX: float32[])
        (targetY: float32[])
        : unit =

        let forwardDir = forwardX dir

        let pressMod = tacticsCfg.PressingIntensity - 1.0

        let n = basePositions.Length

        let tacticalPush = tacticsCfg.ForwardPush * 0.5<meter> * forwardDir
        let defensiveDrop = tacticsCfg.DefensiveDrop * 0.5<meter> * forwardDir
        let ballPullBase = (ballX - HalfwayLineX) * 0.35 * forwardDir
        let pressureCoeff = tacticsCfg.PressureDistance * 0.008

        let widthSpread = 0.7 + tacticsCfg.Width * 0.6

        for i = 0 to n - 1 do
            let basePos = basePositions[i]
            let profile = profiles[i]

            let phaseShift =
                match phase with
                | BuildUp -> (-12.0<meter> + pressMod * 4.0<meter>) * forwardDir * profile.DefensiveHeight
                | Midfield -> (pressMod * 2.0<meter>) * forwardDir * profile.PressingIntensity
                | Attack -> (10.0<meter> + pressMod * 4.0<meter>) * forwardDir * profile.AttackingDepth

            let playerDepthRatio =
                let minX = 2.0<meter>
                let maxX = PitchLength - 7.0<meter>
                let ratio = (basePos.X - minX) / (maxX - minX)
                clamp ratio 0.0 1.0

            let targetPullX = clamp (ballPullBase * playerDepthRatio) -8.0<meter> 8.0<meter>

            let ballPullX = targetPullX * profile.PositionalFreedom

            let currentY = float currentPositions.PosY[i] * 1.0<meter>
            let compactShift = (currentY - 34.0<meter>) * pressureCoeff

            let widthShift =
                (basePos.Y - 34.0<meter>)
                * (widthSpread - 1.0)
                * (abs profile.LateralTendency + 0.5)

            let x = basePos.X + phaseShift + tacticalPush + defensiveDrop + ballPullX
            let y = basePos.Y + compactShift + widthShift

            let finalX = clamp x 2.0<meter> (PitchLength - 7.0<meter>)
            let finalY = clamp y 3.0<meter> (PitchWidth - 3.0<meter>)

            targetX[i] <- float32 finalX
            targetY[i] <- float32 finalY
