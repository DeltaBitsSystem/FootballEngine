namespace FootballEngine.Domain

type TeamTactics =
    | Balanced
    | Attacking
    | Defensive
    | Pressing
    | Counter

type TacticalInstructions =
    { Mentality: int
      DefensiveLine: int
      PressingIntensity: int
      Width: int
      Tempo: int
      Directness: int
      PressTriggerZone: int
      DefensiveShape: int }



type LineupSlot =
    { Index: int
      Role: Position
      X: float
      Y: float
      PlayerId: PlayerId option }

type Lineup =
    { Formation: Formation
      Tactics: TeamTactics
      Instructions: TacticalInstructions option
      Slots: LineupSlot list }

module TacticalInstructions =
    let defaultInstructions =
        { Mentality = 2
          DefensiveLine = 2
          PressingIntensity = 2
          Width = 2
          Tempo = 2
          Directness = 2
          PressTriggerZone = 1
          DefensiveShape = 2 }

    let allIds =
        [ "Mentality"
          "DefensiveLine"
          "PressingIntensity"
          "Width"
          "Tempo"
          "Directness"
          "PressTriggerZone"
          "DefensiveShape" ]

    let get (id: string) (i: TacticalInstructions) =
        match id with
        | "Mentality" -> i.Mentality
        | "DefensiveLine" -> i.DefensiveLine
        | "PressingIntensity" -> i.PressingIntensity
        | "Width" -> i.Width
        | "Tempo" -> i.Tempo
        | "Directness" -> i.Directness
        | "PressTriggerZone" -> i.PressTriggerZone
        | "DefensiveShape" -> i.DefensiveShape
        | _ -> 2

    let set (id: string) (value: int) (i: TacticalInstructions) =
        match id with
        | "Mentality" -> { i with Mentality = value }
        | "DefensiveLine" -> { i with DefensiveLine = value }
        | "PressingIntensity" -> { i with PressingIntensity = value }
        | "Width" -> { i with Width = value }
        | "Tempo" -> { i with Tempo = value }
        | "Directness" -> { i with Directness = value }
        | "PressTriggerZone" -> { i with PressTriggerZone = value }
        | "DefensiveShape" -> { i with DefensiveShape = value }
        | _ -> i

/// The shared tactical contract used by both the compact and detailed views.
/// Keeping the resolved instructions here prevents either view from inventing
/// a parallel representation that the match adapter cannot see.
type TacticalPlan =
    { Formation: Formation
      Tactics: TeamTactics
      Instructions: TacticalInstructions }

module TacticalPlan =

    let fromLineup (lineup: Lineup) : TacticalPlan =
        { Formation = lineup.Formation
          Tactics = lineup.Tactics
          Instructions =
            lineup.Instructions
            |> Option.defaultValue TacticalInstructions.defaultInstructions }

    let withInstructions (instructions: TacticalInstructions) (plan: TacticalPlan) =
        { plan with
            Instructions = instructions }

    let signature (plan: TacticalPlan) =
        string plan.Formation, plan.Instructions

// ---------------------------------------------------------------------------
// Deterministic instruction spec — every option is a trade-off, not a buff.
// All deltas are derived from BalanceConfig.Tactics; no hidden numbers.
// ---------------------------------------------------------------------------

type InstructionOption =
    { Value: int
      Label: string
      ShortDesc: string }

type InstructionSpec =
    { Id: string
      Name: string
      Options: InstructionOption list
      Description: string }

type TacticalInstructionTradeoff =
    { Condition: string
      Advantage: string
      Cost: string
      Parameters: string list
      Explain: TacticalInstructions -> int -> string }

module InstructionSpecs =

    let all: InstructionSpec list =
        [ { Id = "Mentality"
            Name = "Mentality"
            Description = "Risk appetite and attacking intent"
            Options =
              [ { Value = 0
                  Label = "Defensive"
                  ShortDesc = "Protect lead, concede possession" }
                { Value = 2
                  Label = "Balanced"
                  ShortDesc = "No bias" }
                { Value = 4
                  Label = "Attacking"
                  ShortDesc = "Commit extra runners" } ] }
          { Id = "DefensiveLine"
            Name = "Defensive Line"
            Description = "Height of the back line"
            Options =
              [ { Value = 0
                  Label = "Deep"
                  ShortDesc = "Drop off, protect behind" }
                { Value = 2
                  Label = "Mid"
                  ShortDesc = "Balanced" }
                { Value = 4
                  Label = "High"
                  ShortDesc = "Compress space, squeeze midfield" } ] }
          { Id = "PressingIntensity"
            Name = "Pressing"
            Description = "How aggressively to chase the ball"
            Options =
              [ { Value = 0
                  Label = "Low"
                  ShortDesc = "Stay compact, save legs" }
                { Value = 2
                  Label = "Mid"
                  ShortDesc = "Balanced" }
                { Value = 4
                  Label = "High"
                  ShortDesc = "Hunt in packs" } ] }
          { Id = "Width"
            Name = "Width"
            Description = "Horizontal spread in possession"
            Options =
              [ { Value = 0
                  Label = "Narrow"
                  ShortDesc = "Overload centre" }
                { Value = 2
                  Label = "Balanced"
                  ShortDesc = "No bias" }
                { Value = 4
                  Label = "Wide"
                  ShortDesc = "Stretch the opponent" } ] }
          { Id = "Tempo"
            Name = "Tempo"
            Description = "Speed of ball circulation"
            Options =
              [ { Value = 0
                  Label = "Slow"
                  ShortDesc = "Patient, keep ball" }
                { Value = 2
                  Label = "Mixed"
                  ShortDesc = "Balanced" }
                { Value = 4
                  Label = "Fast"
                  ShortDesc = "Transitions, vertical" } ] }
          { Id = "Directness"
            Name = "Directness"
            Description = "Length of passes"
            Options =
              [ { Value = 0
                  Label = "Short"
                  ShortDesc = "Build through thirds" }
                { Value = 2
                  Label = "Mixed"
                  ShortDesc = "Balanced" }
                { Value = 4
                  Label = "Direct"
                  ShortDesc = "Early balls, second balls" } ] }
          { Id = "DefensiveShape"
            Name = "Defensive Shape"
            Description = "Marking approach out of possession"
            Options =
              [ { Value = 0
                  Label = "Zonal"
                  ShortDesc = "Cover space" }
                { Value = 2
                  Label = "Mixed"
                  ShortDesc = "Balanced" }
                { Value = 4
                  Label = "Man"
                  ShortDesc = "Tight marking" } ] } ]

    let tryFind id =
        all |> List.tryFind (fun s -> s.Id = id)

/// A single pure registry joins the instruction identity with its explanation.
/// Values are descriptive contracts; numerical effects remain in BalanceConfig
/// and are evaluated by TacticalStaffAnalysis.
module TacticalInstructionRegistry =

    let private tradeoff id : TacticalInstructionTradeoff =
        let mk condition advantage cost parameters text =
            { Condition = condition
              Advantage = advantage
              Cost = cost
              Parameters = parameters
              Explain = fun _ value -> sprintf text value }

        match id with
        | "Mentality" ->
            mk
                "choice of risk"
                "more attacking commitment"
                "space after loss"
                [ "Mentality" ]
                "Mentality %d trades security for commitment."
        | "DefensiveLine" ->
            mk
                "defending without the ball"
                "field compression"
                "space behind"
                [ "DefensiveLine"; "PressingIntensity" ]
                "Line %d trades depth for compression."
        | "PressingIntensity" ->
            mk
                "a pressing trigger"
                "near-ball recoveries"
                "condition and cover"
                [ "PressingIntensity"; "PressTriggerZone" ]
                "Press %d trades energy for disruption."
        | "Width" ->
            mk
                "possession"
                "outside passing lanes"
                "central or wing cover"
                [ "Width" ]
                "Width %d trades one corridor's cover for another's access."
        | "Tempo" ->
            mk
                "supported circulation"
                "faster progression"
                "execution margin"
                [ "Tempo" ]
                "Tempo %d trades time for completion margin."
        | "Directness" ->
            mk
                "a forward target"
                "early progression"
                "possession security"
                [ "Directness" ]
                "Directness %d trades security for progression."
        | "PressTriggerZone" ->
            mk
                "the opponent enters the zone"
                "timed pressure"
                "unpressured build-up elsewhere"
                [ "PressTriggerZone"; "DefensiveLine" ]
                "Trigger %d trades early disruption for timing."
        | "DefensiveShape" ->
            mk
                "defending without the ball"
                "cover around the ball"
                "width or depth"
                [ "DefensiveShape"; "Width" ]
                "Shape %d trades cover in one area for space elsewhere."
        | _ ->
            mk
                "a declared tactical condition"
                "a situational advantage"
                "a situational cost"
                []
                "Instruction %d has a declared trade-off."

    let all = InstructionSpecs.all |> List.map (fun spec -> spec, tradeoff spec.Id)

    let tryFind id =
        all |> List.tryFind (fun (spec, _) -> spec.Id = id)

    let explain (instructions: TacticalInstructions) (id: string) =
        match tryFind id with
        | Some(_, tradeoff) ->
            let value = TacticalInstructions.get id instructions
            tradeoff.Explain instructions value
        | None -> "Unknown tactical instruction."

/// Evidence that is already known at the decision boundary. A playbook can
/// recommend a change only from these signals; it cannot infer hidden engine
/// state while the view is rendering.
type TacticalEvidence =
    { IsLeading: bool
      IsTrailing: bool
      OpponentHasFastAttack: bool option
      OpponentUsesLowBlock: bool option
      TeamAverageStamina: float option }

type TacticalSituation =
    | ProtectLead
    | ChaseGoal
    | ContainFastAttack
    | OpenLowBlock

type TacticalPlaybook =
    { Id: string
      Name: string
      Situation: TacticalSituation
      Summary: string
      Effects: string list
      Commitments: string list
      RecommendationBasis: string
      Apply: TacticalPlan -> TacticalPlan
      Recommendation: TacticalEvidence -> string option }

module TacticalPlaybooks =

    let private withValues (values: (string * int) list) (plan: TacticalPlan) =
        let instructions =
            values
            |> List.fold (fun current (id, value) -> TacticalInstructions.set id value current) plan.Instructions

        { plan with
            Instructions = instructions }

    let private evidenceRecommendation condition reason (evidence: TacticalEvidence) =
        if condition evidence then Some reason else None

    /// Contextual plans are reusable because they transform the shared plan
    /// instead of replacing formation or creating a second tactical model.
    let all: TacticalPlaybook list =
        [ { Id = "protect-lead"
            Name = "Cerrar el partido"
            Situation = ProtectLead
            Summary = "Reduce el espacio concedido cuando el marcador ya favorece al equipo."
            Effects =
              [ TacticalInstructionRegistry.explain
                    { TacticalInstructions.defaultInstructions with
                        Mentality = 0 }
                    "Mentality"
                TacticalInstructionRegistry.explain
                    { TacticalInstructions.defaultInstructions with
                        DefensiveLine = 0 }
                    "DefensiveLine"
                "Menos transiciones expuestas en campo propio" ]
            Commitments = [ "Cede metros y posesión"; "Genera menos llegadas propias" ]
            RecommendationBasis = "Solo con ventaja confirmada en el marcador"
            Apply =
              withValues
                  [ "Mentality", 0
                    "DefensiveLine", 0
                    "PressingIntensity", 1
                    "Tempo", 0
                    "Directness", 1 ]
            Recommendation =
              evidenceRecommendation
                  (fun e -> e.IsLeading)
                  "El equipo está en ventaja: la recomendación usa el marcador conocido." }
          { Id = "chase-goal"
            Name = "Buscar el gol"
            Situation = ChaseGoal
            Summary = "Aumenta la amenaza y la velocidad de progresión cuando hace falta marcar."
            Effects =
              [ TacticalInstructionRegistry.explain
                    { TacticalInstructions.defaultInstructions with
                        Mentality = 4 }
                    "Mentality"
                TacticalInstructionRegistry.explain
                    { TacticalInstructions.defaultInstructions with
                        Tempo = 4 }
                    "Tempo"
                "Más jugadores llegan a zonas de remate" ]
            Commitments =
              [ "Deja más espacio después de perder la pelota"
                "Reduce el margen de ejecución" ]
            RecommendationBasis = "Solo con desventaja confirmada en el marcador"
            Apply = withValues [ "Mentality", 4; "Tempo", 4; "Directness", 3; "PressingIntensity", 3 ]
            Recommendation =
              evidenceRecommendation
                  (fun e -> e.IsTrailing)
                  "El equipo está abajo en el marcador: la recomendación usa el marcador conocido." }
          { Id = "contain-fast-attack"
            Name = "Proteger la espalda"
            Situation = ContainFastAttack
            Summary = "Ajusta la altura para limitar los balones a la espalda ante un ataque rápido."
            Effects =
              [ TacticalInstructionRegistry.explain
                    { TacticalInstructions.defaultInstructions with
                        DefensiveLine = 0 }
                    "DefensiveLine"
                "Mantiene una salida directa disponible" ]
            Commitments = [ "Concede más territorio"; "Presiona más tarde" ]
            RecommendationBasis = "Solo con scouting confirmado de ataque rival rápido"
            Apply = withValues [ "DefensiveLine", 0; "PressTriggerZone", 0; "Directness", 3 ]
            Recommendation =
              evidenceRecommendation
                  (fun e -> e.OpponentHasFastAttack = Some true)
                  "El scouting conocido marca un ataque rival rápido: la recomendación se apoya en esa evidencia." }
          { Id = "open-low-block"
            Name = "Abrir el bloque"
            Situation = OpenLowBlock
            Summary = "Estira al rival y acelera la circulación para encontrar el lado débil."
            Effects =
              [ TacticalInstructionRegistry.explain
                    { TacticalInstructions.defaultInstructions with
                        Width = 4 }
                    "Width"
                TacticalInstructionRegistry.explain
                    { TacticalInstructions.defaultInstructions with
                        Tempo = 3 }
                    "Tempo" ]
            Commitments =
              [ "Expone más los cambios de orientación"
                "Requiere precisión en el último pase" ]
            RecommendationBasis = "Solo con scouting confirmado de bloque bajo rival"
            Apply = withValues [ "Width", 4; "Tempo", 3; "PressingIntensity", 2 ]
            Recommendation =
              evidenceRecommendation
                  (fun e -> e.OpponentUsesLowBlock = Some true)
                  "El scouting conocido identifica bloque bajo: la recomendación usa esa observación." } ]

    let tryFind id =
        all |> List.tryFind (fun playbook -> playbook.Id = id)

    let applyToInstructions (playbook: TacticalPlaybook) (instructions: TacticalInstructions) =
        { Formation = F433
          Tactics = Balanced
          Instructions = instructions }
        |> playbook.Apply
        |> _.Instructions
