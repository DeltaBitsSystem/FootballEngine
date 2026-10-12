namespace FootballEngine.Client.Animation

open System
open System.Numerics

module BodyPose =
    let initialize (rig: RigDefinition) (plan: ActorPlan) (local: RigLocalPose[]) =
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

        let fatigue = Math.Clamp(1.0f - plan.Root.Condition, 0.0f, 1.0f)
        let head = local[rig.Head]

        local[rig.Head] <-
            { head with
                Rotation =
                    Quaternion.Normalize(
                        head.Rotation
                        * Quaternion.CreateFromYawPitchRoll(plan.Gaze.X - torsoYaw, plan.Gaze.Y + fatigue * 0.10f, 0.0f)
                    ) }
