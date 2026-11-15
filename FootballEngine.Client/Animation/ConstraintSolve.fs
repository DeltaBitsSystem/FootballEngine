namespace FootballEngine.Client.Animation

open System
open System.Numerics
open FootballEngine.Core.Match

module ConstraintSolve =
    let private between (a: Vector3) (b: Vector3) =
        if a.LengthSquared() < 1e-10f || b.LengthSquared() < 1e-10f then
            Quaternion.Identity
        else
            let a = Vector3.Normalize a
            let b = Vector3.Normalize b
            let dot = Math.Clamp(Vector3.Dot(a, b), -1.0f, 1.0f)

            if dot > 0.99999f then
                Quaternion.Identity
            elif dot < -0.99999f then
                let axis = if abs a.X < 0.9f then Vector3.UnitX else Vector3.UnitZ
                Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(a, axis)), MathF.PI)
            else
                Quaternion.Normalize(Quaternion(Vector3.Cross(a, b), 1.0f + dot))

    let private rotate (rig: RigDefinition) (world: Matrix4x4[]) node rotation =
        let pivot = world[node].Translation

        let delta =
            Matrix4x4.CreateTranslation(-pivot)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(pivot)

        for descendant = node to rig.DescendantEnds[node] do
            world[descendant] <- world[descendant] * delta

    let private bendFrame (upper: Vector3) (normal: Vector3) =
        let longitudinal = Vector3.Normalize upper
        let hinge = Vector3.Normalize normal
        let transverse = Vector3.Normalize(Vector3.Cross(hinge, longitudinal))

        Matrix4x4(
            hinge.X,
            hinge.Y,
            hinge.Z,
            0.0f,
            longitudinal.X,
            longitudinal.Y,
            longitudinal.Z,
            0.0f,
            transverse.X,
            transverse.Y,
            transverse.Z,
            0.0f,
            0.0f,
            0.0f,
            0.0f,
            1.0f
        )

    let limb (rig: RigDefinition, world: Matrix4x4[], actor, effector, chain: RigLimb, target) =
        let hip = world[chain.Upper].Translation
        let knee = world[chain.Lower].Translation
        let endPoint = world[chain.End].Translation
        let socket = Vector3.TransformNormal(chain.Socket, world[chain.End])
        let desiredEnd = target - socket
        let delta = desiredEnd - hip
        let upper = Vector3.Distance(hip, knee)
        let lower = Vector3.Distance(knee, endPoint)

        if delta.LengthSquared() < 1e-10f || upper < 1e-5f || lower < 1e-5f then
            ValueSome(JointLimit(actor, chain.Upper, 0.0f))
        else
            let requested = delta.Length()

            let minimum =
                MathF.Sqrt(
                    max
                        1e-8f
                        (upper * upper
                         + lower * lower
                         + 2.0f * upper * lower * MathF.Cos chain.MaximumFlex)
                )

            let maximum =
                MathF.Sqrt(
                    max
                        1e-8f
                        (upper * upper
                         + lower * lower
                         + 2.0f * upper * lower * MathF.Cos chain.MinimumFlex)
                )

            let distance = Math.Clamp(requested, minimum, maximum)
            let direction = delta / requested
            let projected = chain.Pole - direction * Vector3.Dot(chain.Pole, direction)

            let pole =
                if projected.LengthSquared() > 1e-8f then
                    Vector3.Normalize projected
                else
                    Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitX))

            let along =
                (upper * upper + distance * distance - lower * lower) / (2.0f * distance)

            let height = MathF.Sqrt(max 0.0f (upper * upper - along * along))
            let desiredKnee = hip + direction * along + pole * height
            let desiredEnd = hip + direction * distance

            let originalNormal =
                if chain.HingeAxis.LengthSquared() > 0.5f then
                    Vector3.TransformNormal(chain.HingeAxis, world[chain.Upper])
                else
                    Vector3.Cross(knee - hip, endPoint - knee)

            let desiredNormal = Vector3.Cross(desiredKnee - hip, desiredEnd - desiredKnee)

            let minimal = between (knee - hip) (desiredKnee - hip)

            // A nearly straight input limb has no bend plane: its normal shrinks and flips sign through full
            // extension, and trusting it would twist the whole limb half a turn. Without an authored hinge the
            // solve fades to the minimal rotation as the input bend vanishes (sine of the bend below ~0.2).
            let planeTrust =
                if chain.HingeAxis.LengthSquared() > 0.5f then
                    1.0f
                else
                    let bend =
                        originalNormal.Length()
                        / max 1e-8f (Vector3.Distance(hip, knee) * Vector3.Distance(knee, endPoint))

                    let u = Math.Clamp((bend - 0.08f) / 0.12f, 0.0f, 1.0f)
                    u * u * (3.0f - 2.0f * u)

            let upperRotation =
                if
                    planeTrust > 0.0f
                    && originalNormal.LengthSquared() > 1e-12f
                    && desiredNormal.LengthSquared() > 1e-12f
                then
                    // Matching a bone direction alone leaves its hinge twisted relative to the skinned elbow or knee.
                    let originalFrame = bendFrame (knee - hip) originalNormal
                    let desiredFrame = bendFrame (desiredKnee - hip) desiredNormal

                    let planar =
                        Quaternion.Normalize(
                            Quaternion.CreateFromRotationMatrix(Matrix4x4.Transpose(originalFrame) * desiredFrame)
                        )

                    Quaternion.Slerp(minimal, planar, planeTrust)
                else
                    minimal

            rotate rig world chain.Upper upperRotation
            let solvedKnee = world[chain.Lower].Translation
            let solvedEnd = world[chain.End].Translation
            rotate rig world chain.Lower (between (solvedEnd - solvedKnee) (hip + direction * distance - solvedKnee))

            if abs (requested - distance) > 0.001f then
                ValueSome(UnreachableContact(actor, effector, abs (requested - distance)))
            else
                ValueNone

    let orient (rig: RigDefinition) (world: Matrix4x4[]) (chain: RigLimb) normal forward =
        let node = chain.End
        let up = Vector3.TransformNormal(chain.SocketNormal, world[node])
        rotate rig world node (between up normal)
        let current = Vector3.TransformNormal(chain.SocketForward, world[node])
        let projected = current - normal * Vector3.Dot(current, normal)

        if projected.LengthSquared() > 1e-8f then
            rotate rig world node (between projected forward)

    let correctives (rig: RigDefinition) (world: Matrix4x4[]) =
        for rule in rig.Correctives do
            let parent = rig.Parents[rule.Source]
            let mutable parentRotation = world[parent]
            parentRotation.Translation <- Vector3.Zero
            let mutable sourceRotation = world[rule.Source]
            sourceRotation.Translation <- Vector3.Zero
            let mutable supportRotation = world[rule.Support]
            supportRotation.Translation <- Vector3.Zero

            let basis =
                sourceRotation * Matrix4x4.Transpose(parentRotation) * rule.SourceBindInverse

            let delta = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix basis)
            let half = Quaternion.Slerp(Quaternion.Identity, delta, rule.Weight)

            let desired =
                Matrix4x4.CreateFromQuaternion(half) * rule.SupportBindRotation * parentRotation

            let relative = Matrix4x4.Transpose(supportRotation) * desired
            let pivot = world[rule.Support].Translation

            let correction =
                Matrix4x4.CreateTranslation(-pivot)
                * relative
                * Matrix4x4.CreateTranslation(pivot)

            for descendant = rule.Support to rig.DescendantEnds[rule.Support] do
                world[descendant] <- world[descendant] * correction
