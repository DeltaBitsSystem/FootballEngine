namespace FootballEngine.World.Agents

open FootballEngine.Core.Match
open FootballEngine.Domain
open FootballEngine.World

type ManagerIntent =
    | InitiateTransfer of buyerClubId: ClubId * PlayerId * maxFee: decimal
    | ApproveIncomingOffer of NegotiationId
    | RejectIncomingOffer of NegotiationId * reason: string

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

    let private project (balance: BalanceConfig) (clubId: ClubId) (state: GameState) : AgentView =
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

            // SquadPlanner is the "always buy well" gate on every AI signing:
            // refineTargets closes roles already saturated with bench warmers,
            // and cappedFee only tolerates overpay when the gap is real.
            let config = balance

            let targets =
                TargetSelection.selectTargets coach club needs candidates
                |> SquadPlanner.refineTargets
                    config.Transfer
                    state.CurrentDate
                    squad
                    state.PlayerExperiences
                    state.Players
                |> List.map (fun target ->
                    // Matching ladder follows the invariant: a positional gap
                    // outranks a depth gap, and a covered role only signs as a
                    // quality upgrade (no overpay). refineTargets already proved
                    // the target lives in state.Players, so the lookup is total;
                    // cappedFee branches on the case only, so the fallback
                    // aboveSkill payload is irrelevant.
                    let need =
                        match Map.tryFind target.PlayerId state.Players with
                        | Some player ->
                            needs
                            |> List.tryFind (function
                                | NeedsPosition p
                                | NeedsDepth p -> p = player.Position
                                | NeedsQualityUpgrade _ -> false)
                            |> Option.defaultWith (fun () -> NeedsQualityUpgrade 0)
                        | None -> NeedsQualityUpgrade 0

                    { target with
                        MaxFee = SquadPlanner.cappedFee config.Transfer need target.MaxFee })

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

    let make (balance: BalanceConfig) (clubId: ClubId) : Agent<ManagerIntent> =
        { Id = ManagerAgentId clubId
          Trigger = Both(Weekly, OfferReceived(0, clubId))
          Project = project balance clubId
          Decide = decide }

    /// Convenience: derive effective BalanceConfig from SaveSettings (Save > default).
    let makeFromSave (saveOpt: FootballEngine.Settings.SaveSettings option) (clubId: ClubId) : Agent<ManagerIntent> =
        let balance = WeightMergeSettings.mergeWithOverridesSave saveOpt
        make balance clubId

    /// Backward compat for tests — uses effective default (no override).
    let makeDefault (clubId: ClubId) : Agent<ManagerIntent> =
        make (WeightMergeSettings.mergeWithOverridesSave None) clubId
