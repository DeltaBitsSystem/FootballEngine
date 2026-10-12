namespace FootballEngine.Client.Animation

open System.Numerics

module MotionInput =
    let vector (v: Vector3) = Vector3(v.X, v.Z, -v.Y)
    let orientation (q: Quaternion) = Quaternion(q.X, q.Z, -q.Y, q.W)
    let facing angle = angle + System.MathF.PI / 2.0f

    let localPoint root facing point =
        Vector3.Transform(point - root, Matrix4x4.CreateRotationY(-facing))
