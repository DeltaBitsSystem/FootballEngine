namespace FootballEngine

open FootballEngine.Domain

type DuelResult =
    | Won
    | Lost

[<Struct>]
type PlayerMatchMemory =
    { mutable RecentPassFailures: int
      mutable SuccessStreak: int
      mutable LastOpponentId: int // slot index, -1 = none
      mutable DuelWins: int
      mutable DuelLosses: int }

module PlayerMatchMemory =
    let empty () =
        { RecentPassFailures = 0
          SuccessStreak = 0
          LastOpponentId = -1
          DuelWins = 0
          DuelLosses = 0 }

type MatchMemory =
    { Home: PlayerMatchMemory[] // indexed by slot (0..10)
      Away: PlayerMatchMemory[] }

module MatchMemory =
    let create () =
        { Home = Array.init 11 (fun _ -> PlayerMatchMemory.empty ())
          Away = Array.init 11 (fun _ -> PlayerMatchMemory.empty ()) }

    let empty = create ()

    let getSlot (mem: MatchMemory) (side: ClubSide) (slotIdx: int) : byref<PlayerMatchMemory> =
        if side = HomeClub then
            &mem.Home[slotIdx]
        else
            &mem.Away[slotIdx]

    let recordDuel (side: ClubSide) (slotIdx: int) (opponentSlot: int) (result: DuelResult) (mem: MatchMemory) =
        let arr = if side = HomeClub then mem.Home else mem.Away
        arr[slotIdx].LastOpponentId <- opponentSlot

        match result with
        | Won ->
            arr[slotIdx].DuelWins <- min 5 (arr[slotIdx].DuelWins + 1)
            arr[slotIdx].SuccessStreak <- min 5 (arr[slotIdx].SuccessStreak + 1)
        | Lost -> arr[slotIdx].DuelLosses <- min 5 (arr[slotIdx].DuelLosses + 1)

    let recordPassFailure (side: ClubSide) (slotIdx: int) (mem: MatchMemory) =
        let arr = if side = HomeClub then mem.Home else mem.Away
        arr[slotIdx].RecentPassFailures <- min 5 (arr[slotIdx].RecentPassFailures + 1)
        arr[slotIdx].SuccessStreak <- 0

    let recordSuccess (side: ClubSide) (slotIdx: int) (mem: MatchMemory) =
        let arr = if side = HomeClub then mem.Home else mem.Away
        arr[slotIdx].SuccessStreak <- min 5 (arr[slotIdx].SuccessStreak + 1)
        arr[slotIdx].RecentPassFailures <- max 0 (arr[slotIdx].RecentPassFailures - 1)

    let duelHistoryModifier (side: ClubSide) (slotIdx: int) (mem: MatchMemory) : float =
        let arr = if side = HomeClub then mem.Home else mem.Away
        let wins = arr[slotIdx].DuelWins
        let losses = arr[slotIdx].DuelLosses
        float (wins - losses) * 0.033

    let passFailureModifier (side: ClubSide) (slotIdx: int) (mem: MatchMemory) : float =
        let arr = if side = HomeClub then mem.Home else mem.Away
        -(float arr[slotIdx].RecentPassFailures * 0.02)

    let successStreakModifier (side: ClubSide) (slotIdx: int) (mem: MatchMemory) : float =
        let arr = if side = HomeClub then mem.Home else mem.Away
        float arr[slotIdx].SuccessStreak * 0.01

    let decay (mem: MatchMemory) : unit =
        for side in [ mem.Home; mem.Away ] do
            for i = 0 to side.Length - 1 do
                side[i].RecentPassFailures <- max 0 (int (float side[i].RecentPassFailures * 0.8))
                side[i].DuelWins <- max 0 (int (float side[i].DuelWins * 0.8))
                side[i].DuelLosses <- max 0 (int (float side[i].DuelLosses * 0.8))

    let DecayIntervalSubTicks = 12000
