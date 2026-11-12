namespace FootballEngine.Core.Match

/// Position buffers stay as inline values so geometry systems reuse arrays
/// without allocating one object per point.
[<Struct>]
type PositionPoint = { X: float; Y: float }

[<Struct>]
type SlotRole =
    | Goalkeeper
    | Defender
    | FullBack
    | DefensiveMid
    | CenterMid
    | AttackingMid
    | Winger
    | Striker

[<Struct>]
type DefensiveRole =
    | Unassigned
    | FirstDefender
    | Cover
    | Marker
    | Screen
    | ProtectDepth
    | RecoverShape

[<Struct>]
type CollectiveIntent =
    { Pressing: float32
      Holding: float32
      Attacking: float32
      Balanced: float32 }

module IntentCode =
    [<Literal>]
    let None = 0uy

    [<Literal>]
    let BallCarry = 1uy

    [<Literal>]
    let Support = 2uy

    [<Literal>]
    let Press = 3uy

    [<Literal>]
    let Mark = 4uy

    [<Literal>]
    let Recover = 5uy

    [<Literal>]
    let Cover = 6uy

    [<Literal>]
    let HoldPosition = 7uy

    [<Literal>]
    let ExecuteRun = 8uy

    [<Literal>]
    let SetPieceRun = 9uy

    // Regaining shape must not claim the loose-ball intent bonuses attached to Recover.
    [<Literal>]
    let Regroup = 10uy

    let inline isContinuous kind =
        kind = BallCarry
        || kind = Support
        || kind = Press
        || kind = Mark
        || kind = Recover
        || kind = Cover
        || kind = HoldPosition
        || kind = SetPieceRun
        || kind = Regroup

module IntentPhase =
    [<Literal>]
    let None = 0uy

    [<Literal>]
    let Committed = 1uy

    [<Literal>]
    let Executing = 2uy

    [<Literal>]
    let Completed = 3uy

module ExitTrigger =
    [<Literal>]
    let None = 0uy

    [<Literal>]
    let ReachedTarget = 1uy

    [<Literal>]
    let BallNearby = 2uy

    [<Literal>]
    let Expired = 3uy

    [<Literal>]
    let ShapeChanged = 4uy

[<Struct>]
type MentalFrame =
    { mutable Composure: float32[]
      mutable Confidence: float32[]
      mutable Aggression: float32[]
      mutable Focus: float32[]
      mutable Risk: float32[] }

[<Struct>]
type IntentFrame =
    { mutable Kind: byte[]
      mutable TargetX: float32[]
      mutable TargetY: float32[]
      mutable Phase: byte[]
      mutable CommittedUntil: int<subtick>[]
      mutable CommittedAt: int<subtick>[]
      mutable ExitTrigger: byte[] }

[<Struct>]
type PlayerOutReason =
    | Injury
    | RedCard
    | Substituted

[<Struct>]
type PlayerOut =
    { Id: PlayerId
      Reason: PlayerOutReason }

type SubstitutionPhase =
    | NoSubstitution
    | Requested
    | Accepted
    | Pending
    | Applied
    | Rejected

type SubstitutionState =
    { Phase: SubstitutionPhase
      Outgoing: PlayerId option
      Incoming: PlayerId option
      RequestedAt: int<subtick> option
      AppliedAt: int<subtick> option
      UsedIncoming: Set<PlayerId> }

[<Struct>]
type OccupancyKind =
    | Active of slot: int
    | Sidelined of PlayerOut

[<Struct>]
type RunAssignment =
    { Slot: int
      TargetX: float32
      TargetY: float32
      Phase: int
      ExpiresAt: int<subtick> }

/// SoA — ONE source of truth per property, every array length SlotCount: no two
/// copies of a value can desync. The factory below allocates everything — no
/// defaultof, no uninitialized slots.
/// Active-slots cache: activeSlots is queried every tick by every system, and
/// rebuilding the array on each call allocates. Occupancy only changes on
/// substitutions/injuries, so a revision counter invalidates the cache exactly
/// when reality changes. A class box (not a struct field) so every copy of the
/// TeamFrame shares the same cache. `Revision` is bumped on every Occupancy
/// write; `ComputedFor` records the revision the cached array was built at.
type ActiveSlotsCache() =
    member val Slots: int[] = [||] with get, set
    member val Revision: int = 1 with get, set
    member val ComputedFor: int = 0 with get, set

[<Struct>]
type TeamFrame =
    { mutable PosX: float32[]
      mutable PosY: float32[]
      mutable VelX: float32[]
      mutable VelY: float32[]
      mutable PosZ: float32[]
      mutable VelZ: float32[]
      mutable TargetVelX: float32[]
      mutable TargetVelY: float32[]
      mutable TargetFacing: float32[]
      mutable JumpRequested: bool[]
      mutable JumpSequence: uint64[]
      mutable BodyFacing: float32[]
      mutable BodyAngularVelocity: float32[]
      mutable BodyAngularAcceleration: float32[]
      mutable BodyTravelDistance: float32[]
      /// Stride cycle in [0, 1): the first half has the left foot in support, the second the right.
      mutable GaitPhase: float32[]
      mutable ContactGeneration: int[]
      mutable ContactState: BodyContactState[]
      mutable ContactActions: ContactAction option[]
      mutable Condition: float32[]
      mutable Mental: MentalFrame
      mutable Intent: IntentFrame
      /// The intentions and shape as cognition left them on the previous tick: what the body steers by. The
      /// reflex works on the current tick, the decision it follows was taken one tick before.
      mutable SteerIntent: IntentFrame
      mutable SteerShapeX: float32[]
      mutable SteerShapeY: float32[]
      mutable Occupancy: OccupancyKind[]
      mutable SlotRoles: SlotRole[]
      mutable DefensiveRoles: DefensiveRole[]
      mutable CollectiveIntents: CollectiveIntent[]
      mutable SupportX: float32[]
      mutable SupportY: float32[]
      mutable VisibilityTm: bool[]
      mutable VisibilityOpp: bool[]
      // R3 — cognitive perception, built once per team/tick by PlayerPerception.
      mutable OppsNear: int[] // opponents within 7 m of the slot
      mutable PressCount: int[] // opponents pressing the slot
      // R3 — channel geometry. laneClear[i] = no blocker on carrier→i segment;
      // faceCone[i] = teammate i faces the carrier (a receiver that can't see
      // the ball is not a target). Only meaningful while a carrier exists.
      mutable LaneClear: bool[]
      mutable FaceCone: bool[]
      // R5 — cognitive vitality, sigmoid(condition), refreshed in batch.
      mutable Cognitive: float32[]
      // R7 — collective role bias, reassigned only on directive change.
      mutable RoleBias: float32[]
      mutable RunLookup: System.Collections.Generic.Dictionary<int, RunAssignment>
      mutable ActiveCount: int
      SlotCount: int
      mutable ActiveCache: ActiveSlotsCache }

[<Struct>]
type PlayerMemory =
    { mutable LastTouchTick: int<subtick>
      mutable SuccessfulDuels: int
      mutable FailedDuels: int
      mutable PassesMade: int
      mutable PassesCompleted: int
      mutable ShotsTaken: int
      mutable Yellows: int
      mutable CriticalFlagged: bool
      // R6 — exponentially-decaying floats read by PlayerDecision as:
      // passFailureMod / streakMod / duelHistoryMod. Updated by EventApply/Duel,
      // decayed in batch every MemoryDecayIntervalSubTicks.
      mutable PassFailMod: float
      mutable PassSuccessMod: float
      mutable DuelFailMod: float
      mutable DuelSuccessMod: float
      mutable ShotSuccessMod: float
      mutable StreakMod: float
      mutable LastMemoryDecayTick: int<subtick> }

[<Struct>]
type CardsBook =
    { mutable Yellows: int
      mutable Reds: int }

[<Struct>]
type ShapeTargets =
    { mutable TargetsX: float32[]
      mutable TargetsY: float32[] }

type FormationSample =
    { BallX: float<meter>
      BallY: float<meter>
      SlotX: float32[]
      SlotY: float32[] }

type FormationModel =
    { Samples: FormationSample[]
      SlotCount: int }

[<Struct>]
type DirectiveKind =
    | Balanced
    | Attack
    | Defend
    | Press
    | Counter
    | ParkTheBus

[<Struct>]
type DirectiveParams =
    { mutable LineHeight: float32
      mutable PressIntensity: float32
      mutable Width: float32 }

[<Struct>]
type TeamDirective =
    { mutable Kind: DirectiveKind
      mutable SinceTick: int<subtick>
      mutable Params: DirectiveParams }

[<Struct>]
type PhaseKind =
    | Attacking
    | Defending
    | TransitionAttack
    | TransitionDefend

[<Struct>]
type Blackboard =
    { mutable OurPhase: PhaseKind
      mutable OpponentShape: ShapeTargets
      mutable OpponentPressure: int
      mutable ThreatZone: float32
      mutable WeaknessZone: float32
      mutable Urgency: float32
      mutable MomentumStreak: int }

[<Struct>]
type EmergentState =
    { mutable Compactness: float32
      mutable PressingIntensity: float32
      mutable WingplayBias: float32
      mutable FatigueFactor: float32
      // R9 — adaptive channel counters (windowed by PatternWindow): which flank
      // the team has been attacking from. Feed the Cross-vs-Pass bias.
      mutable ChannelCenter: float32
      mutable ChannelLeft: float32
      mutable ChannelRight: float32
      mutable DefenseLine: float32 }

[<Struct>]
type InterceptState =
    { mutable ComputedAt: int<subtick>
      mutable SelfStep: int
      mutable TeammateStep: int
      mutable OpponentStep: int
      mutable BallStep: int
      // Per-slot intercept steps (indexed by slot; GK/sidelined/inactive -> MaxSteps).
      mutable SlotSteps: int[] }

[<Struct>]
type TeamStats =
    { mutable Shots: int
      mutable OnTarget: int
      mutable Passes: int
      mutable PassesCompleted: int
      mutable Fouls: int
      mutable Corners: int
      mutable Offsides: int
      mutable Touches: int
      mutable Dribbles: int
      mutable Crosses: int
      mutable XgSum: float }

[<Struct>]
type CognitiveFrame =
    { mutable LastPerceivedTick: int<subtick>
      mutable LastDecidedTick: int<subtick>
      mutable SupportAttackComputed: bool }

/// Ring buffer of recent event kinds — the single source for windowed counts.
/// This is a reference type because the header is mutable; passing a mutable
/// struct by value would update the arrays but lose Head and Length.
type EventWindow(capacity: int, horizon: int<subtick>) =
    let actualCapacity = max 1 capacity

    member val Head = 0 with get, set
    member val Length = 0 with get, set
    member val Ticks: int<subtick>[] = Array.zeroCreate actualCapacity with get
    member val Kinds: int[] = Array.zeroCreate actualCapacity with get
    member val Counts: int[] = Array.zeroCreate 64 with get
    member val Horizon = max 1<subtick> horizon with get

module EventWindow =

    let createWithHorizon (capacity: int) (horizon: int<subtick>) : EventWindow = EventWindow(capacity, horizon)

    let create (capacity: int) : EventWindow =
        createWithHorizon capacity (max 1 capacity * 1<subtick>)

    /// O(1) amortized: evict entries older than (tick - capacity).
    let push (window: EventWindow) (tick: int<subtick>) (kind: EventKind) : unit =
        let capacity = window.Ticks.Length
        let expired = tick - window.Horizon

        while window.Length > 0 && window.Ticks.[window.Head] <= expired do
            let oldKind = window.Kinds.[window.Head]
            window.Counts.[oldKind] <- window.Counts.[oldKind] - 1
            window.Head <- (window.Head + 1) % capacity
            window.Length <- window.Length - 1

        let idx = (window.Head + window.Length) % capacity

        if window.Length = capacity then
            window.Counts.[window.Kinds.[window.Head]] <- window.Counts.[window.Kinds.[window.Head]] - 1
            window.Head <- (window.Head + 1) % capacity
            window.Length <- window.Length - 1

        window.Ticks.[idx] <- tick

        let k =
            System.Numerics.BitOperations.TrailingZeroCount((EventFlags.ofKind kind).Bits)

        window.Kinds.[idx] <- k
        window.Counts.[k] <- window.Counts.[k] + 1
        window.Length <- window.Length + 1

    /// O(1) count of kind within the last `windowSize` subticks.
    let countInWindow (window: EventWindow) (tick: int<subtick>) (windowSize: int<subtick>) (kind: EventKind) : int =
        let k =
            System.Numerics.BitOperations.TrailingZeroCount((EventFlags.ofKind kind).Bits)

        let cutoff = tick - windowSize

        let mutable count = window.Counts.[k]
        let capacity = window.Ticks.Length
        let mutable i = window.Head
        let mutable n = window.Length

        while n > 0 && window.Ticks.[i] <= cutoff do
            if window.Kinds.[i] = k then
                count <- count - 1

            i <- (i + 1) % capacity
            n <- n - 1

        count

/// The ONLY sanctioned TeamFrame factory — allocates every array at length
/// slotCount, defaults conditions/mental to neutral values.
module TeamFrame =

    let create (slotCount: int) : TeamFrame =
        { PosX = Array.zeroCreate<float32> slotCount
          PosY = Array.zeroCreate<float32> slotCount
          VelX = Array.zeroCreate<float32> slotCount
          VelY = Array.zeroCreate<float32> slotCount
          PosZ = Array.zeroCreate<float32> slotCount
          VelZ = Array.zeroCreate<float32> slotCount
          TargetVelX = Array.zeroCreate<float32> slotCount
          TargetVelY = Array.zeroCreate<float32> slotCount
          TargetFacing = Array.zeroCreate<float32> slotCount
          JumpRequested = Array.create slotCount false
          JumpSequence = Array.zeroCreate<uint64> slotCount
          BodyFacing = Array.zeroCreate<float32> slotCount
          BodyAngularVelocity = Array.zeroCreate<float32> slotCount
          BodyAngularAcceleration = Array.zeroCreate<float32> slotCount
          BodyTravelDistance = Array.zeroCreate<float32> slotCount
          GaitPhase = Array.zeroCreate<float32> slotCount
          ContactGeneration = Array.zeroCreate<int> slotCount
          ContactState = Array.create slotCount BodyContactState.stable
          ContactActions = Array.create slotCount None
          Condition = Array.create slotCount 1.0f
          Mental =
            { Composure = Array.create slotCount 1.0f
              Confidence = Array.create slotCount 1.0f
              Aggression = Array.create slotCount 0.5f
              Focus = Array.create slotCount 1.0f
              Risk = Array.create slotCount 0.5f }
          Intent =
            { Kind = Array.zeroCreate<byte> slotCount
              TargetX = Array.zeroCreate<float32> slotCount
              TargetY = Array.zeroCreate<float32> slotCount
              Phase = Array.zeroCreate<byte> slotCount
              CommittedUntil = Array.zeroCreate<int<subtick>> slotCount
              CommittedAt = Array.zeroCreate<int<subtick>> slotCount
              ExitTrigger = Array.zeroCreate<byte> slotCount }
          SteerIntent =
            { Kind = Array.zeroCreate<byte> slotCount
              TargetX = Array.zeroCreate<float32> slotCount
              TargetY = Array.zeroCreate<float32> slotCount
              Phase = Array.zeroCreate<byte> slotCount
              CommittedUntil = Array.zeroCreate<int<subtick>> slotCount
              CommittedAt = Array.zeroCreate<int<subtick>> slotCount
              ExitTrigger = Array.zeroCreate<byte> slotCount }
          SteerShapeX = Array.zeroCreate<float32> slotCount
          SteerShapeY = Array.zeroCreate<float32> slotCount
          Occupancy = Array.init slotCount (fun i -> OccupancyKind.Active i)
          SlotRoles = Array.create slotCount SlotRole.CenterMid
          DefensiveRoles = Array.create slotCount DefensiveRole.Unassigned
          CollectiveIntents =
            Array.create
                slotCount
                { Pressing = 0.0f
                  Holding = 0.0f
                  Attacking = 0.0f
                  Balanced = 1.0f }
          SupportX = Array.zeroCreate<float32> slotCount
          SupportY = Array.zeroCreate<float32> slotCount
          VisibilityTm = Array.create slotCount true
          VisibilityOpp = Array.create slotCount true
          OppsNear = Array.zeroCreate<int> slotCount
          PressCount = Array.zeroCreate<int> slotCount
          LaneClear = Array.create<bool> slotCount true
          FaceCone = Array.create<bool> slotCount true
          Cognitive = Array.create<float32> slotCount 1.0f
          RoleBias = Array.create<float32> slotCount 1.0f
          RunLookup = System.Collections.Generic.Dictionary<int, RunAssignment>()
          ActiveCount = slotCount
          SlotCount = slotCount
          ActiveCache = ActiveSlotsCache() }
