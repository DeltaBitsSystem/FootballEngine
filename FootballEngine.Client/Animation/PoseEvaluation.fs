namespace FootballEngine.Client.Animation

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
        BodyPose.initialize rig plan scratch.Local

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
            MotionInput.localPoint rootPosition facing (footTarget config (MotionTime.value time) plan.Left)

        let right =
            MotionInput.localPoint rootPosition facing (footTarget config (MotionTime.value time) plan.Right)

        let stature = plan.Root.Morphology.Height / 1.8f

        let leftLeg =
            { rig.LeftLeg with
                Socket = rig.LeftLeg.Socket * stature }

        let rightLeg =
            { rig.RightLeg with
                Socket = rig.RightLeg.Socket * stature }
        // Fix socket orientation before solving its point, then restore it after chain rotations.
        ConstraintSolve.orient rig scratch.World leftLeg Vector3.UnitY Vector3.UnitZ
        ConstraintSolve.orient rig scratch.World rightLeg Vector3.UnitY Vector3.UnitZ

        let leftResidual =
            ConstraintSolve.limb (rig, scratch.World, plan.Root.Actor, MotionEffector.LeftFoot, leftLeg, left)

        let rightResidual =
            ConstraintSolve.limb (rig, scratch.World, plan.Root.Actor, MotionEffector.RightFoot, rightLeg, right)

        ConstraintSolve.orient rig scratch.World leftLeg Vector3.UnitY Vector3.UnitZ
        ConstraintSolve.orient rig scratch.World rightLeg Vector3.UnitY Vector3.UnitZ
        let speed = Vector3(plan.Root.Velocity.X, plan.Root.Velocity.Y, 0.0f).Length()
        let stride = left.Z - right.Z
        let armHeight = (0.91f + min 0.27f (speed * 0.045f)) * stature

        ConstraintSolve.limb (
            rig,
            scratch.World,
            plan.Root.Actor,
            MotionEffector.LeftHand,
            rig.LeftArm,
            Vector3(0.25f * stature, armHeight, -stride * 0.3f)
        )
        |> ignore

        ConstraintSolve.limb (
            rig,
            scratch.World,
            plan.Root.Actor,
            MotionEffector.RightHand,
            rig.RightArm,
            Vector3(-0.25f * stature, armHeight, stride * 0.3f)
        )
        |> ignore

        for joint = 0 to rig.JointNodes.Length - 1 do
            palette[offset + joint] <-
                rig.InverseBind[joint]
                * Matrix4x4.CreateScale(stature)
                * scratch.World[rig.JointNodes[joint]]

        struct (leftResidual, rightResidual)
