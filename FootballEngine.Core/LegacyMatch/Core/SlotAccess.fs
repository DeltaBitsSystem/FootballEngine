namespace FootballEngine

open FootballEngine.Domain
open FootballEngine.Types

/// Helpers to access Player/PlayerId from a slot index.
/// Prevents confusion between slot indices and roster indices.
module SlotAccess =

    let playerAtSlot (frame: TeamFrame) (roster: PlayerRoster) (slotIdx: int) : Player =
        match frame.Physics.Occupancy[slotIdx] with
        | OccupancyKind.Active ri -> roster.Players[ri]
        | _ -> failwithf "SlotAccess.playerAtSlot: slot %d is not active" slotIdx

    let playerIdAtSlot (frame: TeamFrame) (roster: PlayerRoster) (slotIdx: int) : PlayerId =
        (playerAtSlot frame roster slotIdx).Id
