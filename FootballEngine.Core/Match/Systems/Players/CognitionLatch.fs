namespace FootballEngine.Core.Match

/// Hands what cognition decided on the previous tick to the body for this one. Steering and Actions follow those
/// copies, so a decision taken this tick cannot reach the body before the next: cognition can then run beside
/// the body instead of before it.
module CognitionLatch =

    let private latch (team: TeamWorld) =
        let frame = team.Frame
        let source, copy = frame.Intent, frame.SteerIntent
        System.Array.Copy(source.Kind, copy.Kind, source.Kind.Length)
        System.Array.Copy(source.TargetX, copy.TargetX, source.TargetX.Length)
        System.Array.Copy(source.TargetY, copy.TargetY, source.TargetY.Length)
        System.Array.Copy(source.Phase, copy.Phase, source.Phase.Length)
        System.Array.Copy(source.CommittedUntil, copy.CommittedUntil, source.CommittedUntil.Length)
        System.Array.Copy(team.Shape.TargetsX, frame.SteerShapeX, frame.SteerShapeX.Length)
        System.Array.Copy(team.Shape.TargetsY, frame.SteerShapeY, frame.SteerShapeY.Length)

    let step: SystemStep =
        fun _ rng world _ ->
            latch world.Home
            latch world.Away
            rng
