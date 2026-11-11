namespace FootballEngine.Client.Match3D

open System
open System.Numerics
open FootballEngine.Core.Match
open FootballEngine.Client.Animation
open FootballEngine.Client.Views.Components

/// CPU pose evaluation owns its scratch and output; the GPU never selects or advances motion. With a matcher the
/// captured database drives the body and the planner only supplies roots, ball tasks and gaze.
type MotionPaletteEvaluator(rig: RigDefinition, config: AnimationConfig, matcher: MotionMatcher option) =
    let scratch = PoseScratch(rig)
    let palettes = Array.create (22 * rig.JointNodes.Length) Matrix4x4.Identity
    let mutable residuals = 0UL

    let emptyPlan =
        SupportPlanner.initialize
            config
            0
            { Actor = PlayerId 0
              Generation = 0
              Position = Vector3.Zero
              Velocity = Vector3.Zero
              Facing = 0.0f
              AngularVelocity = 0.0f
              Condition = 1.0f
              GaitPhase = 0.0f
              AttentionTarget = ValueNone
              Morphology = PhysicalMorphology.standard
              Active = false }

    member _.Palettes = palettes
    member _.RigHash = rig.Hash
    member _.ResidualCount = residuals

    member _.Evaluate(source: IMotionPlanSource, frame: RenderFrame, time: MotionTime) =
        let mutable ready = true

        for player in frame.Players do
            if not player.IsSidelined then
                let mutable plan = emptyPlan

                if source.TryCopyPlan(PlayerId player.Id, time, &plan) then
                    let slot = (if player.IsHome then 0 else 11) + player.VisualSlot

                    let root =
                        Vector3(float32 player.Position.X, float32 player.Height, -float32 player.Position.Y)

                    let facing = MotionInput.facing (float32 player.BodyFacing)
                    let offset = slot * rig.JointNodes.Length

                    let struct (left, right) =
                        match matcher with
                        | Some matcher ->
                            match matcher.Evaluate(source, plan, time, root, facing, scratch, palettes, offset) with
                            | ValueSome pose -> struct (pose.LeftResidual, pose.RightResidual)
                            | ValueNone ->
                                ready <- false
                                struct (ValueNone, ValueNone)
                        | None -> PoseEvaluation.evaluateInto (config, rig, plan, time, root, facing, scratch, palettes, offset)

                    if left.IsSome then
                        residuals <- residuals + 1UL

                    if right.IsSome then
                        residuals <- residuals + 1UL
                else
                    ready <- false

        ready
