namespace FootballEngine

open FootballEngine.Types
open FootballEngine.Types.SchedulingTypes


module PlayerActionOps =

    let toOnBallIntent (action: PlayerAction) : OnBallIntent option =
        match action with
        | PlayerAction.Shoot -> Some OnBallIntent.Shoot
        | PlayerAction.Pass target -> Some(OnBallIntent.Pass target.Id)
        | PlayerAction.Dribble -> Some OnBallIntent.Dribble
        | PlayerAction.Cross -> Some OnBallIntent.Cross
        | PlayerAction.LongBall
        | PlayerAction.FreeKick
        | PlayerAction.Corner
        | PlayerAction.ThrowIn _
        | PlayerAction.Penalty _ -> None
        | PlayerAction.Tackle opponent -> Some(OnBallIntent.Tackle opponent.Id)
