namespace FootballEngine.Core.Match

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

/// SoA — ONE source of truth per property. Every array is length SlotCount.
/// No duplicated flat arrays; the factory allocates everything (no defaultof).
[<Struct>]
type TeamFrame =
    { mutable PosX: float32[]
      mutable PosY: float32[]
      mutable VelX: float32[]
      mutable VelY: float32[]
      mutable Condition: float32[]
      mutable Mental: MentalFrame
      mutable Intent: IntentFrame
      mutable Occupancy: OccupancyKind[]
      mutable SlotRoles: SlotRole[]
      mutable DefensiveRoles: DefensiveRole[]
      mutable CollectiveIntents: CollectiveIntent[]
      mutable SupportX: float32[]
      mutable SupportY: float32[]
      mutable VisibilityTm: bool[]
      mutable VisibilityOpp: bool[]
      mutable RunLookup: System.Collections.Generic.Dictionary<int, RunAssignment>
      ActiveCount: int
      SlotCount: int }

[<Struct>]
type PlayerMemory =
    { mutable LastTouchTick: int<subtick>
      mutable SuccessfulDuels: int
      mutable FailedDuels: int
      mutable PassesMade: int
      mutable PassesCompleted: int
      mutable ShotsTaken: int }

[<Struct>]
type CardsBook =
    { mutable Yellows: int
      mutable Reds: int }

[<Struct>]
type ShapeTargets =
    { mutable TargetsX: float32[]
      mutable TargetsY: float32[] }

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
      mutable MomentumStreak: int
      mutable TransitionSince: int<subtick> }

[<Struct>]
type EmergentState =
    { mutable Compactness: float32
      mutable PressingIntensity: float32
      mutable WingplayBias: float32
      mutable FatigueFactor: float32 }

[<Struct>]
type TeamStats =
    { mutable Shots: int
      mutable OnTarget: int
      mutable Passes: int
      mutable PassesCompleted: int
      mutable Fouls: int
      mutable Corners: int
      mutable Offsides: int
      mutable Touches: int }

[<Struct>]
type CognitiveFrame =
    { mutable LastPerceivedTick: int<subtick>
      mutable LastDecidedTick: int<subtick>
      mutable SupportAttackComputed: bool }

/// Ring buffer of recent event kinds — the single source for windowed counts.
[<Struct>]
type EventWindow =
    { mutable Head: int
      mutable Length: int
      mutable Ticks: int<subtick>[]
      mutable Kinds: int[]
      mutable Counts: int[] }

module EventWindow =

    let create (capacity: int) : EventWindow =
        { Head = 0
          Length = 0
          Ticks = Array.zeroCreate capacity
          Kinds = Array.zeroCreate capacity
          Counts = Array.zeroCreate 28 }

    /// O(1) amortized: evict entries older than (tick - capacity).
    let push (window: EventWindow) (tick: int<subtick>) (kind: EventKind) : unit =
        let mutable window = window
        let capacity = window.Ticks.Length
        let expired = tick - capacity * 1<subtick>

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
            int (EventFlags.ofKind kind).Bits
            |> fun b -> System.Numerics.BitOperations.TrailingZeroCount(uint b)

        window.Kinds.[idx] <- k
        window.Counts.[k] <- window.Counts.[k] + 1
        window.Length <- window.Length + 1

    /// O(1) count of kind within the last `windowSize` subticks.
    let countInWindow (window: EventWindow) (tick: int<subtick>) (windowSize: int<subtick>) (kind: EventKind) : int =
        let k =
            int (EventFlags.ofKind kind).Bits
            |> fun b -> System.Numerics.BitOperations.TrailingZeroCount(uint b)

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
