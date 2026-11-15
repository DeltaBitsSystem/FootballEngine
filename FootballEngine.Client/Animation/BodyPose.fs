namespace FootballEngine.Client.Animation

open System
open System.Numerics

module BodyPose =
    /// Gaze is layered on whatever body pose is underneath: the neck takes its share, the head the remainder.
    let applyGaze
        (config: AnimationConfig)
        (rig: RigDefinition)
        (plan: ActorPlan)
        (torsoYaw: float32)
        (leanPitch: float32)
        (local: RigLocalPose[])
        =
        let yaw = Math.Clamp(plan.Gaze.X - torsoYaw, -config.MaxGazeYaw, config.MaxGazeYaw)

        let pitch =
            Math.Clamp(plan.Gaze.Y - leanPitch, -config.MaxGazePitch, config.MaxGazePitch)

        let neckYaw =
            Math.Clamp(yaw * config.NeckShare, -config.MaxNeckYaw, config.MaxNeckYaw)

        let neckPitch =
            Math.Clamp(pitch * config.NeckShare, -config.MaxNeckPitch, config.MaxNeckPitch)

        let neck = local[rig.Neck]

        local[rig.Neck] <-
            { neck with
                Rotation =
                    Quaternion.Normalize(neck.Rotation * Quaternion.CreateFromYawPitchRoll(neckYaw, neckPitch, 0.0f)) }

        let head = local[rig.Head]

        local[rig.Head] <-
            { head with
                Rotation =
                    Quaternion.Normalize(
                        head.Rotation
                        * Quaternion.CreateFromYawPitchRoll(yaw - neckYaw, pitch - neckPitch, 0.0f)
                    ) }

    let initialize (config: AnimationConfig) (rig: RigDefinition) (plan: ActorPlan) (local: RigLocalPose[]) =
        Array.Copy(rig.Bind, local, rig.Bind.Length)
        let stature = plan.Root.Morphology.Height / 1.8f

        for node = 0 to local.Length - 1 do
            local[node] <-
                { local[node] with
                    Translation = local[node].Translation * stature }

        let rootYaw = MotionInput.facing plan.Root.Facing
        let lean = Vector3.TransformNormal(plan.Lean, Matrix4x4.CreateRotationY(-rootYaw))
        let torso = local[rig.Torso]
        let torsoYaw = Math.Clamp(plan.Gaze.X * 0.25f, -0.25f, 0.25f)

        local[rig.Torso] <-
            { torso with
                Rotation =
                    Quaternion.Normalize(torso.Rotation * Quaternion.CreateFromYawPitchRoll(torsoYaw, lean.Z, -lean.X)) }
        // Bounded local pelvic correction shares weight without changing the authoritative root.
        let pelvis = local[rig.Pelvis]
        let flex = 0.035f + min 0.06f (plan.Acceleration.Length() * 0.001f)

        local[rig.Pelvis] <-
            { pelvis with
                Translation = pelvis.Translation - Vector3.UnitY * flex }

        applyGaze config rig plan torsoYaw lean.Z local
