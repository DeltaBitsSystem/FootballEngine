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
      PendingActions: System.Collections.Generic.Dictionary<int, PendingPlayerAction>
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
