namespace FootballEngine

open FootballEngine.AppMsgs
open FootballEngine.AppTypes
open FootballEngine.Core.Match
open FootballEngine.LiveMatch

module MatchSlice =
    let clearState =
        { RuntimeId = None
          Camera = FootballEngine.Client.Match3D.ViewerCamera.Broadcast
          RequestedMatchId = None
          PendingResult = None
          Error = None
          Live = None }

    let private applyCore (message: FootballEngine.LiveMatch.LiveMatchMsg) (state: AppTypes.MatchState) =
        let live, _ = LiveMatchUpdate.update message state.Live

        match live, state.Live with
        | Some updated, Some previous when obj.ReferenceEquals(updated, previous) -> state
        | None, None -> state
        | _ -> { state with Live = live }

    let applySemantic (semantic: LiveRuntimeSemantic) (state: AppTypes.MatchState) =
        let updated =
            applyCore
                (FootballEngine.LiveMatch.SemanticReceived(
                    semantic.Revision,
                    semantic.Clock,
                    semantic.Score,
                    fst semantic.Stats,
                    snd semantic.Stats,
                    Array.toList semantic.Events
                ))
                state

        match updated.Live with
        | Some live when semantic.Revision >= live.Revision ->
            let evidence =
                match semantic.Evidence with
                | Some publication -> Some(Observation.merge live.Evidence publication)
                | None -> live.Evidence

            { updated with
                Live = Some { live with Evidence = evidence } }
        | _ -> updated

    let applyFinished (_runtimeId: MatchRuntimeId) (_result: MatchJobResult) (state: AppTypes.MatchState) =
        match state.Live with
        | None -> state
        | Some live ->
            { state with
                Live = Some { live with Status = Finished } }

    let applyFailed message (state: AppTypes.MatchState) =
        let state =
            { state with
                Error = Some message
                RuntimeId = None }

        match state.Live with
        | None -> state
        | Some live ->
            { state with
                Live =
                    Some
                        { live with
                            Status = Failed message
                            Error = Some message } }

    let update (message: LiveMsg) (state: AppTypes.MatchState) =
        match message with
        | LiveMsg.SessionConnected(sessionId, fixture, revision, source) ->
            applyCore (FootballEngine.LiveMatch.SessionConnected(sessionId, fixture, revision, source)) state
        | LiveMsg.SemanticReceived(revision, clock, score, homeStats, awayStats, events) ->
            applyCore
                (FootballEngine.LiveMatch.SemanticReceived(revision, clock, score, homeStats, awayStats, events))
                state
        | LiveMsg.CommandSubmitted command -> applyCore (FootballEngine.LiveMatch.CommandSubmitted command) state
        | LiveMsg.CommandAccepted(commandId, point) ->
            applyCore (FootballEngine.LiveMatch.CommandAccepted(commandId, point)) state
        | LiveMsg.CommandApplied(commandId, revision, appliedAt) ->
            applyCore (FootballEngine.LiveMatch.CommandApplied(commandId, revision, appliedAt)) state
        | LiveMsg.CommandRejected(commandId, rejection) ->
            applyCore (FootballEngine.LiveMatch.CommandRejected(commandId, rejection)) state
        | LiveMsg.RuntimeStatusChanged status -> applyCore (FootballEngine.LiveMatch.RuntimeStatusChanged status) state
        | LiveMsg.SelectManagerWorkspace workspace ->
            applyCore (FootballEngine.LiveMatch.SelectManagerWorkspace workspace) state
        | LiveMsg.SelectViewerCamera camera -> { state with Camera = camera }
        | LiveMsg.SelectIntelligenceWorkspace workspace ->
            applyCore (FootballEngine.LiveMatch.SelectIntelligenceWorkspace workspace) state
        | LiveMsg.SelectPlayer playerId -> applyCore (FootballEngine.LiveMatch.SelectPlayer playerId) state
        | LiveMsg.SetEvidenceQuery query -> applyCore (FootballEngine.LiveMatch.SetEvidenceQuery query) state
        | LiveMsg.SetDraftSubstitutions changes ->
            applyCore (FootballEngine.LiveMatch.SetDraftSubstitutions changes) state
        | LiveMsg.SubmitDraftSubstitutions -> applyCore FootballEngine.LiveMatch.SubmitDraftSubstitutions state
        | LiveMsg.DismissError -> applyCore FootballEngine.LiveMatch.DismissError state
        | LiveMsg.OpenReplay -> state
        | LiveMsg.TogglePause -> state
        | LiveMsg.ConfirmFinish -> state
        | LiveMsg.Close -> clearState
        | LiveMsg.RuntimePublicationAvailable _ -> state
