namespace FootballEngine.Core.Match

type AttackEstimate = FootballEngine.Domain.AttackEstimate

[<Struct>]
type AttackDuty =
    { Player: PlayerId
      Slot: int
      Target: Vec2 }

type AttackEpisode =
    { Id: uint64
      Pattern: AttackPattern
      Context: int
      StartedAt: int<subtick>
      Deadline: int<subtick>
      Stage: AttackStage
      Carrier: PlayerId
      Owner: PlayerId
      Link: AttackDuty
      Runner: AttackDuty
      Security: AttackDuty
      Origin: Vec2
      BaselineSpace: float
      Connections: int
      LastFact: EventId option
      Threat: float
      OwnedPoint: Vec2
      OwnedSpace: float
      OwnedExposure: float
      FactTick: int<subtick>
      Tactics: TacticsConfig
      BlockedSince: int<subtick> option }

type CollectiveAttackState =
    { mutable Episode: AttackEpisode option
      mutable Estimates: Map<int, AttackEstimate>
      mutable Evidence: Map<int, AttackEstimate>
      mutable NextId: uint64
      mutable AvailableAt: int<subtick> }

module CollectiveAttackState =
    let create prior =
        { Episode = None
          Estimates = prior
          Evidence = Map.empty
          NextId = 1UL
          AvailableAt = 0<subtick> }

    let snapshot (state: CollectiveAttackState) = { state with Episode = state.Episode }

    let patternIndex = function ThirdMan -> 0 | AttractSwitch -> 1 | ExploitDepth -> 2 | SecureProgression -> 3
    let key context pattern = context * 4 + patternIndex pattern

    let duty (state: CollectiveAttackState) player =
        state.Episode
        |> Option.bind (fun episode ->
            [ episode.Link; episode.Runner; episode.Security ]
            |> List.tryFind (fun duty -> duty.Player = player))
