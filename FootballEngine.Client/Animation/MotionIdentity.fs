namespace FootballEngine.Client.Animation

open System
open System.IO
open System.Security.Cryptography
open FootballEngine.Core.Match

module MotionIdentity =
    let plannerVersion = 4

    let rigHash (rigPath: string) =
        let manifestPath = Path.ChangeExtension(rigPath, null) + "-motion.json"
        use hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
        hash.AppendData(File.ReadAllBytes rigPath)
        hash.AppendData(File.ReadAllBytes manifestPath)
        hash.GetHashAndReset() |> Convert.ToHexString

    let configHash (config: AnimationConfig) =
        use stream = new MemoryStream()
        use writer = new BinaryWriter(stream)
        writer.Write config.Version
        writer.Write config.TickSeconds
        writer.Write config.WindowTicks
        writer.Write config.RecoveryWindowTicks
        writer.Write config.MaxActors
        writer.Write config.TasksPerActor
        writer.Write config.StepWidth
        writer.Write config.StepReach
        writer.Write config.SwingSeconds
        writer.Write config.MinSwingSeconds
        writer.Write config.SwingSpeedFactor
        writer.Write config.StepTrigger
        writer.Write config.StepSpeedTrigger
        writer.Write config.StepLeadSeconds
        writer.Write config.RunFlightSpeed
        writer.Write config.RunFlightOverlapSeconds
        writer.Write config.MaxLegLeadFraction
        writer.Write config.RetargetDistance
        writer.Write config.ArmSwing
        writer.Write config.ArmBendSpeed
        writer.Write config.ArmIdleFlex
        writer.Write config.ArmRunFlex
        writer.Write config.ArmSwingFlexLimit
        writer.Write config.FootSwingPitch
        writer.Write config.PivotSensitivity
        writer.Write config.PivotTriggerMinimumRatio
        writer.Write config.PelvisMaxDrop
        writer.Write config.Clearance
        writer.Write config.LeanSeconds
        writer.Write config.MaxLean
        writer.Write config.MaxGazeYaw
        writer.Write config.MaxGazePitch
        writer.Write config.GazeNearDistance
        writer.Write config.NeckShare
        writer.Write config.MaxNeckYaw
        writer.Write config.MaxNeckPitch
        writer.Write config.GazeRate
        writer.Flush()
        SHA256.HashData(stream.ToArray()) |> Convert.ToHexString

    let profileHash (profiles: PlayerProfile[]) =
        use stream = new MemoryStream()
        use writer = new BinaryWriter(stream)
        writer.Write profiles.Length

        for profile in profiles do
            let (PlayerId actor) = profile.Id
            let morphology = profile.Morphology
            writer.Write actor
            writer.Write morphology.Height
            writer.Write morphology.Mass
            writer.Write morphology.ThighLength
            writer.Write morphology.ShinLength
            writer.Write morphology.FootLength
            writer.Write morphology.ShoulderWidth

            writer.Write(
                if morphology.PreferredFoot = ContactFoot.Left then
                    0uy
                else
                    1uy
            )

            writer.Write profile.Agility
            writer.Write profile.Flair
            writer.Write profile.Personality.Composure
            writer.Write profile.Personality.Aggression
            writer.Write profile.Personality.RiskTaking
            writer.Write profile.Personality.Flair
            writer.Write profile.Personality.Consistency

        writer.Flush()
        SHA256.HashData(stream.ToArray()) |> Convert.ToHexString

    let forMatch config rigPath (input: MatchInput) =
        try
            let rigHash = rigHash rigPath

            Ok
                { PlannerVersion = plannerVersion
                  ConfigHash = configHash config
                  RigHash = rigHash
                  ProfileHash =
                    profileHash (Array.append input.Context.HomeLineup.Profiles input.Context.AwayLineup.Profiles)
                  TrackId = MatchJob.inputHash input }
        with :? IOException as error ->
            Error error.Message
