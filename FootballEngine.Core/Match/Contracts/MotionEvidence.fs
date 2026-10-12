namespace FootballEngine.Core.Match

open System.Numerics

type MotionFamily =
    | FootStrike = 0
    | Reception = 1
    | Dribble = 2
    | Jump = 3
    | Landing = 4
    | Goalkeeper = 5
    | ThrowIn = 6
    | Header = 7
    | Reaction = 8

type MotionEffector =
    | LeftFoot = 0
    | RightFoot = 1
    | LeftHand = 2
    | RightHand = 3
    | BothHands = 4
    | Head = 5
    | Body = 6

type MotionEvidencePhase =
    | Prepared = 0
    | Confirmed = 1
    | Cancelled = 2

/// Occurrence is system time; availability and commitment are published boundary time.
[<Struct>]
type MotionEvidence =
    { Sequence: uint64
      TaskId: uint64
      Actor: PlayerId
      Generation: int
      Family: MotionFamily
      Effector: MotionEffector
      Phase: MotionEvidencePhase
      OccurredAt: int<subtick>
      AvailableAt: int<subtick>
      StartedAt: int<subtick>
      CommitAt: int<subtick>
      EndsAt: int<subtick>
      PredictedPoint: Vector3
      ConfirmedPoint: Vector3 voption
      BallCenter: Vector3
      IncomingVelocity: Vector3 voption
      OutgoingVelocity: Vector3 voption }

type MotionReadError =
    | MotionOverflow of oldestSequence: uint64
    | MotionFutureCursor

[<Struct>]
type PhysicalMorphology =
    { Height: float32
      Mass: float32
      ThighLength: float32
      ShinLength: float32
      FootLength: float32
      ShoulderWidth: float32
      PreferredFoot: ContactFoot }

module PhysicalMorphology =
    // Proportions describe the physical proxy independently of any skeleton's bone names.
    let ofStature height mass foot =
        { Height = height
          Mass = mass
          ThighLength = height * (0.4402556f / 1.8f)
          ShinLength = height * (0.4352585f / 1.8f)
          FootLength = height * 0.15f
          ShoulderWidth = height * (0.38f / 1.8f)
          PreferredFoot = foot }

    let standard = ofStature 1.8f 75.0f ContactFoot.Right

[<Struct>]
type MotionRootSample =
    { Actor: PlayerId
      Generation: int
      Position: Vector3
      Velocity: Vector3
      Facing: float32
      AngularVelocity: float32
      Condition: float32
      AttentionTarget: Vector3 voption
      Morphology: PhysicalMorphology
      Active: bool }

type MotionBoundary =
    { Tick: int<subtick>
      Roots: MotionRootSample[]
      Evidence: MotionEvidence[] }

/// Systems write a bounded journal independent of pose sampling and observation settings.
type MotionEvidenceJournal(capacity: int) =
    let records = Array.zeroCreate<MotionEvidence> (max 1 capacity)
    let mutable next = 1UL

    member _.NextSequence = next
    member _.Capacity = records.Length
    member _.OldestSequence = max 1UL (next - min (next - 1UL) (uint64 records.Length))

    member _.Append(record: MotionEvidence) =
        let sequence = next
        records[int ((sequence - 1UL) % uint64 records.Length)] <- { record with Sequence = sequence }
        next <- next + 1UL
        sequence

    member this.Read(cursor: uint64, availableAt: int<subtick>, destination: MotionEvidence[]) =
        if cursor < this.OldestSequence then
            Error(MotionOverflow this.OldestSequence)
        elif cursor > next then
            Error MotionFutureCursor
        else
            let mutable current = cursor
            let mutable count = 0
            let mutable waiting = false

            while current < next && count < destination.Length && not waiting do
                let record = records[int ((current - 1UL) % uint64 records.Length)]

                if record.AvailableAt > availableAt then
                    waiting <- true
                else
                    destination[count] <- record
                    count <- count + 1
                    current <- current + 1UL

            Ok(struct (current, count))
