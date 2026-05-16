namespace FootballEngine.Player.Intent

open FootballEngine.Types
open FootballEngine.Types.IntentPhaseTypes
open FootballEngine.Types.SimulationClock


module IntentPhase =

    [<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)>]
    let duration (clock: SimulationClock) (intent: MovementIntent) : int<tickDelta> * ExitTrigger =
        let secs s =
            SimulationClock.secondsToSubTicks clock s |> int |> fun x -> x * 1<tickDelta>

        match intent with
        | MaintainShape _ -> secs 1.0, ExitTrigger.PossessionChanged
        | PressBall _ -> secs 0.3, ExitTrigger.PossessionChanged
        | MarkMan _ -> secs 0.75, ExitTrigger.PossessionChanged
        | CoverSpace _ -> secs 1.2, ExitTrigger.PossessionChanged
        | SupportAttack _ -> secs 1.5, ExitTrigger.BallInFlight
        | RecoverBall _ -> secs 1.0, ExitTrigger.BallReceived
        | ExecuteRun run -> run.DurationSubTicks, ExitTrigger.BallReceived
        | MoveToSetPiecePos _ -> secs 4.0, ExitTrigger.SetPieceAwarded

    let triggerFired (trigger: ExitTrigger) (committedAt: int) (history: PossessionHistory) : bool =
        match trigger with
        | ExitTrigger.PossessionChanged -> history.LastChangeTick > committedAt
        | ExitTrigger.BallInFlight -> history.LastBallInFlightTick > committedAt
        | ExitTrigger.BallReceived -> history.LastBallReceivedTick > committedAt
        | ExitTrigger.SetPieceAwarded -> history.LastSetPieceTick > committedAt
        | _ -> false

    let shouldRecalculate (frame: IntentDataFrame) (i: int) (subTick: int<subtick>) (history: PossessionHistory) : bool =
        let phase = frame.Phase[i]

        if phase = Executing && int subTick < frame.CommittedUntil[i] then
            let trigger: ExitTrigger = LanguagePrimitives.EnumOfValue frame.ExitTrigger[i]

            not (triggerFired trigger frame.CommittedAt[i] history)
        else
            true
