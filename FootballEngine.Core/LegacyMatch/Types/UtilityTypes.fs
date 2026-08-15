namespace FootballEngine

[<Struct>]
type PressDepth =
    | PressHigh
    | PressMid
    | PressLow

[<Struct>]
type FlankTarget =
    | FlankLeft
    | FlankRight

type CollectiveAction =
    | Press of PressDepth
    | DropDeep
    | CounterPress
    | BuildFromBack
    | PlayWings
    | OverloadFlank of FlankTarget
    | DirectPlay
    | SitAndCounter
    | HoldPossession
    | CompactBlock
    | HighLine
    | Structured
