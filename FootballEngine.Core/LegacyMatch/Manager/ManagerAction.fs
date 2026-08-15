namespace FootballEngine

open FootballEngine.Domain

type ManagerAction =
    | MakeSubstitution of clubId: ClubId * outIdx: int * incoming: Player
    | AdjustTactics of clubId: ClubId * newTactics: TeamTactics
    | ManagerIdle
