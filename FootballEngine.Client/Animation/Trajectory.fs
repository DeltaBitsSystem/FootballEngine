namespace FootballEngine.Client.Animation

open System
open System.Numerics

module Trajectory =
    /// Derivatives are metres per simulation second, not per presentation frame.
    let evaluate tickSeconds time (segment: MotionSegment) =
        let durationTicks = max 1e-5f (segment.Finish - segment.Start)
        let duration = durationTicks * tickSeconds
        let u = Math.Clamp((time - segment.Start) / durationTicks, 0.0f, 1.0f)
        let u2 = u * u
        let u3 = u * u * u

        let position =
            segment.From * (2.0f * u3 - 3.0f * u2 + 1.0f)
            + segment.FromVelocity * ((u3 - 2.0f * u2 + u) * duration)
            + segment.Target * (-2.0f * u3 + 3.0f * u2)
            + segment.TargetVelocity * ((u3 - u2) * duration)
        // Quartic clearance has zero boundary velocity, preserving C1 at support transitions.
        let lift = 16.0f * u2 * (1.0f - u) * (1.0f - u) * segment.Clearance

        let liftVelocity =
            32.0f * u * (1.0f - u) * (1.0f - 2.0f * u) * segment.Clearance / duration

        let velocity =
            (segment.From * (6.0f * u2 - 6.0f * u)
             + segment.FromVelocity * ((3.0f * u2 - 4.0f * u + 1.0f) * duration)
             + segment.Target * (-6.0f * u2 + 6.0f * u)
             + segment.TargetVelocity * ((3.0f * u2 - 2.0f * u) * duration))
            / duration

        struct (position + Vector3.UnitY * lift, velocity + Vector3.UnitY * liftVelocity)

    let retarget tickSeconds time target finish segment =
        let struct (position, velocity) = evaluate tickSeconds time segment

        { segment with
            Start = time
            Finish = max (time + 1.0f) finish
            From = position
            FromVelocity = velocity
            Target = target }
