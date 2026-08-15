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
