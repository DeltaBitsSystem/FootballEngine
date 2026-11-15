namespace FootballEngine.Client.Animation

open System
open System.Numerics
open FootballEngine.Core.Match

module MotionValidation =
    let private vector = MotionContract.vector

    let private segment (curve: MotionSegment) =
        Single.IsFinite curve.Start
        && Single.IsFinite curve.Finish
        && curve.Finish > curve.Start
        && vector curve.From
        && vector curve.Target
        && vector curve.FromVelocity
        && vector curve.TargetVelocity
        && Single.IsFinite curve.Clearance
        && curve.Clearance >= 0.0f
        && curve.Clearance <= 1.0f

    let foot (plan: FootPlan) =
        int plan.Phase >= 0
        && int plan.Phase <= 3
        && vector plan.Anchor
        && Single.IsFinite plan.ReleaseAt
        && segment plan.Segment

    let private overlay (value: FootOverlay) =
        segment value.Preparation
        && segment value.Recovery
        && Single.IsFinite value.PreparationSocketStart
        && value.PreparationSocketStart >= 0.0f
        && value.PreparationSocketStart <= 1.0f
        && Single.IsFinite value.RecoverySocketStart
        && value.RecoverySocketStart >= 0.0f
        && value.RecoverySocketStart <= 1.0f
        && Single.IsFinite value.PreparationSocketVelocity
        && Single.IsFinite value.RecoverySocketVelocity
        && (not value.Active || value.TaskId > 0UL)

    let plan (value: ActorPlan) =
        let (PlayerId identity) = value.Root.Actor

        let seed =
            uint64 (uint32 identity) * 0x9E3779B97F4A7C15UL + uint64 value.Root.Generation

        MotionContract.root value.Root
        && foot value.Left
        && foot value.Right
        && overlay value.LeftOverlay
        && overlay value.RightOverlay
        && vector value.Acceleration
        && vector value.Lean
        && Single.IsFinite value.Gaze.X
        && Single.IsFinite value.Gaze.Y
        && value.Tick >= 0
        && value.Seed = seed
