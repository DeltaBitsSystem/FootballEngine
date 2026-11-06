namespace FootballEngine.Core.Match

open System.Numerics

module ContactActionPlanner =
    let private preparationDuration (config: ContactActionConfig) kind =
        match kind with
        | ContactActionKind.ContactContain -> config.ContainPreparation
        | ContactActionKind.ContactShield -> config.ShieldPreparation
        | ContactActionKind.ContactShoulderChallenge -> config.ShoulderPreparation
        | ContactActionKind.ContactStandingTackle -> config.StandingTacklePreparation
        | ContactActionKind.ContactSlidingTackle -> config.SlidingTacklePreparation
        | ContactActionKind.ContactTrap -> config.TrapPreparation
        | ContactActionKind.ContactStrike -> config.StrikePreparation
        | ContactActionKind.ContactHeader -> config.HeaderPreparation

    let private motionEffector foot kind =
        match kind, foot with
        | ContactActionKind.ContactHeader, _ -> MotionEffector.Head
        | ContactActionKind.ContactContain, _
        | ContactActionKind.ContactShield, _
        | ContactActionKind.ContactShoulderChallenge, _ -> MotionEffector.Body
        | _, Some ContactFoot.Left -> MotionEffector.LeftFoot
        | _, Some ContactFoot.Right -> MotionEffector.RightFoot
        | _ -> MotionEffector.Body

    let private motionPoint (action: ContactAction) =
        Vector3(float32 action.PredictedX, float32 action.PredictedY, float32 action.PredictedZ)

    let publish (world: World) (action: ContactAction) phase confirmedPoint =
        let ball = world.State.Ball
        let taskId = ActionId.value action.Id

        world.Motion.Append
            { Sequence = 0UL
              TaskId = taskId
              Actor = action.Actor
              Generation = action.Generation
              Family = MotionFamily.PhysicalContact
              Effector = motionEffector action.Foot action.Kind
              Phase = phase
              OccurredAt = world.State.Tick
              AvailableAt = world.State.Tick + 1<subtick>
              StartedAt = action.StartedAt
              CommitAt = action.CommitAt
              EndsAt = action.EndsAt
              PredictedPoint = motionPoint action
              ConfirmedPoint = confirmedPoint
              BallCenter =
                Vector3(
                    float32 ball.Position.X,
                    float32 ball.Position.Y,
                    float32 (ball.Position.Z + ball.Radius)
                )
              IncomingVelocity = ValueNone
              OutgoingVelocity = ValueNone }
        |> ignore

    let prepare
        (ctx: SystemContext)
        (world: World)
        (side: ClubSide)
        (slot: int)
        (kind: ContactActionKind)
        (target: PlayerId option)
        : ContactAction option =
        let team = MatchWorld.team world side
        let frame = team.Frame

        if
            world.State.Flow <> MatchFlow.Live
            || not (MatchWorld.isActiveSlot frame.Occupancy[slot])
             || frame.ContactActions[slot] |> Option.exists (fun action ->
                 action.Generation = frame.ContactGeneration[slot]
                && action.EndsAt > world.State.Tick
                 && action.Phase <> ContactActionPhase.ContactActionCancelled)
        then
            None
        else
            let tick = world.State.Tick
            let profile = MatchWorld.profileOf ctx.Ctx side slot
            let config = ctx.Ctx.Config.ContactAction
            let commitAt = tick + preparationDuration config kind
            let endsAt = commitAt + config.CommittedDuration + config.RecoveryDuration
            let seconds = float (commitAt - tick) / float ctx.Clock.SubTicksPerSecond
            let ball = world.State.Ball
            let predictedX = float ball.Position.X + float ball.Position.Vx * seconds
            let predictedY = float ball.Position.Y + float ball.Position.Vy * seconds
            let predictedZ =
                max 0.0 (
                    float ball.Position.Z
                    + float ball.Position.Vz * seconds
                    - 0.5 * float ctx.Ctx.Config.Ball.Gravity * seconds * seconds
                )

            let id = ActionId(0x8000000000000000UL ||| world.Motion.NextSequence)
            let action =
                { Id = id
                  Actor = profile.Id
                  Generation = frame.ContactGeneration[slot]
                  Kind = kind
                  Target = target
                  Phase = ContactActionPhase.ContactActionPreparing
                  StartedAt = tick
                  CommitAt = commitAt
                  EndsAt = endsAt
                  Foot =
                    if kind = ContactActionKind.ContactHeader then
                        None
                    else
                        Some profile.Morphology.PreferredFoot
                  PredictedX = predictedX * 1.0<meter>
                  PredictedY = predictedY * 1.0<meter>
                  PredictedZ = predictedZ * 1.0<meter>
                  ErrorX = 0.0f
                  ErrorY = 0.0f
                  ErrorZ = 0.0f }

            frame.ContactActions[slot] <- Some action

            world.Motion.Append
                { Sequence = 0UL
                  TaskId = ActionId.value id
                  Actor = profile.Id
                  Generation = action.Generation
                  Family = MotionFamily.PhysicalContact
                  Effector = motionEffector action.Foot action.Kind
                  Phase = MotionEvidencePhase.Prepared
                  OccurredAt = tick
                  AvailableAt = tick + 1<subtick>
                  StartedAt = tick
                  CommitAt = commitAt
                  EndsAt = endsAt
                  PredictedPoint = motionPoint action
                  ConfirmedPoint = ValueNone
                  BallCenter =
                    Vector3(
                        float32 ball.Position.X,
                        float32 ball.Position.Y,
                        float32 (ball.Position.Z + ball.Radius)
                    )
                  IncomingVelocity = ValueNone
                  OutgoingVelocity = ValueNone }
            |> ignore

            Some action
