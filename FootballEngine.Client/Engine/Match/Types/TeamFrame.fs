namespace FootballEngine.Types

open FootballEngine

type TeamFrame() =
    member val Physics: PhysicsFrame = Unchecked.defaultof<_> with get, set
    member val Intent: IntentDataFrame = Unchecked.defaultof<_> with get, set
    member val Cognitive: CognitiveOutputFrame = Unchecked.defaultof<_> with get, set
    member val Cond: ConditionFrame = Unchecked.defaultof<_> with get, set
    member val Mental: MentalFrame = Unchecked.defaultof<_> with get, set

    member this.SlotCount = this.Physics.SlotCount

    member val Condition: float32[] = Array.empty with get, set
    member val SupportPositionX: float32[] = Array.empty with get, set
    member val SupportPositionY: float32[] = Array.empty with get, set
    member val DefensiveRole: byte[] = Array.empty with get, set
    member val SlotRoles: SlotRole[] = Array.empty with get, set
    member val CollectiveIntents: CollectiveIntent[] = Array.empty with get, set
    member val CachedTargetX: float32[] = Array.empty with get, set
    member val CachedTargetY: float32[] = Array.empty with get, set
    member val CachedExecution: float32[] = Array.empty with get, set
    member val ComposureLevel: float32[] = Array.empty with get, set
    member val ConfidenceLevel: float32[] = Array.empty with get, set
    member val AggressionLevel: float32[] = Array.empty with get, set
    member val FocusLevel: float32[] = Array.empty with get, set
    member val RiskTolerance: float32[] = Array.empty with get, set

module TeamFrame =
    let init (roster: PlayerRoster) (basePositions: Spatial[]) : TeamFrame =
        let n = roster.SlotCount
        let frame = TeamFrame()
        frame.Physics <- PhysicsFrame.init n basePositions
        frame.Intent <- IntentFrameOps.init n
        frame.Cognitive <- CognitiveOutputFrame.init n
        frame.Cond <- ConditionFrame.init n roster basePositions
        frame.Mental <- MentalFrame.init n roster
        frame.Condition <- Array.init n (fun i -> float32 roster.Players[i].Condition)
        frame.SupportPositionX <- Array.zeroCreate n
        frame.SupportPositionY <- Array.zeroCreate n
        frame.DefensiveRole <- Array.create n (byte DefensiveRole.Marker)
        frame.SlotRoles <- Array.create n FreeRole
        frame.CollectiveIntents <- Array.create n CollectiveIntent.neutral
        frame.CachedTargetX <- Array.init n (fun i -> float32 basePositions[i].X)
        frame.CachedTargetY <- Array.init n (fun i -> float32 basePositions[i].Y)
        frame.CachedExecution <- Array.create n 1.0f
        frame.ComposureLevel <- Array.init n (fun i -> float32 (MentalState.initial roster.Players[i]).ComposureLevel)
        frame.ConfidenceLevel <- Array.init n (fun i -> float32 (MentalState.initial roster.Players[i]).ConfidenceLevel)
        frame.AggressionLevel <- Array.init n (fun i -> float32 (MentalState.initial roster.Players[i]).AggressionLevel)
        frame.FocusLevel <- Array.init n (fun i -> float32 (MentalState.initial roster.Players[i]).FocusLevel)
        frame.RiskTolerance <- Array.init n (fun i -> float32 (MentalState.initial roster.Players[i]).RiskTolerance)
        frame
