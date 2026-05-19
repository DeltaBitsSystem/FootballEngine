namespace FootballEngine.Types

open FootballEngine.Domain
open FootballEngine.Types.PhysicsContract

[<Struct>]
type PitchZone =
    | AttackingZone
    | MidfieldZone
    | DefensiveZone

[<Struct>]
type FlankZone =
    | LeftFlank
    | CentralFlank
    | RightFlank

module PitchZoneOps =

    let ofPosition (x: float<meter>) (y: float<meter>) : PitchZone =
        if x < 30.0<meter> then DefensiveZone
        elif x <= 70.0<meter> then MidfieldZone
        else AttackingZone

    let toFlank (y: float<meter>) : FlankZone =
        if y < 22.0<meter> then LeftFlank
        elif y > 46.0<meter> then RightFlank
        else CentralFlank

    let isFinalThird (zone: PitchZone) (attackDir: AttackDir) : bool =
        match attackDir, zone with
        | LeftToRight, AttackingZone -> true
        | RightToLeft, DefensiveZone -> true
        | _ -> false
