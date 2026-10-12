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
            rotate rig world chain.Upper (between (knee - hip) (desiredKnee - hip))
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
