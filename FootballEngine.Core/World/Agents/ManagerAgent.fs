namespace FootballEngine.World.Agents

open FootballEngine.Domain
open FootballEngine.World

type ManagerIntent =
    | InitiateTransfer of buyerClubId: ClubId * PlayerId * maxFee: decimal
    | ApproveIncomingOffer of NegotiationId
    | RejectIncomingOffer of NegotiationId * reason: string
    | SetTrainingSchedule of PlayerId * TrainingSchedule

module ManagerAgent =

    let private transferCandidates (clubId: ClubId) (state: GameState) : Player list =
        state.Players
        |> Map.values
        |> Seq.filter (fun p ->
            p.Status = Available
            && match p.Affiliation with
               | FreeAgent -> true
               | Contracted(cid, _) -> cid <> clubId
               | _ -> false)
        |> List.ofSeq

    let private project (clubId: ClubId) (state: GameState) : AgentView =
        let club = state.Clubs[clubId]

        let squad =
            state.Players
            |> Map.toList
            |> List.map snd
            |> List.filter (fun p -> Player.clubOf p = Some clubId)

        match GameState.headCoach clubId state with
        | None -> ManagerView(club, [])
        | Some coach ->
            let needs = SquadAnalysis.assessNeeds state.CurrentDate squad
            let candidates = transferCandidates clubId state
            let targets = TargetSelection.selectTargets coach club needs candidates
            ManagerView(club, targets)

    let private decide (view: AgentView) : Intent<ManagerIntent> list =
        match view with
        | ManagerView(club, targets) ->
            targets
            |> List.map (fun target ->
                { AgentId = ManagerAgentId club.Id
                  Priority = int target.Priority
                  Payload = InitiateTransfer(club.Id, target.PlayerId, target.MaxFee) })
        | _ -> []

    let make (clubId: ClubId) : Agent<ManagerIntent> =
        { Id = ManagerAgentId clubId
          Trigger = Both(Weekly, OfferReceived(0, clubId))
          Project = project clubId
          Decide = decide }
