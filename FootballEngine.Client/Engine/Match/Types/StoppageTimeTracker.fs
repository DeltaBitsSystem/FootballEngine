namespace FootballEngine

open Stats

type StoppageReason =
    | GoalDelay
    | CardDelay
    | InjuryDelay of severity: int
    | SubstitutionDelay
    | SetPieceDelay
    | VARReviewDelay
    | OtherDelay of description: string

type StoppageEntry =
    { SubTick: int
      Reason: StoppageReason
      SecondsAdded: int }



module StoppageReason =
    let defaultSeconds =
        function
        | GoalDelay -> 30
        | CardDelay -> 15
        | InjuryDelay severity -> 30 + severity * 10
        | SubstitutionDelay -> 15
        | SetPieceDelay -> 5
        | VARReviewDelay -> 0
        | OtherDelay _ -> 10

open StoppageReason

type StoppageTimeTracker() =
    member val private entries: ResizeArray<StoppageEntry> = ResizeArray() with get
    member val HalfTimeAdded = 0 with get, set
    member val FullTimeAdded = 0 with get, set
    member val HalfTimeDecided = false with get, set
    member val FullTimeDecided = false with get, set

    member this.Add(subTick, reason, ?seconds) =
        let secs = defaultArg seconds (defaultSeconds reason)

        let varDelay =
            match reason with
            | StoppageReason.VARReviewDelay -> normalInt 30.0 10.0 20 50
            | _ -> 0

        let total = secs + varDelay

        this.entries.Add
            { SubTick = subTick
              Reason = reason
              SecondsAdded = total }

        total

    member this.AccumulatedSeconds = this.entries |> Seq.sumBy _.SecondsAdded

    member this.DecideHalfTime() =
        let baseSeconds = this.AccumulatedSeconds
        let minAdded = max 1 (baseSeconds / 60)
        let maxAdded = max minAdded (baseSeconds / 45 + 2)
        let decided = normalInt (float (minAdded + maxAdded) / 2.0) 0.5 minAdded maxAdded
        this.HalfTimeAdded <- decided
        this.HalfTimeDecided <- true
        decided

    member this.DecideFullTime() =
        let secondHalfEntries =
            this.entries |> Seq.filter (fun e -> e.SubTick > 0) |> Seq.sumBy _.SecondsAdded

        let baseSeconds = secondHalfEntries
        let minAdded = max 1 (baseSeconds / 60)
        let maxAdded = max minAdded (baseSeconds / 45 + 2)
        let decided = normalInt (float (minAdded + maxAdded) / 2.0) 0.5 minAdded maxAdded
        this.FullTimeAdded <- decided
        this.FullTimeDecided <- true
        decided

    member this.Entries = this.entries |> Seq.toList
