namespace FootballEngine.Types

open FootballEngine.Domain
open FootballEngine.Types.PhysicsContract

[<Struct>]
type PassTarget = private PassTarget of idx: int * playerId: PlayerId * position: Spatial * distance: float<meter>

module PassTarget =
    let create
        (passerIdx: int)
        (candidateIdx: int)
        (pid: PlayerId)
        (pos: Spatial)
        (dist: float<meter>)
        : PassTarget option =
        if candidateIdx = passerIdx then
            None
        else
            Some(PassTarget(candidateIdx, pid, pos, dist))

    let idx (PassTarget(i, _, _, _)) = i

    let playerId (PassTarget(_, pid, _, _)) = pid

    let position (PassTarget(_, _, p, _)) = p

    let distance (PassTarget(_, _, _, d)) = d
