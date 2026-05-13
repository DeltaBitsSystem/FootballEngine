namespace FootballEngine.Types

open FootballEngine.Domain
open FootballEngine.Types.PhysicsContract



type DefensiveRole =
    | FirstDefender = 0uy
    | Cover = 1uy
    | Marker = 2uy

type PlayerOut =
    | SidelinedByRedCard
    | SidelinedByInjury
    | SidelinedBySub

type OccupancyKind =
    | Active of int
    | Sidelined of PlayerOut

type HomeSlotIndex = HomeSlotIndex of int
type AwaySlotIndex = AwaySlotIndex of int

module SlotIndex =
    let home i = HomeSlotIndex i
    let away i = AwaySlotIndex i
    let unboxHome (HomeSlotIndex i) = i
    let unboxAway (AwaySlotIndex i) = i

[<Struct>]
type MentalState =
    { ComposureLevel: float
      ConfidenceLevel: float
      FocusLevel: float
      AggressionLevel: float
      RiskTolerance: float }

module MentalState =
    let initial (player: Player) =
        { ComposureLevel = normaliseAttr player.Mental.Composure
          ConfidenceLevel = normaliseCondition player.Morale
          FocusLevel = normaliseAttr player.Mental.Concentration
          AggressionLevel = normaliseAttr player.Mental.Aggression
          RiskTolerance = 0.5 }

type PlayerRoster =
    { Players: Player[]
      Profiles: BehavioralProfile[]
      SlotCount: int }

module PlayerRoster =
    let build (players: Player[]) : PlayerRoster =
        let profiles = players |> Array.map Player.profile

        { Players = players
          Profiles = profiles
          SlotCount = players.Length }

type PhysicsFrame =
    { PosX: float32[]
      PosY: float32[]
      VelX: float32[]
      VelY: float32[]
      Occupancy: OccupancyKind[]
      SlotCount: int
      ActiveCount: int }

module PhysicsFrame =
    let init (n: int) (basePositions: Spatial[]) : PhysicsFrame =
        { PosX = Array.init n (fun i -> float32 basePositions[i].X)
          PosY = Array.init n (fun i -> float32 basePositions[i].Y)
          VelX = Array.zeroCreate n
          VelY = Array.zeroCreate n
          Occupancy = Array.init n (fun i -> OccupancyKind.Active i)
          SlotCount = n
          ActiveCount = n }

type CognitiveOutputFrame =
    { ShapeTargetX: float32[]
      ShapeTargetY: float32[]
      SupportPosX: float32[]
      SupportPosY: float32[]
      DefensiveRole: byte[] }

module CognitiveOutputFrame =
    let init (n: int) : CognitiveOutputFrame =
        { ShapeTargetX = Array.zeroCreate n
          ShapeTargetY = Array.zeroCreate n
          SupportPosX = Array.zeroCreate n
          SupportPosY = Array.zeroCreate n
          DefensiveRole = Array.create n (byte DefensiveRole.Marker) }

type ConditionFrame =
    { Condition: float32[]
      CachedTargetX: float32[]
      CachedTargetY: float32[]
      CachedExecution: float32[] }

module ConditionFrame =
    let init (n: int) (roster: PlayerRoster) (basePositions: Spatial[]) : ConditionFrame =
        { Condition = Array.init n (fun i -> float32 roster.Players[i].Condition)
          CachedTargetX = Array.init n (fun i -> float32 basePositions[i].X)
          CachedTargetY = Array.init n (fun i -> float32 basePositions[i].Y)
          CachedExecution = Array.create n 1.0f }

type MentalFrame =
    { Composure: float32[]
      Confidence: float32[]
      Focus: float32[]
      Aggression: float32[]
      RiskTolerance: float32[] }

module MentalFrame =
    let init (n: int) (roster: PlayerRoster) : MentalFrame =
        { Composure = Array.init n (fun i -> float32 (MentalState.initial roster.Players[i]).ComposureLevel)
          Confidence = Array.init n (fun i -> float32 (MentalState.initial roster.Players[i]).ConfidenceLevel)
          Focus = Array.init n (fun i -> float32 (MentalState.initial roster.Players[i]).FocusLevel)
          Aggression = Array.init n (fun i -> float32 (MentalState.initial roster.Players[i]).AggressionLevel)
          RiskTolerance = Array.create n 0.5f }
