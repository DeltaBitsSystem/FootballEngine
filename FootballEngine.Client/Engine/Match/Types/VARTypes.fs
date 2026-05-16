namespace FootballEngine.Types

open FootballEngine.Domain


type VARReviewableIncident =
    | GoalCheck of scoringClub: ClubSide * scorerId: PlayerId option * isOwnGoal: bool * goalSubTick: int<subtick>
    | PenaltyCheck of awardedTo: ClubSide * foulX: float * foulY: float
    | RedCardCheck of playerId: PlayerId * incident: FoulSeverity
    | OffsideCheck of attackingPlayer: PlayerId * passSubTick: int<subtick>

type VARDecision =
    | NoIncident
    | CheckComplete
    | Overturn
    | OnFieldReview

type VARReviewState =
    | Reviewing of incident: VARReviewableIncident * reviewStartSubTick: int<subtick> * reviewDurationSubTicks: int<tickDelta>
    | ReviewComplete of incident: VARReviewableIncident * decision: VARDecision

type VARState() =
    member val private reviewHistory: ResizeArray<VARReviewableIncident * VARDecision * int<subtick>> = ResizeArray() with get
    member val CurrentReview: VARReviewState option = None with get, set
    member val IsChecking: bool = false with get, set

    member this.AddReview(incident: VARReviewableIncident, startSubTick: int<subtick>, durationSubTicks: int<tickDelta>) =
        this.CurrentReview <- Some(Reviewing(incident, startSubTick, durationSubTicks))
        this.IsChecking <- true

    member this.CompleteReview(decision: VARDecision) =
        match this.CurrentReview with
        | Some(Reviewing(incident, startSubTick, _)) ->
            this.reviewHistory.Add(incident, decision, startSubTick)
            this.CurrentReview <- Some(ReviewComplete(incident, decision))
            this.IsChecking <- false
        | _ -> ()

    member this.History = this.reviewHistory |> Seq.toList

    member this.Clear() =
        this.CurrentReview <- None
        this.IsChecking <- false
