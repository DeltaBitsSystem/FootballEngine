namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types

open FootballEngine.Types.PhysicsContract


type TeamSimState() =
    member val Frame: TeamFrame = TeamFrame() with get, set
    member val Sidelined: Map<PlayerId, PlayerOut> = Map.empty with get, set
    member val Yellows: Map<PlayerId, int> = Map.empty with get, set
    member val SubsUsed: int = 0 with get, set
    member val Tactics: TeamTactics = TeamTactics.Balanced with get, set
    member val Instructions: TacticalInstructions option = None with get, set
    member val ActiveRuns: RunAssignment list = [] with get, set
    member val EmergentState: EmergentState = EmergentState.initial with get, set
    member val AdaptiveState: AdaptiveState = AdaptiveTactics.initial with get, set
    member val MatchStats: MatchStats = MatchStats.empty with get, set
    member val TransitionPressExpiry: int<subtick> = 0<subtick> with get, set
    member val ShapeTargetX: float32[] = Array.empty with get, set
    member val ShapeTargetY: float32[] = Array.empty with get, set
    member val SetPiecePositions: Spatial[] option = None with get, set
    member val VisCanSeeTM: bool[] = Array.empty with get, set
    member val VisCanSeeOpp: bool[] = Array.empty with get, set
    member val ActiveRunLookup: System.Collections.Generic.Dictionary<PlayerId, RunAssignment> = System.Collections.Generic.Dictionary() with get, set

    member val Directive: TeamDirectiveState = TeamDirectiveState.Active(TeamDirectiveOps.empty 0<subtick>) with get, set


module TeamSimState =
    let empty () =
        let ts = TeamSimState()
        ts.Frame <- TeamFrame()
        ts

type BuildUpSide =
    | LeftFlank
    | RightFlank
    | Central
    | Balanced

type SubstitutionRequest =
    { ClubId: ClubId
      OutPlayerId: PlayerId
      InPlayerId: PlayerId
      RequestedSubTick: int<subtick>
      CommandId: int64 option }

type PenaltyShootout =
    { HomeKicks: (PlayerId * bool * int) list
      AwayKicks: (PlayerId * bool * int) list
      CurrentKick: int
      IsComplete: bool }

type SimState() =
    member val SubTick = 0<subtick> with get, set
    member val HomeScore = 0 with get, set
    member val AwayScore = 0 with get, set

    member val Flow: MatchFlow =
        RestartDelay
            { Kind = SetPieceKind.KickOff
              Team = HomeClub
              Cause = InitialKickOff
              RemainingTicks = 0<tickDelta> } with get, set

    member val Config = BalanceConfig.defaultConfig with get, set
    member val MatchMemory: MatchMemory = MatchMemory.empty with get, set
    member val MatchEvents: ResizeArray<MatchEvent> = ResizeArray<MatchEvent>(512) with get, set
    member val LastMemoryDecaySubTick = 0<subtick> with get, set
    member val HalfTimeHandled = false with get, set
    member val EffectiveFullTimeSubTick = 0<subtick> with get, set
    member val FullTimeHandled = false with get, set

    member val Ball: BallPhysicsState =
        { Position =
            { X = 52.5<meter>
              Y = 34.0<meter>
              Z = 0.0<meter>
              Vx = 0.0<meter / second>
              Vy = 0.0<meter / second>
              Vz = 0.0<meter / second> }
          Spin = Spin.zero
          LastTouchBy = None
          Control = Free
          PendingOffsideSnapshot = None
          StationarySinceSubTick = None
          GKHoldSinceSubTick = None
          PlayerHoldSinceSubTick = None
          Trajectory = None } with get, set

    member val LastAttackingClub = HomeClub with get, set

    member this.AttackingClub =
        match this.Ball.Control with
        | Controlled(club, _)
        | Receiving(club, _, _) -> Some club
        | Contesting(club) -> Some club
        | Airborne
        | Free -> None

    member this.AttackingSide =
        this.AttackingClub |> Option.defaultValue this.LastAttackingClub

    member val HomeAttackDir = LeftToRight with get, set
    member val Momentum = 0.0 with get, set
    member val BallXSmooth = 52.5<meter> with get, set

    member this.PendingOffsideSnapshot = this.Ball.PendingOffsideSnapshot

    member val HomeBasePositions = Array.empty<Spatial> with get, set
    member val AwayBasePositions = Array.empty<Spatial> with get, set
    member val Home = TeamSimState.empty () with get, set
    member val Away = TeamSimState.empty () with get, set

    member val HomeCognitiveFrame = CognitiveFrameDefaults.empty with get, set
    member val AwayCognitiveFrame = CognitiveFrameDefaults.empty with get, set
    member val PossessionHistory: PossessionHistory = PossessionHistory.empty with get, set

    member val HomeInfluenceFrame = InfluenceTypes.emptyInfluenceFrame () with get, set
    member val AwayInfluenceFrame = InfluenceTypes.emptyInfluenceFrame () with get, set

    member val HomeCFrameBuffers: CognitiveFrameBuffers option = None with get, set
    member val AwayCFrameBuffers: CognitiveFrameBuffers option = None with get, set

    member val StoppageTime: StoppageTimeTracker = StoppageTimeTracker() with get, set
    member val HomePendingSubstitutions: SubstitutionRequest list = [] with get, set
    member val AwayPendingSubstitutions: SubstitutionRequest list = [] with get, set
    member val PendingSemanticEvents: ResizeArray<SemanticEvent> = ResizeArray(8) with get, set
    member val TickEvents: ResizeArray<MatchEvent> = ResizeArray<MatchEvent>(64) with get, set
