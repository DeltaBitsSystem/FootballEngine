namespace FootballEngine.Client.Animation

open System
open System.Numerics

[<Struct>]
type RigLocalPose =
    { Translation: Vector3
      Rotation: Quaternion
      Scale: Vector3 }

[<Struct>]
type RigLimb =
    { Upper: int
      Lower: int
      End: int
      Socket: Vector3
      BallSocket: Vector3
      SocketNormal: Vector3
      SocketForward: Vector3
      Pole: Vector3
      HingeAxis: Vector3
      MinimumFlex: float32
      MaximumFlex: float32 }

[<Struct>]
type RigCorrective =
    { Support: int
      Source: int
      Weight: float32
      SourceBindInverse: Matrix4x4
      SupportBindRotation: Matrix4x4 }

type RigDefinition =
    { Hash: string
      /// Node names in evaluation order; motion data names the joints it drives.
      Names: string[]
      Parents: int[]
      DescendantEnds: int[]
      Bind: RigLocalPose[]
      JointNodes: int[]
      InverseBind: Matrix4x4[]
      Correctives: RigCorrective[]
      LeftLeg: RigLimb
      RightLeg: RigLimb
      LeftArm: RigLimb
      RightArm: RigLimb
      Pelvis: int
      Torso: int
      Neck: int
      Head: int }

module RigDefinition =
    let conform (rig: RigDefinition) (proxy: FootballEngine.Core.Match.PhysicalMorphology) =
        let scale = proxy.Height / 1.8f
        let upper = rig.Bind[rig.LeftLeg.Lower].Translation.Length() * scale
        let lower = rig.Bind[rig.LeftLeg.End].Translation.Length() * scale

        if
            not (Single.IsFinite proxy.Height && Single.IsFinite proxy.Mass)
            || proxy.Height < 1.45f
            || proxy.Height > 2.20f
            || proxy.Mass <= 0.0f
        then
            Error "physical morphology is outside the rig's supported stature range"
        elif
            abs (upper - proxy.ThighLength) > 0.005f
            || abs (lower - proxy.ShinLength) > 0.005f
        then
            Error "rig limb proportions do not conform to the physical proxy"
        else
            Ok()

    let validate (rig: RigDefinition) =
        let mutable valid =
            rig.Parents.Length = rig.Bind.Length
            && rig.Names.Length = rig.Bind.Length
            && rig.DescendantEnds.Length = rig.Bind.Length
            && rig.JointNodes.Length = rig.InverseBind.Length

        for node = 0 to rig.Bind.Length - 1 do
            let pose = rig.Bind[node]
            valid <- valid && rig.Parents[node] < node && rig.Parents[node] >= -1

            valid <-
                valid
                && Single.IsFinite pose.Translation.X
                && Single.IsFinite pose.Translation.Y
                && Single.IsFinite pose.Translation.Z

            valid <-
                valid
                && abs (pose.Rotation.LengthSquared() - 1.0f) < 0.001f
                && Vector3.Distance(pose.Scale, Vector3.One) < 0.001f

        for limb in [| rig.LeftLeg; rig.RightLeg; rig.LeftArm; rig.RightArm |] do
            valid <-
                valid
                && limb.Upper >= 0
                && limb.Lower > limb.Upper
                && limb.End > limb.Lower
                && limb.End < rig.Bind.Length

            valid <-
                valid
                && limb.MinimumFlex >= 0.0f
                && limb.MaximumFlex < MathF.PI
                && limb.MaximumFlex > limb.MinimumFlex

        for rule in rig.Correctives do
            valid <-
                valid
                && rule.Source >= 0
                && rule.Source < rig.Bind.Length
                && rule.Support >= 0
                && rule.Support < rig.Bind.Length
                && rig.Parents[rule.Source] >= 0
                && rig.Parents[rule.Source] = rig.Parents[rule.Support]
                && rule.Weight = 0.5f

        if valid then
            Ok()
        else
            Error "rig topology, bind transform or anatomical limits are invalid"

    let descendantEnds (parents: int[]) =
        Array.init parents.Length (fun root ->
            let mutable last = root

            for node = root + 1 to parents.Length - 1 do
                let mutable ancestor = node

                while ancestor > root do
                    ancestor <- parents[ancestor]

                if ancestor = root then
                    last <- node

            last)
