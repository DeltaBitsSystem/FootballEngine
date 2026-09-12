namespace FootballEngine.Domain

/// Attribute groups a manager can target. Groups may span physical/technical/mental
/// records: a creator needs vision as much as passing, and the engine already reads
/// both. Goalkeeping is GK-only; the phase falls back to maintenance for outfielders.
type AttributeGroup =
    | Finishing
    | Creation
    | Technique
    | Defending
    | Physical
    | Mental
    | Goalkeeping

/// One decision per player per week. Rehab is derived from injury status, never stored:
/// an injured player follows recovery regardless of the plan on file.
type IndividualPlan =
    | Develop of AttributeGroup
    | Perform
    | Rest

type HealthTransition =
    { ConditionDelta: int
      FatigueDelta: int
      PreparationDelta: int
      LoadDelta: int }

/// Weekly team-wide emphasis. Picks the training tilt for the whole squad and doubles
/// as match preparation: the emphasis set during a week boosts one area of the next
/// fixture played within seven days.
type TeamEmphasis =
    | EmHighPress
    | EmPossession
    | EmAttack
    | EmDefense
    | EmSetPieces
    | EmRecovery

module Training =

    let healthTransition (plan: IndividualPlan option) : HealthTransition =
        match plan with
        | None ->
            { ConditionDelta = 6
              FatigueDelta = -20
              PreparationDelta = 0
              LoadDelta = -20 }
        | Some(Develop _) ->
            { ConditionDelta = -8
              FatigueDelta = 18
              PreparationDelta = 4
              LoadDelta = 28 }
        | Some Perform ->
            { ConditionDelta = 4
              FatigueDelta = 8
              PreparationDelta = 6
              LoadDelta = 18 }
        | Some Rest ->
            { ConditionDelta = 18
              FatigueDelta = -24
              PreparationDelta = -4
              LoadDelta = -24 }

    let groupLabel (g: AttributeGroup) : string =
        match g with
        | Finishing -> "Finishing"
        | Creation -> "Creation"
        | Technique -> "Technique"
        | Defending -> "Defending"
        | Physical -> "Physical"
        | Mental -> "Mental"
        | Goalkeeping -> "Goalkeeping"

    let emphasisLabel (e: TeamEmphasis) : string =
        match e with
        | EmHighPress -> "High Press"
        | EmPossession -> "Possession"
        | EmAttack -> "Attack"
        | EmDefense -> "Defense"
        | EmSetPieces -> "Set Pieces"
        | EmRecovery -> "Recovery"

    let planLabel (plan: IndividualPlan) : string =
        match plan with
        | Develop g -> $"Develop · {groupLabel g}"
        | Perform -> "Perform"
        | Rest -> "Rest"

    let allGroups: AttributeGroup list =
        [ Finishing; Creation; Technique; Defending; Physical; Mental; Goalkeeping ]

    /// Attribute group tilted by each team emphasis for the whole squad.
    let tiltOf (e: TeamEmphasis) : AttributeGroup option =
        match e with
        | EmHighPress -> Some Physical
        | EmPossession -> Some Creation
        | EmAttack -> Some Finishing
        | EmDefense -> Some Defending
        | EmSetPieces -> Some Technique
        | EmRecovery -> None
