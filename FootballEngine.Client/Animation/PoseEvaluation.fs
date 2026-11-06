namespace FootballEngine.Client.Animation

open System
open System.Numerics
open FootballEngine.Core.Match

type PoseScratch(rig: RigDefinition) =
    member val Local = Array.zeroCreate<RigLocalPose> rig.Bind.Length
    member val World = Array.create rig.Bind.Length Matrix4x4.Identity

module PoseEvaluation =
    let footTarget config time (foot: FootPlan) =
        if foot.Phase = SupportPhase.Support || foot.Phase = SupportPhase.ToeOff then
            foot.Anchor
        else
            let struct (position, _) = Trajectory.evaluate config.TickSeconds time foot.Segment in position

    let private orientFoot (config: AnimationConfig) time (rig: RigDefinition) (world: Matrix4x4[]) leg (foot: FootPlan) =
        let pitch =
            if foot.Phase = SupportPhase.Swing || foot.Phase = SupportPhase.Landing then
                let duration = foot.Segment.Finish - foot.ReleaseAt
                let u = Math.Clamp((MotionTime.value time - foot.ReleaseAt) / max 1e-5f duration, 0.0f, 1.0f)
                let lift = MathF.Sin(MathF.PI * u)
                config.FootSwingPitch * lift * lift
            else
                0.0f

        let rotation = Matrix4x4.CreateRotationX pitch
        ConstraintSolve.orient rig world leg (Vector3.TransformNormal(Vector3.UnitY, rotation)) (Vector3.TransformNormal(Vector3.UnitZ, rotation))

    let private armTarget (config: AnimationConfig) speed (world: Matrix4x4[]) (arm: RigLimb) swing =
        let shoulder = world[arm.Upper].Translation
        let elbow = world[arm.Lower].Translation
        let wrist = world[arm.End].Translation
        let upper = Vector3.Distance(shoulder, elbow)
        let lower = Vector3.Distance(elbow, wrist)
        let blend = Math.Clamp(speed * config.ArmBendSpeed, 0.0f, 1.0f)
        let flex = config.ArmIdleFlex + (config.ArmRunFlex - config.ArmIdleFlex) * blend
        let swingAngle = Math.Clamp(swing * config.ArmSwing, -flex * config.ArmSwingFlexLimit, flex * config.ArmSwingFlexLimit)
        // Elbow flexion brings the forearm forward; centring both segments around vertical leaves running hands behind the hips.
        let upperAngle = min 0.0f (swingAngle - flex * 0.25f)
        let lowerAngle = upperAngle + flex
        shoulder + Vector3(0.0f, -upper * MathF.Cos(upperAngle) - lower * MathF.Cos(lowerAngle), upper * MathF.Sin(upperAngle) + lower * MathF.Sin(lowerAngle))

    let private orientHand (rig: RigDefinition) (world: Matrix4x4[]) (arm: RigLimb) inward =
        let elbow = world[arm.Lower].Translation
        let wrist = world[arm.End].Translation
        let direction = Vector3.Normalize(wrist - elbow)
        let normal = Vector3.Normalize(inward - direction * Vector3.Dot(inward, direction))
        ConstraintSolve.orient rig world arm normal direction

    let private target config time (foot: FootPlan) (overlay: FootOverlay) =
        if overlay.Active && MotionTime.value time <= overlay.Recovery.Finish then
            let struct (position, _) = TaskPlanner.overlayPoint config (MotionTime.value time) overlay
            position
        else
            footTarget config (MotionTime.value time) foot

    let private socket config time (overlay: FootOverlay) (leg: RigLimb) stature =
        let mix = if overlay.Active && MotionTime.value time <= overlay.Recovery.Finish then let struct (value, _) = TaskPlanner.socketState config.TickSeconds (MotionTime.value time) overlay in value else 0.0f
        Vector3.Lerp(leg.Socket, leg.BallSocket, mix) * stature

    let private requiredPelvisDrop (world: Matrix4x4[]) (leg: RigLimb) target =
        let hip = world[leg.Upper].Translation
        let knee = world[leg.Lower].Translation
        let ankle = world[leg.End].Translation
        let targetAnkle = target - Vector3.TransformNormal(leg.Socket, world[leg.End])
        let length = Vector3.Distance(hip, knee) + Vector3.Distance(knee, ankle)
        let horizontal = Vector2(targetAnkle.X - hip.X, targetAnkle.Z - hip.Z).LengthSquared()
        let verticalReach = MathF.Sqrt(max 0.0f (length * length - horizontal))
        max 0.0f (hip.Y - targetAnkle.Y - verticalReach)

    let evaluateInto
        (
            config,
            rig: RigDefinition,
            plan: ActorPlan,
            time: MotionTime,
            rootPosition,
            facing,
            scratch: PoseScratch,
            palette: Matrix4x4[],
            offset
        ) =
        BodyPose.initialize config rig plan scratch.Local

        for node = 0 to rig.Bind.Length - 1 do
            let pose = scratch.Local[node]

            let matrix =
                Matrix4x4.CreateFromQuaternion(pose.Rotation)
                * Matrix4x4.CreateTranslation(pose.Translation)

            scratch.World[node] <-
                if rig.Parents[node] < 0 then
                    matrix
                else
                    matrix * scratch.World[rig.Parents[node]]

        let left =
            MotionInput.localPoint rootPosition facing (target config time plan.Left plan.LeftOverlay)

        let right =
            MotionInput.localPoint rootPosition facing (target config time plan.Right plan.RightOverlay)

        let stature = plan.Root.Morphology.Height / 1.8f

        let leftLeg =
            { rig.LeftLeg with
                Socket = socket config time plan.LeftOverlay rig.LeftLeg stature }

        let rightLeg =
            { rig.RightLeg with
                Socket = socket config time plan.RightOverlay rig.RightLeg stature }
        // Fix socket orientation before solving its point, then restore it after chain rotations.
        orientFoot config time rig scratch.World leftLeg plan.Left
        orientFoot config time rig scratch.World rightLeg plan.Right

        let pelvisDrop =
            min (config.PelvisMaxDrop * stature) (max (requiredPelvisDrop scratch.World leftLeg left) (requiredPelvisDrop scratch.World rightLeg right))

        if pelvisDrop > 0.0f then
            let offset = Matrix4x4.CreateTranslation(0.0f, -pelvisDrop, 0.0f)
            for node = rig.Pelvis to rig.DescendantEnds[rig.Pelvis] do
                scratch.World[node] <- scratch.World[node] * offset

        let leftResidual =
            ConstraintSolve.limb (rig, scratch.World, plan.Root.Actor, MotionEffector.LeftFoot, leftLeg, left)

        let rightResidual =
            ConstraintSolve.limb (rig, scratch.World, plan.Root.Actor, MotionEffector.RightFoot, rightLeg, right)

        orientFoot config time rig scratch.World leftLeg plan.Left
        orientFoot config time rig scratch.World rightLeg plan.Right
        let speed = Vector3(plan.Root.Velocity.X, plan.Root.Velocity.Y, 0.0f).Length()
        let stride = left.Z - right.Z
        ConstraintSolve.limb (
            rig,
            scratch.World,
            plan.Root.Actor,
            MotionEffector.LeftHand,
            rig.LeftArm,
            armTarget config speed scratch.World rig.LeftArm (-stride)
        )
        |> ignore

        ConstraintSolve.limb (
            rig,
            scratch.World,
            plan.Root.Actor,
            MotionEffector.RightHand,
            rig.RightArm,
            armTarget config speed scratch.World rig.RightArm stride
        )
        |> ignore

        orientHand rig scratch.World rig.LeftArm -Vector3.UnitX
        orientHand rig scratch.World rig.RightArm Vector3.UnitX

        ConstraintSolve.correctives rig scratch.World

        for joint = 0 to rig.JointNodes.Length - 1 do
            palette[offset + joint] <-
                rig.InverseBind[joint]
                * Matrix4x4.CreateScale(stature)
                * scratch.World[rig.JointNodes[joint]]

        struct (leftResidual, rightResidual)
