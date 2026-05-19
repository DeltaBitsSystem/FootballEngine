namespace FootballEngine.Types

open FootballEngine.Domain
open FootballEngine.Types.PhysicsContract
open FootballEngine.Types

module ShapeEngine =

    let ShapeInterval = 15

    let computeShapeTargets
        (basePositions: Spatial[])
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
        let ballPullBase = (ballX - HalfwayLineX) * 0.08 * forwardDir
        let pressureCoeff = tacticsCfg.PressureDistance * 0.008

        let widthSpread = 0.7 + tacticsCfg.Width * 0.6

        let damping = 0.15

        for i = 0 to n - 1 do
            let basePos = basePositions[i]
            let profile = profiles[i]

            let phaseShift =
                match phase with
                | BuildUp -> (-4.0<meter> + pressMod * 2.0<meter>) * forwardDir * profile.DefensiveHeight
                | Midfield -> (pressMod * 1.0<meter>) * forwardDir * profile.PressingIntensity
                | Attack -> (3.0<meter> + pressMod * 2.0<meter>) * forwardDir * profile.AttackingDepth

            let targetPullX =
                clamp (ballPullBase * (float i / float (n - 1))) -6.0<meter> 6.0<meter>

            let ballPullX = targetPullX * damping * profile.PositionalFreedom

            let compactShift = (basePos.Y - 34.0<meter>) * pressureCoeff

            let widthShift =
                (basePos.Y - 34.0<meter>) * (widthSpread - 1.0) * (abs profile.LateralTendency + 0.5)

            let x = basePos.X + phaseShift + tacticalPush + defensiveDrop + ballPullX
            let y = basePos.Y + compactShift + widthShift

            let finalX = clamp x 2.0<meter> (PitchLength - 7.0<meter>)
            let finalY = clamp y 3.0<meter> (PitchWidth - 3.0<meter>)

            targetX[i] <- float32 finalX
            targetY[i] <- float32 finalY
