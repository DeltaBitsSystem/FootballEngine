namespace FootballEngine.Core.Match

/// The fields reassessed every cadence are mutable: the team's defensive transition system owns the episode and
/// updates it in place, because a fresh record per cadence was the largest steady allocation of the team layer.
/// Anything that must outlive a change copies the record (see `snapshot`).
type DefensiveEpisode =
    { Id: EventId
      LostAt: int<subtick>
      Origin: Vec2
      mutable Mode: TransitionMode
      mutable Reason: TransitionReason
      mutable ModeSince: int<subtick>
      mutable CommitUntil: int<subtick>
      mutable Primary: int option
      mutable Cover: int option
      mutable EvaluatedAt: int<subtick>
      mutable Tactics: TacticsConfig
      mutable ParticipationRevision: int
      mutable StabilizingSince: int<subtick> option
      EndedAt: int<subtick> option
      Outcome: TransitionEnd option }

[<Struct>]
type DefensiveTask =
    { Role: DefensiveRole
      Target: Vec2
      Opponent: PlayerId voption }

type DefensiveTransitionState =
    { mutable Episode: DefensiveEpisode option
      mutable ControlChangedAt: int<subtick> option
      Tasks: DefensiveTask voption[] }

module DefensiveTransitionState =
    let create slots =
        { Episode = None
          ControlChangedAt = None
          Tasks = Array.create slots ValueNone }

    let snapshot (state: DefensiveTransitionState) =
        { state with
            Episode = state.Episode |> Option.map (fun episode -> { episode with Id = episode.Id })
            Tasks = Array.copy state.Tasks }

    let clearTasks (frame: TeamFrame) (state: DefensiveTransitionState) =
        Array.fill state.Tasks 0 state.Tasks.Length ValueNone
        Array.fill frame.DefensiveRoles 0 frame.DefensiveRoles.Length DefensiveRole.Unassigned
        Array.fill frame.RoleBias 0 frame.RoleBias.Length 1.0f
