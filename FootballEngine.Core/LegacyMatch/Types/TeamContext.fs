namespace FootballEngine.Types

open FootballEngine.Domain
open FootballEngine.Types // PlayerFrame, DirectiveTypes, HomeBonus


type ChemistryGraph =
    { Familiarity: float[,]
      Leadership: float[]
      PlayerCount: int }

module ChemistryGraph =
    let init playerCount =
        { Familiarity = Array2D.create playerCount playerCount 0.5
          Leadership = Array.zeroCreate playerCount
          PlayerCount = playerCount }





type MatchStats =
    { PassAttempts: int
      PassSuccesses: int
      PressAttempts: int
      PressSuccesses: int
      FlankAttempts: int
      FlankSuccesses: int }

module MatchStats =
    let empty =
        { PassAttempts = 0
          PassSuccesses = 0
          PressAttempts = 0
          PressSuccesses = 0
          FlankAttempts = 0
          FlankSuccesses = 0 }

[<Struct>]
type HomeBonus =
    { AttackDuel: float
      DefendDuel: float
      Tackle: float
      PassAcc: float
      ShotCompos: float
      SetPlay: float
      FreeKick: float
      Penalty: float
      CardReduc: float
      FatigueReduc: float }

[<Struct>]
type TeamPerspective =
    { ClubSide: ClubSide
      ClubId: ClubId
      AttackDir: AttackDir
      OwnFrame: TeamFrame
      OppFrame: TeamFrame
      OwnRoster: PlayerRoster
      OppRoster: PlayerRoster
      Bonus: HomeBonus }
