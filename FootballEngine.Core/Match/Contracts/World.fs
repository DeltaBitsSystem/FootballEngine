namespace FootballEngine.Core.Match

/// A multi-step plan: the carrier's mind maps out future receivers after the
/// current pass, so the team moves as one coordinated unit. The plan is
/// written once by Decision, consumed by Intent, and cleared on possession
/// change, carrier change, or expiry.
type ChainPlan =
    { FutureReceivers: (int * float<meter> * float<meter>)[]
      ExpiresAt: int<subtick> }

/// One team's live match state. All books are owned by this match (no globals).
type TeamWorld =
    { mutable Frame: TeamFrame
      mutable Memory: PlayerMemory[]
      mutable Cards: CardsBook
      mutable SubsUsed: int
      mutable Shape: ShapeTargets
      mutable Directive: TeamDirective
      mutable Blackboard: Blackboard
      mutable Emergent: EmergentState
      mutable Intercept: InterceptState
      mutable Stats: TeamStats
      mutable CFrame: CognitiveFrame
      mutable EventWindow: EventWindow
      mutable ActivePlan: ChainPlan option
      mutable LastCarrierSlot: int }

/// The single object systems see. All arrays/books owned by this match.
type World =
    { mutable State: MatchState
      Home: TeamWorld
      Away: TeamWorld }
