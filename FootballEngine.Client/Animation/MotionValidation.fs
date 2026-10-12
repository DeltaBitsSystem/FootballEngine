namespace FootballEngine.Client.Animation

open System
open System.Numerics
open FootballEngine.Core.Match

module MotionValidation =
    let vector (v: Vector3) =
        Single.IsFinite v.X
        && Single.IsFinite v.Y
        && Single.IsFinite v.Z
        && v.LengthSquared() < 1e12f

    let private optional =
        function
        | ValueNone -> true
        | ValueSome v -> vector v

    let root (sample: MotionRootSample) =
        sample.Generation >= 0
        && vector sample.Position
        && vector sample.Velocity
        && optional sample.AttentionTarget
        && Single.IsFinite sample.Facing
        && Single.IsFinite sample.AngularVelocity
        && Single.IsFinite sample.Condition
        && sample.Condition >= 0.0f
        && sample.Condition <= 1.0f
        && Single.IsFinite sample.Morphology.Height
        && sample.Morphology.Height > 0.0f
        && Single.IsFinite sample.Morphology.Mass
        && sample.Morphology.Mass > 0.0f
        && Single.IsFinite sample.Morphology.ThighLength
        && sample.Morphology.ThighLength > 0.0f
        && Single.IsFinite sample.Morphology.ShinLength
        && sample.Morphology.ShinLength > 0.0f
        && Single.IsFinite sample.Morphology.FootLength
        && sample.Morphology.FootLength > 0.0f
        && Single.IsFinite sample.Morphology.ShoulderWidth
        && sample.Morphology.ShoulderWidth > 0.0f

    let foot (plan: FootPlan) =
        let curve = plan.Segment

        int plan.Phase >= 0
        && int plan.Phase <= 3
        && vector plan.Anchor
        && Single.IsFinite plan.ReleaseAt
        && Single.IsFinite curve.Start
        && Single.IsFinite curve.Finish
        && curve.Finish > curve.Start
        && vector curve.From
        && vector curve.Target
        && vector curve.FromVelocity
        && vector curve.TargetVelocity
        && Single.IsFinite curve.Clearance
        && curve.Clearance >= 0.0f
        && curve.Clearance <= 1.0f

    let plan (value: ActorPlan) =
        root value.Root
        && foot value.Left
        && foot value.Right
        && vector value.Acceleration
        && vector value.Lean
        && Single.IsFinite value.Gaze.X
        && Single.IsFinite value.Gaze.Y
        && value.Tick >= 0

    let evidence (value: MotionEvidence) =
        value.Sequence > 0UL
        && value.Sequence < UInt64.MaxValue
        && value.TaskId > 0UL
        && value.Generation >= 0
        && int value.Family >= 0
        && int value.Family <= 8
        && int value.Effector >= 0
        && int value.Effector <= 6
        && int value.Phase >= 0
        && int value.Phase <= 2
        && value.OccurredAt >= 0<subtick>
        && value.OccurredAt <= value.AvailableAt
        && value.StartedAt >= 0<subtick>
        && value.StartedAt <= value.CommitAt
        && value.CommitAt <= value.EndsAt
        && vector value.PredictedPoint
        && vector value.BallCenter
        && optional value.ConfirmedPoint
        && optional value.IncomingVelocity
        && optional value.OutgoingVelocity
