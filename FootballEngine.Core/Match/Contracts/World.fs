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
    {
        OurPhase: PhaseKind
        InTransition: bool
        /// Slots holding an action the body may still execute, bit per slot.
        PendingSlots: uint64
    }

/// Which kind of player a rival manager wants on and off the pitch when a move comes with a change.
[<RequireQualifiedAccess>]
type SubstitutionIntent =
    | Attack
    | Defend
    | Keep

/// What a rival manager remembers during one match: when he last looked, what he saw, what he did and what he
/// still owes his team. ManagerSystem owns it; the object is shared between the live team and cognition's view
/// of it, so a review made on the cognition thread is never lost at the barrier.
type ManagerBrainState() =
    member val LastReviewMinute = 0 with get, set
    /// Minute a goal, a red card or half-time scheduled a review for; -1 when none is waiting.
    member val PendingReviewMinute = -1 with get, set
    member val LastMoveMinute = -1000 with get, set
    member val LastXgFor = 0.0 with get, set
    member val LastXgAgainst = 0.0 with get, set
    /// Decayed xG of each side: the spell the coach remembers when he judges who is on top.
    member val RecentXgFor = 0.0 with get, set
    member val RecentXgAgainst = 0.0 with get, set
    member val Shift: ManagerShift voption = ValueNone with get, set
    /// A goal or a red card since the last move: such an event lifts the pause between moves.
    member val EventSinceMove = false with get, set
    member val OwnRedSinceReview = false with get, set
    /// Half-time has passed without a review: the next one may ask hotheads for the double change.
    member val HalfTimeSeen = false with get, set
    /// The plan the coach started with; a shift is always measured from it, so shifts never stack.
    member val BasePlan: TacticsConfig voption = ValueNone with get, set
    /// Changes still to make after the one in flight, because a team holds one substitution at a time.
    member val QueuedChanges = 0 with get, set
    member val QueuedIntent = SubstitutionIntent.Keep with get, set

type TeamWorld =
    {
        mutable Frame: TeamFrame
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
        /// Only the sides the user does not command use it; see ManagerSystem.
        Brain: ManagerBrainState
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
        mutable EvaluatorScratch: PositionPoint[]
    }

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
    // Int32.MinValue is no player id, so the first tick always fills a body's constants.
    let bodyPlayer = Array.create<int> size System.Int32.MinValue
    let baseAcceleration = Array.zeroCreate<float> size
    let footLength = Array.zeroCreate<float> size

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
    /// Body constants cached per body index: the player they were computed for (compared by id value), the
    /// acceleration cap before the contact-state scale, and the foot length. They only change when a substitution
    /// or red card puts another player in the slot, so the solver refills an index when its player differs.
    member _.BodyPlayer = bodyPlayer
    member _.BaseAcceleration = baseAcceleration
    member _.FootLength = footLength

type EventBuffer =
    { mutable Events: ResizeArray<MatchEvent>
      mutable Flags: EventFlags
      mutable Applied: ResizeArray<MatchEvent> }

/// The level-1 options of a decision, in parallel arrays that grow by doubling so a lineup of any size fits.
[<Sealed>]
type PlanOptions() =
    member val Utility: float[] = Array.zeroCreate 16 with get, set
    member val Code: int[] = Array.zeroCreate 16 with get, set
    member val Target: int[] = Array.zeroCreate 16 with get, set
    member val Count = 0 with get, set

    member this.Clear() = this.Count <- 0

    member private this.Grow() =
        let size = this.Utility.Length * 2
        let utility = Array.zeroCreate<float> size
        let code = Array.zeroCreate<int> size
        let target = Array.zeroCreate<int> size
        System.Array.Copy(this.Utility, utility, this.Count)
        System.Array.Copy(this.Code, code, this.Count)
        System.Array.Copy(this.Target, target, this.Count)
        this.Utility <- utility
        this.Code <- code
        this.Target <- target

    member this.Add(utility: float, code: int, target: int) =
        if this.Count = this.Utility.Length then
            this.Grow()

        this.Utility[this.Count] <- utility
        this.Code[this.Count] <- code
        this.Target[this.Count] <- target
        this.Count <- this.Count + 1

    /// A new first option: the carry, the clearance and the cross go ahead of the passes.
    member this.InsertFirst(utility: float, code: int, target: int) =
        this.Add(utility, code, target)
        let last = this.Count - 1
        System.Array.Copy(this.Utility, 0, this.Utility, 1, last)
        System.Array.Copy(this.Code, 0, this.Code, 1, last)
        System.Array.Copy(this.Target, 0, this.Target, 1, last)
        this.Utility[0] <- utility
        this.Code[0] <- code
        this.Target[0] <- target

    /// Reverses the options from `from` on: passes are found in slot order but listed last found first.
    member this.ReverseFrom(from: int) =
        System.Array.Reverse(this.Utility, from, this.Count - from)
        System.Array.Reverse(this.Code, from, this.Count - from)
        System.Array.Reverse(this.Target, from, this.Count - from)

/// The planner's snapshot of the world when a decision began: both teams' positions and motion in arrays of
/// their own, reused by every decision of the match.
[<Sealed>]
type ChainRoot() =
    member val BallX = 0.0 with get, set
    member val BallY = 0.0 with get, set
    member val CarrierSlot = -1 with get, set
    member val Side = ClubSide.Home with get, set
    member val OffsideLine = 0.0 with get, set
    member val Dir = 1.0 with get, set
    member val Horizon = 0.0 with get, set
    member val Confidence = 0.0 with get, set
    /// Bit per slot, as in `SlotMask`.
    member val GoalkeeperSlots = 0UL with get, set
    member val OurGoalkeeperSlots = 0UL with get, set
    member val CarrySpeed = 0.0 with get, set
    member val OurCount = 0 with get, set
    member val OppCount = 0 with get, set
    /// A state with no opponent motion keeps its offside line instead of projecting it.
    member val HasOppMotion = false with get, set
    member val OurSlot: int[] = Array.zeroCreate 16 with get, set
    member val OurX: float[] = Array.zeroCreate 16 with get, set
    member val OurY: float[] = Array.zeroCreate 16 with get, set
    member val OurVx: float[] = Array.zeroCreate 16 with get, set
    member val OurVy: float[] = Array.zeroCreate 16 with get, set
    member val OurMoves: bool[] = Array.zeroCreate 16 with get, set
    member val OppSlot: int[] = Array.zeroCreate 16 with get, set
    member val OppX: float[] = Array.zeroCreate 16 with get, set
    member val OppY: float[] = Array.zeroCreate 16 with get, set
    member val OppVx: float[] = Array.zeroCreate 16 with get, set
    member val OppVy: float[] = Array.zeroCreate 16 with get, set
    member val OppMoves: bool[] = Array.zeroCreate 16 with get, set

    /// Room for `ours` and `opponents` players; storage is kept when it already fits.
    member this.Ensure(ours: int, opponents: int) =
        if ours > this.OurSlot.Length then
            let size = max ours (this.OurSlot.Length * 2)
            this.OurSlot <- Array.zeroCreate size
            this.OurX <- Array.zeroCreate size
            this.OurY <- Array.zeroCreate size
            this.OurVx <- Array.zeroCreate size
            this.OurVy <- Array.zeroCreate size
            this.OurMoves <- Array.zeroCreate size

        if opponents > this.OppSlot.Length then
            let size = max opponents (this.OppSlot.Length * 2)
            this.OppSlot <- Array.zeroCreate size
            this.OppX <- Array.zeroCreate size
            this.OppY <- Array.zeroCreate size
            this.OppVx <- Array.zeroCreate size
            this.OppVy <- Array.zeroCreate size
            this.OppMoves <- Array.zeroCreate size

/// What a search found, per option and in the options' order. Chains are not kept: only tests read them.
[<Sealed>]
type PlanResult() =
    member val Value: float[] = Array.zeroCreate 16 with get, set
    member val ActionCode: int[] = Array.zeroCreate 16 with get, set
    member val TargetSlot: int[] = Array.zeroCreate 16 with get, set
    member val CandidateEvaluations: int[] = Array.zeroCreate 16 with get, set
    member val Count = 0 with get, set

    member this.Ensure(count: int) =
        if count > this.Value.Length then
            let size = max count (this.Value.Length * 2)
            this.Value <- Array.zeroCreate size
            this.ActionCode <- Array.zeroCreate size
            this.TargetSlot <- Array.zeroCreate size
            this.CandidateEvaluations <- Array.zeroCreate size

/// A carrier's decision while its search runs; one per match, because only one carrier deliberates at a time.
/// PlayerDecision writes the inputs, the search (on the planner's thread when the match has one) writes Result,
/// and the deliberation reads it once the thread is joined. The search is a pure function of the inputs
/// taken when the decision began, so it may run on any thread across ticks.
[<Sealed>]
type PlanSlot() =
    member val Active = false with get, set
    member val Due = 0<subtick> with get, set
    member val Side = ClubSide.Home with get, set
    member val Slot = -1 with get, set
    member val Actor = PlayerId 0 with get, set
    /// The carrier's cognition when the decision began (not the frame's value at the moment it is due).
    member val Cognitive = 0.0 with get, set
    member val Pressure = 0 with get, set
    member val Options = PlanOptions()
    member val Root = ChainRoot()
    member val Result = PlanResult()
    /// Searches this slot; fixed for the match, so handing it to the planner thread allocates nothing.
    member val Search: (unit -> unit) voption = ValueNone with get, set

/// Runs a tick's cognition while the caller runs the body, and waits for it at the barrier.
type ICognitionRunner =
    inherit System.IDisposable
    abstract Start: work: (unit -> unit) -> unit
    abstract Join: unit -> unit

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
      mutable NextContactId: uint64
      Cognition: CognitionSide }

/// What cognition writes through during a tick: its own events, an observation journal and, when it runs on
/// another thread, a view of the world whose bodies are copied at the barrier. Kept per match so the hot path
/// allocates nothing.
and CognitionSide() =
    member val Events = ResizeArray<MatchEvent>()
    member val Buffer: EventBuffer voption = ValueNone with get, set
    /// The world as cognition sees it: the live teams with the observation journal in place of the record.
    member val Direct: World voption = ValueNone with get, set
    /// The world with its own copies of every body, used while cognition runs beside the body.
    member val View: World voption = ValueNone with get, set
    member val Runner: ICognitionRunner voption = ValueNone with get, set
    /// A second thread for the away team's half of a team-by-team cognition system: such a system writes only
    /// its own team's state and reads the other team's bodies, so the two halves run at once.
    member val Lanes: ICognitionRunner voption = ValueNone with get, set
    /// The carrier's plan still being searched, and the thread it is searched on when the match has one.
    member val Plan = PlanSlot()
    member val Planner: ICognitionRunner voption = ValueNone with get, set
