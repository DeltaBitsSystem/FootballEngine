namespace FootballEngine.Player.Actions

open FootballEngine.Domain
open FootballEngine.ML

open FootballEngine.Stats
open FootballEngine.Types
open FootballEngine.Types.PhysicsContract


type CornerRoutine =
    | NearPost
    | FarPost
    | ShortCorner
    | FlickOn
    | EdgeOfBox

type CornerDefense =
    | ZonalMarking of zones: int
    | ManMarking
    | Mixed of zonalCount: int

type FreeKickRoutine =
    | DirectShot
    | LayOff
    | OverTheWall
    | NearPostRun
    | TrickPlay

type ThrowInRoutine =
    | ShortThrow
    | LongThrow
    | QuickThrow

type SetPieceTaker = { PlayerId: PlayerId; Score: float }

module SetPieceTakerSelection =

    let scoreFreeKickTaker (p: Player) : float =
        let fk = float p.Technical.FreeKick / 20.0
        let pass = float p.Technical.Passing / 20.0
        0.6 * fk + 0.4 * pass

    let scoreCrossingTaker (p: Player) : float =
        let cross = float p.Technical.Crossing / 20.0
        let pass = float p.Technical.Passing / 20.0
        0.7 * cross + 0.3 * pass

    let selectTaker (players: Player[]) (scoreFunc: Player -> float) : SetPieceTaker =
        players
        |> Array.map (fun p -> { PlayerId = p.Id; Score = scoreFunc p })
        |> Array.maxBy _.Score

module CornerRoutineSelection =

    let select (taker: Player) (opponent: Player[]) (clock: SimulationClock) : CornerRoutine =
        let crossing = float taker.Technical.Crossing / 20.0
        let vision = float taker.Mental.Vision / 20.0

        let weights =
            [ (0.25 + crossing * 0.2), NearPost
              (0.2 + vision * 0.15), FarPost
              (0.15 + vision * 0.1), ShortCorner
              (0.15 + crossing * 0.1), FlickOn
              (0.1 + vision * 0.05), EdgeOfBox ]

        pickWeighted weights

    let selectDefense (defenders: Player[]) : CornerDefense =
        let avgMarking =
            defenders |> Array.averageBy (fun d -> float d.Technical.Marking / 20.0)

        let avgPositioning =
            defenders |> Array.averageBy (fun d -> float d.Mental.Positioning / 20.0)

        if avgPositioning > 0.6 then ZonalMarking 3
        elif avgMarking > 0.6 then ManMarking
        else Mixed 2

module FreeKickRoutineSelection =

    let select (taker: Player) (distToGoal: float<meter>) (clock: SimulationClock) : FreeKickRoutine =
        let dist = float distToGoal
        let fk = float taker.Technical.FreeKick / 20.0
        let longShots = float taker.Technical.LongShots / 20.0
        let vision = float taker.Mental.Vision / 20.0

        if dist < 25.0 && fk > 0.7 then
            let weights =
                [ (0.4 + fk * 0.3), DirectShot
                  0.15, LayOff
                  (0.15 + vision * 0.1), OverTheWall
                  0.1, NearPostRun
                  0.05, TrickPlay ]

            pickWeighted weights
        elif dist < 35.0 then
            let weights =
                [ 0.2, DirectShot
                  0.3, LayOff
                  (0.25 + longShots * 0.15), OverTheWall
                  0.15, NearPostRun
                  0.1, TrickPlay ]

            pickWeighted weights
        else
            let weights =
                [ 0.1, DirectShot
                  0.35, LayOff
                  0.3, OverTheWall
                  0.15, NearPostRun
                  0.1, TrickPlay ]

            pickWeighted weights

module ThrowInSelection =

    let select (thrower: Player) (hasLongThrow: bool) (clock: SimulationClock) : ThrowInRoutine =
        let throwing = float thrower.Technical.Crossing / 20.0

        if hasLongThrow && throwing > 0.6 then
            let weights = [ 0.3, ShortThrow; 0.4, LongThrow; 0.3, QuickThrow ]
            pickWeighted weights
        else
            let weights = [ 0.6, ShortThrow; 0.1, LongThrow; 0.3, QuickThrow ]
            pickWeighted weights
