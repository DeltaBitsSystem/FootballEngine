namespace FootballEngine.Core.Match

type ParticipationInterval =
    { PlayerId: PlayerId
      Role: SlotRole
      StartAt: int<subtick>
      mutable EndAt: int<subtick> option }

/// Participation is owned by the match so substitutions, dismissals and
/// injuries close the same interval that later becomes the World statistic.
type ParticipationBook() =
    member val Intervals = System.Collections.Generic.List<ParticipationInterval>()

module ParticipationBook =

    let create () = ParticipationBook()

    let generation (book: ParticipationBook) (playerId: PlayerId) =
        book.Intervals
        |> Seq.filter (fun interval -> interval.PlayerId = playerId)
        |> Seq.length

    let start (book: ParticipationBook) (playerId: PlayerId) (role: SlotRole) (at: int<subtick>) =
        if
            book.Intervals
            |> Seq.exists (fun interval -> interval.PlayerId = playerId && interval.EndAt.IsNone)
        then
            ()
        else
            book.Intervals.Add
                { PlayerId = playerId
                  Role = role
                  StartAt = at
                  EndAt = None }

    let stop (book: ParticipationBook) (playerId: PlayerId) (at: int<subtick>) =
        let mutable stopped = false

        for i = book.Intervals.Count - 1 downto 0 do
            if not stopped then
                let interval = book.Intervals.[i]

                if interval.PlayerId = playerId && interval.EndAt.IsNone then
                    interval.EndAt <- Some(max interval.StartAt at)
                    stopped <- true

    let snapshot (book: ParticipationBook) (endAt: int<subtick>) : ParticipationInterval[] =
        book.Intervals
        |> Seq.map (fun interval ->
            { interval with
                EndAt = Some(max interval.StartAt (interval.EndAt |> Option.defaultValue endAt)) })
        |> Array.ofSeq

/// Action proposals cannot share storage with movement commitments or review evidence.
type PlayerActionChoice =
    | DeliverPass of receiverSlot: int
    | TakeShot
    | ContestDuel
    | ClearBall
    | DeliverCross

type PendingPlayerAction =
    { Choice: PlayerActionChoice
      DecidedAt: int<subtick>
      ExpiresAt: int<subtick> }

type OffsideReviewSnapshot =
    { ScorerX: float32<meter>
      DefensiveLineX: float32<meter>
      CapturedAt: int<subtick> }

/// Reviews survive action execution without sharing the movement assignment namespace.
type RefereeReviewBook() =
    member val Offside = System.Collections.Generic.Dictionary<ClubSide * int, OffsideReviewSnapshot>()

/// A committed save: the gloves will be at Hand around ContactAt; Stretch is the share of the maximum dive used.
[<Struct>]
type KeeperDive =
    { KeeperSlot: int
      CommittedAt: int<subtick>
      ContactAt: int<subtick>
      Hand: Vec3
      Stretch: float }

/// Team-level cognition the body reads, as the previous tick left it.
[<Struct>]
type LatchedTeam =
    { OurPhase: PhaseKind
      InTransition: bool }

type TeamWorld =
    { mutable Frame: TeamFrame
      mutable Tactics: TacticsConfig
      RosterIds: PlayerId[]
      mutable Memory: PlayerMemory[]
      mutable Cards: CardsBook
      mutable SubsUsed: int
      mutable Shape: ShapeTargets
      mutable Directive: TeamDirective
      mutable Blackboard: Blackboard
      Defense: DefensiveTransitionState
      mutable Emergent: EmergentState
      mutable Intercept: InterceptState
      mutable Stats: TeamStats
      mutable CFrame: CognitiveFrame
      mutable EventWindow: EventWindow
      mutable Substitution: SubstitutionState
      Participation: ParticipationBook
      /// Actions cognition proposed this tick; the latch moves them to PendingActions on the next one.
      Proposals: System.Collections.Generic.Dictionary<int, PendingPlayerAction>
      /// Actions the body may execute; owned by the world side.
      PendingActions: System.Collections.Generic.Dictionary<int, PendingPlayerAction>
      mutable Latched: LatchedTeam
      Attack: CollectiveAttackState
      mutable LastCarrierSlot: int
      mutable LastRoleAssignmentTick: int<subtick>
      mutable Dive: KeeperDive voption
      mutable FormationCache: FormationModel option
      mutable LateralPairs: (int * int)[] option
      mutable GoalkeeperSlot: int option
      mutable OwnPositions: PositionPoint[]
      mutable MirroredPositions: PositionPoint[]
      mutable FormationTargets: PositionPoint[]
      mutable EvaluatorScratch: PositionPoint[] }

[<Struct>]
type RefereeSignal =
    | NoSignal
    | Whistle
    | RestartDirection of SetPieceKind * ClubSide

[<Struct>]
type MatchOfficial = { X: float<meter>; Y: float<meter> }

[<Struct>]
type OfficialsState =
    { Referee: MatchOfficial
      NearAssistant: MatchOfficial
      FarAssistant: MatchOfficial
      Signal: RefereeSignal }

/// Per-match fixed-capacity storage keeps contact integration allocation-free after creation.
type ContactScratch(capacity: int) =
    let size = max 1 capacity
    let sides = Array.zeroCreate<ClubSide> size
    let slots = Array.zeroCreate<int> size
    let startX = Array.zeroCreate<float> size
    let startY = Array.zeroCreate<float> size
    let startZ = Array.zeroCreate<float> size
    let velocityX = Array.zeroCreate<float> size
    let velocityY = Array.zeroCreate<float> size
    let velocityZ = Array.zeroCreate<float> size
    let accelerationX = Array.zeroCreate<float> size
    let accelerationY = Array.zeroCreate<float> size
    let radius = Array.zeroCreate<float> size
    let height = Array.zeroCreate<float> size
    let airborneAtStart = Array.zeroCreate<bool> size
    let resolvedBodyPairs = Array.zeroCreate<bool> (size * size)
    let resolvedBallPlayers = Array.zeroCreate<bool> size
    let reach = Array.zeroCreate<float> size
    let bodyReach = Array.zeroCreate<float> size
    let endX = Array.zeroCreate<float> size
    let endY = Array.zeroCreate<float> size
    let travel = Array.zeroCreate<float> size
    let ballCandidate = Array.zeroCreate<bool> size
    let pairCandidates = Array.zeroCreate<uint64> size
    let mayBoot = Array.zeroCreate<bool> size

    member val Count = 0 with get, set
    member val SaturationCount = 0UL with get, set
    member _.Sides = sides
    member _.Slots = slots
    member _.StartX = startX
    member _.StartY = startY
    member _.StartZ = startZ
    member _.VelocityX = velocityX
    member _.VelocityY = velocityY
    member _.VelocityZ = velocityZ
    member _.AccelerationX = accelerationX
    member _.AccelerationY = accelerationY
    member _.Radius = radius
    member _.Height = height
    member _.AirborneAtStart = airborneAtStart
    member _.ResolvedBodyPairs = resolvedBodyPairs
    member _.ResolvedBallPlayers = resolvedBallPlayers
    /// Bound on how far any contact surface of a body extends from its root this step.
    member _.Reach = reach
    /// Horizontal part of Reach that can meet another body: torso and boot, not the head.
    member _.BodyReach = bodyReach
    /// Predicted end of the current sub-step and the horizontal distance travelled to it.
    member _.EndX = endX
    member _.EndY = endY
    member _.Travel = travel
    /// Bodies that can still meet the ball in what is left of the tick; refreshed after every impact.
    member _.BallCandidate = ballCandidate
    /// Bit b of entry a (b > a): bodies a and b can still meet in what is left of the tick; refreshed after every impact.
    /// One word per body is enough because the laws field at most 22 bodies.
    member _.PairCandidates = pairCandidates
    /// Whether the body held a boot challenge when the tick began: within a tick a challenge can end, never start.
    member _.MayBoot = mayBoot

/// The single object systems see. All arrays/books owned by this match.
type World =
    { mutable State: MatchState
      mutable Officials: OfficialsState
      Home: TeamWorld
      Away: TeamWorld
      AppliedEventIds: System.Collections.Generic.HashSet<EventId>
      Observation: ObservationBuffer
      Motion: MotionEvidenceJournal
      Reviews: RefereeReviewBook
      Contact: ContactScratch
      mutable NextRestartId: uint64
      mutable NextContactId: uint64 }
