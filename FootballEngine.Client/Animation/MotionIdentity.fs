namespace FootballEngine.Client.Animation

open System
open System.IO
open System.Security.Cryptography
open FootballEngine.Core.Match

module MotionIdentity =
    let plannerVersion = 1

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
        writer.Write config.Clearance
        writer.Write config.LeanSeconds
        writer.Write config.MaxLean
        writer.Write config.MaxGazeYaw
        writer.Write config.MaxGazePitch
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
            let rigHash = SHA256.HashData(File.ReadAllBytes rigPath) |> Convert.ToHexString

            Ok
                { PlannerVersion = plannerVersion
                  ConfigHash = configHash config
                  RigHash = rigHash
                  ProfileHash =
                    profileHash (Array.append input.Context.HomeLineup.Profiles input.Context.AwayLineup.Profiles)
                  TrackId = MatchJob.inputHash input }
        with :? IOException as error ->
            Error error.Message
