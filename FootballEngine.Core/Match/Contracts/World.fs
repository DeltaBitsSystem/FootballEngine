namespace FootballEngine.Core.Match

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
      mutable EventWindow: EventWindow }

/// The single object systems see. All arrays/books owned by this match.
type World =
    { mutable State: MatchState
      Home: TeamWorld
      Away: TeamWorld }
