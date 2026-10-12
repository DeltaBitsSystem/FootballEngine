namespace FootballEngine.Core.Match

type DefensiveEpisode =
    { Id: EventId
      LostAt: int<subtick>
      Origin: Vec2
      Mode: TransitionMode
      Reason: TransitionReason
      ModeSince: int<subtick>
      CommitUntil: int<subtick>
      Primary: int option
      Cover: int option
      EvaluatedAt: int<subtick>
      Tactics: TacticsConfig
      ParticipationRevision: int
      StabilizingSince: int<subtick> option
      EndedAt: int<subtick> option
      Outcome: TransitionEnd option }

[<Struct>]
type DefensiveTask =
    { Role: DefensiveRole
      Target: Vec2
      Opponent: PlayerId option }

type DefensiveTransitionState =
    { mutable Episode: DefensiveEpisode option
      mutable ControlChangedAt: int<subtick> option
      Tasks: DefensiveTask option[] }

module DefensiveTransitionState =
    let create slots =
        { Episode = None
          ControlChangedAt = None
          Tasks = Array.create slots None }

    let snapshot (state: DefensiveTransitionState) =
        { state with
            Tasks = Array.copy state.Tasks }

    let clearTasks (frame: TeamFrame) (state: DefensiveTransitionState) =
        Array.fill state.Tasks 0 state.Tasks.Length None
        Array.fill frame.DefensiveRoles 0 frame.DefensiveRoles.Length DefensiveRole.Unassigned
        Array.fill frame.RoleBias 0 frame.RoleBias.Length 1.0f
