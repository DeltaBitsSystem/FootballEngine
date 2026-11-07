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
    | PhysicalContact = 9

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

/// The one definition of a publishable motion boundary. Core systems produce it and every
/// presentation consumes it, so the engine and the viewer cannot disagree on validity.
module MotionContract =
    let vector (v: Vector3) =
        System.Single.IsFinite v.X
        && System.Single.IsFinite v.Y
        && System.Single.IsFinite v.Z
        && v.LengthSquared() < 1e12f

    let private optional =
        function
        | ValueNone -> true
        | ValueSome v -> vector v

    let private positiveFinite (value: float32) =
        System.Single.IsFinite value && value > 0.0f

    // Built from the enum once so new cases are accepted without a literal bound, and the
    // per-record check stays allocation-free (Enum.IsDefined boxes on every call).
    let private definedTable (values: int[]) =
        let table = Array.zeroCreate<bool> (Array.max values + 1)

        for value in values do
            if value >= 0 then
                table[value] <- true

        table

    let private families =
        definedTable (System.Enum.GetValues<MotionFamily>() |> Array.map int)

    let private effectors =
        definedTable (System.Enum.GetValues<MotionEffector>() |> Array.map int)

    let private phases =
        definedTable (System.Enum.GetValues<MotionEvidencePhase>() |> Array.map int)

    let private defined (table: bool[]) (value: int) =
        value >= 0 && value < table.Length && table[value]

    let root (sample: MotionRootSample) =
        sample.Generation >= 0
        && vector sample.Position
        && vector sample.Velocity
        && optional sample.AttentionTarget
        && System.Single.IsFinite sample.Facing
        && System.Single.IsFinite sample.AngularVelocity
        && System.Single.IsFinite sample.Condition
        && sample.Condition >= 0.0f
        && sample.Condition <= 1.0f
        && positiveFinite sample.Morphology.Height
        && positiveFinite sample.Morphology.Mass
        && positiveFinite sample.Morphology.ThighLength
        && positiveFinite sample.Morphology.ShinLength
        && positiveFinite sample.Morphology.FootLength
        && positiveFinite sample.Morphology.ShoulderWidth

    let evidence (value: MotionEvidence) =
        value.Sequence > 0UL
        && value.Sequence < System.UInt64.MaxValue
        // Task 0 is the "no task" sentinel of foot overlays, so a published task must be numbered.
        && value.TaskId > 0UL
        && value.Generation >= 0
        && defined families (int value.Family)
        && defined effectors (int value.Effector)
        && defined phases (int value.Phase)
        && value.OccurredAt >= 0<subtick>
        && value.OccurredAt <= value.AvailableAt
        && value.StartedAt >= 0<subtick>
        && value.StartedAt <= value.CommitAt
        && value.CommitAt <= value.EndsAt
        && vector value.PredictedPoint
        && vector value.BallCenter
        && optional value.ConfirmedPoint
        && optional value.IncomingVelocity
        && optional value.OutgoingVelocity

    /// First contract violation of a boundary, given the next expected evidence sequence.
    let violation (expectedSequence: uint64) (boundary: MotionBoundary) : string option =
        let roots = boundary.Roots

        let duplicate =
            roots
            |> Array.countBy (fun r -> r.Actor)
            |> Array.tryFind (fun (_, count) -> count > 1)

        match roots |> Array.tryFind (root >> not), duplicate with
        | Some bad, _ -> Some $"invalid root %A{bad}"
        | None, Some(actor, _) -> Some $"duplicate root %A{actor}"
        | None, None ->
            let mutable next = expectedSequence
            let mutable error = None

            for record in boundary.Evidence do
                if error.IsNone then
                    if not (evidence record) then
                        error <- Some $"invalid evidence %A{record}"
                    elif record.AvailableAt > boundary.Tick then
                        error <- Some $"evidence available after its boundary %A{record}"
                    elif record.Sequence <> next then
                        error <- Some $"evidence sequence gap: expected {next}, got {record.Sequence}"

                next <- record.Sequence + 1UL

            error

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
